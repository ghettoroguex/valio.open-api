using ValIO.Core.Interfaces;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Plugins;
using ValIO.Generators.Rust.Ast.Builders;
using ValIO.Generators.Rust.Ast.Renderers;
using ValIO.Generators.Rust.Options;

namespace ValIO.Generators.Rust.Steps;

/// <summary>
/// Generates one Rust file per IR model under <c>{crate}/src/models/{snake}.rs</c>.
/// </summary>
public sealed class RustModelsStep : ICodeGenerationStep
{
    /// <summary>Converts source identifiers into the target language naming conventions.</summary>
    private readonly INamingConverter _namingConverter;

    /// <summary>Renders language-specific model ASTs into source text.</summary>
    private readonly IRustAstRenderer _renderer;

    /// <summary>Maps shared IR property types into target language types.</summary>
    private readonly RustIrTypeMapper _typeMapper;

    /// <summary>Gets the stable identifier used to select this model generation step.</summary>
    /// <value>The language-specific model step identifier.</value>
    public string Name => "rust-models";

    /// <summary>Gets the execution order placing models before dependent client files.</summary>
    /// <value>The model generation pipeline order.</value>
    public int Order => 10;

    /// <summary>Initializes model generation with the configured language services.</summary>
    /// <param name="namingConverter">The converter for generated identifiers and file names.</param>
    /// <param name="renderer">The renderer for model ASTs.</param>
    /// <param name="typeMapper">The shared IR to target language type mapper.</param>
    public RustModelsStep(
        INamingConverter namingConverter,
        IRustAstRenderer renderer,
        RustIrTypeMapper typeMapper)
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
        var opts = context.PluginOptions as RustGenerationOptions ?? new RustGenerationOptions();
        var modelsDir = RustStepHelper.ModelsDir(opts);

        var builder = new RustModelAstBuilder(_namingConverter, _typeMapper);
        var modelLookup = context.DocumentContext.ModelsByName;
        foreach (var model in context.IrDocument.Models)
        {
            var effectiveModel = model with { Properties = IrInheritancePlan.FlattenProperties(model, modelLookup) };
            var file = builder.BuildFile(effectiveModel);
            var content = _renderer.Render(file);
            var snake = _namingConverter.ToSnakeCase(model.OriginalName);
            var relativePath = $"{modelsDir}/{snake}.rs";
            context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
        }

        return Task.CompletedTask;
    }
}
