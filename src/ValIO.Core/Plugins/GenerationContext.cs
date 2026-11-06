using ValIO.Core.IR;
using ValIO.Core.Models;

namespace ValIO.Core.Plugins;

/// <summary>Owns one generation request's inputs, shared document indexes, and mutable results.</summary>
/// <remarks>
/// Document-index initialization is thread-safe. Result collections remain owned by the request
/// and are not intended for concurrent writes by pipeline steps.
/// </remarks>
public sealed class GenerationContext
{
    /// <summary>Publishes exactly one document context, even when multiple consumers request it concurrently.</summary>
    private readonly Lazy<IrDocumentContext> _documentContext;

    /// <summary>Defers document indexing until required object-initializer inputs have been supplied.</summary>
    public GenerationContext()
    {
        _documentContext = new Lazy<IrDocumentContext>(
            () => new IrDocumentContext(IrDocument),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the finalized IR document for this generation request.</summary>
    /// <value>The required document from which shared indexes are created.</value>
    public required IrDocument IrDocument { get; init; }

    /// <summary>Gets the indexes shared by every step and builder in this request.</summary>
    /// <value>The lazily created context cached for the lifetime of this instance.</value>
    /// <exception cref="InvalidOperationException">The document contains colliding declaration names.</exception>
    public IrDocumentContext DocumentContext => _documentContext.Value;

    /// <summary>Gets the configuration governing output generation.</summary>
    /// <value>The required configuration for this request.</value>
    public required GenerationConfiguration Configuration { get; init; }

    /// <summary>Gets the selected generator's typed options through the plugin boundary.</summary>
    /// <value>The plugin-specific options, or null when defaults apply.</value>
    public object? PluginOptions { get; init; }

    /// <summary>Gets the output files produced by this request's pipeline.</summary>
    /// <value>The request-owned mutable output collection.</value>
    public List<GeneratedFile> GeneratedFiles { get; } = [];

    /// <summary>Gets intermediate data exchanged between steps in this request.</summary>
    /// <value>The request-owned mutable step-data dictionary.</value>
    public Dictionary<string, object> StepData { get; } = [];

    /// <summary>
    /// Gets non-blocking diagnostics contributed by generation steps and surfaced by the orchestrator.
    /// </summary>
    /// <value>The request-owned mutable warning collection.</value>
    public List<string> Warnings { get; } = [];
}
