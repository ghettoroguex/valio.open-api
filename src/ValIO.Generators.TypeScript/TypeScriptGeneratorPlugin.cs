using Microsoft.Extensions.DependencyInjection;
using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Axios.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Steps;
using ValIO.Generators.TypeScript.TanStackQuery.Ast.Renderers;
using ValIO.Generators.TypeScript.TypeBox.Ast.Renderers;
using ValIO.Generators.TypeScript.Valibot.Ast.Renderers;
using ValIO.Generators.TypeScript.Zod.Ast.Renderers;

namespace ValIO.Generators.TypeScript;

/// <summary>
/// Plugin для генерации TypeScript кода из IR.
/// Регистрирует себя через AddGeneratorPlugin&lt;TypeScriptGeneratorPlugin&gt;().
/// </summary>
public sealed class TypeScriptGeneratorPlugin : IGeneratorPlugin
{
    public string LanguageId => "typescript";
    public string DisplayName => "TypeScript";
    public IReadOnlyList<string> LanguageAliases => ["ts", "typescript"];
    public IGeneratorOptionsDescriptor OptionsDescriptor { get; } = new TypeScriptOptionsDescriptor();

    public ICodeGenerationPipeline CreatePipeline(IServiceProvider services)
    {
        var namingConverter = services.GetRequiredService<INamingConverter>();
        var nodeRenderers = services.GetServices<ITypeScriptNodeRenderer>().ToArray();
        var steps = new ICodeGenerationStep[]
        {
            // Resolves the query-hook transport and auto-adds it to the selected transports (with a warning)
            // before any client/hook step reads the transport list.
            new TypeScriptTransportResolutionStep(),
            new TypeScriptModelsStep(namingConverter, nodeRenderers),
            new TypeScriptEnumsStep(namingConverter, nodeRenderers),
            new TypeScriptParamsStep(namingConverter, nodeRenderers),
            new TypeScriptResultsStep(namingConverter, nodeRenderers),
            new TypeScriptResultsSchemasStep(namingConverter, nodeRenderers),
            new TypeScriptParamsSchemasStep(namingConverter, nodeRenderers),
            new TypeScriptUuidFormatsStep(),
            new ZodSchemasStep(namingConverter),
            new ValibotSchemasStep(namingConverter, nodeRenderers),
            new TypeBoxSchemasStep(namingConverter, nodeRenderers),
            // Shared cache keys/tags — emitted before the transports and framework adapters that import them.
            new TypeScriptQueryKeysStep(namingConverter, nodeRenderers),
            new FetchClientStep(namingConverter, nodeRenderers),
            new AxiosClientStep(namingConverter, nodeRenderers),
            new KyClientStep(namingConverter, nodeRenderers),
            new UndiciClientStep(namingConverter, nodeRenderers),
            new OFetchClientStep(namingConverter, nodeRenderers),
            new GotClientStep(namingConverter, nodeRenderers),
            new AngularRxjsClientStep(namingConverter, nodeRenderers),
            new SolidStartClientStep(namingConverter, nodeRenderers),
            new NextjsClientStep(namingConverter, nodeRenderers),
            new TanStackQueryStep(namingConverter, nodeRenderers),
            new TanStackVueStep(namingConverter, nodeRenderers),
            new TanStackAngularStep(namingConverter, nodeRenderers),
            new TanStackSolidStep(namingConverter, nodeRenderers),
            new TanStackSvelteStep(namingConverter, nodeRenderers),
            new MswStep(namingConverter, nodeRenderers),
            new TypeScriptIndexStep(),
        };
        return new CodeGenerationPipeline(steps);
    }

    public void ConfigureServices(IServiceCollection services)
    {
        // Chain of Responsibility — feature-plugin renderers consumed by
        // TypeScriptAstRenderer via ITypeScriptNodeRenderer. Order is unimportant:
        // TypeScriptAstRenderer sorts by descending Priority itself.
        services.AddSingleton<ITypeScriptNodeRenderer, ZodNodeRenderer>();
        services.AddSingleton<ITypeScriptNodeRenderer, ValibotNodeRenderer>();
        services.AddSingleton<ITypeScriptNodeRenderer, TypeBoxNodeRenderer>();
        services.AddSingleton<ITypeScriptNodeRenderer, AxiosNodeRenderer>();
        services.AddSingleton<ITypeScriptNodeRenderer, TanStackNodeRenderer>();
    }
}
