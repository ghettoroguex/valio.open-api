using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: генерирует <c>{operation}.params.schema.ts</c> рядом с клиентом — именованные схемы валидации
/// route/query параметров, которые импортируют все транспорты (axios/fetch/got/ky/ofetch/undici).
/// Активируется при включённой валидации (zod/valibot/typebox) для любой библиотеки — заменяет
/// прежний zod-специфичный шаг.
/// </summary>
public sealed class TypeScriptParamsSchemasStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-operation-params-schemas";
    public int Order => 16;

    public TypeScriptParamsSchemasStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TypeScriptParamsSchemasStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
    {
        if (!context.Configuration.GenerateClients || context.IrDocument.Clients.Count == 0)
            return false;

        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return ClientValidationStrategyFactory.Create(opts).IsEnabled;
    }

    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        var validation = ClientValidationStrategyFactory.Create(opts);
        var builder = new TypeScriptParamsSchemasAstBuilder(context.DocumentContext, _namingConverter, validation, opts?.UuidFormat);

        var fileOrg = context.Configuration.FileOrganization;
        var clientsPath = fileOrg.ClientsPath ?? "api";

        foreach (var client in context.IrDocument.Clients)
        {
            foreach (var operation in client.Operations)
            {
                ct.ThrowIfCancellationRequested();

                var sourceFile = builder.BuildParamsSchemasSourceFile(operation, fileOrg);
                if (sourceFile == null)
                    continue;

                var fileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
                var content = _renderer.Render(sourceFile);
                var relativePath = $"{clientsPath}/{fileName}/{fileName}.params.schema.ts";
                context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
            }
        }

        return Task.CompletedTask;
    }
}
