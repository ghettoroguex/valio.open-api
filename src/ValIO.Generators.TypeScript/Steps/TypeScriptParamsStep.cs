using ValIO.Core.Interfaces;
using ValIO.Core.IR.Types;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: генерирует <c>{operation}.params.ts</c> файлы рядом с клиентом операции.
/// Каждый файл экспортирует интерфейс параметров (плоский, либо агрегатный
/// с под‑интерфейсами Route/Query/Headers).
/// </summary>
public sealed class TypeScriptParamsStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-operation-params";
    public int Order => 15;

    public TypeScriptParamsStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TypeScriptParamsStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0;

    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var astBuilder = new TypeScriptAstBuilder(context.DocumentContext, _namingConverter, opts);
        var paramsBuilder = new TypeScriptParamsAstBuilder(context.DocumentContext, _namingConverter, astBuilder.BuildType);
        var fileOrg = context.Configuration.FileOrganization;
        var clientsPath = fileOrg.ClientsPath ?? "api";

        foreach (var client in context.IrDocument.Clients)
        {
            foreach (var operation in client.Operations)
            {
                ct.ThrowIfCancellationRequested();

                if (!paramsBuilder.HasParameters(operation))
                    continue;

                var fileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
                var sourceFile = paramsBuilder.BuildParamsSourceFile(
                    operation,
                    importPath: null,
                    fileOrganization: fileOrg,
                    currentOperationName: operation.MethodName);
                var content = _renderer.Render(sourceFile);
                var relativePath = $"{clientsPath}/{fileName}/{fileName}.params.ts";
                context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
            }
        }

        return Task.CompletedTask;
    }
}
