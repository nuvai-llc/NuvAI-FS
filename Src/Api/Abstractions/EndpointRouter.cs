namespace NuvAI_FS.Src.Api.Abstractions
{
    public sealed class EndpointRouter
    {
        private readonly Dictionary<(string method, string path), IApiEndpoint> _map;

        public EndpointRouter(IEnumerable<IApiEndpoint> endpoints)
        {
            _map = endpoints.ToDictionary(
                e => (e.Method.ToUpperInvariant(), Normalize(e.Path)),
                e => e);
        }

        private static string Normalize(string p)
        {
            var s = (p ?? "/").Trim().ToLowerInvariant();
            if (s.Length > 1 && s.EndsWith('/')) s = s[..^1];
            return s;
        }

        public IApiEndpoint? Resolve(string method, string path)
        {
            var key = (method.ToUpperInvariant(), Normalize(path));
            return _map.TryGetValue(key, out var ep) ? ep : null;
        }
    }
}
