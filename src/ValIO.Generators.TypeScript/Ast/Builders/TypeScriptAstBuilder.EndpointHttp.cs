using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Builds HTTP endpoint parameter validation and the plain fetch transport implementation.
/// </summary>
public partial class TypeScriptAstBuilder
{
    private sealed class HttpParamSourcesContext
    {
        public List<StatementNode> LeadingStatements { get; } = new();
        public ExpressionNode? RouteSource { get; set; }
        public ExpressionNode? QuerySource { get; set; }
        public ExpressionNode? BodySource { get; set; }
        public ExpressionNode? HeadersSource { get; set; }
        public bool IsFlat { get; set; }

        /// <summary>
        /// Whether <see cref="QuerySource"/> may be <c>undefined</c> at runtime (drives optional chaining in the
        /// query-string build). Reset to <see langword="false"/> once the source is rebound to a validated
        /// <c>parsedQuery</c> local.
        /// </summary>
        public bool QueryMayBeUndefined { get; set; }
    }

    /// <summary>Prepares validated parameter groups and native multipart request data.</summary>
    /// <param name="operation">The operation whose request is generated.</param>
    /// <returns>The parameter sources and statements needed by the endpoint.</returns>
    private HttpParamSourcesContext PrepareHttpParamSourcesWithOptionalZod(IrOperation operation)
    {
        var ctx = new HttpParamSourcesContext();

        // The flat/aggregate decision and per-group access are owned by TypeScriptParameterModel so the
        // fetch/axios bodies can never drift from the emitted params interface or the other transports. Route,
        // query and headers flatten to `params` when they are the sole group; the request body is always reached
        // via params.body / params.form (even in flat mode) — see TypeScriptParameterModel.BodyObject.
        var model = TypeScriptParameterModel.Create(operation);
        ctx.IsFlat = model.IsFlat;

        var rb = operation.RequestBody;
        var hasRoute = model.HasRoute;
        var hasQuery = model.HasQuery;
        var hasBody = model.HasBody || model.HasForm;

        // Initial per-group sources; rebound to the validated locals below when validation is enabled.
        var routeSource = model.RouteObject();
        var querySource = model.QueryObject();
        var headersSource = model.HeadersObject();
        var bodySource = model.BodyObject();
        var queryMayBeUndefined = model.QueryMayBeUndefined;

        if (_validation.IsEnabled)
        {
            var opName = _namingConverter.ToPascalCase(operation.MethodName);
            var before = ctx.LeadingStatements.Count;

            // Route/query параметры валидируются по импортируемой именованной схеме
            // ({op}.params.schema.ts) — единообразно для всех библиотек.
            if (hasRoute)
            {
                var parseCall = _validation.ValidateNamed($"{opName}RouteParamsSchema", model.RouteObject()!);
                ctx.LeadingStatements.Add(new VariableDeclarationStatementNode(
                    new IdentifierNode("parsedRoute"),
                    type: null,
                    initializer: parseCall));
                routeSource = new IdentifierExpressionNode(new IdentifierNode("parsedRoute"));
            }
            if (hasQuery)
            {
                var queryInput = model.QueryMayBeUndefined
                    ? new BinaryExpressionNode(model.QueryObject()!, BinaryOperator.NullishCoalescing,
                        new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty))
                    : model.QueryObject()!;
                var parseCall = _validation.ValidateNamed($"{opName}QueryParamsSchema", queryInput);
                ctx.LeadingStatements.Add(new VariableDeclarationStatementNode(
                    new IdentifierNode("parsedQuery"),
                    type: null,
                    initializer: parseCall));
                querySource = new IdentifierExpressionNode(new IdentifierNode("parsedQuery"));
                // The validated local is always defined; no optional chaining on its field access.
                queryMayBeUndefined = false;
            }
            // Тело запроса валидируется схемой, построенной из типа тела (модель/union/...) выбранной библиотекой.
            if (hasBody && ValidationTypes.IsValidatable(rb!.Type))
            {
                var validatedBody = _validation.Validate(rb.Type, model.BodyObject()!);
                var parseCall = model.BodyMayBeUndefined
                    ? (ExpressionNode)new ConditionalExpressionNode(
                        new BinaryExpressionNode(model.BodyObject()!, BinaryOperator.NotEqual, Id("undefined")),
                        validatedBody,
                        Id("undefined"))
                    : validatedBody;
                ctx.LeadingStatements.Add(new VariableDeclarationStatementNode(
                    new IdentifierNode("parsedBody"),
                    type: null,
                    initializer: parseCall));
                bodySource = new IdentifierExpressionNode(new IdentifierNode("parsedBody"));
            }

            if (ctx.LeadingStatements.Count > before)
                ctx.LeadingStatements.Add(CreateBlankLineStatement());
        }

