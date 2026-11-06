using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Options;

namespace ValIO.Generators.CSharp.Steps;

/// <summary>
/// Step: генерирует C# enum из IR enums.
/// </summary>
public sealed class CSharpEnumsStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly ICSharpAstRenderer _renderer;

    public string Name => "csharp-enums";
    public int Order => 20;

    public CSharpEnumsStep(INamingConverter namingConverter, ICSharpAstRenderer renderer)
    {
        _namingConverter = namingConverter;
        _renderer = renderer;
    }

    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateEnums && context.IrDocument.Enums.Count > 0;

    /// <inheritdoc/>
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as CSharpGenerationOptions;
        var ns = opts?.Namespace ?? context.Configuration.Namespace ?? "Generated";
        var builder = new CSharpAstBuilder(_namingConverter, context.DocumentContext, opts);

        foreach (var irEnum in context.IrDocument.Enums)
        {
            var compilationUnit = builder.BuildCompilationUnit(irEnum, ns);
            var content = _renderer.Render(compilationUnit);
            var relativePath = $"Models/{irEnum.OriginalName}.cs";
            context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
        }

        return Task.CompletedTask;
    }
}
