using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Angular.Ast.Builders;
using ValIO.Generators.TypeScript.Angular.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Emits Angular observable and promise HttpClient services per <c>IrClient</c>
/// (grouped by tag), with one method per operation returning
/// <c>Observable&lt;{Op}Result&gt;</c>. Unlike per-operation transports
/// (axios/ky/fetch/etc.), Angular's convention is one injectable service
/// class per tag/resource — so this step writes a single
/// <c>{ServiceName}.service.ts</c> file per IrClient.
/// <para/>
/// Activated for <see cref="TypeScriptOptionIds.Transport.AngularRxjs"/> or
/// <see cref="TypeScriptOptionIds.Transport.AngularPromise"/>. Promise services use a distinct class and file
/// basename so both transports can be selected together.
/// </summary>
public sealed class AngularRxjsClientStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    /// <summary>The renderer with configured validation-node support.</summary>
    private readonly AngularSourceFileRenderer _renderer;

    public string Name => "typescript-angular-rxjs-client";
    public int Order => 46;

    /// <summary>Creates the shared Angular observable and promise generation step.</summary>
    /// <param name="namingConverter">The generated identifier policy.</param>
    /// <param name="nodeRenderers">The optional validation AST renderer chain.</param>
    /// <exception cref="ArgumentNullException">The naming converter is null.</exception>
    public AngularRxjsClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer>? nodeRenderers = null)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _renderer = new AngularSourceFileRenderer(nodeRenderers);
    }

    /// <inheritdoc />
    public bool ShouldExecute(GenerationContext context)
    {
        if (!context.Configuration.GenerateClients) return false;
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return opts?.Transports?.Any(transport =>
            string.Equals(transport, TypeScriptOptionIds.Transport.AngularRxjs, StringComparison.OrdinalIgnoreCase)
            || string.Equals(transport, TypeScriptOptionIds.Transport.AngularPromise, StringComparison.OrdinalIgnoreCase)) == true;
    }

    /// <inheritdoc />
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var fileOrg = context.Configuration.FileOrganization;
        var clientsPath = fileOrg.ClientsPath ?? "api";

        foreach (var promiseMode in new[] { false, true })
        {
            var transport = promiseMode ? TypeScriptOptionIds.Transport.AngularPromise : TypeScriptOptionIds.Transport.AngularRxjs;
            if (opts?.Transports?.Contains(transport, StringComparer.OrdinalIgnoreCase) != true)
                continue;
            var builder = new AngularServiceAstBuilder(context.DocumentContext, _namingConverter, opts, promiseMode);
            foreach (var client in context.IrDocument.Clients)
            {
                ct.ThrowIfCancellationRequested();
                var sourceFile = builder.BuildServiceSourceFile(client, fileOrg, opts);
                var content = _renderer.Render(sourceFile);
                var relativePath = $"{clientsPath}/{builder.ServiceFileName(client.Name)}.service.ts";
                context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
            }
        }

        return Task.CompletedTask;
    }
}
