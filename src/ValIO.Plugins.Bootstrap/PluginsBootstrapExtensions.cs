using Microsoft.Extensions.DependencyInjection;
using ValIO.Application.DependencyInjection;
using ValIO.Generators.Cpp;
using ValIO.Generators.CSharp;
using ValIO.Generators.Dart;
using ValIO.Generators.FSharp;
using ValIO.Generators.Java;
using ValIO.Generators.Kotlin;
using ValIO.Generators.Php;
using ValIO.Generators.Python;
using ValIO.Generators.Ruby;
using ValIO.Generators.Rust;
using ValIO.Generators.Swift;
using ValIO.Generators.TypeScript;

namespace ValIO.Plugins.Bootstrap;

/// <summary>
/// Single helper used by every front-end to register the full set of generator plugins
/// without duplicating the list in three places (CLI / Desktop / Web).
/// </summary>
public static class PluginsBootstrapExtensions
{
    /// <summary>Registers every supported language plugin and its services once per service collection.</summary>
    /// <param name="services">The application services being composed.</param>
    /// <returns>The supplied service collection for chaining.</returns>
    /// <remarks>Repeated calls preserve existing registrations and do not configure plugin services again.</remarks>
    public static IServiceCollection AddAllGeneratorPlugins(this IServiceCollection services)
    {
        services.AddGeneratorPlugin<CppGeneratorPlugin>();
        services.AddGeneratorPlugin<TypeScriptGeneratorPlugin>();
        services.AddGeneratorPlugin<CSharpGeneratorPlugin>();
        services.AddGeneratorPlugin<PythonGeneratorPlugin>();
        services.AddGeneratorPlugin<PhpGeneratorPlugin>();
        services.AddGeneratorPlugin<JavaGeneratorPlugin>();
        services.AddGeneratorPlugin<KotlinGeneratorPlugin>();
        services.AddGeneratorPlugin<SwiftGeneratorPlugin>();
        services.AddGeneratorPlugin<DartGeneratorPlugin>();
        services.AddGeneratorPlugin<FSharpGeneratorPlugin>();
        services.AddGeneratorPlugin<RubyGeneratorPlugin>();
        services.AddGeneratorPlugin<RustGeneratorPlugin>();
        return services;
    }
}
