using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.Msw.Ast.Builders;

/// <summary>
/// Builds an MSW (Mock Service Worker) handlers file as TypeScript AST.
/// Output: a single <c>mocks/handlers.ts</c> exporting <c>const handlers = [...]</c>
/// with one <c>http.{verb}("/path", () =&gt; HttpResponse.json({} as Model[, { status }]))</c>
/// entry per IR operation.
///
/// Stub payloads are intentionally minimal — <c>{} as ModelName</c> relies on the user
/// to override individual handlers in tests. Path parameters in IR (e.g. <c>{petId}</c>)
/// are rewritten to MSW colon syntax (<c>:petId</c>).
/// </summary>
public sealed class MswHandlersAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;


    /// <summary>Creates a MswHandlersAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public MswHandlersAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
    }

    public SourceFileNode BuildHandlersSourceFile(IrDocument document, FileOrganization fileOrganization)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(fileOrganization);

        // Collect every operation across all clients so the file is a single flat
        // handler array — matches MSW's setupWorker(...handlers) ergonomics.
        var allOperations = document.Clients.SelectMany(client => client.Operations).ToList();

        // Model types needed for the typed casts. SortedSet keeps imports deterministic.
        var referencedModels = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var op in allOperations)
        {
            var model = FirstSuccessResponseModelName(op);
            if (model != null)
                referencedModels.Add(model);
        }

        var imports = ImmutableList.CreateBuilder<ImportNode>();
        imports.Add(new ImportNode(
            items: ImmutableList.Create(new IdentifierNode("http"), new IdentifierNode("HttpResponse")),
            from: "msw",
            kind: ImportKind.Named));

        foreach (var modelName in referencedModels)
        {
            imports.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(modelName)),
                from: BuildModelImportPath(modelName, fileOrganization),
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        var handlerElements = allOperations
            .Select(BuildHandlerExpression)
            .ToImmutableList();

        var arrayLiteral = new ArrayLiteralExpressionNode(
            elements: handlerElements,
            preferMultilineFormat: handlerElements.Count > 1);

        var declaration = new VariableDeclarationNode(
            name: new IdentifierNode("handlers"),
            declarationKind: VariableDeclarationKind.Const,
            initializer: arrayLiteral,
            isExported: true);

        return new SourceFileNode(
            imports: imports.ToImmutable(),
            declarations: ImmutableList.Create<DeclarationNode>(declaration),
            fileDocumentation: TypeScriptDoc.FileHeader(
                "handlers.ts",
                "mocks/handlers",
                "Mock Service Worker (MSW) request handlers for all API operations."));
    }

    /// <summary>
    /// Build a single <c>http.{verb}("/path", () =&gt; HttpResponse.json(body, options))</c>
    /// call expression for one operation.
    /// </summary>
    private ExpressionNode BuildHandlerExpression(IrOperation operation)
    {
        var verb = operation.HttpMethod.ToLowerInvariant();
        var pathWithColons = ConvertPathParamsToColonSyntax(operation.Path);

        var modelName = FirstSuccessResponseModelName(operation);
        var statusCode = FirstSuccessResponseStatusCode(operation);

        // Body argument: {} cast to the response model so HttpResponse.json infers a
        // narrow type. If there's no typed success response, we drop the cast and
        // emit a bare {} — MSW accepts JsonBodyType including arbitrary objects.
        ExpressionNode bodyArgument;
        if (modelName != null)
        {
            bodyArgument = new CastExpressionNode(
                targetType: new SimpleTypeNode(new IdentifierNode(modelName), isPrimitive: false),
                expression: new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty),
                useAsOperator: true);
        }
        else
        {
            bodyArgument = new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty);
        }

        // HttpResponse.json arguments. For non-200 success codes (e.g. 201 Created),
        // pass `{ status: <code> }` so the mocked response carries the right status.
        var httpResponseArgs = ImmutableList.CreateBuilder<ExpressionNode>();
        httpResponseArgs.Add(bodyArgument);
        if (statusCode is int code && code != 200)
        {
            httpResponseArgs.Add(new ObjectLiteralExpressionNode(ImmutableList.Create(
                new PropertyAssignmentNode(
                    new IdentifierNode("status"),
                    new LiteralExpressionNode(code, LiteralKind.Number)))));
        }

        var httpResponseCall = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("HttpResponse")),
                new IdentifierNode("json")),
            arguments: httpResponseArgs.ToImmutable());

        // The MSW resolver is `(): HttpResponse => HttpResponse.json(...)`. We use the
        // expression-bodied arrow so the call expression's multiline arg formatting
        // stays readable inside the array literal.
        var resolver = new ArrowFunctionExpressionNode(
            parameters: ImmutableList<ParameterDeclarationNode>.Empty,
            bodyExpression: httpResponseCall);

        return new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("http")),
                new IdentifierNode(verb)),
            arguments: ImmutableList.Create<ExpressionNode>(
                new LiteralExpressionNode(pathWithColons, LiteralKind.String),
                resolver));
    }

    /// <summary>
    /// Rewrite OpenAPI-style path tokens (<c>/pets/{petId}</c>) to MSW colon syntax
    /// (<c>/pets/:petId</c>). MSW's <c>http</c> matcher uses path-to-regexp under the hood
    /// and does not accept curly-brace tokens.
    /// </summary>
    private static string ConvertPathParamsToColonSyntax(string irPath)
    {
        return Regex.Replace(irPath, "{([^}]+)}", ":$1");
    }

    private static string? FirstSuccessResponseModelName(IrOperation operation)
    {
        foreach (var response in operation.Responses)
        {
            if (!int.TryParse(response.StatusCode, out var code))
                continue;
            if (code < 200 || code >= 300)
                continue;
            if (response.Type is IrModelReference modelRef)
                return modelRef.ModelName;
        }
        return null;
    }

    private static int? FirstSuccessResponseStatusCode(IrOperation operation)
    {
        foreach (var response in operation.Responses)
        {
            if (!int.TryParse(response.StatusCode, out var code))
                continue;
            if (code >= 200 && code < 300)
                return code;
        }
        return null;
    }

    private string BuildModelImportPath(string modelName, FileOrganization fileOrganization)
    {
        // mocks/handlers.ts lives one level under the output root, so we always go
        // up one level then into the models path. Mirrors how other builders compute
        // schema import paths relative to per-operation files.
        var modelsPath = fileOrganization.ModelsPath ?? "models";
        var modelFileName = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, modelName));
        return fileOrganization.SeparateModels
            ? $"../{modelsPath}/{modelFileName}/{modelFileName}"
            : $"../{modelsPath}/{modelFileName}";
    }
}
