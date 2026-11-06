using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Ast.Nodes.UE;
using ValIO.Generators.Cpp.Ast.Nodes.UE.Specifiers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders;

/// <summary>Builds Unreal result headers with shared document declaration indexes.</summary>
public sealed class UEResultAstBuilder : AstBuilderBase
{
    /// <summary>Maps types using the shared document context.</summary>
    private readonly UETypeMapper _typeMapper;
    /// <summary>Supplies Unreal identifier and path naming.</summary>
    private readonly UENamingConvention _naming;
    /// <summary>Supplies the selected Unreal generation options.</summary>
    private readonly CppGenerationOptions _options;

    /// <summary>Requires one document context shared with the type mapper.</summary>
    /// <param name="typeMapper">The document-aware Unreal type mapper.</param>
    /// <param name="naming">The Unreal identifier and file naming rules.</param>
    /// <param name="options">The Unreal generation options.</param>
    /// <param name="documentContext">The mandatory finalized document indexes.</param>
    /// <exception cref="ArgumentNullException">A dependency is null.</exception>
    /// <exception cref="ArgumentException">The mapper belongs to a different document context.</exception>
    public UEResultAstBuilder(UETypeMapper typeMapper, UENamingConvention naming, CppGenerationOptions options, IrDocumentContext documentContext) : base(documentContext)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        if (!ReferenceEquals(typeMapper.DocumentContext, documentContext))
            throw new ArgumentException("The mapper and builder must share a document context.", nameof(typeMapper));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Builds the operation header using this document's enum references.</summary>
    /// <param name="operation">The operation contract to emit.</param>
    /// <param name="clientName">The owning client's generated name.</param>
    /// <returns>The generated Unreal header AST.</returns>
    public CppHeaderFileNode Build(IrOperation operation, string clientName)
    {
        var enumNames = EnumNames;
        var resultName = _naming.ResultStructName(operation.MethodName);
        // File name uses the ORIGINAL (pre-affix) method name so the emitted file/include doesn't change with affixes.
        var fileName = _naming.ResultFileName(operation.OriginalMethodName);
        var category = _naming.OperationTypeCategory(_options.BlueprintCategory, clientName, operation.MethodName);

        var fields = new List<UPropertyFieldNode>();

        // StatusCode — всегда присутствует
        fields.Add(new UPropertyFieldNode(
            name: new IdentifierNode("StatusCode"),
            type: new SimpleTypeNode(new IdentifierNode("int32"), isPrimitive: true),
            specifiers: UPropertySpecifiers.ResultField(category),
            defaultValue: new LiteralExpressionNode(0, LiteralKind.Number),
            documentation: new CommentNode("@brief HTTP status code.\n@details Use IsSuccess() to check if 2xx.", CommentKind.MultiLine)));

        // Response — всегда присутствует (сырой ответ)
        fields.Add(new UPropertyFieldNode(
            name: new IdentifierNode("Response"),
            type: new SimpleTypeNode(new IdentifierNode("FValIOResponse")),
            specifiers: UPropertySpecifiers.ResultField(category),
            documentation: new CommentNode("@brief Raw HTTP response.\n@details Contains StatusCode, headers, body.", CommentKind.MultiLine)));

        // SuccessBody — присутствует если есть 2xx ответ с телом
        var successResponse = FindSuccessResponse(operation);
        if (successResponse?.Type != null)
        {
            fields.Add(new UPropertyFieldNode(
                name: new IdentifierNode("SuccessBody"),
                type: _typeMapper.MapType(successResponse.Type),
                specifiers: UPropertySpecifiers.ResultField(category),
                documentation: new CommentNode(
                    $"@brief Success response body.\n@details Valid when StatusCode is 2xx ({successResponse.StatusCode}).",
                    CommentKind.MultiLine)));
        }

        // Body per status code — для каждого не-успешного ответа из схемы
        var nonSuccessResponses = GetNonSuccessResponses(operation);
        foreach (var r in nonSuccessResponses)
        {
            var fieldName = _naming.ResultBodyFieldName(r.StatusCode);
            var bodyType = r.Type != null ? _typeMapper.MapType(r.Type) : new SimpleTypeNode(new IdentifierNode("FString"));
            fields.Add(new UPropertyFieldNode(
                name: new IdentifierNode(fieldName),
                type: bodyType,
                specifiers: UPropertySpecifiers.ResultField(category),
                documentation: new CommentNode(
                    $"@brief Error response body.\n@details Valid when StatusCode is {r.StatusCode}.{(string.IsNullOrWhiteSpace(r.Description) ? "" : $" {r.Description}")}",
                    CommentKind.MultiLine)));
        }

        // IsSuccess() inline метод
        var isSuccessMethod = new CppFreeFunctionNode(
            name: new IdentifierNode("IsSuccess"),
            returnType: new SimpleTypeNode(new IdentifierNode("bool"), isPrimitive: true),
            isDeclarationOnly: false); // будет отрендерен inline

        var structNode = new UStructDeclarationNode(
            name: new IdentifierNode(resultName),
            specifiers: new UStructSpecifiers { BlueprintType = true, Category = category },
            fields: fields,
            inlineMethods: new[] { isSuccessMethod },
            documentation: new CommentNode(DoxygenCommentBuilder.BuildResultStructDocumentation(operation, resultName), CommentKind.MultiLine));

        var includes = new List<CppIncludeDirectiveNode>();
        if (_options.AddUnrealEngineIncludesInHeaders)
            includes.Add(new("CoreMinimal.h"));
        includes.Add(new(_options.ValIOResponseIncludePath)); // Result использует FValIOResponse — всегда нужен

        // Result в Public/Endpoints/{OpId}/, Models/Enums в Public/Models, Public/Enums
        if (successResponse?.Type is IrModelReference successModelRef)
        {
            // enum/model classification uses the affixed name; the include PATH uses the ORIGINAL name.
            var path = enumNames.Contains(successModelRef.ModelName)
                ? _naming.EnumIncludePathFromEndpoint(successModelRef.OriginalModelName)
                : _naming.ModelIncludePathFromEndpoint(successModelRef.OriginalModelName);
            includes.Add(new CppIncludeDirectiveNode(path));
        }

        foreach (var r in nonSuccessResponses)
        {
            if (r.Type is IrModelReference modelRef)
            {
                // enum/model classification uses the affixed name; the include PATH uses the ORIGINAL name.
                var path = enumNames.Contains(modelRef.ModelName)
                    ? _naming.EnumIncludePathFromEndpoint(modelRef.OriginalModelName)
                    : _naming.ModelIncludePathFromEndpoint(modelRef.OriginalModelName);
                includes.Add(new CppIncludeDirectiveNode(path));
            }
        }

        // .generated.h должен быть всегда последним (требование UHT)
        includes.Add(new CppIncludeDirectiveNode(fileName.Replace(".h", _options.GeneratedHeaderSuffix)));

        return new CppHeaderFileNode(
            fileName: fileName,
            headerComment: _options.Copyright != null ? new CommentNode(_options.Copyright, CommentKind.MultiLine) : null,
            includes: includes,
            declarations: new AstNode[] { structNode });
    }

    /// <summary>
    /// Определяет есть ли успешный тип ответа (для SuccessBody)
    /// </summary>
    public bool HasSuccessBody(IrOperation operation)
    {
        return FindSuccessResponse(operation)?.Type != null;
    }

    /// <summary>
    /// Находит первый 2xx ответ с телом
    /// </summary>
    private static IrResponse? FindSuccessResponse(IrOperation operation)
    {
        return operation.Responses.FirstOrDefault(r =>
            r.StatusCode.StartsWith('2') && r.Type != null);
    }

    /// <summary>
    /// Возвращает все не-успешные ответы (4xx, 5xx, default)
    /// </summary>
    public static IReadOnlyList<IrResponse> GetNonSuccessResponses(IrOperation operation)
    {
        return operation.Responses
            .Where(r => !r.StatusCode.StartsWith('2'))
            .ToList();
    }
}
