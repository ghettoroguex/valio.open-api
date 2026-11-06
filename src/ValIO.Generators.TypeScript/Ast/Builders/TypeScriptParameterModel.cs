using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Describes how an operation's generated <c>params</c> argument is shaped and how each parameter group
/// (route / query / headers / body / form) is accessed from it, as reusable AST expressions.
/// </summary>
/// <remarks>
/// <para>
/// The shape mirrors <see cref="TypeScriptParamsAstBuilder"/> exactly: the number of parameter <em>groups</em>
/// (route, query, JSON body, form body, headers) decides whether the params object is <b>flat</b>
/// (a single group is emitted as top-level properties, e.g. <c>params.id</c>) or <b>aggregate</b>
/// (each group is nested, e.g. <c>params.route.id</c>, <c>params.query</c>). Body and form count as separate
/// groups so this decision is byte-identical to the one the params-interface builder makes; because an OpenAPI
/// operation carries at most one request body, body and form are mutually exclusive in practice.
/// </para>
/// <para>
/// This type is the single source of truth for that decision so the transport clients, the params interface and
/// the TanStack query keys can never drift apart in how they reach <c>params.route.id</c> / <c>params.query</c>.
/// </para>
/// </remarks>
/// <seealso cref="TypeScriptParamsAstBuilder"/>
/// <seealso cref="TypeScriptRequestPath"/>
/// <seealso cref="TypeScriptCacheKey"/>
public sealed class TypeScriptParameterModel
{
    /// <summary>The root <c>params</c> identifier expression that every access is rooted at.</summary>
    private readonly ExpressionNode _paramsExpr;

    /// <summary>Initializes the model. Use <see cref="Create"/> rather than calling this directly.</summary>
    /// <param name="paramsExpr">The root <c>params</c> identifier expression.</param>
    /// <param name="isFlat">Whether the params object is flat (single group) rather than aggregate.</param>
    /// <param name="hasRoute">Whether the operation has path/route parameters.</param>
    /// <param name="hasQuery">Whether the operation has query parameters.</param>
    /// <param name="hasHeaders">Whether the operation has header parameters.</param>
    /// <param name="hasBody">Whether the operation has a non-form (JSON/binary) request body.</param>
    /// <param name="hasForm">Whether the operation has a form (multipart / url-encoded) request body.</param>
    /// <param name="queryMayBeUndefined">Whether the aggregate <c>params.query</c> group may be <c>undefined</c>.</param>
    /// <param name="headersMayBeUndefined">Whether the aggregate <c>params.headers</c> group may be <c>undefined</c>.</param>
    /// <param name="bodyMayBeUndefined">Whether the body/form group may be <c>undefined</c>.</param>
    private TypeScriptParameterModel(
        ExpressionNode paramsExpr,
        bool isFlat,
        bool hasRoute,
        bool hasQuery,
        bool hasHeaders,
        bool hasBody,
        bool hasForm,
        bool queryMayBeUndefined,
        bool headersMayBeUndefined,
        bool bodyMayBeUndefined)
    {
        _paramsExpr = paramsExpr;
        IsFlat = isFlat;
        HasRoute = hasRoute;
        HasQuery = hasQuery;
        HasHeaders = hasHeaders;
        HasBody = hasBody;
        HasForm = hasForm;
        QueryMayBeUndefined = queryMayBeUndefined;
        HeadersMayBeUndefined = headersMayBeUndefined;
        BodyMayBeUndefined = bodyMayBeUndefined;
    }

    /// <summary>Whether the params object is flat (a single group at top level) rather than aggregate.</summary>
    /// <value><see langword="true"/> when <c>groupCount &lt;= 1</c>.</value>
    public bool IsFlat { get; }

    /// <summary>Whether the operation has path/route parameters.</summary>
    public bool HasRoute { get; }

    /// <summary>Whether the operation has query parameters.</summary>
    public bool HasQuery { get; }

    /// <summary>Whether the operation has header parameters.</summary>
    public bool HasHeaders { get; }

    /// <summary>Whether the operation has a non-form (JSON/binary) request body.</summary>
    public bool HasBody { get; }

    /// <summary>Whether the operation has a form (multipart/form-data or url-encoded) request body.</summary>
    public bool HasForm { get; }

    /// <summary>
    /// Whether the aggregate <c>params.query</c> group may be <c>undefined</c> at runtime (all query params optional).
    /// Always <see langword="false"/> in flat mode, where the query fields live directly on the required
    /// <c>params</c> object.
    /// </summary>
    public bool QueryMayBeUndefined { get; }

    /// <summary>Whether every header is optional and its aggregate group may therefore be absent.</summary>
    /// <value>Whether header access requires optional chaining.</value>
    public bool HeadersMayBeUndefined { get; }

    /// <summary>Whether the body/form group may be <c>undefined</c> at runtime (optional request body).</summary>
    public bool BodyMayBeUndefined { get; }

