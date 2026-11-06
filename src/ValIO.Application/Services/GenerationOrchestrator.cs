using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using ValIO.Core.Interfaces;
using ValIO.Core.IR.Builders;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Core.Services;
using ValIO.Parsers.Interfaces;

namespace ValIO.Application.Services;

/// <summary>
/// Default orchestrator. Stateless — same instance is safe for concurrent generation requests
/// (each call gets its own <see cref="GenerationContext"/>).
/// </summary>
public sealed class GenerationOrchestrator : IGenerationOrchestrator
{
    /// <summary>Reads source documents without retaining per-request state.</summary>
    private readonly IOpenApiDocumentParser _parser;
    /// <summary>Resolves the requested language plugin.</summary>
    private readonly IGeneratorRegistry _registry;
    /// <summary>Converts source names while building each request's IR.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Reports generation progress and actionable diagnostics.</summary>
    private readonly ILogger<GenerationOrchestrator> _logger;
    /// <summary>Resolves fresh pipeline dependencies for each generation request.</summary>
    private readonly IServiceProvider _services;

    /// <summary>Initializes the shared orchestrator with stateless application services.</summary>
    /// <param name="parser">The source document parser.</param>
    /// <param name="registry">The supported language plugins.</param>
    /// <param name="namingConverter">The IR naming policy.</param>
    /// <param name="logger">The diagnostic logger.</param>
    /// <param name="services">The provider used to create a new pipeline per request.</param>
    public GenerationOrchestrator(
        IOpenApiDocumentParser parser,
        IGeneratorRegistry registry,
        INamingConverter namingConverter,
        ILogger<GenerationOrchestrator> logger,
        IServiceProvider services)
    {
        _parser = parser;
        _registry = registry;
        _namingConverter = namingConverter;
        _logger = logger;
        _services = services;
    }

    /// <inheritdoc />
    public async Task<OrchestrationResult> GenerateAsync(
        GenerationConfiguration config,
        string schemaPath,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(schemaPath))
            return OrchestrationResult.Failure("Schema path is required");

        var plugin = _registry.FindPlugin(config.LanguageId);
        if (plugin is null)
            return OrchestrationResult.Failure($"No generator found for language: {config.LanguageId}");

        _logger.LogInformation("Using generator: {DisplayName}", plugin.DisplayName);
        _logger.LogInformation("Parsing OpenAPI schema from: {Schema}", schemaPath);

        OpenApiDocument doc;
        try
        {
            if (Uri.TryCreate(schemaPath, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                 uri.Scheme.Equals("file", StringComparison.OrdinalIgnoreCase)))
            {
                doc = await _parser.ParseFromUrlAsync(schemaPath);
            }
            else
            {
                doc = await _parser.ParseFromFileAsync(schemaPath);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to parse OpenAPI schema");
            return OrchestrationResult.Failure($"Failed to parse OpenAPI schema: {ex.Message}");
        }

        _logger.LogInformation("OpenAPI document parsed successfully");

        var schemaResolver = new CachingSchemaResolver(new SchemaResolver(doc));
        var irBuilder = new IrBuilder(schemaResolver, _namingConverter);
        var warnings = new List<string>();
        var affixOptions = BuildAffixOptions(config.NamingConvention);
        var irDocument = irBuilder.BuildFromOpenApi(doc, warnings, affixOptions);

        foreach (var w in warnings)
            _logger.LogWarning("{Warning}", w);

        _logger.LogInformation(
            "IR built: {Models} models, {Enums} enums, {Clients} clients",
            irDocument.Models.Count, irDocument.Enums.Count, irDocument.Clients.Count);

        var context = new GenerationContext
        {
            IrDocument = irDocument,
            Configuration = config,
            PluginOptions = config.PluginOptions,
        };

        try
        {
            // Validate declaration identity once before any language step emits output.
            _ = context.DocumentContext;
            var pipeline = plugin.CreatePipeline(_services);
            await pipeline.ExecuteAsync(context, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Generator pipeline threw");
            return OrchestrationResult.Failure($"Generator pipeline failed: {ex.Message}");
        }

        // Drain non-blocking diagnostics contributed by generation steps (e.g. transport auto-fix) so they are
        // logged and surfaced alongside the IR-phase warnings.
        foreach (var w in context.Warnings)
            _logger.LogWarning("{Warning}", w);
        warnings.AddRange(context.Warnings);

        _logger.LogInformation("Generated {Count} files", context.GeneratedFiles.Count);

        return new OrchestrationResult(
            Success: true,
            Files: context.GeneratedFiles.ToArray(),
            Warnings: warnings,
            ErrorMessage: null);
    }

    /// <summary>
    /// Projects declaration affixes into IR naming before the shared document context is created.
    /// </summary>
    /// <param name="naming">The configured declaration naming conventions.</param>
    /// <returns>The IR model, enum, and operation affixes.</returns>
    private static NameAffixOptions BuildAffixOptions(NamingConventions naming)
        => new()
        {
            ModelPrefix = naming.ModelPrefix,
            ModelSuffix = naming.ModelSuffix,
            MethodPrefix = naming.MethodPrefix,
            MethodSuffix = naming.MethodSuffix,
            EnumPrefix = naming.EnumPrefix,
            EnumSuffix = naming.EnumSuffix,
        };
}
