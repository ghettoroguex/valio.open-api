using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Модели UE-профиля: Public/Models/{Model}.h (USTRUCT) для каждой IrModel.
/// </summary>
public sealed class CppUeModelsStep(ICppAstRenderer renderer) : CppUeStepBase(renderer)
{
    /// <inheritdoc />
    public override string Name => "cpp-ue-models";
    /// <inheritdoc />
    public override int Order => 10;

    /// <inheritdoc />
    public override bool ShouldExecute(GenerationContext context)
        => base.ShouldExecute(context)
           && context.Configuration.GenerateModels
           && context.IrDocument.Models.Count > 0;

    /// <inheritdoc />
    public override Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var builder = CreateBuilder(context);
        var naming = new UENamingConvention();

        foreach (var model in context.IrDocument.Models)
        {
            ct.ThrowIfCancellationRequested();
            var header = builder.BuildModelHeader(model, context.Warnings);
            var content = Renderer.Render(header);
            context.GeneratedFiles.Add(new GeneratedFile(naming.ModelFullPath(model.OriginalName), content));
        }

        return Task.CompletedTask;
    }
}
