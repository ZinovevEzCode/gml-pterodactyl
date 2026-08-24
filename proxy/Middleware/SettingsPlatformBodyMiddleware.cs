using System.Text;

namespace Gml.Web.Proxy.Middleware;

public sealed class SettingsPlatformBodyMiddleware(RequestDelegate next, ILogger<SettingsPlatformBodyMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (HttpMethods.IsPut(context.Request.Method) &&
            context.Request.Path.Equals("/api/v1/settings/platform", StringComparison.OrdinalIgnoreCase))
        {
            await NormalizeBodyAsync(context.Request);
        }

        await next(context);
    }

    private async Task NormalizeBodyAsync(HttpRequest request)
    {
        request.EnableBuffering();

        string json;
        using (var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                   bufferSize: 1024, leaveOpen: true))
        {
            json = await reader.ReadToEndAsync();
        }

        if (string.IsNullOrWhiteSpace(json))
            return;

        if (!SettingsPlatformPayloadNormalizer.TryNormalize(json, out var normalized))
        {
            request.Body.Position = 0;
            return;
        }

        logger.LogInformation("Normalized PUT /api/v1/settings/platform body for GML TimeSpan/enum binding");

        var bytes = Encoding.UTF8.GetBytes(normalized);
        request.Body = new MemoryStream(bytes);
        request.ContentLength = bytes.Length;
        request.ContentType = "application/json; charset=utf-8";
    }
}
