using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.Kotlin.Ast.Builders;
using ValIO.Generators.Kotlin.Ast.Renderers;
using ValIO.Generators.Kotlin.Options;

namespace ValIO.Generators.Kotlin.Steps;

/// <summary>
/// Generates one Kotlin file per IR client (OkHttp + coroutines, suspend methods).
/// </summary>
public sealed class OkHttpClientStep : ICodeGenerationStep
{
    /// <summary>Converts source identifiers into Kotlin class and file names.</summary>
    private readonly INamingConverter _namingConverter;

    /// <summary>Renders client ASTs into Kotlin source text.</summary>
    private readonly IKotlinAstRenderer _renderer;

    /// <summary>Maps shared IR request and response types into Kotlin types.</summary>
    private readonly KotlinIrTypeMapper _typeMapper;

    /// <summary>Gets the identifier selecting this OkHttp generation step.</summary>
    /// <value>The Kotlin OkHttp step identifier.</value>
    public string Name => "kotlin-okhttp-client";

    /// <summary>Gets the pipeline order placing clients after their model declarations.</summary>
    /// <value>The client generation order.</value>
    public int Order => 40;

    /// <summary>Initializes client generation with the configured Kotlin services.</summary>
    /// <param name="namingConverter">The converter for generated identifiers and file names.</param>
    /// <param name="renderer">The renderer for Kotlin client ASTs.</param>
    /// <param name="typeMapper">The shared IR to Kotlin type mapper.</param>
    public OkHttpClientStep(
        INamingConverter namingConverter,
        IKotlinAstRenderer renderer,
        KotlinIrTypeMapper typeMapper)
    {
        _namingConverter = namingConverter;
        _renderer = renderer;
        _typeMapper = typeMapper;
    }

    /// <summary>Determines whether clients are enabled and present in the source document.</summary>
    /// <param name="context">The generation configuration and finalized source document.</param>
    /// <returns>Whether this step should generate client files.</returns>
    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0;

    /// <summary>Generates clients using the request's shared model and enum name index.</summary>
    /// <param name="context">The request containing language options, shared indexes, and output files.</param>
    /// <param name="ct">The cancellation token supplied by the generation pipeline.</param>
    /// <returns>A completed task after the client source files have been added.</returns>
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as KotlinGenerationOptions ?? new KotlinGenerationOptions();
        var apiPackage = KotlinStepHelper.ApiPackage(opts);
        var apiDir = KotlinStepHelper.PackageToPath(apiPackage);
        var modelsPackage = KotlinStepHelper.ModelsPackage(opts);
        var knownNames = context.DocumentContext.KnownTypeNames;

        var builder = new OkHttpClientAstBuilder(_namingConverter, _typeMapper);
        foreach (var client in context.IrDocument.Clients)
        {
            var file = builder.BuildFile(client, apiPackage, modelsPackage, knownNames);
            var content = _renderer.Render(file);
            var className = ToClassName(client.Name);
            var relativePath = $"{apiDir}/{className}.kt";
            context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
        }

        return Task.CompletedTask;
    }

    /// <summary>Converts a client identifier into a Kotlin API class name without duplicating its suffix.</summary>
    /// <param name="clientName">The source client identifier.</param>
    /// <returns>The PascalCase class name ending in <c>Api</c>.</returns>
    private string ToClassName(string clientName)
    {
        var pascal = _namingConverter.ToPascalCase(clientName);
        return pascal.EndsWith("Api", StringComparison.Ordinal) ? pascal : pascal + "Api";
    }
}
