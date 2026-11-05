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
/// HTTP endpoint: общая подготовка params/Zod и реализация fetch-тела.
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
                var parseCall = _validation.ValidateNamed($"{opName}QueryParamsSchema", model.QueryObject()!);
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
                var parseCall = _validation.Validate(rb.Type, model.BodyObject()!);
                ctx.LeadingStatements.Add(new VariableDeclarationStatementNode(
                    new IdentifierNode("parsedBody"),
                    type: null,
                    initializer: parseCall));
                bodySource = new IdentifierExpressionNode(new IdentifierNode("parsedBody"));
            }

            if (ctx.LeadingStatements.Count > before)
                ctx.LeadingStatements.Add(CreateBlankLineStatement());
        }

        if (rb is { IsMultipartFormData: true } && bodySource != null)
        {
            bodySource = BuildMultipartFormData(rb, bodySource, ctx.LeadingStatements);
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
    /// Materializes a multipart request model into browser <see cref="FormData"/> instead of passing the parsed
    /// model object to the transport, which Axios would serialize as JSON.
    /// </summary>
    /// <param name="requestBody">The multipart request body whose model properties define the wire fields.</param>
    /// <param name="bodySource">The validated or raw TypeScript request model expression.</param>
    /// <param name="statements">The endpoint statements that receive the <c>FormData</c> construction.</param>
    /// <returns>The <c>formData</c> expression, or <paramref name="bodySource"/> when the multipart model is unavailable.</returns>
    private ExpressionNode BuildMultipartFormData(
        IrRequestBody requestBody,
        ExpressionNode bodySource,
        List<StatementNode> statements)
    {
        if (requestBody.Type is not IrModelReference modelReference
            || !ModelsByName.TryGetValue(modelReference.ModelName, out var model))
        {
            return bodySource;
        }

        var formData = Id("formData");
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("formData"),
            type: null,
            initializer: new CallExpressionNode(
                Id("FormData"),
                ImmutableList<ExpressionNode>.Empty,
                isNewExpression: true)));

        foreach (var property in model.Properties)
        {
            var value = Member(bodySource, property.Name);
            var append = BuildMultipartAppendStatement(
                formData,
                property.OriginalName,
                value,
                property.Type);
            var isPresent = new BinaryExpressionNode(
                new BinaryExpressionNode(value, BinaryOperator.NotEqual, Id("undefined")),
                BinaryOperator.LogicalAnd,
                new BinaryExpressionNode(value, BinaryOperator.NotEqual, Id("null")));
            statements.Add(new IfStatementNode(
                isPresent,
                new BlockNode(ImmutableList.Create<StatementNode>(append))));
        }

        return formData;
    }

    /// <summary>
    /// Builds one multipart append statement, preserving repeated wire fields for array-valued properties.
    /// </summary>
    /// <param name="formData">The <c>FormData</c> local receiving the value.</param>
    /// <param name="wireName">The original OpenAPI form field name.</param>
    /// <param name="value">The property value expression.</param>
    /// <param name="type">The property type that controls scalar/binary conversion.</param>
    /// <returns>A scalar append or a <c>for...of</c> statement for arrays.</returns>
    private static StatementNode BuildMultipartAppendStatement(
        IdentifierExpressionNode formData,
        string wireName,
        MemberAccessExpressionNode value,
        IrType type)
    {
        if (TryGetMultipartArrayElementType(type, out var elementType))
        {
            var item = Id("item");
            var appendItem = BuildMultipartAppendCall(formData, wireName, item, elementType);
            return new ForEachStatementNode(
                new IdentifierNode("item"),
                value,
                new BlockNode(ImmutableList.Create<StatementNode>(appendItem)));
        }

        return BuildMultipartAppendCall(formData, wireName, value, type);
    }

    /// <summary>
    /// Builds the <c>FormData.append</c> call for one non-null scalar or binary multipart value.
    /// </summary>
    /// <param name="formData">The destination <c>FormData</c> local.</param>
    /// <param name="wireName">The original OpenAPI form field name.</param>
    /// <param name="value">The value expression to append.</param>
    /// <param name="type">The value type that determines binary versus text encoding.</param>
    /// <returns>The append expression statement.</returns>
    private static ExpressionStatementNode BuildMultipartAppendCall(
        IdentifierExpressionNode formData,
        string wireName,
        ExpressionNode value,
        IrType type)
    {
        ExpressionNode appendValue = IsMultipartBinary(type)
            ? value
            : IsMultipartStructured(type)
                ? ExpressionBuilders.BuildJsonStringify(value)
                : new CallExpressionNode(Id("String"), ImmutableList.Create(value));
        return new ExpressionStatementNode(new CallExpressionNode(
            Member(formData, "append"),
            ImmutableList.Create<ExpressionNode>(Str(wireName), appendValue)));
    }

    /// <summary>
    /// Determines whether the supplied multipart property must be appended as a binary value rather than converted
    /// to text.
    /// </summary>
    /// <param name="type">The property type from the operation model.</param>
    /// <returns><see langword="true"/> for binary/file values; otherwise <see langword="false"/>.</returns>
    private static bool IsMultipartBinary(IrType type)
    {
        while (type is IrNullableType { InnerType: var nullableType })
        {
            type = nullableType;
        }

        while (type is IrOptionalType { InnerType: var optionalType })
        {
            type = optionalType;
        }

        return type is IrPrimitiveType
        {
            PrimitiveKind: IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File,
        };
    }

    /// <summary>
    /// Determines whether a multipart field has structured JSON semantics rather than scalar text semantics.
    /// </summary>
    /// <param name="type">The property or array-element type to inspect.</param>
    /// <returns><see langword="true"/> when the field must be serialized with <c>JSON.stringify</c>.</returns>
    private static bool IsMultipartStructured(IrType type)
    {
        while (type is IrNullableType { InnerType: var nullableType })
        {
            type = nullableType;
        }

        while (type is IrOptionalType { InnerType: var optionalType })
        {
            type = optionalType;
        }

        return type is IrModelReference or IrDictionaryType or IrUnionType or IrIntersectionType;
    }

    /// <summary>
    /// Resolves the element type of a nullable/optional multipart array.
    /// </summary>
    /// <param name="type">The property type to inspect.</param>
    /// <param name="elementType">The array element type when the property is an array.</param>
    /// <returns><see langword="true"/> when <paramref name="type"/> is an array; otherwise <see langword="false"/>.</returns>
    private static bool TryGetMultipartArrayElementType(IrType type, out IrType elementType)
    {
        while (type is IrNullableType { InnerType: var nullableType })
        {
            type = nullableType;
        }

        while (type is IrOptionalType { InnerType: var optionalType })
        {
            type = optionalType;
        }

        if (type is IrArrayType array)
        {
            elementType = array.ElementType;
            return true;
        }

        elementType = type;
        return false;
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

    private static ExpressionNode? BuildFetchHeadersObject(
        ExpressionNode? headersSource,
        bool hasJsonBody)
    {
        if (!hasJsonBody && headersSource == null)
            return null;

        if (hasJsonBody && headersSource == null)
        {
            return new ObjectLiteralExpressionNode(new[]
            {
                new PropertyAssignmentNode(
                    new IdentifierNode("'Content-Type'"),
                    new LiteralExpressionNode("application/json", LiteralKind.String))
            }.ToImmutableList());
        }

        if (!hasJsonBody && headersSource != null)
            return CastToHeadersInit(headersSource);

        var placeholder = new IdentifierNode("_");
        return new ObjectLiteralExpressionNode(new[]
        {
            new PropertyAssignmentNode(placeholder, CastToHeadersInit(headersSource!), isSpread: true),
            new PropertyAssignmentNode(
                new IdentifierNode("'Content-Type'"),
                new LiteralExpressionNode("application/json", LiteralKind.String))
        }.ToImmutableList(), preferMultilineFormat: true);
    }

    private static CastExpressionNode CastToHeadersInit(ExpressionNode source)
    {
        // {Op}HeaderParams интерфейс не имеет index-signature, а HeadersInit
        // требует Record<string, string>. Делаем `as HeadersInit`-cast.
        return new CastExpressionNode(
            new SimpleTypeNode(new IdentifierNode("HeadersInit"), isPrimitive: false),
            source,
            useAsOperator: true);
    }

    /// <summary>Builds a fetch request and handles bodyless successes before parsing JSON.</summary>
    /// <param name="operation">The operation whose request and responses are emitted.</param>
    /// <returns>The endpoint function body.</returns>
    private BlockNode BuildFetchFunctionBody(IrOperation operation)
    {
        var statements = new List<StatementNode>();
        var ctx = PrepareHttpParamSourcesWithOptionalZod(operation);
        statements.AddRange(ctx.LeadingStatements);

        var axiosBuilder = new Axios.Ast.Builders.AxiosAstBuilder(_namingConverter, BuildType);
        var pathUrlExpr = axiosBuilder.BuildUrlWithSource(operation, ctx.RouteSource);
        // Explicit URLSearchParams build (shared with the Next.js transport): skips absent optional params
        // (no `name=undefined`) and lets URLSearchParams handle encoding.
        var requestUrlExpr = TypeScriptQueryString.AppendQueryToUrl(
            operation, pathUrlExpr, ctx.QuerySource, ctx.QueryMayBeUndefined, statements);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("requestUrl"),
            type: null,
            initializer: requestUrlExpr));
        statements.Add(CreateBlankLineStatement());

        var methodUpper = operation.HttpMethod.ToUpperInvariant();
        var methodLiteral = new LiteralExpressionNode(methodUpper, LiteralKind.String);

        var hasJsonBody = operation.RequestBody != null && operation.RequestBody.IsJson && ctx.BodySource != null;
        var headersExpr = BuildFetchHeadersObject(ctx.HeadersSource, hasJsonBody);

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
            // Не-JSON тело: form-data / url-encoded (params.form) или иной content-type,
            // напр. application/octet-stream (params.body). Типизированный источник (узкий
            // интерфейс) не совпадает с BodyInit — кастуем через unknown, runtime строит
            // FormData/Blob/stream сам.
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
        var fetchCall = new CallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode(fetchName)),
            new[]
            {
                new IdentifierExpressionNode(new IdentifierNode("requestUrl")),
                new IdentifierExpressionNode(new IdentifierNode("init"))
            }.ToImmutableList());

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("response"),
            type: null,
            initializer: new AwaitExpressionNode(fetchCall)));
        statements.Add(CreateBlankLineStatement());

        // Fetch must handle documented bodyless successes before attempting response.json().
        var hasTypedResponses = operation.Responses.Any(r => r.Type != null);
        foreach (var bodyless in operation.Responses.Where(r => r.Type is null
            && int.TryParse(r.StatusCode, out var code) && code >= 200 && code < 300))
        {
            var code = int.Parse(bodyless.StatusCode);
            ExpressionNode? returnValue = hasTypedResponses
                ? new ObjectLiteralExpressionNode(new[]
                {
                    new PropertyAssignmentNode(new IdentifierNode("status"), new LiteralExpressionNode(code, LiteralKind.Number)),
                    new PropertyAssignmentNode(new IdentifierNode("body"), new IdentifierExpressionNode(new IdentifierNode("undefined"))),
                    new PropertyAssignmentNode(new IdentifierNode("isSuccessful"), new LiteralExpressionNode(true, LiteralKind.Boolean)),
                    new PropertyAssignmentNode(new IdentifierNode("isFailed"), new LiteralExpressionNode(false, LiteralKind.Boolean))
                }.ToImmutableList())
                : null;
            var status = new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("response")), new IdentifierNode("status"));
            var matches = new BinaryExpressionNode(status, BinaryOperator.Equal, new LiteralExpressionNode(code, LiteralKind.Number));
            statements.Add(new IfStatementNode(matches,
                new BlockNode(ImmutableList.Create<StatementNode>(new ReturnStatementNode(returnValue))), null));
        }
        if (!hasTypedResponses)
            return new BlockNode(statements.ToImmutableList());

        var jsonCall = new CallExpressionNode(
            new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("response")),
                new IdentifierNode("json")),
            ImmutableList<ExpressionNode>.Empty);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("responseData"),
            type: null,
            initializer: new AwaitExpressionNode(jsonCall)));
        statements.Add(CreateBlankLineStatement());

        var useResultType = operation.Responses.Any(r => r.Type != null);
        var operationReturnType = useResultType
            ? new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: false)
            : ExtractReturnType(operation.Responses);

        if (useResultType)
        {
            var responseStatusRef = new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("response")),
                new IdentifierNode("status"));
            var responseDataRef = new IdentifierExpressionNode(new IdentifierNode("responseData"));
            var responsesWithType = operation.Responses
                .Where(r => r.Type != null && int.TryParse(r.StatusCode, out _))
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