        if (rb is { IsForm: true } && bodySource != null)
        {
            bodySource = TypeScriptFormBody.Build(rb, bodySource, ModelsByName, ctx.LeadingStatements);
            ctx.LeadingStatements.Add(CreateBlankLineStatement());
        }

        ctx.RouteSource = routeSource;
        ctx.QuerySource = querySource;
        ctx.BodySource = bodySource;
        ctx.HeadersSource = headersSource;
        ctx.QueryMayBeUndefined = queryMayBeUndefined;
        return ctx;
    }

    /// <summary>
    /// Builds an identifier expression for TypeScript runtime globals and generated locals.
    /// </summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The identifier expression.</returns>
    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    /// <summary>
    /// Builds a member access expression for a generated TypeScript value.
    /// </summary>
    /// <param name="source">The object expression.</param>
    /// <param name="member">The member name.</param>
    /// <returns>The member access expression.</returns>
    private static MemberAccessExpressionNode Member(ExpressionNode source, string member) =>
        new(source, new IdentifierNode(member));

    /// <summary>
    /// Builds a TypeScript string literal expression.
    /// </summary>
    /// <param name="value">The literal value.</param>
    /// <returns>The string literal expression.</returns>
    private static LiteralExpressionNode Str(string value) => new(value, LiteralKind.String);

    /// <summary>Converts a generated wire-header object to the native fetch HeadersInit boundary type.</summary>
    /// <param name="source">The generated header object.</param>
    /// <returns>The fetch-compatible header expression.</returns>
    private static CastExpressionNode CastToHeadersInit(ExpressionNode source)
    {
        // {Op}HeaderParams интерфейс не имеет index-signature, а HeadersInit
        // требует Record<string, string>. Делаем `as HeadersInit`-cast.
        return new CastExpressionNode(
            new SimpleTypeNode(new IdentifierNode("HeadersInit"), isPrimitive: false),
            source,
            useAsOperator: true);
    }

    /// <summary>Builds the plain fetch client function body for an operation.</summary>
    /// <param name="operation">The operation whose request and response flow is generated.</param>
    /// <returns>The generated function body.</returns>
    private BlockNode BuildFetchFunctionBody(IrOperation operation)
    {
        var statements = new List<StatementNode>();
        var ctx = PrepareHttpParamSourcesWithOptionalZod(operation);
        statements.AddRange(ctx.LeadingStatements);

        var axiosBuilder = new Axios.Ast.Builders.AxiosAstBuilder(DocumentContext, _namingConverter, BuildType);
        var pathUrlExpr = axiosBuilder.BuildUrlWithSource(operation, ctx.RouteSource);
        // The URLSearchParams builder shared with Next.js skips absent optional params and handles encoding.
        var requestUrlExpr = TypeScriptQueryString.AppendQueryToUrl(
            operation, pathUrlExpr, ctx.QuerySource, ctx.QueryMayBeUndefined, statements);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("requestUrl"),
            type: null,
            initializer: requestUrlExpr));
        statements.Add(CreateBlankLineStatement());

        var methodUpper = operation.HttpMethod.ToUpperInvariant();
        var methodLiteral = new LiteralExpressionNode(methodUpper, LiteralKind.String);

        var parameterModel = TypeScriptParameterModel.Create(operation);
        var wireHeaders = TypeScriptWireParameterObject.BuildHeaders(
            operation, ctx.HeadersSource, parameterModel.HeadersMayBeUndefined, includeJsonContentType: true);
        var headersExpr = wireHeaders.Properties.Count == 0 ? null : CastToHeadersInit(wireHeaders);

        var initProps = new List<PropertyAssignmentNode>
        {
            new(new IdentifierNode("method"), methodLiteral)
        };

        if (headersExpr != null)
            initProps.Add(new PropertyAssignmentNode(new IdentifierNode("headers"), headersExpr));

        if (operation.RequestBody != null && operation.RequestBody.IsJson && ctx.BodySource != null)
        {
            var jsonStringify = new CallExpressionNode(
                new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("JSON")),
                    new IdentifierNode("stringify")),
                new[] { ctx.BodySource }.ToImmutableList());
            initProps.Add(new PropertyAssignmentNode(new IdentifierNode("body"), jsonStringify));
        }
        else if (operation.RequestBody != null && ctx.BodySource != null)
        {
            // A typed non-JSON body source does not implement BodyInit directly. Cast through unknown because the
            // runtime caller supplies the concrete FormData, Blob, stream, or url-encoded body.
            var bodyCast = new CastExpressionNode(
                new SimpleTypeNode(new IdentifierNode("BodyInit"), isPrimitive: false),
                new CastExpressionNode(
                    new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true),
                    ctx.BodySource,
                    useAsOperator: true),
                useAsOperator: true);
            initProps.Add(new PropertyAssignmentNode(new IdentifierNode("body"), bodyCast));
        }

        var signalRef = new IdentifierExpressionNode(new IdentifierNode("signal"));
        var signalSpread = new ConditionalExpressionNode(
            signalRef,
            new ObjectLiteralExpressionNode(new[]
            {
                new PropertyAssignmentNode(new IdentifierNode("signal"), signalRef)
            }.ToImmutableList()),
            new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty));

        var baseInit = new ObjectLiteralExpressionNode(initProps.ToImmutableList(), type: null, metadata: null, preferMultilineFormat: true);
        var optionsRef = new IdentifierExpressionNode(new IdentifierNode("options"));
        var emptyObj = new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty);
        var optionsSpread = new BinaryExpressionNode(optionsRef, BinaryOperator.NullishCoalescing, emptyObj);
        var placeholder = new IdentifierNode("_");
        var mergedInit = new ObjectLiteralExpressionNode(new[]
        {
            new PropertyAssignmentNode(placeholder, new IdentifierExpressionNode(new IdentifierNode("baseInit")), isSpread: true),
            new PropertyAssignmentNode(placeholder, optionsSpread, isSpread: true),
            new PropertyAssignmentNode(placeholder, signalSpread, isSpread: true)
        }.ToImmutableList(), type: null, metadata: null, preferMultilineFormat: true);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("baseInit"),
            type: new SimpleTypeNode(new IdentifierNode("RequestInit"), isPrimitive: false),
            initializer: baseInit));

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("init"),
            type: new SimpleTypeNode(new IdentifierNode("RequestInit"), isPrimitive: false),
            initializer: mergedInit));

        var fetchName = GetFetchImplementationName();
        statements.Add(TypeScriptHttpClientInjection.Declaration(fetchName));
        var fetchCall = new CallExpressionNode(
            Id("httpClient"),
            new[]
            {
                new IdentifierExpressionNode(new IdentifierNode("requestUrl")),
                new IdentifierExpressionNode(new IdentifierNode("init"))
            }.ToImmutableList());

        var useResultType = operation.Responses.Any(response => response.Type != null);
        var operationReturnType = useResultType
            ? new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: false)
            : ExtractReturnType(operation.Responses);
        if (!useResultType && operationReturnType is SimpleTypeNode { Name.Name: "void" })
        {
            statements.Add(new ExpressionStatementNode(new AwaitExpressionNode(fetchCall)));
            return new BlockNode(statements.ToImmutableList());
        }

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("response"), type: null, initializer: new AwaitExpressionNode(fetchCall)));
        statements.Add(CreateBlankLineStatement());

        var responseStatus = Member(Id("response"), "status");
        var jsonCall = new CallExpressionNode(Member(Id("response"), "json"), ImmutableList<ExpressionNode>.Empty);
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("responseData"), type: null,
            initializer: TypeScriptResponseBody.Read(operation, responseStatus, new AwaitExpressionNode(jsonCall))));
        statements.Add(CreateBlankLineStatement());

        if (useResultType)
        {
            var responseStatusRef = new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("response")),
                new IdentifierNode("status"));
            var responseDataRef = new IdentifierExpressionNode(new IdentifierNode("responseData"));
            var responsesWithType = operation.Responses
                .Where(r => int.TryParse(r.StatusCode, out _))
                .ToList();

            foreach (var resp in responsesWithType)
            {
                var statusCode = int.Parse(resp.StatusCode);
                var isSuccessful = statusCode >= 200 && statusCode < 300;
                var parseCall = BuildStatusResultBody(operation, resp, responseDataRef);

                var returnObj = new ObjectLiteralExpressionNode(new[]
                {
                    new PropertyAssignmentNode(new IdentifierNode("status"),
                        new LiteralExpressionNode(statusCode, LiteralKind.Number)),
                    new PropertyAssignmentNode(new IdentifierNode("body"), parseCall),
                    new PropertyAssignmentNode(new IdentifierNode("isSuccessful"),
                        new LiteralExpressionNode(isSuccessful, LiteralKind.Boolean)),
                    new PropertyAssignmentNode(new IdentifierNode("isFailed"),
                        new LiteralExpressionNode(!isSuccessful, LiteralKind.Boolean))
                }.ToImmutableList());

                var statusLiteral = new LiteralExpressionNode(statusCode, LiteralKind.Number);
                var condition = new BinaryExpressionNode(responseStatusRef, BinaryOperator.Equal, statusLiteral);

                statements.Add(new IfStatementNode(
                    condition,
                    new BlockNode(new[] { new ReturnStatementNode(returnObj) }.ToImmutableList()),
                    null));
            }

            statements.Add(new ReturnStatementNode(
                new ObjectLiteralExpressionNode(new[]
                {
                    new PropertyAssignmentNode(new IdentifierNode("status"), responseStatusRef),
                    new PropertyAssignmentNode(new IdentifierNode("body"), responseDataRef),
                    // Undocumented status → catch-all `…ResultOther`, which is typed as a literal failure
                    // (isSuccessful: false, isFailed: true) to keep the result union discriminable.
                    new PropertyAssignmentNode(new IdentifierNode("isSuccessful"),
                        new LiteralExpressionNode(false, LiteralKind.Boolean)),
                    new PropertyAssignmentNode(new IdentifierNode("isFailed"),
                        new LiteralExpressionNode(true, LiteralKind.Boolean))
                }.ToImmutableList())));
        }
        else if (operationReturnType is not SimpleTypeNode { Name.Name: "void" })
        {
            if (_validation.IsEnabled)
            {
                var successResponse = operation.Responses.FirstOrDefault(r =>
                    int.TryParse(r.StatusCode, out var code) && code >= 200 && code < 300);
                if (successResponse?.Type is IrModelReference responseModelRef)
                {
                    var parseCall = CreateParseCall($"{responseModelRef.ModelName}Schema",
                        new IdentifierExpressionNode(new IdentifierNode("responseData")));
                    statements.Add(new VariableDeclarationStatementNode(
                        new IdentifierNode("parsedResponse"),
                        type: null,
                        initializer: parseCall));
                    statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("parsedResponse"))));
                }
                else
                {
                    statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("responseData"))));
                }
            }
            else
            {
                statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("responseData"))));
            }
        }

        return new BlockNode(statements.ToImmutableList());
    }

    private string GetFetchImplementationName() => "fetch";
}
