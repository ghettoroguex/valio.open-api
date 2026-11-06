using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.TanStackQuery.Angular.Builders;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits TanStack Angular Query hooks (<c>injectQuery</c> /
/// <c>injectMutation</c>) per operation. Activated when
/// <c>opts.QueryWrapper == "tanstack-angular"</c>.
/// </summary>
public sealed class TanStackAngularStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-tanstack-angular";
    public int Order => 51;

    public TanStackAngularStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TanStackAngularStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
    {
        if (!context.Configuration.GenerateClients) return false;
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return opts?.QueryWrapper?.Equals(TypeScriptOptionIds.QueryWrapper.TanStackAngular, StringComparison.OrdinalIgnoreCase) == true;
    }

    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var fileOrg = context.Configuration.FileOrganization;
        var clientsPath = fileOrg.ClientsPath ?? "api";
        var clientSuffix = TypeScriptClientTransport.ResolveHookClientSuffix(opts);

        var astBuilder = new TypeScriptAstBuilder(context.DocumentContext, _namingConverter, opts);
        var hookBuilder = new TanStackAngularHookAstBuilder(context.DocumentContext, _namingConverter, astBuilder.BuildType, opts?.ResultTypeSuffix, opts?.ResultTypePrefix);

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
