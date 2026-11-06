using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Options;
using ValIO.Generators.Cpp.Steps;

namespace ValIO.Generators.Cpp;

/// <summary>
/// Registers the Unreal Engine and standalone C++ generation profiles in one pipeline.
/// </summary>
public sealed class CppGeneratorPlugin : IGeneratorPlugin
{
    /// <inheritdoc />
    public string LanguageId => "cpp";
    /// <inheritdoc />
    public string DisplayName => "C++";
    /// <inheritdoc />
    public IReadOnlyList<string> LanguageAliases => ["cpp", "c++", "cpp-ue", "cpp-std", "ue", "unreal"];
    /// <inheritdoc />
    public IGeneratorOptionsDescriptor OptionsDescriptor { get; } = new CppOptionsDescriptor();

    /// <inheritdoc />
    public ICodeGenerationPipeline CreatePipeline(IServiceProvider services)
    {
        var renderer = services.GetRequiredService<ICppAstRenderer>();
        var steps = new ICodeGenerationStep[]
        {
            // Each step selects its runtime profile through ShouldExecute.
            new CppUeModelsStep(renderer),
            new CppUeEnumsStep(renderer),
            new CppUeEndpointsStep(renderer),

            new CppStdEnumsStep(renderer),
            new CppStdModelsStep(renderer),
            new CppStdSerializersStep(renderer),
            new CppStdRuntimeStep(renderer),
            new CppStdClientStep(renderer),
            new CppStdCmakeStep(renderer),
        };
        return new CodeGenerationPipeline(steps);
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddTransient<ICppAstRenderer, CppAstRenderer>();
    }
}
