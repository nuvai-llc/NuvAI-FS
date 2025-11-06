// Src/Api/Endpoints/CargaTablaEndpoint.cs
#nullable enable
using NuvAI_FS.Src.Api.Abstractions;
using NuvAI_FS.Src.Services;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Linq;

namespace NuvAI_FS.Api.Endpoints
{
    [SupportedOSPlatform("windows")]
    public sealed class CargaTabla : IApiEndpoint
    {
        public string Method => "POST";
        public string Path => "/cargatabla";

        private readonly OleDbAccdbService _db;
        private readonly IUiNotifier _ui; // dejado por si se necesita en el futuro

        public CargaTabla(OleDbAccdbService db, IUiNotifier ui)
        {
            _db = db;
            _ui = ui;
        }

        private sealed class Body
        {
            public string? Tabla { get; set; }
            public string? Filtro { get; set; }   // modo libre: ej. "CODART = 'AGU001' ORDER BY CODART DESC LIMIT 10"
            public string? Campo { get; set; }    // modo estructurado (opcional)
            public string? Operador { get; set; } // =, <>, >, >=, <, <=, LIKE, IN, BETWEEN, IS NULL, IS NOT NULL
            public string? Valor { get; set; }    // ej. "AGU001", "10", "2025-11-06", "a,b,c", "10,20"
            public string? OrderBy { get; set; }  // ej. "CODART DESC"
        }

