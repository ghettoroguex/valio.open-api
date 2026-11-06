using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: генерирует <c>{operation}.results.schemas.ts</c> рядом с клиентом — именованные схемы
/// валидации тел ответов (union/array и т.п.), чтобы не пересоздавать их в рантайме на каждый вызов.
/// Активируется при включённой валидации (zod/valibot/typebox).
/// </summary>
public sealed class TypeScriptResultsSchemasStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-operation-results-schemas";
    public int Order => 17;

    public TypeScriptResultsSchemasStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public TypeScriptResultsSchemasStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
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
        var builder = new TypeScriptResultsSchemasAstBuilder(context.DocumentContext, _namingConverter, validation);

        var fileOrg = context.Configuration.FileOrganization;
        var clientsPath = fileOrg.ClientsPath ?? "api";

        foreach (var client in context.IrDocument.Clients)
        {
            foreach (var operation in client.Operations)
            {
                ct.ThrowIfCancellationRequested();

                var sourceFile = builder.BuildResultsSchemasSourceFile(operation, fileOrg);
                if (sourceFile == null)
                    continue;

                var fileName = _namingConverter.ToCamelCase(operation.OriginalMethodName);
                var content = _renderer.Render(sourceFile);
                var relativePath = $"{clientsPath}/{fileName}/{fileName}.results.schemas.ts";
                context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
            }
        }

        return Task.CompletedTask;
    }
}
