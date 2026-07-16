using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Abstractions;

/// <summary>
/// Bir bağlamın (bounded context) modül giriş noktası.
/// Host, tüm IModule'leri yansıma ile bulup Register + MapEndpoints çağırır.
/// </summary>
public interface IModule
{
    /// <summary>Modülün adı (loglama/teşhis için).</summary>
    string Name { get; }

    /// <summary>DI kayıtları (servisler, DbContext, repo'lar).</summary>
    void Register(IServiceCollection services, IConfiguration configuration);

    /// <summary>Minimal API endpoint eşlemesi (Api yüzeyi olmayan modül boş bırakır).</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
