using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Plugins;
using ValIO.Generators.Php.Ast.Builders;
using ValIO.Generators.Php.Ast.Renderers;
using ValIO.Generators.Php.Options;

namespace ValIO.Generators.Php.Steps;

/// <summary>
/// Generates PHP DTO classes from IR models.
/// </summary>
public sealed class PhpModelsStep : ICodeGenerationStep
{
    /// <summary>Converts source identifiers into the target language naming conventions.</summary>
    private readonly INamingConverter _namingConverter;

    /// <summary>Renders language-specific model ASTs into source text.</summary>
    private readonly IPhpAstRenderer _renderer;

    /// <summary>Maps shared IR property types into target language types.</summary>
    private readonly PhpIrTypeMapper _typeMapper;

    /// <summary>Gets the stable identifier used to select this model generation step.</summary>
    /// <value>The language-specific model step identifier.</value>
    public string Name => "php-models";

    /// <summary>Gets the execution order placing models before dependent client files.</summary>
    /// <value>The model generation pipeline order.</value>
    public int Order => 10;

    /// <summary>Initializes model generation with the configured language services.</summary>
    /// <param name="namingConverter">The converter for generated identifiers and file names.</param>
    /// <param name="renderer">The renderer for model ASTs.</param>
    /// <param name="typeMapper">The shared IR to target language type mapper.</param>
    public PhpModelsStep(INamingConverter namingConverter, IPhpAstRenderer renderer, PhpIrTypeMapper typeMapper)
    {
        _namingConverter = namingConverter;
        _renderer = renderer;
        _typeMapper = typeMapper;
    }

    /// <summary>Determines whether the document requires model generation.</summary>
    /// <param name="context">The generation configuration and source document.</param>
    /// <returns>Whether models are enabled and at least one IR model exists.</returns>
    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateModels && context.IrDocument.Models.Count > 0;

    /// <summary>
    /// Generates model files with inherited allOf properties resolved against the complete document.
    /// </summary>
    /// <param name="context">The generation state containing all referenced models and output files.</param>
    /// <param name="ct">The cancellation token supplied by the generation pipeline.</param>
    /// <returns>A completed task after the model files have been generated.</returns>
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as PhpGenerationOptions ?? new PhpGenerationOptions();
        var builder = new PhpModelAstBuilder(_namingConverter, _typeMapper);

        var documentContext = context.DocumentContext;
        foreach (var model in documentContext.ModelsInDependencyOrder)
            AddModelFile(context, builder, model, documentContext.ModelsByName, opts.Namespace);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Renders a flattened generation view without modifying the source IR model or its composition.
    /// </summary>
    /// <param name="context">The output collection receiving the generated file.</param>
    /// <param name="builder">The language-specific model AST builder.</param>
    /// <param name="model">The source model whose declared properties remain unchanged.</param>
    /// <param name="modelLookup">All models available to resolve transitive allOf bases.</param>
    /// <param name="namespace">The root namespace containing the generated DTOs.</param>
    private void AddModelFile(
        GenerationContext context,
        PhpModelAstBuilder builder,
        IrModel model,
        IReadOnlyDictionary<string, IrModel> modelLookup,
        string @namespace)
    {
        var effectiveModel = model with { Properties = IrInheritancePlan.FlattenProperties(model, modelLookup) };
        var sourceFile = builder.BuildSourceFile(effectiveModel, @namespace);
        var content = _renderer.Render(sourceFile);
        var relativePath = $"Models/{model.Name}.php";
        context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
    }
}
