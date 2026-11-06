using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.TanStackQuery.Svelte.Builders;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits TanStack Svelte Query hooks (<c>createQuery</c> /
/// <c>createMutation</c>) per operation. Activated when
/// <c>opts.QueryWrapper == "tanstack-svelte"</c>.
/// </summary>
public sealed class TanStackSvelteStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-tanstack-svelte";
    public int Order => 53;

    public TanStackSvelteStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TanStackSvelteStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
    {
        if (!context.Configuration.GenerateClients) return false;
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return opts?.QueryWrapper?.Equals(TypeScriptOptionIds.QueryWrapper.TanStackSvelte, StringComparison.OrdinalIgnoreCase) == true;
    }

    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var fileOrg = context.Configuration.FileOrganization;
        var clientsPath = fileOrg.ClientsPath ?? "api";
        var clientSuffix = TypeScriptClientTransport.ResolveHookClientSuffix(opts);

        var astBuilder = new TypeScriptAstBuilder(context.DocumentContext, _namingConverter, opts);
        var hookBuilder = new TanStackSvelteHookAstBuilder(context.DocumentContext, _namingConverter, astBuilder.BuildType, opts?.ResultTypeSuffix, opts?.ResultTypePrefix);

        foreach (var client in context.IrDocument.Clients)
        {
            foreach (var operation in client.Operations)
            {
                ct.ThrowIfCancellationRequested();
                var isMutation = operation.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE";
                var fileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
                var sourceFile = hookBuilder.BuildHookSourceFile(operation, fileName, clientSuffix, isMutation);
                var content = _renderer.Render(sourceFile);
                var relativePath = $"{clientsPath}/{fileName}/{fileName}.query.ts";
                context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
            }
        }

        return Task.CompletedTask;
    }
}
