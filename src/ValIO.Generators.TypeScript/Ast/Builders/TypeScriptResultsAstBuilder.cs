using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.Models;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Строитель AST для Result типов (discriminated union по status codes).
/// По аналогии с UE C++: каждый status code → свой variant { status: N; body: T }.
/// </summary>
public sealed class TypeScriptResultsAstBuilder : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly Func<IrType, TypeNode> _typeBuilder;
    private readonly string _resultSuffix;
    private readonly string _resultPrefix;


    /// <summary>Creates a TypeScriptResultsAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="typeBuilder">The language type mapping function.</param>
    /// <param name="resultSuffix">The optional operation result type suffix.</param>
    /// <param name="resultPrefix">The optional operation result type prefix.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public TypeScriptResultsAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter,
        Func<IrType, TypeNode> typeBuilder,
        string? resultSuffix = null,
        string? resultPrefix = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
        _resultSuffix = ResolveResultSuffix(resultSuffix);
        _resultPrefix = resultPrefix?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Normalises a configured result-type suffix to the value used in generated names:
    /// null/whitespace falls back to <see cref="TypeScriptOptionIds.ResultSuffix.Default"/>
    /// (<c>Results</c>); otherwise the trimmed value with its first character capitalised.
    /// Shared so every result-name producer resolves the suffix identically.
    /// </summary>
    public static string ResolveResultSuffix(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            return TypeScriptOptionIds.ResultSuffix.Default;

        var trimmed = configured.Trim();
        return char.ToUpperInvariant(trimmed[0]) + trimmed[1..];
    }

    /// <summary>
    /// Имя Result типа (discriminated union alias): {OperationName}{ResultSuffix} (default "Results").
    /// </summary>
    public string ResultTypeName(string operationName)
    {
        return $"{_resultPrefix}{_namingConverter.ToPascalCase(operationName)}{_resultSuffix}";
    }

    /// <summary>
    /// Composes the result-union type name from its parts (prefix + Pascal(op) + suffix) without a
    /// builder instance. Shared so inline producers (e.g. the client return type) stay identical to
    /// <see cref="ResultTypeName"/>.
    /// </summary>
    public static string ComposeResultTypeName(INamingConverter namingConverter, string operationName, string? resultPrefix, string? resultSuffix)
        => $"{resultPrefix?.Trim() ?? string.Empty}{namingConverter.ToPascalCase(operationName)}{ResolveResultSuffix(resultSuffix)}";

    /// <summary>
    /// Имя variant интерфейса: {OperationName}Result{StatusCode}. The per-status variants keep the
    /// fixed <c>Result</c> base regardless of the configured union-alias suffix.
    /// </summary>
    public string ResultVariantInterfaceName(string operationName, string statusCode)
        => ResultVariantName(_namingConverter, operationName, statusCode);

    /// <summary>
    /// Единый источник имени status-варианта результата (<c>{Op}Result{Status}</c>), общий для
    /// интерфейсов результата, схем (<c>+ "Schema"</c>) и клиента.
    /// </summary>
    public static string ResultVariantName(INamingConverter namingConverter, string operationName, string statusCode)
    {
        var suffix = statusCode == "default" ? "Default" : statusCode;
        return $"{namingConverter.ToPascalCase(operationName)}Result{suffix}";
    }

    /// <summary>
    /// Builds a result union including documented bodyless statuses when any response has a body.
    /// </summary>
    /// <param name="operation">The operation whose responses define the union.</param>
    /// <param name="importPath">The optional import path override.</param>
    /// <param name="fileOrganization">The generated model layout settings.</param>
    /// <param name="currentOperationName">The operation name used for generated result declarations.</param>
    /// <returns>The result variants and union source file.</returns>
    public SourceFileNode BuildResultsSourceFile(
        IrOperation operation,
        string? importPath,
        FileOrganization? fileOrganization,
        string currentOperationName)
    {


        var declarations = new List<DeclarationNode>();
        var imports = new List<ImportNode>();

        var resultTypeName = ResultTypeName(currentOperationName);
        var responsesWithType = operation.Responses.Where(r => r.Type != null).ToList();

        if (responsesWithType.Count == 0)
        {
            var voidAlias = new TypeAliasDeclarationNode(
                new IdentifierNode(resultTypeName),
                new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true),
                new CommentNode($"Result type for {currentOperationName}. No response body defined."));
            declarations.Add(voidAlias);
        }
        else
        {
            var variantTypes = new List<TypeNode>();

            foreach (var response in operation.Responses)
            {
                var statusLiteral = ParseStatusCodeLiteral(response.StatusCode);
                var bodyType = response.Type == null
                    ? new SimpleTypeNode(new IdentifierNode("undefined"), isPrimitive: true)
                    : _typeBuilder(response.Type);
                var isSuccess = IsSuccessStatusCode(response.StatusCode);

                var variantInterfaceName = ResultVariantInterfaceName(currentOperationName, response.StatusCode);
                var members = new List<DeclarationNode>
                {
                    new PropertySignatureNode(
                        new IdentifierNode("status"),
                        statusLiteral,
                        isRequired: true,
                        documentation: new CommentNode($"HTTP {response.StatusCode}")),
                    new PropertySignatureNode(
                        new IdentifierNode("body"),
                        bodyType,
                        isRequired: true,
                        documentation: new CommentNode(response.Description ?? $"Response body for {response.StatusCode}")),
                    new PropertySignatureNode(
                        new IdentifierNode("isSuccessful"),
                        new SimpleTypeNode(new IdentifierNode("boolean"), isPrimitive: true, literalValue: isSuccess),
                        isRequired: true,
                        documentation: new CommentNode(isSuccess ? "True — response is 2xx" : "False — response is not 2xx")),
                    new PropertySignatureNode(
                        new IdentifierNode("isFailed"),
                        new SimpleTypeNode(new IdentifierNode("boolean"), isPrimitive: true, literalValue: !isSuccess),
                        isRequired: true,
                        documentation: new CommentNode(isSuccess ? "False — response is 2xx" : "True — response is not 2xx"))
                };
                var variantInterface = new InterfaceDeclarationNode(
                    name: new IdentifierNode(variantInterfaceName),
                    accessModifier: AccessModifier.Public,
                    baseTypes: ImmutableList<TypeNode>.Empty,
                    members: members.ToImmutableList(),
                    documentation: new CommentNode($"Response variant for status {response.StatusCode}"));

                declarations.Add(variantInterface);
                variantTypes.Add(new SimpleTypeNode(new IdentifierNode(variantInterfaceName), isPrimitive: false));
            }

            // Catch-all variant для неизвестных status codes: { status: number; body: unknown }
            var otherVariantName = $"{_namingConverter.ToPascalCase(currentOperationName)}ResultOther";
            var otherVariantInterface = new InterfaceDeclarationNode(
                name: new IdentifierNode(otherVariantName),
                accessModifier: AccessModifier.Public,
                baseTypes: ImmutableList<TypeNode>.Empty,
                members: new List<DeclarationNode>
                {
                    new PropertySignatureNode(
                        new IdentifierNode("status"),
                        new SimpleTypeNode(new IdentifierNode("number"), isPrimitive: true),
                        isRequired: true,
                        documentation: new CommentNode("HTTP status code not in schema (e.g. 204, 301, 503)")),
                    new PropertySignatureNode(
                        new IdentifierNode("body"),
                        new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true),
                        isRequired: true,
                        documentation: new CommentNode("Response body for unhandled status")),
                    new PropertySignatureNode(
                        new IdentifierNode("isSuccessful"),
                        // Literal `false` (not `boolean`) so this catch-all is a discriminable failure
                        // member: `if (r.isSuccessful)` excludes it and narrows to the typed variant(s).
                        new SimpleTypeNode(new IdentifierNode("boolean"), isPrimitive: true, literalValue: false),
                        isRequired: true,
                        documentation: new CommentNode("Always false — undocumented status codes are treated as non-success")),
                    new PropertySignatureNode(
                        new IdentifierNode("isFailed"),
                        new SimpleTypeNode(new IdentifierNode("boolean"), isPrimitive: true, literalValue: true),
                        isRequired: true,
                        documentation: new CommentNode("Always true — undocumented status codes are treated as failures"))
                }.ToImmutableList(),
                documentation: new CommentNode("Response for status codes not explicitly defined in OpenAPI schema. " +
                    "Treated as a failure (isSuccessful: false) so the union stays discriminable; an undocumented 2xx (e.g. 204) lands here."));
            declarations.Add(otherVariantInterface);
            variantTypes.Add(new SimpleTypeNode(new IdentifierNode(otherVariantName), isPrimitive: false));

            var unionType = new UnionTypeNode(variantTypes);

            var resultTypeAlias = new TypeAliasDeclarationNode(
                new IdentifierNode(resultTypeName),
                unionType,
                new CommentNode($"Discriminated union result for {currentOperationName}. Check status to narrow type."));
            declarations.Add(resultTypeAlias);

            var modelRefs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var response in responsesWithType)
            {
                if (response.Type != null)
                    ExtractModelReferences(response.Type, modelRefs);
            }

            var modelsPath = fileOrganization?.ModelsPath ?? "models";
            foreach (var modelName in modelRefs.OrderBy(n => n, StringComparer.Ordinal))
            {
                var modelFileName = _namingConverter.ToCamelCase(OriginalNameMap.GetTypeOriginalName(DocumentContext, modelName));
                var relativePath = $"../../{modelsPath}/{modelFileName}/{modelFileName}";
                imports.Add(new ImportNode(
                    items: new[] { new IdentifierNode(modelName) }.ToImmutableList(),
                    from: relativePath,
                    kind: ImportKind.Named,
                    isTypeOnly: true));
            }
        }

        var exports = declarations.Select(d => new ExportNode(d, ExportKind.Named)).ToImmutableList();

        var fileBase = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        return new SourceFileNode(
            imports: imports.ToImmutableList(),
            exports: exports,
            declarations: declarations.ToImmutableList(),
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{fileBase}.results.ts",
                $"endpoints/{fileBase}",
                $"Discriminated-union result types for the {currentOperationName} operation."));
    }

    /// <summary>
    /// Определяет, является ли status code успешным (2xx)
    /// </summary>
    private static bool IsSuccessStatusCode(string statusCode)
    {
        if (statusCode == "default") return false;
        return int.TryParse(statusCode, out var code) && code >= 200 && code < 300;
    }

    /// <summary>
    /// Парсит status code в TypeNode для literal (200, 400, или number для default)
    /// </summary>
    private static TypeNode ParseStatusCodeLiteral(string statusCode)
    {
        if (statusCode == "default")
            return new SimpleTypeNode(new IdentifierNode("number"), isPrimitive: true);

        if (int.TryParse(statusCode, out var code))
        {
            return new SimpleTypeNode(
                new IdentifierNode("number"),
                isPrimitive: true,
                literalValue: code);
        }

        return new SimpleTypeNode(new IdentifierNode("number"), isPrimitive: true);
    }

    /// <summary>
    /// Есть ли у операции ответы с телом (для генерации results)
    /// </summary>
    public bool HasResponsesWithBody(IrOperation operation)
    {
        return operation.Responses.Any(r => r.Type != null);
    }

    private static void ExtractModelReferences(IrType type, HashSet<string> sink)
    {
        switch (type)
        {
            case IrModelReference modelRef:
                sink.Add(modelRef.ModelName);
                break;
            case IrArrayType arr:
                ExtractModelReferences(arr.ElementType, sink);
                break;
            case IrDictionaryType dict:
                ExtractModelReferences(dict.KeyType, sink);
                ExtractModelReferences(dict.ValueType, sink);
                break;
            case IrUnionType union:
                foreach (var t in union.Types) ExtractModelReferences(t, sink);
                break;
            case IrIntersectionType inter:
                foreach (var t in inter.Types) ExtractModelReferences(t, sink);
                break;
            case IrNullableType nul:
                ExtractModelReferences(nul.InnerType, sink);
                break;
            case IrOptionalType opt:
                ExtractModelReferences(opt.InnerType, sink);
                break;
        }
    }
}
