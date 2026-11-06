using System.Collections.Frozen;
using ValIO.Core.IR;

namespace ValIO.Core.Ast.Builders;

/// <summary>Shares read-only document indexes with builders that resolve IR declarations.</summary>
/// <remarks>Language-specific mapping, output buffers, and transport behavior remain in derived builders.</remarks>
public abstract class AstBuilderBase : IAstBuilder
{
    /// <summary>Requires the document context before any declaration can be built.</summary>
    /// <param name="documentContext">The immutable indexes belonging to this builder's document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="documentContext"/> is null.</exception>
    protected AstBuilderBase(IrDocumentContext documentContext)
    {
        ArgumentNullException.ThrowIfNull(documentContext);
        DocumentContext = documentContext;
    }

    /// <summary>Gets the shared input context for the builder's document.</summary>
    /// <value>The context supplied at construction.</value>
    protected IrDocumentContext DocumentContext { get; }

    /// <summary>Gets models indexed by their exact generated declaration names.</summary>
    /// <value>The shared immutable ordinal model index.</value>
    public FrozenDictionary<string, IrModel> ModelsByName => DocumentContext.ModelsByName;

    /// <summary>Gets enums indexed by their exact generated declaration names.</summary>
    /// <value>The shared immutable ordinal enum index.</value>
    public FrozenDictionary<string, IrEnum> EnumsByName => DocumentContext.EnumsByName;

    /// <summary>Gets enum declaration names for reference classification.</summary>
    /// <value>The shared immutable ordinal enum-name set.</value>
    public FrozenSet<string> EnumNames => DocumentContext.EnumNames;
}
