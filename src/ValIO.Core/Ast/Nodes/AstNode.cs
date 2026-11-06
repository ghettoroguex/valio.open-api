using System.Collections.Immutable;
using ValIO.Core.Ast.Visitors;

namespace ValIO.Core.Ast.Nodes;

/// <summary>
/// Represents an immutable language-independent syntax node.
/// </summary>
public abstract class AstNode
{
    /// <summary>
    /// Gets the syntax and type nodes directly owned by this node.
    /// </summary>
    /// <value>The ordered child nodes, excluding metadata values.</value>
    public abstract IReadOnlyList<AstNode> Children { get; }
    
    /// <summary>
    /// Gets the immutable snapshot of additional information supplied at construction.
    /// </summary>
    /// <value>The metadata entries, or an empty dictionary when none were supplied.</value>
    /// <remarks>Metadata values remain opaque and are shared; callers own their immutability.</remarks>
    public IReadOnlyDictionary<string, object> Metadata { get; }
    
    /// <summary>
    /// Initializes a node without retaining a mutable metadata dictionary owned by its caller.
    /// </summary>
    /// <param name="metadata">Optional metadata whose entries are copied into an immutable dictionary.</param>
    protected AstNode(IReadOnlyDictionary<string, object>? metadata = null)
    {
        Metadata = metadata?.ToImmutableDictionary() ?? ImmutableDictionary<string, object>.Empty;
    }
    
    /// <summary>
    /// Dispatches this node to the matching visitor operation.
    /// </summary>
    /// <typeparam name="TResult">The result produced by the visitor.</typeparam>
    /// <param name="visitor">The visitor that processes the node.</param>
    /// <returns>The visitor result for this node.</returns>
    public abstract TResult Accept<TResult>(IAstVisitor<TResult> visitor);
    
    /// <summary>
    /// Creates a structurally equivalent node with independently cloned syntax children.
    /// </summary>
    /// <returns>A new node of the same runtime type with all semantic flags preserved.</returns>
    /// <remarks>Opaque metadata values are shared rather than recursively cloned.</remarks>
    public abstract AstNode Clone();
    
    /// <summary>
    /// Returns a diagnostic description of the node.
    /// </summary>
    /// <returns>The runtime node type name unless a derived node provides more detail.</returns>
    public override string ToString() => GetType().Name;
}
