// Src/Api/Endpoints/LanzarConsultaEndpoint.cs
#nullable enable
using NuvAI_FS.Src.Api.Abstractions;
using NuvAI_FS.Src.Services;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace NuvAI_FS.Api.Endpoints
{
    /// <summary>
    /// POST /lanzarconsulta
    /// Body:
    /// {
    ///   "consulta": "SELECT CODART, EANART FROM F_ART WHERE CODART LIKE '%001%' ORDER BY CODART DESC LIMIT 10 OFFSET 20"
    /// }
    /// - Solo admite SELECT.
    /// - Acepta comparadores, ORDER BY y LIMIT (y LIMIT + OFFSET estilo MySQL).
    ///   Se reescribe a SQL compatible con Access (TOP / doble TOP para OFFSET).
    /// Respuesta:
    ///  OK -> { "resultado": [[{columna, dato}, ...], ...], "respuesta": "OK" }
    ///  KO -> { "resultado": "[]", "respuesta": "KO" }
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class LanzarConsulta : IApiEndpoint
    {
        public string Method => "POST";
        public string Path => "/lanzarconsulta";

        private readonly OleDbAccdbService _db;

        public LanzarConsulta(OleDbAccdbService db)
        {
            _db = db;
        }

        private sealed class Body
        {
            public string? Consulta { get; set; }
        }

        public async Task HandleAsync(ApiContext ctx, CancellationToken ct)
        {
            string raw;
            try { raw = await ctx.ReadBodyAsync().ConfigureAwait(false); }
            catch { await WriteKoAsync(ctx); return; }

            Body? body;
            try
            {
                body = JsonSerializer.Deserialize<Body>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                await WriteKoAsync(ctx);
                return;
            }

            if (body is null || string.IsNullOrWhiteSpace(body.Consulta))
            {
                await WriteKoAsync(ctx);
                return;
            }

            var sqlInput = body.Consulta.Trim();

            // Validación básica: solo SELECT y una sentencia.
            if (!IsSafeSelect(sqlInput))
            {
                await WriteKoAsync(ctx);
                return;
            }

            // Reescritura para Access: manejar LIMIT / LIMIT+OFFSET al final
            string accessSql;
            try
            {
                accessSql = RewriteSelectForAccess(sqlInput);
            }
            catch
            {
                await WriteKoAsync(ctx);
                return;
            }

            try
            {
                var rows = await _db.ExecuteQueryAsync(accessSql, ct).ConfigureAwait(false);
                if (rows is null || rows.Count == 0)
                {
                    await WriteKoAsync(ctx);
                    return;
                }

                var resultado = rows.Select(row =>
                    row.Select(kv => new
                    {
                        columna = kv.Key,
                        dato = kv.Value?.ToString() ?? string.Empty
                    }).ToList()
                ).ToList();

                await ctx.WriteJsonAsync(new
                {
                    resultado,
                    respuesta = "OK"
                });
            }
            catch
            {
                await WriteKoAsync(ctx);
            }
        }

        // =================== Helpers ===================

        private static bool IsSafeSelect(string sql)
        {
            // No múltiples sentencias
            if (sql.IndexOf(';') >= 0) return false;

            // Debe empezar con SELECT (ignorando espacios y comentarios -- // /* */)
            var trimmed = StripLeadingComments(sql).TrimStart();
            if (!trimmed.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) return false;

            // Bloquear verbos peligrosos por si cuelan con sub-claúsulas
            var forbidden = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE" };
            foreach (var word in forbidden)
            {
                if (Regex.IsMatch(trimmed, $@"\b{word}\b", RegexOptions.IgnoreCase))
                    return false;
            }

            return true;
        }

        private static string StripLeadingComments(string s)
        {
            var i = 0;
            while (i < s.Length)
            {
                // Saltar espacios
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;

                if (i + 1 < s.Length && s[i] == '-' && s[i + 1] == '-')
                {
                    // Comentario de línea
                    var nl = s.IndexOfAny(new[] { '\r', '\n' }, i + 2);
                    i = nl >= 0 ? nl + 1 : s.Length;
                    continue;
                }
                if (i + 1 < s.Length && s[i] == '/' && s[i + 1] == '*')
                {
                    // Comentario bloque
                    var end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end >= 0 ? end + 2 : s.Length;
                    continue;
                }
                break;
            }
            return s.Substring(i);
        }

        /// <summary>
        /// Reescribe SELECT ... [ORDER BY ...] [LIMIT n [OFFSET m]] (estilo MySQL)
        /// a Access SQL:
        ///  - LIMIT n        -> insertar TOP n tras SELECT
        ///  - LIMIT n OFFSET m -> doble TOP con inversión; requiere ORDER BY.
        /// </summary>
        private static string RewriteSelectForAccess(string sql)
        {
            var clause = sql.Trim();

            // 1) Extraer LIMIT y OFFSET del final de la sentencia
            ExtractLimitOffsetFromEnd(ref clause, out var limit, out var offset);

            // 2) Si no hay limit/offset, devolver tal cual
            if (!limit.HasValue && !offset.HasValue)
            {
                return clause;
            }

            // 3) Separar el ORDER BY (limpio, sin LIMIT/OFFSET)
            SplitOrderBy(clause, out var baseWithoutOrder, out var orderBy);

            if (offset.HasValue)
            {
                if (!limit.HasValue || limit.Value <= 0)
                    throw new InvalidOperationException("OFFSET requiere LIMIT > 0.");
                if (string.IsNullOrWhiteSpace(orderBy))
                    throw new InvalidOperationException("LIMIT + OFFSET requieren ORDER BY para paginar.");

                var topInner = limit.Value + offset.Value;

                // Insertar TOP (limit+offset) en la consulta base completa (con ORDER BY)
                var innerWithOrder = clauseWithTop(clause, topInner); // usa la consulta completa con su ORDER BY
                // …pero para el patrón doble TOP, preferimos controlar los ORDER BY explícitos:
                // Construimos nosotros: SELECT TOP (n+o) * FROM (<base sin ORDER>) ORDER BY <order>
                var inner = $"SELECT TOP {topInner} * FROM ({EnsureSelectStar(baseWithoutOrder)}) AS B1";
                inner += $" ORDER BY {orderBy}";

                var mid = $"SELECT TOP {limit.Value} * FROM ({inner}) AS T1 ORDER BY {orderBy} DESC";
                var final = $"SELECT * FROM ({mid}) AS T2 ORDER BY {orderBy} ASC";
                return final;
            }
            else
            {
                // LIMIT simple: insertar TOP n tras SELECT
                return clauseWithTop(clause, limit!.Value);
            }

            // ---- local helpers ----
            static string clauseWithTop(string selectSql, int top)
            {
                // Inserta "TOP n " justo después del primer SELECT (case-insensitive)
                var rx = new Regex(@"^\s*SELECT\s+", RegexOptions.IgnoreCase);
                if (!rx.IsMatch(selectSql))
                    throw new InvalidOperationException("Sentencia SELECT no válida.");
                return rx.Replace(selectSql, m => m.Value + $"TOP {top} ");
            }

            static string EnsureSelectStar(string selectWithoutOrder)
            {
                // Si el SELECT ya proyecta columnas específicas, lo dejamos tal cual.
                // Para envolverlo como subconsulta, Access permite SELECT * FROM (<query>) AS Alias
                // Por tanto devolvemos el texto tal cual; lo usaremos dentro de paréntesis.
                return selectWithoutOrder;
            }
        }

        private static void ExtractLimitOffsetFromEnd(ref string clause, out int? limit, out int? offset)
        {
            limit = null; offset = null;
            if (string.IsNullOrWhiteSpace(clause)) return;

            // Busca "LIMIT n" o "LIMIT n OFFSET m" al final (ignorando espacios)
            var rx = new Regex(@"\s+LIMIT\s+(?<n>\d+)(\s+OFFSET\s+(?<o>\d+))?\s*$",
                               RegexOptions.IgnoreCase);
            var m = rx.Match(clause);
            if (!m.Success) return;

            if (int.TryParse(m.Groups["n"].Value, out var n)) limit = n;
            if (m.Groups["o"].Success && int.TryParse(m.Groups["o"].Value, out var o)) offset = o;

            clause = rx.Replace(clause, "").TrimEnd();
        }

        private static void SplitOrderBy(string clause, out string withoutOrder, out string? orderBy)
        {
            // Busca el ÚLTIMO ORDER BY (para no romper subconsultas que puedan tener ORDER BY internos)
            var idx = clause.LastIndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                withoutOrder = clause[..idx].TrimEnd();
                orderBy = clause[(idx + " ORDER BY ".Length)..].Trim();
            }
            else
            {
                withoutOrder = clause.TrimEnd();
                orderBy = null;
            }
        }

        private static Task WriteKoAsync(ApiContext ctx)
        {
            return ctx.WriteJsonAsync(new
            {
                resultado = "[]",
                respuesta = "KO"
            });
        }
    }
}