    /// <summary>
    /// Computes the parameter model for <paramref name="operation"/>, using the same group-count / flat-vs-aggregate
    /// rule as <see cref="TypeScriptParamsAstBuilder"/>.
    /// </summary>
    /// <param name="operation">The IR operation.</param>
    /// <param name="paramsIdentifier">The name of the generated params argument (defaults to <c>params</c>).</param>
    /// <returns>A model describing the params shape and per-group access expressions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="operation"/> is <see langword="null"/>.</exception>
    public static TypeScriptParameterModel Create(IrOperation operation, string paramsIdentifier = "params")
    {
        ArgumentNullException.ThrowIfNull(operation);

        var hasRoute = operation.PathParameters.Count > 0;
        var hasQuery = operation.QueryParameters.Count > 0;
        var hasHeaders = operation.HeaderParameters.Count > 0;
        var requestBody = operation.RequestBody;
        var hasForm = requestBody is { IsForm: true };
        var hasBody = requestBody != null && !hasForm;

        var groupCount = (hasRoute ? 1 : 0) + (hasQuery ? 1 : 0) + (hasHeaders ? 1 : 0) + (hasBody ? 1 : 0) + (hasForm ? 1 : 0);
        var isFlat = groupCount <= 1;

        return new TypeScriptParameterModel(
            paramsExpr: new IdentifierExpressionNode(new IdentifierNode(paramsIdentifier)),
            isFlat: isFlat,
            hasRoute: hasRoute,
            hasQuery: hasQuery,
            hasHeaders: hasHeaders,
            hasBody: hasBody,
            hasForm: hasForm,
            // In aggregate mode the `query` property is optional exactly when every query param is optional.
            queryMayBeUndefined: hasQuery && !isFlat && operation.QueryParameters.All(p => !p.IsRequired),
            // A required wire header also requires the aggregate header group.
            headersMayBeUndefined: hasHeaders && !isFlat && operation.HeaderParameters.All(parameter => !parameter.IsRequired),
            bodyMayBeUndefined: (hasBody || hasForm) && requestBody?.IsRequired == false);
    }

    /// <summary>The root <c>params</c> expression (the whole params object).</summary>
    /// <returns>The <c>params</c> identifier expression.</returns>
    public ExpressionNode ParamsExpression() => _paramsExpr;

    /// <summary>
    /// The route object expression — <c>params.route</c> (aggregate) or <c>params</c> (flat) — or
    /// <see langword="null"/> when the operation has no route parameters. Route fields flatten to the top level
    /// in flat mode, so (unlike the body) there is no <c>params.route</c> wrapper then.
    /// </summary>
    /// <returns>The route object expression, or <see langword="null"/>.</returns>
    /// <seealso cref="RouteAccess"/>
    public ExpressionNode? RouteObject() =>
        HasRoute ? (IsFlat ? _paramsExpr : Member(_paramsExpr, "route")) : null;

    /// <summary>
    /// Builds the access expression for a single route parameter: <c>params.route.&lt;name&gt;</c> (aggregate)
    /// or <c>params.&lt;name&gt;</c> (flat). Route parameters are always required, so no optional chaining is used.
    /// </summary>
    /// <param name="parameterName">The (sanitized) route parameter name.</param>
    /// <returns>The member-access expression for the route parameter value.</returns>
    public ExpressionNode RouteAccess(string parameterName)
    {
        var target = IsFlat ? _paramsExpr : Member(_paramsExpr, "route");
        return Member(target, parameterName);
    }

    /// <summary>
    /// The query object expression — <c>params.query</c> (aggregate) or <c>params</c> (flat) — or
    /// <see langword="null"/> when the operation has no query parameters.
    /// </summary>
    /// <returns>The query object expression, or <see langword="null"/>.</returns>
    public ExpressionNode? QueryObject() =>
        HasQuery ? (IsFlat ? _paramsExpr : Member(_paramsExpr, "query")) : null;

    /// <summary>
    /// The headers object expression — <c>params.headers</c> (aggregate) or <c>params</c> (flat) — or
    /// <see langword="null"/> when the operation has no header parameters.
    /// </summary>
    /// <returns>The headers object expression, or <see langword="null"/>.</returns>
    public ExpressionNode? HeadersObject() =>
        HasHeaders ? (IsFlat ? _paramsExpr : Member(_paramsExpr, "headers")) : null;

    /// <summary>
    /// The body/form object expression — always <c>params.body</c> (JSON/binary) or <c>params.form</c>
    /// (multipart / url-encoded) — or <see langword="null"/> when the operation has no request body.
    /// </summary>
    /// <returns>The body/form object expression, or <see langword="null"/>.</returns>
    /// <remarks>
    /// Unlike route/query/header groups (whose fields flatten to the top level of <c>params</c> when they are the
    /// sole group), the request body is <b>always</b> reached via <c>params.body</c> / <c>params.form</c>, even in
    /// flat mode — the params interface keeps a dedicated <c>body</c>/<c>form</c> property because a body is a
    /// single value rather than a set of spreadable named fields. Every transport client must follow this.
    /// </remarks>
    public ExpressionNode? BodyObject()
    {
        if (!HasBody && !HasForm)
            return null;
        return Member(_paramsExpr, HasForm ? "form" : "body");
    }

    /// <summary>Builds a plain (non-optional) member access <c>source.member</c>.</summary>
    /// <param name="source">The object expression.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The member-access expression.</returns>
    private static MemberAccessExpressionNode Member(ExpressionNode source, string member) =>
        new(source, new IdentifierNode(member));
}
