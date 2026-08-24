namespace Gml.Web.Proxy;

public sealed class PublicOriginRewriteMiddleware(RequestDelegate next)
{
    private static readonly string[] LocalOrigins =
    [
        "http://localhost:8081",
        "http://127.0.0.1:8081",
        "http://0.0.0.0:8081",
        "https://localhost:8081",
        "https://127.0.0.1:8081",
        "http://localhost:8080",
        "http://127.0.0.1:8080",
        "http://[::1]:8081"
    ];

    private static readonly string[] HeadersToRewrite =
    [
        "Location",
        "Refresh",
        "x-middleware-redirect",
        "x-nextjs-redirect",
        "x-action-redirect"
    ];

    public async Task InvokeAsync(HttpContext context)
    {
        var publicOrigin = ResolvePublicOrigin();
        if (publicOrigin is not null)
        {
            context.Response.OnStarting(() =>
            {
                RewriteHeaders(context.Response.Headers, publicOrigin);
                return Task.CompletedTask;
            });
        }

        await next(context);
    }

    private static string? ResolvePublicOrigin()
    {
        var host = Environment.GetEnvironmentVariable("PUBLIC_PANEL_HOST")
                   ?? Environment.GetEnvironmentVariable("AUTH_URL")
                   ?? Environment.GetEnvironmentVariable("NEXTAUTH_URL")
                   ?? string.Empty;

        host = host.Trim();
        if (host.Length == 0)
            return null;

        host = host.Replace("https://", "", StringComparison.OrdinalIgnoreCase)
            .Replace("http://", "", StringComparison.OrdinalIgnoreCase)
            .Trim()
            .Trim('/');

        if (host.Length == 0 ||
            host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.StartsWith("127.", StringComparison.Ordinal) ||
            host.StartsWith("0.0.0.0", StringComparison.Ordinal))
            return null;

        return "https://" + host;
    }

    private static void RewriteHeaders(IHeaderDictionary headers, string publicOrigin)
    {
        foreach (var name in HeadersToRewrite)
        {
            if (!headers.TryGetValue(name, out var value) || string.IsNullOrEmpty(value))
                continue;

            var current = value.ToString();
            var rewritten = Rewrite(current, publicOrigin);
            if (!string.Equals(current, rewritten, StringComparison.Ordinal))
                headers[name] = rewritten;
        }
    }

    internal static string Rewrite(string value, string publicOrigin)
    {
        var result = value;
        foreach (var origin in LocalOrigins)
        {
            result = result.Replace(origin, publicOrigin, StringComparison.OrdinalIgnoreCase);
            result = result.Replace(
                Uri.EscapeDataString(origin),
                Uri.EscapeDataString(publicOrigin),
                StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
