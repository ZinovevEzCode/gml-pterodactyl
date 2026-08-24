using Gml.Web.Proxy;
using Gml.Web.Proxy.Middleware;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<GmlWebClientStateManager>();
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.UseMiddleware<PublicOriginRewriteMiddleware>();
app.MapReverseProxy(pipeline =>
{
    pipeline.UseMiddleware<HealthInfoMiddleware>();
    pipeline.UseMiddleware<MntRedirectMiddleware>();
});
app.Run();
