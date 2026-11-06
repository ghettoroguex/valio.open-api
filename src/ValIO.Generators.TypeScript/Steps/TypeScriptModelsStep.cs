using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: генерирует TypeScript interfaces из IR моделей.
/// Итерирует в топологическом порядке для корректных импортов.
/// </summary>
public sealed class TypeScriptModelsStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-models";
    public int Order => 10;

    public TypeScriptModelsStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TypeScriptModelsStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter;
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateModels && context.IrDocument.Models.Count > 0;

    /// <inheritdoc />
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var builder = new TypeScriptAstBuilder(context.DocumentContext, _namingConverter, opts);
        var fileOrg = context.Configuration.FileOrganization;
        var modelsPath = fileOrg.ModelsPath ?? "models";

        foreach (var model in context.DocumentContext.ModelsInDependencyOrder)
        {
            ct.ThrowIfCancellationRequested();
            AddModelFile(context, builder, model, modelsPath);
        }

        return Task.CompletedTask;
    }

    private void AddModelFile(GenerationContext context, TypeScriptAstBuilder builder,
        ValIO.Core.IR.IrModel model, string modelsPath)
    {
        var fileOrg = context.Configuration.FileOrganization;
        var sourceFile = builder.BuildSourceFile(model, fileOrganization: fileOrg);
        var content = _renderer.Render(sourceFile);
        var fileName = _namingConverter.ToCamelCase(model.OriginalName);
        var relativePath = fileOrg.SeparateModels
            ? $"{modelsPath}/{fileName}/{fileName}.ts"
            : $"{modelsPath}/{fileName}.ts";
        context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
    }
}
