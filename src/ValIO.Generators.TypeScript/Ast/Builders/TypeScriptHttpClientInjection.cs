using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>Builds optional native transport injection without replacing a transport's own hooks or settings.</summary>
internal static class TypeScriptHttpClientInjection
{
    /// <summary>Appends an optional native client while preserving existing operation argument positions.</summary>
    /// <param name="type">The original transport's complete callable type.</param>
    /// <returns>The optional client parameter.</returns>
    public static ParameterDeclarationNode Parameter(TypeNode type) => new(
        new IdentifierNode("client"), type, isRequired: false,
        documentation: new CommentNode("Optional configured native HTTP client, including authentication and interceptors.", CommentKind.JSDoc));

    /// <summary>Selects the caller's client or the transport's existing default once per operation.</summary>
    /// <param name="defaultName">The imported or global default transport identifier.</param>
    /// <param name="localName">The selected client variable referenced by the generated invocation.</param>
    /// <returns>The local native transport selection.</returns>
    public static VariableDeclarationStatementNode Declaration(string defaultName, string localName = "httpClient") =>
        Declaration(new IdentifierExpressionNode(new IdentifierNode(defaultName)), localName);

    /// <summary>Selects a caller client or a qualified configured export without shadowing its original name.</summary>
    /// <param name="defaultClient">The imported native transport expression.</param>
    /// <param name="localName">The selected client variable used by the request.</param>
    /// <returns>The local transport selection.</returns>
    public static VariableDeclarationStatementNode Declaration(ExpressionNode defaultClient, string localName) => new(
        new IdentifierNode(localName), type: null,
        initializer: new BinaryExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("client")), BinaryOperator.NullishCoalescing, defaultClient));
}
