using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Enum-хедеры std-профиля: Enums/{N}.h — enum class + ToWire/FromWire, без JSON.
/// </summary>
public sealed class CppStdEnumsStep(ICppAstRenderer renderer) : CppStdStepBase(renderer)
{
    public override string Name => "cpp-std-enums";
    public override int Order => 40;

    public override bool ShouldExecute(GenerationContext context)
        => base.ShouldExecute(context)
           && context.Configuration.GenerateEnums
           && context.IrDocument.Enums.Count > 0;

    public override Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var (naming, mapper) = CreateServices(context);
        DrainWarnings(context, mapper);
        var builder = new StdEnumAstBuilder(naming);
        var rootNamespace = ResolveRootNamespace(context);

        foreach (var irEnum in context.IrDocument.Enums)
        {
            ct.ThrowIfCancellationRequested();
            var header = builder.Build(irEnum, rootNamespace);
            context.GeneratedFiles.Add(new GeneratedFile(
                naming.EnumHeaderPath(irEnum.OriginalName), Renderer.Render(header)));
        }

        return Task.CompletedTask;
    }
}
