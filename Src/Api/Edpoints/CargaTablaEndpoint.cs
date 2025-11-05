using NuvAI_FS.Src.Api.Abstractions;
using NuvAI_FS.Src.Services;
using System.Runtime.Versioning;
using System.Text.Json;

namespace NuvAI_FS.Api.Endpoints
{
    [SupportedOSPlatform("windows")]
    public sealed class CargaTablaEndpoint : IApiEndpoint
    {
        public string Method => "POST";
        public string Path => "/cargatabla";

        private readonly OleDbAccdbService _db;
        private readonly IUiNotifier _ui;

        public CargaTablaEndpoint(OleDbAccdbService db, IUiNotifier ui)
        {
            _db = db;
            _ui = ui;
        }

        private sealed class Body { public string? Tabla { get; set; } }

        public async Task HandleAsync(ApiContext ctx, CancellationToken ct)
        {
            var raw = await ctx.ReadBodyAsync().ConfigureAwait(false);

            // debug UI (como antes)
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                _ui.ShowInfo("POST CargaTabla", pretty.Length > 4000 ? pretty[..4000] + "\n[...]" : pretty);
            }
            catch
            {
                _ui.ShowInfo("POST CargaTabla", string.IsNullOrWhiteSpace(raw) ? "(cuerpo vacío)" : raw);
            }

            Body? body;
            try
            {
                body = JsonSerializer.Deserialize<Body>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                await ctx.WriteJsonAsync(new { ok = false, error = "INVALID_JSON: " + ex.Message }, 400);
                return;
            }

            if (body is null || string.IsNullOrWhiteSpace(body.Tabla))
            {
                await ctx.WriteJsonAsync(new { ok = false, error = "El campo 'tabla' es obligatorio." }, 400);
                return;
            }

            try
            {
                var rows = await _db.SelectAllAsync(body.Tabla, ct).ConfigureAwait(false);
                await ctx.WriteJsonAsync(new { ok = true, data = new { items = rows, total = rows.Count } });
            }
            catch (Exception ex)
            {
                await ctx.WriteJsonAsync(new { ok = false, error = ex.Message }, 500);
            }
        }
    }
}
