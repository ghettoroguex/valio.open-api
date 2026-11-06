using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Ast.Nodes.UE;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders;

/// <summary>
/// Строитель AST для UE enum (UENUM) из IR представления.
/// Генерирует CppHeaderFileNode, содержащий UEnumDeclarationNode.
/// </summary>
public sealed class UEEnumAstBuilder
{
    private readonly UENamingConvention _naming;
    private readonly CppGenerationOptions _options;

    public UEEnumAstBuilder(UENamingConvention naming, CppGenerationOptions options)
    {
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>Builds an Unreal enum header with documentation even when schema prose is absent.</summary>
    /// <param name="irEnum">The enum declaration to emit.</param>
    /// <returns>The generated Unreal enum header AST.</returns>
    public CppHeaderFileNode Build(IrEnum irEnum)
    {
        var enumName = _naming.EnumName(irEnum.Name);
        // File name uses the ORIGINAL (pre-affix) name so the emitted file/include doesn't change with affixes.
        var fileName = _naming.EnumFileName(irEnum.OriginalName);

        // UE Blueprints требуют uint8 как базовый тип для UENUM
        var underlyingType = new SimpleTypeNode(new IdentifierNode("uint8"), isPrimitive: true);

        var values = irEnum.Values
            .Select((v, i) => new UEnumValueNode(
                name: new IdentifierNode(_naming.PropertyName(v.Name)),
                value: new LiteralExpressionNode(i, LiteralKind.Number),
                displayName: v.Description ?? v.Name))
            .ToList();

        var category = _naming.EnumTypeCategory(_options.BlueprintCategory);
        var enumNode = new UEnumDeclarationNode(
            name: new IdentifierNode(enumName),
            underlyingType: underlyingType,
            values: values,
            category: category,
            documentation: StdDocumentation.Create($"{irEnum.Name} enum.", irEnum.Description));

        var includes = new List<CppIncludeDirectiveNode>();
        if (_options.AddUnrealEngineIncludesInHeaders)
            includes.Add(new("CoreMinimal.h"));
        includes.Add(new(fileName.Replace(".h", _options.GeneratedHeaderSuffix))); // .generated.h последним (UHT)

        return new CppHeaderFileNode(
            fileName: fileName,
            headerComment: _options.Copyright != null ? new CommentNode(_options.Copyright, CommentKind.MultiLine) : null,
            includes: includes,
            declarations: new AstNode[] { enumNode });
    }
}
