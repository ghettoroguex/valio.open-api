using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Клиенты std-профиля: clientShape = class | interface+impl (I{Client}.h + {Client}Client);
/// apiStyle = sync | async | both. Тела операций — с именованными локальными
/// переменными (path/httpRequest/body/sendResult/response).
/// </summary>
public sealed class CppStdClientStep(ICppAstRenderer renderer) : CppStdStepBase(renderer)
{
    /// <inheritdoc />
    public override string Name => "cpp-std-clients";
    /// <inheritdoc />
    public override int Order => 80;

    /// <inheritdoc />
    public override bool ShouldExecute(GenerationContext context)
        => base.ShouldExecute(context)
           && context.Configuration.GenerateClients
           && context.IrDocument.Clients.Count > 0;

    /// <inheritdoc />
    public override Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var (naming, mapper) = CreateServices(context);
        var options = EffectiveOptions(context);
        var builder = new StdClientAstBuilder(mapper, naming, context.DocumentContext);
        var rootNamespace = ResolveRootNamespace(context);

        foreach (var client in context.IrDocument.Clients)
        {
            ct.ThrowIfCancellationRequested();
            var build = builder.Build(client, rootNamespace, options, context.Warnings);

            if (build.InterfaceHeader != null)
                context.GeneratedFiles.Add(new GeneratedFile(
                    naming.InterfaceHeaderPath(client.Name), Renderer.Render(build.InterfaceHeader)));
            context.GeneratedFiles.Add(new GeneratedFile(
                naming.ClientHeaderPath(client.Name), Renderer.Render(build.ClientHeader)));
            context.GeneratedFiles.Add(new GeneratedFile(
                naming.ClientSourcePath(client.Name), Renderer.Render(build.ClientSource)));
        }

        DrainWarnings(context, mapper);
        return Task.CompletedTask;
    }
}
