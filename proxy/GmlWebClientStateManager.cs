using Microsoft.Extensions.Caching.Memory;
using Yarp.ReverseProxy.Configuration;

namespace Gml.Web.Proxy;

public class GmlWebClientStateManager(IProxyConfigProvider proxyConfigProvider, IMemoryCache cache)
{
    private const string CacheKey = "GmlWebClientStateManager:IsInstalled";

    public async Task<bool> CheckInstalled()
    {
        try
        {
            if (cache.TryGetValue(CacheKey, out bool isInstalled))
                return isInstalled;

            var backend = proxyConfigProvider.GetConfig();
            var cluster = backend.Clusters.First(item => item.ClusterId == "backend");

            using var client = new HttpClient
            {
                BaseAddress = new Uri(cluster.Destinations!["backend/d1"].Address)
            };

            var response = await client.GetAsync("/api/v1/settings/checkInstalled");
            isInstalled = !response.IsSuccessStatusCode;
            cache.Set(CacheKey, isInstalled, TimeSpan.FromSeconds(10));
            return isInstalled;
        }
        catch (Exception exception)
        {
            Console.WriteLine(exception);
            return false;
        }
    }
}
