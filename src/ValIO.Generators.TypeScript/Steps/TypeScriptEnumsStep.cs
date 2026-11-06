using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: генерирует TypeScript enum-типы из IR enums (union types, values array, type guard).
/// </summary>
public sealed class TypeScriptEnumsStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-enums";
    public int Order => 20;

    public TypeScriptEnumsStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TypeScriptEnumsStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter;
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateEnums && context.IrDocument.Enums.Count > 0;

    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var builder = new TypeScriptAstBuilder(context.DocumentContext, _namingConverter, opts);
        var fileOrg = context.Configuration.FileOrganization;
        var modelsPath = fileOrg.ModelsPath ?? "models";

        foreach (var irEnum in context.IrDocument.Enums)
        {
            var sourceFile = builder.BuildSourceFile(irEnum, fileOrganization: fileOrg);
            var content = _renderer.Render(sourceFile);
            var fileName = _namingConverter.ToCamelCase(irEnum.OriginalName);
            var relativePath = fileOrg.SeparateModels
                ? $"{modelsPath}/{fileName}/{fileName}.ts"
                : $"{modelsPath}/{fileName}.ts";
            context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
        }

        return Task.CompletedTask;
    }
}
