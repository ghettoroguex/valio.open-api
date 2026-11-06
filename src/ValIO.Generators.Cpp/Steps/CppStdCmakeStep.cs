using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Runtime;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Emits independent model, serializer, and HTTP client build targets for standalone output.
/// </summary>
public sealed class CppStdCmakeStep(ICppAstRenderer renderer) : CppStdStepBase(renderer)
{
    public override string Name => "cpp-std-cmake";
    public override int Order => 90;

    /// <inheritdoc />
    public override bool ShouldExecute(GenerationContext context)
        => base.ShouldExecute(context)
           && EffectiveOptions(context).EmitCmake
           && ((context.Configuration.GenerateModels && context.IrDocument.Models.Count > 0)
               || (context.Configuration.GenerateEnums && context.IrDocument.Enums.Count > 0)
               || (context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0));

    public override Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var options = EffectiveOptions(context);
        var content = StdRuntimeTemplates.CmakeLists(options.CxxStandard, ResolveRootNamespace(context), options.TemporalBackend, options.UuidBackend);
        context.GeneratedFiles.Add(new GeneratedFile(StdRuntimeTemplates.CmakeListsFileName, content));
        return Task.CompletedTask;
    }
}
