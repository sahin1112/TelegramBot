using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ContentPlatform.Abstractions;

/// <summary>Verilen assembly'lerdeki tüm IModule'leri bulur, örnekler ve kaydeder.</summary>
public static class ModuleRegistrar
{
    public static IReadOnlyList<IModule> Discover(params Assembly[] assemblies)
    {
        var modules = new List<IModule>();
        foreach (var asm in assemblies.Distinct())
        {
            var types = asm.GetTypes().Where(t =>
                typeof(IModule).IsAssignableFrom(t) &&
                !t.IsInterface && !t.IsAbstract);

            foreach (var t in types)
            {
                if (Activator.CreateInstance(t) is IModule m) modules.Add(m);
            }
        }
        return modules;
    }

    public static IReadOnlyList<IModule> RegisterAll(
        IServiceCollection services,
        IConfiguration configuration,
        params Assembly[] assemblies)
    {
        var modules = Discover(assemblies);
        foreach (var m in modules) m.Register(services, configuration);
        services.AddSingleton<IReadOnlyList<IModule>>(modules);
        return modules;
    }

    public static void MapAll(IEndpointRouteBuilder endpoints, IReadOnlyList<IModule> modules)
    {
        foreach (var m in modules) m.MapEndpoints(endpoints);
    }
}
