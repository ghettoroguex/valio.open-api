using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Valibot.Ast.Builders;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step that generates Valibot schemas for IR models and enums.
/// </summary>
public sealed class ValibotSchemasStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly IReadOnlyList<ITypeScriptNodeRenderer> _nodeRenderers;

    public string Name => "typescript-valibot-schemas";
    public int Order => 30;

    public ValibotSchemasStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public ValibotSchemasStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter;
        _nodeRenderers = nodeRenderers as IReadOnlyList<ITypeScriptNodeRenderer> ?? nodeRenderers.ToArray();
    }

    public bool ShouldExecute(GenerationContext context)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return opts?.ValidationLibrary == "valibot";
    }

    /// <inheritdoc />
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var fileOrg = context.Configuration.FileOrganization;
        var modelsPath = fileOrg.ModelsPath ?? "models";
        var documentContext = context.DocumentContext;
        var builder = new ValibotSchemaSourceFileBuilder(documentContext, _namingConverter, modelsPath, opts?.UuidFormat);
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
        ValibotSchemaSourceFileBuilder builder,
        TypeScriptAstRenderer renderer,
        ValIO.Core.IR.IrModel model,
        string modelsPath,
        bool separateModels)
    {
        var content = renderer.Render(builder.BuildModelSchemaSourceFile(model, separateModels));
        var fileName = _namingConverter.ToCamelCase(model.OriginalName);
        // Co-locate the schema with its model under modelsPath (not the separate schemas/ tree).
        var relativePath = separateModels
            ? $"{modelsPath}/{fileName}/{fileName}.schema.ts"
            : $"{modelsPath}/{fileName}.schema.ts";

        context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
    }

    private void AddEnumSchema(
        GenerationContext context,
        ValibotSchemaSourceFileBuilder builder,
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
