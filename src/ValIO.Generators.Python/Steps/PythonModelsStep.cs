using ValIO.Core.Interfaces;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Plugins;
using ValIO.Generators.Python.Ast.Builders;
using ValIO.Generators.Python.Ast.Nodes;
using ValIO.Generators.Python.Ast.Renderers;

namespace ValIO.Generators.Python.Steps;

/// <summary>
/// Generates one Pydantic module for each IR model.
/// </summary>
public sealed class PythonModelsStep : ICodeGenerationStep
{
    /// <summary>Converts source identifiers into the target language naming conventions.</summary>
    private readonly INamingConverter _namingConverter;

    /// <summary>Renders language-specific model ASTs into source text.</summary>
    private readonly IPythonAstRenderer _renderer;

    /// <summary>Maps shared IR property types into target language types.</summary>
    private readonly PythonIrTypeMapper _typeMapper;

    /// <summary>Gets the stable identifier used to select this model generation step.</summary>
    /// <value>The language-specific model step identifier.</value>
    public string Name => "python-models";

    /// <summary>Gets the execution order placing models before dependent client files.</summary>
    /// <value>The model generation pipeline order.</value>
    public int Order => 10;

    /// <summary>Initializes model generation with the configured language services.</summary>
    /// <param name="namingConverter">The converter for generated identifiers and file names.</param>
    /// <param name="renderer">The renderer for model ASTs.</param>
    /// <param name="typeMapper">The shared IR to target language type mapper.</param>
    public PythonModelsStep(
        INamingConverter namingConverter,
        IPythonAstRenderer renderer,
        PythonIrTypeMapper typeMapper)
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
        var documentContext = context.DocumentContext;
        var builder = new PydanticModelAstBuilder(documentContext, _namingConverter, _typeMapper);
        foreach (var model in documentContext.ModelsInDependencyOrder)
            AddModelFile(context, builder, model, documentContext.ModelsByName);

        var imports = documentContext.ModelsInDependencyOrder
            .Select(model => new PythonImportNode("." + _namingConverter.ToSnakeCase(model.OriginalName), new[] { model.Name }))
            .ToList();
        if (context.Configuration.GenerateEnums)
        {
            imports.AddRange(context.IrDocument.Enums.Select(
                enumModel => new PythonImportNode("." + _namingConverter.ToSnakeCase(enumModel.OriginalName), new[] { enumModel.Name })));
        }
        var package = new PythonModuleNode(
            imports: imports,
            trailingStatements: documentContext.ModelsInDependencyOrder
                .Select(model => $"{model.Name}.model_rebuild(_types_namespace=globals())")
                .ToArray());
        context.GeneratedFiles.Add(new GeneratedFile("models/__init__.py", _renderer.Render(package)));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Renders a flattened generation view without modifying the source IR model or its composition.
    /// </summary>
    /// <param name="context">The output collection receiving the generated file.</param>
    /// <param name="builder">The language-specific model AST builder.</param>
    /// <param name="model">The source model whose declared properties remain unchanged.</param>
    /// <param name="modelLookup">All models available to resolve transitive allOf bases.</param>
    private void AddModelFile(
        GenerationContext context,
        PydanticModelAstBuilder builder,
        ValIO.Core.IR.IrModel model,
        IReadOnlyDictionary<string, ValIO.Core.IR.IrModel> modelLookup)
    {
        var effectiveModel = model with { Properties = IrInheritancePlan.FlattenProperties(model, modelLookup) };
        var module = builder.BuildModule(effectiveModel);
        var content = _renderer.Render(module);
        var fileName = _namingConverter.ToSnakeCase(model.OriginalName);
        var relativePath = $"models/{fileName}.py";
        context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
    }
}
