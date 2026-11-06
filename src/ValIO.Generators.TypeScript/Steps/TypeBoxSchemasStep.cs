using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.TypeBox.Ast.Builders;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>Generates TypeBox schemas for the shared document's models and enums.</summary>
public sealed class TypeBoxSchemasStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly IReadOnlyList<ITypeScriptNodeRenderer> _nodeRenderers;

    public string Name => "typescript-typebox-schemas";
    public int Order => 31;

    public TypeBoxSchemasStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TypeBoxSchemasStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter;
        _nodeRenderers = nodeRenderers as IReadOnlyList<ITypeScriptNodeRenderer> ?? nodeRenderers.ToArray();
    }

    public bool ShouldExecute(GenerationContext context)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return opts?.ValidationLibrary == "typebox";
    }

    /// <inheritdoc />
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var fileOrg = context.Configuration.FileOrganization;
        var modelsPath = fileOrg.ModelsPath ?? "models";
        var documentContext = context.DocumentContext;
        var builder = new TypeBoxSchemaSourceFileBuilder(documentContext, _namingConverter, modelsPath, opts?.UuidFormat);
        var renderer = new TypeScriptAstRenderer(_nodeRenderers);

        foreach (var model in documentContext.ModelsInDependencyOrder)
        {
            ct.ThrowIfCancellationRequested();
            AddModelSchema(context, builder, renderer, model, modelsPath, fileOrg.SeparateModels);
        }

        foreach (var irEnum in documentContext.Document.Enums)
        {
            ct.ThrowIfCancellationRequested();
            AddEnumSchema(context, builder, renderer, irEnum, modelsPath, fileOrg.SeparateModels);
        }

        return Task.CompletedTask;
    }

    private void AddModelSchema(
        GenerationContext context,
        TypeBoxSchemaSourceFileBuilder builder,
        TypeScriptAstRenderer renderer,
        ValIO.Core.IR.IrModel model,
        string modelsPath,
        bool separateModels)
    {
        var fileName = _namingConverter.ToCamelCase(model.OriginalName);
        var content = renderer.Render(builder.BuildModelSchemaSourceFile(
            model,
            currentFileName: fileName,
            schemasPath: modelsPath));

        // Co-locate the schema with its model under modelsPath (not the separate schemas/ tree).
        var relativePath = separateModels
            ? $"{modelsPath}/{fileName}/{fileName}.schema.ts"
            : $"{modelsPath}/{fileName}.schema.ts";

        context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
    }

    private void AddEnumSchema(
        GenerationContext context,
        TypeBoxSchemaSourceFileBuilder builder,
        TypeScriptAstRenderer renderer,
        ValIO.Core.IR.IrEnum irEnum,
        string modelsPath,
        bool separateModels)
    {
        var content = renderer.Render(builder.BuildEnumSchemaSourceFile(irEnum, separateModels));

        var fileName = _namingConverter.ToCamelCase(irEnum.OriginalName);
        // Co-locate the schema with its model under modelsPath (not the separate schemas/ tree).
        var relativePath = separateModels
            ? $"{modelsPath}/{fileName}/{fileName}.schema.ts"
            : $"{modelsPath}/{fileName}.schema.ts";

        context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
    }
}
