using System.Net;

namespace TapFun.Tests;

/// <summary>Records the requests instead of sending them, and answers with <see cref="Respond"/>.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    public sealed record Part(string? Name, string? FileName, string? ContentType, byte[] Bytes);

    public sealed record Captured(HttpMethod Method, Uri Uri, string? Authorization, string UserAgent, string? ContentType, string Body, List<Part> Parts);

    public List<Captured> Requests { get; } = new();

    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"result\":\"success\"}") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var parts = new List<Part>();
        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (HttpContent part in multipart)
            {
                parts.Add(new Part(part.Headers.ContentDisposition?.Name, part.Headers.ContentDisposition?.FileName,
                    part.Headers.ContentType?.MediaType, await part.ReadAsByteArrayAsync(cancellationToken)));
            }
        }
        Requests.Add(new Captured(
            request.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            string.Join(" ", request.Headers.GetValues("User-Agent")),
            request.Content?.Headers.ContentType?.MediaType,
            request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken),
            parts));
        return Respond(request);
    }
}
