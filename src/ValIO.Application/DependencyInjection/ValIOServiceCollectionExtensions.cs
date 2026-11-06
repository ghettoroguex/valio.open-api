using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValIO.Application.Services;
using ValIO.Configuration.Interfaces;
using ValIO.Configuration.Services;
using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Core.Services;
using ValIO.Parsers.Interfaces;
using ValIO.Parsers.Services;

namespace ValIO.Application.DependencyInjection;

/// <summary>
/// One-stop DI extensions used by every front-end (Cli, Web, Desktop).
/// Methods are idempotent — safe to call from any composition root.
/// </summary>
public static class ValIOServiceCollectionExtensions
{
    /// <summary>
    /// Registers core stateless services: naming converter, schema parser, configuration loader/validator,
    /// and the generator registry. Plugins themselves are registered via <c>AddGeneratorPlugin&lt;T&gt;</c>
    /// (typically through <c>ValIO.Plugins.Bootstrap.AddAllGeneratorPlugins()</c>).
    /// </summary>
    /// <param name="services">The application services being composed.</param>
    /// <returns>The supplied service collection for chaining.</returns>
    public static IServiceCollection AddValIOCore(this IServiceCollection services)
    {
        services.TryAddSingleton<INamingConverter, NamingConverter>();
        services.TryAddSingleton<IOpenApiDocumentParser, OpenApiDocumentParser>();
        services.TryAddSingleton<IConfigurationLoader, ConfigurationLoader>();
        services.TryAddSingleton<IConfigurationValidator, ConfigurationValidator>();
        services.TryAddSingleton<IGeneratorRegistry>(sp =>
            new GeneratorRegistry(sp.GetServices<IGeneratorPlugin>()));
        return services;
    }

    /// <summary>Registers the orchestrator (singleton, stateless).</summary>
    /// <param name="services">The application services being composed.</param>
    /// <returns>The supplied service collection for chaining.</returns>
    public static IServiceCollection AddValIOApplication(this IServiceCollection services)
    {
        services.TryAddSingleton<IGenerationOrchestrator, GenerationOrchestrator>();
        return services;
    }

    /// <summary>
    /// Registers a generator plugin. The plugin also registers its own internal services
    /// via <see cref="IGeneratorPlugin.ConfigureServices"/> on its first registration.
    /// </summary>
    /// <typeparam name="TPlugin">The concrete generator plugin implementation.</typeparam>
    /// <param name="services">The application services being composed.</param>
    /// <returns>The supplied service collection for chaining.</returns>
    /// <remarks>A repeated registration of the same implementation type leaves all services unchanged.</remarks>
    public static IServiceCollection AddGeneratorPlugin<TPlugin>(this IServiceCollection services)
        where TPlugin : class, IGeneratorPlugin
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IGeneratorPlugin)
            && descriptor.ImplementationType == typeof(TPlugin)))
        {
            return services;
        }

        services.AddSingleton<IGeneratorPlugin, TPlugin>();
        services.TryAddSingleton<TPlugin>(sp => (TPlugin)sp.GetServices<IGeneratorPlugin>()
            .OfType<TPlugin>().First());
        var instance = Activator.CreateInstance<TPlugin>();
        instance.ConfigureServices(services);
        return services;
    }
}
