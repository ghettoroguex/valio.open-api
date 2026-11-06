using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Options;

namespace ValIO.Generators.CSharp.Steps;

/// <summary>
/// Generates C# model files in the document context's canonical dependency order.
/// </summary>
public sealed class CSharpModelsStep : ICodeGenerationStep
{
    /// <summary>Converts schema identifiers to C# names.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Renders this pipeline's declarations without sharing buffers with other pipelines.</summary>
    private readonly ICSharpAstRenderer _renderer;

    /// <inheritdoc />
    public string Name => "csharp-models";
    /// <inheritdoc />
    public int Order => 10;

    /// <summary>Initializes model generation with pipeline-owned services.</summary>
    /// <param name="namingConverter">The C# identifier converter.</param>
    /// <param name="renderer">The renderer owned by this pipeline.</param>
    public CSharpModelsStep(INamingConverter namingConverter, ICSharpAstRenderer renderer)
    {
        _namingConverter = namingConverter;
        _renderer = renderer;
    }

    /// <inheritdoc />
    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateModels && context.IrDocument.Models.Count > 0;

    /// <inheritdoc/>
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as CSharpGenerationOptions;
        var ns = opts?.Namespace ?? context.Configuration.Namespace ?? "Generated";
        var builder = new CSharpAstBuilder(_namingConverter, context.DocumentContext, opts);

        foreach (var model in context.DocumentContext.ModelsInDependencyOrder)
            AddModelFile(context, builder, model, ns);

        return Task.CompletedTask;
    }

    /// <summary>Renders one model into the request-owned output collection.</summary>
    /// <param name="context">The request receiving generated files.</param>
    /// <param name="builder">The builder sharing the document context.</param>
    /// <param name="model">The model to render.</param>
    /// <param name="ns">The generated namespace.</param>
    private void AddModelFile(GenerationContext context, CSharpAstBuilder builder,
        ValIO.Core.IR.IrModel model, string ns)
    {
        var compilationUnit = builder.BuildCompilationUnit(model, ns);
        var content = _renderer.Render(compilationUnit);
        var relativePath = $"Models/{model.OriginalName}.cs";
        context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
    }
}
