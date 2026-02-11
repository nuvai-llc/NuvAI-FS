// Src\Api\Abstractions\ApiContext.cs
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NuvAI_FS.Src.Api.Abstractions
{
    public sealed class ApiContext
    {
        public HttpListenerRequest Request { get; }
        public HttpListenerResponse Response { get; }

        public ApiContext(HttpListenerContext raw)
        {
            Request = raw.Request;
            Response = raw.Response;
        }

        public async Task<string> ReadBodyAsync()
        {
            using var sr = new StreamReader(Request.InputStream, Request.ContentEncoding);
            return await sr.ReadToEndAsync().ConfigureAwait(false);
        }

        public static void WriteCors(HttpListenerResponse resp)
        {
            resp.Headers["Access-Control-Allow-Origin"] = "*";
            resp.Headers["Access-Control-Allow-Methods"] = "GET,POST,OPTIONS";
            resp.Headers["Access-Control-Allow-Headers"] = "Content-Type,Authorization";
        }

        public async Task WriteJsonAsync(object payload, int statusCode = 200)
        {
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var bytes = Encoding.UTF8.GetBytes(json);
            Response.ContentType = "application/json; charset=utf-8";
            Response.StatusCode = statusCode;
            WriteCors(Response);
            Response.ContentLength64 = bytes.Length;
            await Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            Response.OutputStream.Close();
        }
    }
}
