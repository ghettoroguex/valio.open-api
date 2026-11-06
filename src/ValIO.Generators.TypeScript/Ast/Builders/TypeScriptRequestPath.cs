using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Core.IR;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Builds the request-URL expression for an operation by substituting <c>{name}</c> path placeholders with the
/// corresponding route-parameter access expressions from a <see cref="TypeScriptParameterModel"/>.
/// </summary>
/// <remarks>
/// This is the single implementation of the path-templating that was previously copy-pasted across every
/// transport client builder (fetch/nextjs/ky/got/ofetch/undici/angular/axios). The output is a plain string
/// literal when the path has no parameters, or a template literal such as <c>`/api/apartments/${params.route.id}`</c>
/// otherwise. Placeholder matching uses <see cref="IrParameter.OriginalName"/> because sanitizing characters such
/// as dots or hyphens changes the TypeScript member name, while the path template remains an OpenAPI wire value.
/// Matching prefers exact OpenAPI parameter identity and replaces repeated occurrences. A unique case-insensitive
/// match supports documents whose path token casing differs from the parameter declaration; ambiguous matches
/// remain literal so distinct wire names cannot be silently conflated.
/// </remarks>
/// <seealso cref="TypeScriptParameterModel"/>
public static class TypeScriptRequestPath
{
    /// <summary>
    /// Builds the URL expression for <paramref name="operation"/> using <paramref name="model"/> for route access.
    /// </summary>
    /// <param name="operation">The IR operation whose <see cref="IrOperation.Path"/> is templated.</param>
    /// <param name="model">The parameter model providing route-parameter access expressions.</param>
    /// <returns>
    /// A <see cref="LiteralExpressionNode"/> when the path has no substitutable placeholders, otherwise a
    /// <see cref="TemplateLiteralExpressionNode"/> interleaving the literal path fragments with route accesses.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> or <paramref name="model"/> is <see langword="null"/>.</exception>
    public static ExpressionNode BuildUrl(IrOperation operation, TypeScriptParameterModel model)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(model);

        if (operation.PathParameters.Count == 0 || !model.HasRoute)
            return new LiteralExpressionNode(operation.Path, LiteralKind.String);

        return BuildTemplate(operation, parameter => model.RouteAccess(parameter.Name));
    }

    /// <summary>
    /// Builds the URL expression from an explicit <paramref name="routeSource"/> object expression rather than the
    /// parameter model, so a transport that has rebound its route source (e.g. to a validated <c>parsedRoute</c>)
    /// templates the path against that rebound source.
    /// </summary>
    /// <param name="operation">The IR operation whose <see cref="IrOperation.Path"/> is templated.</param>
    /// <param name="routeSource">
    /// The route object the placeholders resolve against (e.g. <c>params.route</c>, <c>params</c>, or a validated
    /// <c>parsedRoute</c>), or <see langword="null"/> when the operation has no route source.
    /// </param>
    /// <returns>
    /// A <see cref="LiteralExpressionNode"/> when the path has no substitutable placeholders (or no route source),
    /// otherwise a <see cref="TemplateLiteralExpressionNode"/> interleaving literal fragments with per-parameter
    /// access built by <see cref="BuildParameterAccess"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <seealso cref="BuildParameterAccess"/>
    public static ExpressionNode BuildUrl(IrOperation operation, ExpressionNode? routeSource)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.PathParameters.Count == 0 || routeSource == null)
            return new LiteralExpressionNode(operation.Path, LiteralKind.String);

        return BuildTemplate(operation, parameter => BuildParameterAccess(routeSource, parameter.Name, mayBeUndefined: false));
    }

    /// <summary>Substitutes path tokens in template order through a shared value-access strategy.</summary>
    /// <param name="operation">The operation with original wire parameter names.</param>
    /// <param name="access">Creates the expression for each matching token.</param>
    /// <returns>A path literal or template that preserves unmatched text and repeated tokens.</returns>
    private static ExpressionNode BuildTemplate(IrOperation operation, Func<IrParameter, ExpressionNode> access)
    {
        var parts = new List<TemplateLiteralPart>();
        var literalStart = 0;
        var cursor = 0;
        while (cursor < operation.Path.Length)
        {
            var open = operation.Path.IndexOf('{', cursor);
            if (open < 0)
                break;
            var close = operation.Path.IndexOf('}', open + 1);
            if (close < 0)
                break;
            cursor = close + 1;
            var name = operation.Path[(open + 1)..close];
            var parameter = ResolveParameter(operation.PathParameters, name);
            if (parameter == null)
                continue;
            if (open > literalStart)
                parts.Add(new TemplateLiteralPart(operation.Path[literalStart..open]));
            parts.Add(new TemplateLiteralPart(access(parameter)));
            literalStart = cursor;
        }

        if (parts.Count == 0)
            return new LiteralExpressionNode(operation.Path, LiteralKind.String);
        if (literalStart < operation.Path.Length)
            parts.Add(new TemplateLiteralPart(operation.Path[literalStart..]));
        return new TemplateLiteralExpressionNode(parts);
    }

    /// <summary>Resolves a wire token exactly before accepting an unambiguous casing-compatible declaration.</summary>
    /// <param name="parameters">The operation's path parameter declarations.</param>
    /// <param name="wireName">The original path token without braces.</param>
    /// <returns>The matching declaration, or null when no unique declaration exists.</returns>
    private static IrParameter? ResolveParameter(IReadOnlyList<IrParameter> parameters, string wireName)
    {
        var exact = parameters.FirstOrDefault(parameter => string.Equals(parameter.OriginalName, wireName, StringComparison.Ordinal));
        if (exact != null)
            return exact;

        IrParameter? compatible = null;
        foreach (var parameter in parameters)
        {
            if (!string.Equals(parameter.OriginalName, wireName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (compatible != null)
                return null;
            compatible = parameter;
        }

        return compatible;
    }

    /// <summary>
    /// Builds a per-field member-access expression <c>source.name</c> (optionally with optional chaining) for
    /// reaching a single route/query/header value off its group object — the access the transport clients use
    /// when folding query params into a query string or headers into a header object.
    /// </summary>
    /// <param name="source">The group object expression (e.g. <c>params.query</c>, or a validated <c>parsedQuery</c>).</param>
    /// <param name="name">The (sanitized) parameter name.</param>
    /// <param name="mayBeUndefined">
    /// Whether the group object may be <c>undefined</c> at runtime; when <see langword="true"/> the access uses
    /// optional chaining (<c>source?.name</c>).
    /// </param>
    /// <returns>
    /// The member-access expression, or <paramref name="source"/> unchanged when it is the literal
    /// <c>undefined</c> identifier (a validated source that resolved to nothing).
    /// </returns>
    public static ExpressionNode BuildParameterAccess(ExpressionNode source, string name, bool mayBeUndefined) =>
        source is IdentifierExpressionNode { Identifier.Name: "undefined" }
            ? source
            : new MemberAccessExpressionNode(source, new IdentifierNode(name), useOptionalChaining: mayBeUndefined);
}
