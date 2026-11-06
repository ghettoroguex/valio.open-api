using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.TanStackQuery.Ast.Builders;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: генерирует TanStack Query хуки для всех операций.
/// Активируется при opts.QueryWrapper содержит "tanstack".
/// </summary>
public sealed class TanStackQueryStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-tanstack-query";
    public int Order => 50;

    public TanStackQueryStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TanStackQueryStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter;
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
    {
        if (!context.Configuration.GenerateClients) return false;
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return IsTanStackReact(opts?.QueryWrapper);
    }

    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var fileOrg = context.Configuration.FileOrganization;
        var clientsPath = fileOrg.ClientsPath ?? "api";
        var clientSuffix = TypeScriptClientTransport.ResolveHookClientSuffix(opts);

        var astBuilder = new TypeScriptAstBuilder(context.DocumentContext, _namingConverter, opts);
        var hookBuilder = new TanStackQueryHookAstBuilder(
            context.DocumentContext,
            _namingConverter,
            astBuilder.BuildType,
            clientSuffix,
            opts?.ResultTypeSuffix,
            opts?.ResultTypePrefix);

        foreach (var client in context.IrDocument.Clients)
        {
            foreach (var operation in client.Operations)
            {
                ct.ThrowIfCancellationRequested();

                var isMutation = operation.HttpMethod is "POST" or "PUT" or "PATCH" or "DELETE";
                var fileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
                var sourceFile = hookBuilder.BuildHookSourceFile(operation, fileName, isMutation);
                var content = _renderer.Render(sourceFile);
                var relativePath = $"{clientsPath}/{fileName}/{fileName}.query.ts";
                context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
            }
        }

        return Task.CompletedTask;
    }

    private static bool IsTanStackReact(string? queryWrapper) =>
        queryWrapper?.Equals("tanstack", StringComparison.OrdinalIgnoreCase) == true
        || queryWrapper?.Equals(TypeScriptOptionIds.QueryWrapper.TanStackReact, StringComparison.OrdinalIgnoreCase) == true
        || queryWrapper?.Equals("react-query", StringComparison.OrdinalIgnoreCase) == true;
}
