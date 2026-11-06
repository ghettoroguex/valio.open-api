using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Builds TypeScript object literals whose property keys preserve the OpenAPI wire names while their values are
/// read through sanitized TypeScript parameter identifiers.
/// </summary>
/// <remarks>
/// OpenAPI names such as <c>Paging.Offset</c> and <c>X-Request-ID</c> cannot be used as plain TypeScript member
/// identifiers. The generated params contract therefore exposes sanitized members such as
/// <c>Paging_Offset</c> and <c>X_Request_ID</c>, but the HTTP request must retain the original names.
/// </remarks>
public static class TypeScriptWireParameterObject
{
    /// <summary>Builds HTTP headers with original names and a single correctly defaulted content type.</summary>
    /// <param name="operation">The operation whose body encoding determines the content type.</param>
    /// <param name="source">The optional header parameter group.</param>
    /// <param name="sourceMayBeUndefined">Whether the parameter group may be absent.</param>
    /// <param name="includeJsonContentType">Whether this transport requires an explicit JSON content type.</param>
    /// <returns>The header object with unavailable values omitted.</returns>
    public static ObjectLiteralExpressionNode BuildHeaders(
        IrOperation operation,
        ExpressionNode? source,
        bool sourceMayBeUndefined,
        bool includeJsonContentType = false)
    {
        var contentType = DefaultContentType(operation.RequestBody, includeJsonContentType);
        var headerParameters = operation.HeaderParameters;
        var contentTypeParameter = contentType == null ? null : headerParameters.FirstOrDefault(parameter =>
            string.Equals(parameter.OriginalName, "Content-Type", StringComparison.OrdinalIgnoreCase));
        var remaining = contentTypeParameter == null
            ? headerParameters
            : headerParameters.Where(parameter => parameter != contentTypeParameter).ToList();
        var wireHeaders = source == null
            ? EmptyObject()
            : BuildFromSource(remaining, source, sourceMayBeUndefined, omitUndefinedValues: true);
        if (contentType == null)
            return wireHeaders;

        ExpressionNode value = new LiteralExpressionNode(contentType, LiteralKind.String);
        if (contentTypeParameter != null && source != null)
        {
            var provided = TypeScriptRequestPath.BuildParameterAccess(source, contentTypeParameter.Name, sourceMayBeUndefined);
            value = new BinaryExpressionNode(provided, BinaryOperator.NullishCoalescing, value);
        }

        return new ObjectLiteralExpressionNode(
            wireHeaders.Properties.ToImmutableList().Insert(0, new PropertyAssignmentNode(WireKey(contentTypeParameter?.OriginalName ?? "Content-Type"), value)),
            preferMultilineFormat: true);
    }

    /// <summary>Resolves default content types for request bodies that serialize to text.</summary>
    /// <param name="body">The optional request-body contract.</param>
    /// <param name="includeJson">Whether to explicitly describe JSON bodies.</param>
    /// <returns>The required text content type, or no default for native multipart/binary values.</returns>
    internal static string? DefaultContentType(IrRequestBody? body, bool includeJson) => body switch
    {
        { IsFormUrlEncoded: true } => "application/x-www-form-urlencoded",
        { IsJson: true } when includeJson => "application/json",
        _ => null,
    };

    /// <summary>
    /// Builds an object literal by reading every parameter from <paramref name="source"/> through its sanitized
    /// <see cref="IrParameter.Name"/> and writing it under its original <see cref="IrParameter.OriginalName"/>.
    /// </summary>
    /// <param name="parameters">The ordered parameters to serialize.</param>
    /// <param name="source">The generated params group or validated params object.</param>
    /// <param name="sourceMayBeUndefined">Whether member access must use optional chaining.</param>
    /// <param name="omitUndefinedValues">
    /// Whether optional or unavailable values are emitted through conditional spreads so they are absent from the
    /// resulting object instead of being assigned <c>undefined</c>.
    /// </param>
    /// <returns>A multiline object literal suitable for a transport's query or headers configuration.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="parameters"/> or <paramref name="source"/> is <see langword="null"/>.
    /// </exception>
    public static ObjectLiteralExpressionNode BuildFromSource(
        IReadOnlyList<IrParameter> parameters,
        ExpressionNode source,
        bool sourceMayBeUndefined,
        bool omitUndefinedValues)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(source);

