// Src/Api/Endpoints/EscribirRegistro.cs
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
    /// <summary>
    /// POST /escribirregistro
    /// Body:
    /// {
    ///   "tabla": "F_ANT",
    ///   "registro": [
    ///     { "columna": "CODANT", "dato": 20 },
    ///     { "columna": "FECANT", "dato": "2019-08-27" },
    ///     { "columna": "IMPANT", "dato": 210.06 }
    ///   ]
    /// }
    ///
    /// Respuesta:
    ///  OK -> { "resultado": "", "respuesta": "OK" }
    ///  KO -> { "resultado": "", "respuesta": "KO" }
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class EscribirRegistro : IApiEndpoint
    {
        public string Method => "POST";
        public string Path => "/escribirregistro";

        private readonly OleDbAccdbService _db;

        public EscribirRegistro(OleDbAccdbService db)
        {
            _db = db;
        }

        private sealed class Body
        {
            public string? Tabla { get; set; }
            public List<Field>? Registro { get; set; }
        }

        private sealed class Field
        {
            public string? Columna { get; set; }
            public JsonElement Dato { get; set; }
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

            // Validaciones mínimas
            if (body is null || string.IsNullOrWhiteSpace(body.Tabla) || body.Registro is null || body.Registro.Count == 0)
            {
                await WriteKoAsync(ctx);
                return;
            }

            var table = SanitizeTableName(body.Tabla!);
            if (table is null)
            {
                await WriteKoAsync(ctx);
                return;
            }

            // Construcción del INSERT
            List<string> colNames = new();
            List<string> values = new();

            foreach (var f in body.Registro!)
            {
                if (string.IsNullOrWhiteSpace(f.Columna)) { await WriteKoAsync(ctx); return; }
                var col = SanitizeColumnName(f.Columna!);
                if (col is null) { await WriteKoAsync(ctx); return; }

                colNames.Add($"[{col}]");
                values.Add(FormatValueForAccess(f.Dato));
            }

            if (colNames.Count == 0) { await WriteKoAsync(ctx); return; }

            var cols = string.Join(", ", colNames);
            var vals = string.Join(", ", values);

            var sql = $"INSERT INTO [{table}] ({cols}) VALUES ({vals})";

            try
            {
                var affected = await _db.ExecuteNonQueryAsync(sql, ct).ConfigureAwait(false);
                if (affected > 0)
                {
                    await ctx.WriteJsonAsync(new { resultado = "", respuesta = "OK" });
                    return;
                }
                await WriteKoAsync(ctx);
            }
            catch
            {
                // Por contrato, no exponemos detalle. Si quisieras, aquí puedes mapear OleDbException 3314 (required), etc.
                await WriteKoAsync(ctx);
            }
        }

        // ---------- Helpers ----------
        private static string? SanitizeTableName(string tabla)
        {
            var t = tabla.Trim();
            if (string.IsNullOrEmpty(t)) return null;
            // letras, números, _, -
            if (!Regex.IsMatch(t, @"^[A-Za-z0-9_\-]+$")) return null;
            return t;
        }

        private static string? SanitizeColumnName(string col)
        {
            var c = col.Trim();
            if (string.IsNullOrEmpty(c)) return null;
            if (!Regex.IsMatch(c, @"^[A-Za-z0-9_\-]+$")) return null;
            return c;
        }

        /// <summary>
        /// Formatea un valor JSON a sintaxis Access/ACE:
        /// - null → NULL
        /// - true/false → TRUE/FALSE
        /// - number → tal cual (InvariantCulture)
        /// - string con fecha válida → #MM/dd/yyyy HH:mm:ss#
        /// - string general → 'escapado'
        /// </summary>
        private static string FormatValueForAccess(JsonElement el)
        {
            switch (el.ValueKind)
            {
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    return "NULL";

                case JsonValueKind.True:
                    return "TRUE";
                case JsonValueKind.False:
                    return "FALSE";

                case JsonValueKind.Number:
                    if (el.TryGetInt64(out var i64)) return i64.ToString(CultureInfo.InvariantCulture);
                    if (el.TryGetDouble(out var dbl)) return dbl.ToString(CultureInfo.InvariantCulture);
                    return el.ToString();

                case JsonValueKind.String:
                    var s = el.GetString() ?? string.Empty;
                    if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt) ||
                        DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    {
                        var formatted = dt.ToString("MM'/'dd'/'yyyy HH':'mm':'ss", CultureInfo.InvariantCulture);
                        return $"#{formatted}#";
                    }
                    var escaped = s.Replace("'", "''");
                    return $"'{escaped}'";

                default:
                    var asText = el.ToString() ?? string.Empty;
                    var esc = asText.Replace("'", "''");
                    return $"'{esc}'";
            }
        }

        private static Task WriteKoAsync(ApiContext ctx)
        {
            return ctx.WriteJsonAsync(new
            {
                resultado = "",
                respuesta = "KO"
            });
        }
    }
}
