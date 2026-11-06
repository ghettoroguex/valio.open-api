using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Options;
using ValIO.Generators.CSharp.Steps;

namespace ValIO.Generators.CSharp;

/// <summary>
/// Creates C# generation pipelines with independently owned renderers and shared request indexes.
/// </summary>
public sealed class CSharpGeneratorPlugin : IGeneratorPlugin
{
    /// <inheritdoc />
    public string LanguageId => "csharp";
    /// <inheritdoc />
    public string DisplayName => "C#";
    /// <inheritdoc />
    public IReadOnlyList<string> LanguageAliases => ["cs", "csharp", "c#"];
    /// <inheritdoc />
    public IGeneratorOptionsDescriptor OptionsDescriptor { get; } = new CSharpOptionsDescriptor();

    /// <inheritdoc />
    public ICodeGenerationPipeline CreatePipeline(IServiceProvider services)
    {
        var namingConverter = services.GetRequiredService<INamingConverter>();
        var renderer = services.GetRequiredService<ICSharpAstRenderer>();
        var steps = new ICodeGenerationStep[]
        {
            new CSharpModelsStep(namingConverter, renderer),
            new CSharpEnumsStep(namingConverter, renderer),
            new CSharpClientStep(namingConverter, renderer),
        };
        return new CodeGenerationPipeline(steps);
    }

    /// <inheritdoc />
    public void ConfigureServices(IServiceCollection services)
    {
        services.TryAddTransient<ICSharpAstRenderer, CSharpAstRenderer>();
    }
}
