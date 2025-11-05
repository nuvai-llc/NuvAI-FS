using System;

namespace NuvAI_FS.Src.Api.Abstractions
{
    public interface IApiEndpoint
    {
        string Method { get; }      // "GET" | "POST"
        string Path { get; }        // "/cargatabla, /etc" (en minúsculas, sin / final)
        Task HandleAsync(ApiContext ctx, CancellationToken ct);
    }
}
