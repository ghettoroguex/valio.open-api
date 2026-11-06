using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Builders;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Enum'ы UE-профиля: Public/Enums/{Enum}.h (UENUM) для каждого IrEnum.
/// </summary>
public sealed class CppUeEnumsStep(ICppAstRenderer renderer) : CppUeStepBase(renderer)
{
    public override string Name => "cpp-ue-enums";
    public override int Order => 20;

    public override bool ShouldExecute(GenerationContext context)
        => base.ShouldExecute(context)
           && context.Configuration.GenerateEnums
           && context.IrDocument.Enums.Count > 0;

    public override Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var builder = CreateBuilder(context);
        var naming = new UENamingConvention();

        foreach (var irEnum in context.IrDocument.Enums)
        {
            ct.ThrowIfCancellationRequested();
            var header = builder.BuildEnumHeader(irEnum);
            var content = Renderer.Render(header);
            context.GeneratedFiles.Add(new GeneratedFile(naming.EnumFullPath(irEnum.OriginalName), content));
        }

        return Task.CompletedTask;
    }
}