        return Build(
            parameters,
            parameter => TypeScriptRequestPath.BuildParameterAccess(source, parameter.Name, sourceMayBeUndefined),
            sourceMayBeUndefined,
            omitUndefinedValues);
    }

    /// <summary>
    /// Builds an object literal for legacy flat client functions whose values are standalone sanitized identifiers.
    /// </summary>
    /// <param name="parameters">The ordered parameters to serialize.</param>
    /// <param name="omitUndefinedValues">
    /// Whether optional values are omitted through conditional spreads instead of being assigned
    /// <c>undefined</c>.
    /// </param>
    /// <returns>An object literal keyed by the parameters' original wire names.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is <see langword="null"/>.</exception>
    public static ObjectLiteralExpressionNode BuildFromIdentifiers(
        IReadOnlyList<IrParameter> parameters,
        bool omitUndefinedValues)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        return Build(
            parameters,
            parameter => Id(parameter.Name),
            sourceMayBeUndefined: false,
            omitUndefinedValues);
    }

    /// <summary>Builds the wire-keyed object from a caller-provided value expression.</summary>
    /// <param name="parameters">The ordered parameters to serialize.</param>
    /// <param name="valueFactory">Creates the sanitized value access for one parameter.</param>
    /// <param name="sourceMayBeUndefined">Whether the containing source may be unavailable.</param>
    /// <param name="omitUndefinedValues">Whether unavailable values must be omitted.</param>
    /// <returns>The generated object literal.</returns>
    private static ObjectLiteralExpressionNode Build(
        IReadOnlyList<IrParameter> parameters,
        Func<IrParameter, ExpressionNode> valueFactory,
        bool sourceMayBeUndefined,
        bool omitUndefinedValues)
    {
        var properties = ImmutableList.CreateBuilder<PropertyAssignmentNode>();

        foreach (var parameter in parameters)
        {
            var value = valueFactory(parameter);
            var assignment = new PropertyAssignmentNode(WireKey(parameter.OriginalName), value);
            var mayBeUndefined = sourceMayBeUndefined || !parameter.IsRequired;

            if (!omitUndefinedValues || !mayBeUndefined)
            {
                properties.Add(assignment);
                continue;
            }

            var populated = new ObjectLiteralExpressionNode(
                ImmutableList.Create(assignment),
                preferMultilineFormat: true);
            var conditional = new ConditionalExpressionNode(
                new BinaryExpressionNode(value, BinaryOperator.NotEqual, Id("undefined")),
                populated,
                EmptyObject());
            properties.Add(new PropertyAssignmentNode(new IdentifierNode("_"), conditional, isSpread: true));
        }

        return new ObjectLiteralExpressionNode(properties.ToImmutable(), preferMultilineFormat: true);
    }

    /// <summary>Creates a raw TypeScript property key from an arbitrary OpenAPI wire name.</summary>
    /// <param name="wireName">The unmodified OpenAPI parameter name.</param>
    /// <returns>An identifier node containing the unescaped OpenAPI wire name.</returns>
    /// <remarks>
    /// The TypeScript renderer owns object-key quoting. Passing a pre-quoted JSON literal here makes the renderer
    /// quote it a second time, which produces an actual key such as <c>'"Paging.Offset"'</c> on the wire.
    /// </remarks>
    private static IdentifierNode WireKey(string wireName) => new(wireName);

    /// <summary>Creates an identifier expression.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The identifier expression.</returns>
    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    /// <summary>Creates an empty object literal.</summary>
    /// <returns>An object literal without properties.</returns>
    private static ObjectLiteralExpressionNode EmptyObject() =>
        new(ImmutableList<PropertyAssignmentNode>.Empty);
}
