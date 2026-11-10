#nullable enable

namespace TapFun
{
    /// <summary>Result of a TapFun API call. A 2xx status means success; the body is JSON.</summary>
    public sealed class ApiResult
    {
        /// <summary>Creates a result.</summary>
        public ApiResult(int statusCode, string body, string? error = null)
        {
            StatusCode = statusCode;
            Body = body;
            Error = error;
        }

        /// <summary>HTTP status, or 0 when the request did not reach TapFun (see <see cref="Error"/>).</summary>
        public int StatusCode { get; }

        /// <summary>Response body.</summary>
        public string Body { get; }

        /// <summary>Why the request failed when <see cref="StatusCode"/> is 0, for example a timeout.</summary>
        public string? Error { get; }

        /// <summary><c>true</c> for a 2xx status.</summary>
        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

        /// <summary>For logs: <c>HTTP 200 {...}</c> or <c>error: ...</c>.</summary>
        public override string ToString() => Error != null ? $"error: {Error}" : $"HTTP {StatusCode} {Body}";
    }
}
