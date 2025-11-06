using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using System.IO;
using System.Runtime.Versioning;
using System.Text;

namespace NuvAI_FS.Src.Services
{
    [SupportedOSPlatform("windows")]
    public sealed class OleDbAccdbService
    {
        private readonly string _accdbPath;

        public OleDbAccdbService(string accdbPath)
        {
            if (string.IsNullOrWhiteSpace(accdbPath) || !File.Exists(accdbPath))
                throw new ArgumentException("Ruta ACCDB inválida.", nameof(accdbPath));
            _accdbPath = accdbPath;
        }

        private static string ProviderPrefered => "Microsoft.ACE.OLEDB.16.0";

        private static string SanitizeIdentifier(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "[Unknown]";
            var sb = new StringBuilder(name.Length);
            foreach (var ch in name.Trim())
                if (char.IsLetterOrDigit(ch) || ch == '_') sb.Append(ch);
            if (sb.Length == 0) sb.Append("Unknown");
            return $"[{sb}]";
        }

        public async Task<List<Dictionary<string, object?>>> SelectAllAsync(string table, CancellationToken ct)
        {
            var tableId = SanitizeIdentifier(table);
            var sql = $"SELECT * FROM {tableId};";
            return await ExecuteQueryAsync(sql, ct);
        }

        public async Task<List<Dictionary<string, object?>>> ExecuteQueryAsync(string sql, CancellationToken ct)
        {
            var result = new List<Dictionary<string, object?>>();
            var cs16 = $"Provider={ProviderPrefered};Data Source={_accdbPath};Persist Security Info=False;";

            try
            {
                return await Task.Run(() =>
                {
                    using var cn = new OleDbConnection(cs16);
                    cn.Open();
                    using var cmd = new OleDbCommand(sql, cn) { CommandType = CommandType.Text };
                    using var rd = cmd.ExecuteReader();
                    if (rd is null) return result;

                    var schema = rd.GetColumnSchema();
                    while (rd.Read())
                    {
                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < rd.FieldCount; i++)
                        {
                            var colName = schema?[i].ColumnName ?? rd.GetName(i);
                            row[colName] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                        }
                        result.Add(row);
                    }
                    return result;
                }, ct).ConfigureAwait(false);
            }
            catch (OleDbException) // fallback 12.0
            {
                var cs12 = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={_accdbPath};Persist Security Info=False;";
                return await Task.Run(() =>
                {
                    using var cn = new OleDbConnection(cs12);
                    cn.Open();
                    using var cmd = new OleDbCommand(sql, cn) { CommandType = CommandType.Text };
                    using var rd = cmd.ExecuteReader();
                    if (rd is null) return result;

                    var schema = rd.GetColumnSchema();
                    while (rd.Read())
                    {
                        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        for (int i = 0; i < rd.FieldCount; i++)
                        {
                            var colName = schema?[i].ColumnName ?? rd.GetName(i);
                            row[colName] = rd.IsDBNull(i) ? null : rd.GetValue(i);
                        }
                        result.Add(row);
                    }
                    return result;
                }, ct).ConfigureAwait(false);
            }
        }

        // === NUEVO: ejecuta INSERT/UPDATE/DELETE y devuelve filas afectadas ===
        public async Task<int> ExecuteNonQueryAsync(string sql, CancellationToken ct)
        {
            var cs16 = $"Provider={ProviderPrefered};Data Source={_accdbPath};Persist Security Info=False;";

            try
            {
                return await Task.Run(() =>
                {
                    using var cn = new OleDbConnection(cs16);
                    cn.Open();
                    using var cmd = new OleDbCommand(sql, cn) { CommandType = CommandType.Text };
                    return cmd.ExecuteNonQuery();
                }, ct).ConfigureAwait(false);
            }
            catch (OleDbException) // fallback 12.0
            {
                var cs12 = $"Provider=Microsoft.ACE.OLEDB.12.0;Data Source={_accdbPath};Persist Security Info=False;";
                return await Task.Run(() =>
                {
                    using var cn = new OleDbConnection(cs12);
                    cn.Open();
                    using var cmd = new OleDbCommand(sql, cn) { CommandType = CommandType.Text };
                    return cmd.ExecuteNonQuery();
                }, ct).ConfigureAwait(false);
            }
        }
    }
}
