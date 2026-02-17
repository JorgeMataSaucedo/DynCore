using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DynCore.Core;

public static class DynCoreExtensions
{
    /// <summary>
    /// Registra DynCore con opciones por defecto.
    ///
    ///   builder.Services.AddDynCore("Commands");
    /// </summary>
    public static IServiceCollection AddDynCore(this IServiceCollection services, string commandsPath)
        => services.AddDynCore(opt => opt.CommandsPath = commandsPath);

    /// <summary>
    /// Registra DynCore con opciones personalizadas.
    ///
    ///   builder.Services.AddDynCore(opt => {
    ///       opt.CommandsPath = "Commands";
    ///       opt.ErrorColumn = "Error";        // Tu convención, no la de WCS
    ///       opt.MessageColumn = "Mensaje";
    ///       opt.EnableHotReload = true;
    ///   });
    /// </summary>
    public static IServiceCollection AddDynCore(this IServiceCollection services, Action<DynCoreOptions> configure)
    {
        var options = new DynCoreOptions();
        configure(options);

        // Options: Singleton
        services.AddSingleton(options);

        // Registry: Singleton - carga JSONs una vez al iniciar
        services.AddSingleton(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<DynRegistry>>();
            var registry = new DynRegistry(logger);
            registry.LoadFromDirectory(options.CommandsPath, options.EnableHotReload);
            return registry;
        });

        // Cache: Singleton
        services.AddMemoryCache();

        // Context: Scoped - uno por sesión/request
        services.AddScoped<DynContext>();

        // Engine: Scoped
        services.AddScoped<IDynEngine, DynEngine>();

        return services;
    }
}
