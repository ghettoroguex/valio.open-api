using ValIO.Core.Interfaces;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Plugins;
using ValIO.Generators.FSharp.Ast.Builders;
using ValIO.Generators.FSharp.Ast.Renderers;
using ValIO.Generators.FSharp.Options;

namespace ValIO.Generators.FSharp.Steps;

/// <summary>
/// Generates one F# file per IR model (record or DU depending on composition).
/// </summary>
public sealed class FSharpModelsStep : ICodeGenerationStep
{
    /// <summary>Converts source identifiers into the target language naming conventions.</summary>
    private readonly INamingConverter _namingConverter;

    /// <summary>Renders language-specific model ASTs into source text.</summary>
    private readonly IFSharpAstRenderer _renderer;

    /// <summary>Maps shared IR property types into target language types.</summary>
    private readonly FSharpIrTypeMapper _typeMapper;

    /// <summary>Gets the stable identifier used to select this model generation step.</summary>
    /// <value>The language-specific model step identifier.</value>
    public string Name => "fsharp-models";

    /// <summary>Gets the execution order placing models before dependent client files.</summary>
    /// <value>The model generation pipeline order.</value>
    public int Order => 10;

    /// <summary>Initializes model generation with the configured language services.</summary>
    /// <param name="namingConverter">The converter for generated identifiers and file names.</param>
    /// <param name="renderer">The renderer for model ASTs.</param>
    /// <param name="typeMapper">The shared IR to target language type mapper.</param>
    public FSharpModelsStep(
        INamingConverter namingConverter,
        IFSharpAstRenderer renderer,
        FSharpIrTypeMapper typeMapper)
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
        var opts = context.PluginOptions as FSharpGenerationOptions ?? new FSharpGenerationOptions();
        var builder = new FSharpModelAstBuilder(_namingConverter, _typeMapper);

        var modelLookup = context.DocumentContext.ModelsByName;
        foreach (var model in context.DocumentContext.ModelsInDependencyOrder)
        {
            // Emitted module/type identifiers keep the affixed name; the file name
            // derives from the pre-affix original so generated paths don't change.
            var typeName = _namingConverter.ToPascalCase(model.Name);
            var fileName = _namingConverter.ToPascalCase(model.OriginalName);
            var moduleName = FSharpStepHelper.ModelModuleName(opts, typeName);
            var effectiveModel = model with { Properties = IrInheritancePlan.FlattenProperties(model, modelLookup) };
            var file = builder.BuildFile(effectiveModel, moduleName);
            var content = _renderer.Render(file);
            var relativePath = $"{FSharpStepHelper.ModelsDir}/{fileName}.fs";
            context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
        }

        return Task.CompletedTask;
    }
}
