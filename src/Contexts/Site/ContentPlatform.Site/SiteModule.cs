using ContentPlatform.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Site;

/// <summary>Site bağlamı — iskelet. Sıralı geliştirmede katmanları (Domain/Application/Infrastructure/Api) eklenecek.</summary>
public sealed class SiteModule : IModule
{
    public string Name => "Site";
    public void Register(IServiceCollection services, IConfiguration configuration) { }
    public void MapEndpoints(IEndpointRouteBuilder endpoints) { }
}
