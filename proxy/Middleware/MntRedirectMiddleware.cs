using Yarp.ReverseProxy;
using Yarp.ReverseProxy.Model;

namespace Gml.Web.Proxy.Middleware;

public class MntRedirectMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IProxyStateLookup _stateLookup;

    public MntRedirectMiddleware(RequestDelegate next, IProxyStateLookup stateLookup)
    {
        _next = next;
        _stateLookup = stateLookup;
    }

    public async Task InvokeAsync(HttpContext context, GmlWebClientStateManager stateManager)
    {
        if (_stateLookup.TryGetCluster("backend", out var cluster))
        {
            var destination = cluster.Destinations.First().Value;
            if (destination.Health.Active == DestinationHealth.Healthy)
            {
                var path = context.Request.Path;
                var isInstalled = await stateManager.CheckInstalled();

                if (path.HasValue && path.Equals("/") && !isInstalled)
                {
                    context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
                    context.Response.Headers.Location = "/mnt";
                    return;
                }

                if (path.HasValue && path.StartsWithSegments("/mnt") && isInstalled)
                {
                    context.Response.StatusCode = StatusCodes.Status307TemporaryRedirect;
                    context.Response.Headers.Location = "/";
                    return;
                }
            }
        }

        await _next(context);
    }
}