        public async Task HandleAsync(ApiContext ctx, CancellationToken ct)
        {
            string raw;
            try { raw = await ctx.ReadBodyAsync().ConfigureAwait(false); }
            catch { await WriteKoAsync(ctx); return; }

            Body? body;
            try
            {
                body = JsonSerializer.Deserialize<Body>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch { await WriteKoAsync(ctx); return; }

            if (body is null || string.IsNullOrWhiteSpace(body.Tabla))
            {
                await WriteKoAsync(ctx); return;
            }

            var table = SanitizeTableName(body.Tabla!);
            if (table is null) { await WriteKoAsync(ctx); return; }

            // 1) Construir cláusula completa (puede traer WHERE + ORDER BY + LIMIT/OFFSET)
            string clause = BuildWhereClause(body).Trim();

            // 2) Extraer y quitar LIMIT/OFFSET del FINAL de la cláusula completa (antes de separar ORDER BY)
            int? limit; int? offset;
            ExtractLimitOffsetFromClause(ref clause, out limit, out offset);

            // 3) Separar WHERE y ORDER BY limpios (ya sin LIMIT/OFFSET)
            SplitWhereAndOrder(clause, out var predicate, out var orderByInClause);

            // 4) Si no hay ORDER BY en la cláusula, acepta body.OrderBy (normalizado)
            var finalOrderBy = !string.IsNullOrWhiteSpace(orderByInClause)
                ? orderByInClause
                : NormalizeOrderBy(body.OrderBy);

            // 5) Construir SQL Access (TOP / doble TOP para OFFSET)
            string sql;
            try
            {
                if (offset.HasValue)
                {
                    if (string.IsNullOrWhiteSpace(finalOrderBy)) { await WriteKoAsync(ctx); return; }
                    if (!limit.HasValue || limit.Value <= 0) { await WriteKoAsync(ctx); return; }

                    var topInner = limit.Value + offset.Value;
                    var whereClause = string.IsNullOrWhiteSpace(predicate) ? "" : $" WHERE {predicate}";

                    var inner = $"SELECT TOP {topInner} * FROM [{table}]{whereClause} ORDER BY {finalOrderBy}";
                    var mid = $"SELECT TOP {limit.Value} * FROM ({inner}) AS T1 ORDER BY {finalOrderBy} DESC";
                    sql = $"SELECT * FROM ({mid}) AS T2 ORDER BY {finalOrderBy} ASC";
                }
                else if (limit.HasValue && limit.Value > 0)
                {
                    var whereClause = string.IsNullOrWhiteSpace(predicate) ? "" : $" WHERE {predicate}";
                    sql = $"SELECT TOP {limit.Value} * FROM [{table}]{whereClause}";
                    if (!string.IsNullOrWhiteSpace(finalOrderBy))
                        sql += $" ORDER BY {finalOrderBy}";
                }
                else
                {
                    var whereClause = string.IsNullOrWhiteSpace(predicate) ? "" : $" WHERE {predicate}";
                    sql = $"SELECT * FROM [{table}]{whereClause}";
                    if (!string.IsNullOrWhiteSpace(finalOrderBy))
                        sql += $" ORDER BY {finalOrderBy}";
                }
            }
            catch { await WriteKoAsync(ctx); return; }

            try
            {
                var rows = await _db.ExecuteQueryAsync(sql, ct).ConfigureAwait(false);
                if (rows is null || rows.Count == 0) { await WriteKoAsync(ctx); return; }

                var resultado = rows.Select(row =>
                    row.Select(kv => new { columna = kv.Key, dato = kv.Value?.ToString() ?? string.Empty }).ToList()
                ).ToList();

                await ctx.WriteJsonAsync(new { resultado, respuesta = "OK" });
            }
            catch { await WriteKoAsync(ctx); }
        }

        // -------- Helpers LIMIT/ORDER --------

        // Quita "LIMIT n" o "LIMIT n OFFSET m" del FINAL de la cláusula COMPLETA y saca los valores.
        // Importante: debe ejecutarse ANTES de separar el ORDER BY.
        private static void ExtractLimitOffsetFromClause(ref string clause, out int? limit, out int? offset)
        {
            limit = null; offset = null;
            if (string.IsNullOrWhiteSpace(clause)) return;

            var rx = new Regex(@"\s+LIMIT\s+(?<n>\d+)(\s+OFFSET\s+(?<o>\d+))?\s*$",
                               RegexOptions.IgnoreCase);

            var m = rx.Match(clause);
            if (!m.Success) return;

            if (int.TryParse(m.Groups["n"].Value, out var n)) limit = n;
            if (m.Groups["o"].Success && int.TryParse(m.Groups["o"].Value, out var o)) offset = o;

            clause = rx.Replace(clause, "").Trim();
        }

        // Separa "predicado [ORDER BY ...]" en predicado y orderBy (sin la keyword)
        private static void SplitWhereAndOrder(string whereOrOrder, out string predicate, out string? orderBy)
        {
            predicate = whereOrOrder?.Trim() ?? string.Empty;
            orderBy = null;
            if (string.IsNullOrEmpty(predicate)) return;

            var idx = predicate.LastIndexOf(" ORDER BY ", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                orderBy = predicate[(idx + " ORDER BY ".Length)..].Trim();
                predicate = predicate[..idx].Trim();
            }
        }

        // Valida y normaliza un OrderBy externo (solo campos, comas, ASC/DESC opcional)
        private static string? NormalizeOrderBy(string? ob)
        {
            if (string.IsNullOrWhiteSpace(ob)) return null;
            var s = ob.Trim();
            if (Regex.IsMatch(
                s,
                @"^[A-Za-z0-9_\-]+(\s+(ASC|DESC))?(\s*,\s*[A-Za-z0-9_\-]+(\s+(ASC|DESC))?)*\s*$",
                RegexOptions.IgnoreCase))
            {
                return s;
            }
            return null;
        }

        // -------- Otros helpers --------

        private static string? SanitizeTableName(string tabla)
        {
            var t = tabla.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            if (!Regex.IsMatch(t, @"^[A-Za-z0-9_\-]+$")) return null; // letras, números, _, -
            return t;
        }

        /// <summary>
        /// Construye la parte WHERE (sin "WHERE").
        /// - Si viene Filtro libre, lo devuelve tal cual.
        /// - Si viene modo estructurado (campo, operador, valor), lo formatea con tipos.
        /// Admite: =, <>, >, >=, <, <=, LIKE, IN, BETWEEN, IS NULL, IS NOT NULL.
        /// </summary>
        private static string BuildWhereClause(Body b)
        {
            // 1) Modo libre (puede incluir ORDER BY y/o LIMIT/OFFSET, se tratarán luego)
            if (!string.IsNullOrWhiteSpace(b.Filtro))
            {
                return b.Filtro!.Trim();
            }

            // 2) Modo estructurado
            if (string.IsNullOrWhiteSpace(b.Campo) || string.IsNullOrWhiteSpace(b.Operador))
                return string.Empty;

            var campo = b.Campo.Trim();
            if (!Regex.IsMatch(campo, @"^[A-Za-z0-9_\-]+$"))
                throw new InvalidOperationException("Campo inválido.");

            var op = b.Operador.Trim().ToUpperInvariant();

            switch (op)
            {
                case "=":
                case "<>":
                case ">":
                case ">=":
                case "<":
                case "<=":
                    {
                        var v = FormatValueForAccess(b.Valor);
                        return $"[{campo}] {op} {v}";
                    }

                case "LIKE":
                    {
                        var pattern = b.Valor ?? string.Empty;
                        var v = FormatValueForAccess(pattern);
                        return $"[{campo}] LIKE {v}";
                    }

                case "IN":
                    {
                        var raw = b.Valor ?? string.Empty;
                        var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim())
                                       .Where(s => s.Length > 0)
                                       .Select(FormatValueForAccess);
                        var list = string.Join(", ", parts);
                        return $"[{campo}] IN ({list})";
                    }

                case "BETWEEN":
                    {
                        var raw = b.Valor ?? string.Empty;
                        var parts = raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => s.Trim()).ToArray();
                        if (parts.Length != 2)
                            throw new InvalidOperationException("BETWEEN requiere dos valores: min,max");
                        var v1 = FormatValueForAccess(parts[0]);
                        var v2 = FormatValueForAccess(parts[1]);
                        return $"[{campo}] BETWEEN {v1} AND {v2}";
                    }

                case "IS NULL":
                    return $"[{campo}] IS NULL";

                case "IS NOT NULL":
                    return $"[{campo}] IS NOT NULL";

                default:
                    throw new InvalidOperationException("Operador no soportado.");
            }
        }

        /// <summary>
        /// Formatea a sintaxis Access/ACE:
        /// - NULL → NULL
        /// - bool → TRUE/FALSE
        /// - numérico → tal cual (invariant)
        /// - fecha → #MM/dd/yyyy HH:mm:ss#
        /// - texto → 'escapado'
        /// </summary>
        private static string FormatValueForAccess(string? value)
        {
            if (value is null) return "NULL";

            var v = value.Trim();

            if (string.Equals(v, "NULL", StringComparison.OrdinalIgnoreCase))
                return "NULL";

            if (bool.TryParse(v, out var b))
                return b ? "TRUE" : "FALSE";

            if (decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var dec))
                return dec.ToString(CultureInfo.InvariantCulture);

            if (DateTime.TryParse(v, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt) ||
                DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            {
                var formatted = dt.ToString("MM'/'dd'/'yyyy HH':'mm':'ss", CultureInfo.InvariantCulture);
                return $"#{formatted}#";
            }

            var escaped = v.Replace("'", "''");
            return $"'{escaped}'";
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
