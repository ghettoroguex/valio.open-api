using System.Globalization;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders.Std;

/// <summary>
/// Builds standalone C++ enum declarations, collision-free wire converters, and separate JSON codecs.
/// </summary>
public sealed class StdEnumAstBuilder
{
    /// <summary>Names enum declarations and their separate codec files.</summary>
    private readonly StdNamingConvention _naming;

    /// <summary>Creates the enum builder with the generated naming convention.</summary>
    /// <param name="naming">The generated naming convention.</param>
    /// <exception cref="ArgumentNullException">The naming convention is missing.</exception>
    public StdEnumAstBuilder(StdNamingConvention naming)
    {
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    /// <summary>
    /// Builds a pure enum definition header without parsing, serialization, or third-party dependencies.
    /// </summary>
    /// <param name="irEnum">The enum contract.</param>
    /// <param name="rootNamespace">The generated namespace.</param>
    /// <returns>The JSON-independent enum header.</returns>
    public CppHeaderFileNode Build(IrEnum irEnum, string rootNamespace)
    {
        var enumName = _naming.EnumName(irEnum.Name);
        var isInteger = irEnum.BaseType == IrEnumBaseType.Integer;

        var members = new List<EnumMemberDeclarationNode>();
        for (var i = 0; i < irEnum.Values.Count; i++)
        {
            var value = irEnum.Values[i];
            var wire = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? value.Name;
            members.Add(new EnumMemberDeclarationNode(
                new IdentifierNode(_naming.EnumMemberName(value.Name)),
                isInteger ? IntegerWireLiteral(ParseIntegerWire(wire)) : new LiteralExpressionNode(i, LiteralKind.Number),
                wireValue: isInteger ? null : wire,
                documentation: StdDocumentation.Create($"Represents wire value {wire}.", value.Description)));
        }

        var enumNode = new EnumDeclarationNode(
            new IdentifierNode(enumName),
            underlyingType: new SimpleTypeNode(new IdentifierNode(isInteger ? IntegerWireType(irEnum) : "std::int32_t"), isPrimitive: true),
            members: members,
            documentation: StdDocumentation.Create($"Allowed values for {irEnum.OriginalName}.", irEnum.Description));

        var includes = new List<CppIncludeDirectiveNode>
        {
            new("cstdint", isSystemInclude: true),
        };

        return new CppHeaderFileNode(
            fileName: _naming.EnumFileName(irEnum.OriginalName),
            includes: includes,
            declarations:
            [
                new NamespaceDeclarationNode(new IdentifierNode(rootNamespace), [enumNode]),
            ]);
    }

    /// <summary>
    /// Builds enum JSON declarations and definitions using its named wire parser.
    /// </summary>
    /// <param name="irEnum">The enum contract.</param>
    /// <param name="rootNamespace">The generated namespace.</param>
    /// <param name="options">The configured JSON header paths.</param>
    /// <returns>The serializer header and source ASTs.</returns>
    public (CppHeaderFileNode Header, CppSourceFileNode Source) BuildJson(IrEnum irEnum, string rootNamespace, CppGenerationOptions options)
    {
        var enumName = _naming.EnumName(irEnum.Name);
        var isInteger = irEnum.BaseType == IrEnumBaseType.Integer;
        var wireCppType = isInteger ? IntegerWireType(irEnum) : "std::string";
        var wireFunctions = BuildWireFunctions(irEnum);

        var codecDeclarations = new DeclarationNode[]
        {
            new CppFreeFunctionNode(
                new IdentifierNode("to_json"),
                new RawTypeNode("void"),
                [
                    new CppParameterNode(new IdentifierNode("j"), new RawTypeNode("nlohmann::json"), isReference: true),
                    new CppParameterNode(new IdentifierNode("value"), new SimpleTypeNode(new IdentifierNode(enumName)), isConst: true, isReference: true),
                ],
                isDeclarationOnly: true,
                documentation: BuildCodecDocumentation(enumName, isToJson: true)),
            new CppFreeFunctionNode(
                new IdentifierNode("from_json"),
                new RawTypeNode("void"),
                [
                    new CppParameterNode(new IdentifierNode("j"), new RawTypeNode("const nlohmann::json"), isReference: true),
                    new CppParameterNode(new IdentifierNode("value"), new SimpleTypeNode(new IdentifierNode(enumName)), isReference: true),
                ],
                isDeclarationOnly: true,
                documentation: BuildCodecDocumentation(enumName, isToJson: false)),
        };

        var header = new CppHeaderFileNode(
            fileName: _naming.SerializerHeaderPath(irEnum.OriginalName),
            includes:
            [
                new(options.JsonFwdInclude, isSystemInclude: true),
                new(_naming.EnumHeaderPath(irEnum.OriginalName)),
                new("optional", isSystemInclude: true),
                new("string", isSystemInclude: true),
                new("string_view", isSystemInclude: true),
            ],
            declarations: [new NamespaceDeclarationNode(new IdentifierNode(rootNamespace),
            [
                .. codecDeclarations,
                .. wireFunctions.Select(function => new CppFreeFunctionNode(function.Name, function.ReturnType, function.Parameters, isDeclarationOnly: true, documentation: function.Documentation)),
            ])]);

        var toJsonBody = new BlockNode(
        [
            new ExpressionStatementNode(new BinaryExpressionNode(
                StdExpressions.Ident("j"),
                BinaryOperator.Assign,
                new CallExpressionNode(StdExpressions.Ident("ToWire"), [StdExpressions.Ident("value")]),
                isAssignment: true)),
        ]);

        var fromJsonBody = new BlockNode(
        [
            new VariableDeclarationStatementNode(
                new IdentifierNode("parsed"),
                new GenericTypeNode(new IdentifierNode("std::optional"), [new SimpleTypeNode(new IdentifierNode(enumName))]),
                new CallExpressionNode(
                    StdExpressions.Ident($"{enumName}FromWire"),
                    [new CallExpressionNode(
                        StdExpressions.Ident("ValIO::ReadJson"),
                        [StdExpressions.Ident("j")],
                        genericTypeName: wireCppType)])),
            new IfStatementNode(
                StdExpressions.Ident("parsed"),
                new BlockNode(
                [
                    new ExpressionStatementNode(new BinaryExpressionNode(
                        StdExpressions.Ident("value"),
                        BinaryOperator.Assign,
                        new UnaryExpressionNode(UnaryOperator.Dereference, StdExpressions.Ident("parsed")),
                        isAssignment: true)),
                ]),
                new BlockNode(
                [
                    new ThrowStatementNode(new CallExpressionNode(
                        BuildNlohmannOutOfRangeCreate(),
                        [
                            new LiteralExpressionNode("1701", LiteralKind.Number),
                            new LiteralExpressionNode($"unknown {enumName} wire value", LiteralKind.String),
                            StdExpressions.Ident("nullptr"),
                        ])),
                ])),
        ]);

        var source = new CppSourceFileNode(
            fileName: _naming.SerializerSourcePath(irEnum.OriginalName),
            includes:
            [
                new(_naming.SerializerHeaderPath(irEnum.OriginalName)),
                new(options.JsonInclude, isSystemInclude: true),
                new("Runtime/ValIOJsonValidation.h"),
            ],
            sections:
            [
                new CppSourceSection(members:
                [
                    new NamespaceDeclarationNode(new IdentifierNode(rootNamespace),
                    [
                        .. wireFunctions,
                        new CppFreeFunctionNode(
                            new IdentifierNode("to_json"),
                            new RawTypeNode("void"),
                            [
                                new CppParameterNode(new IdentifierNode("j"), new RawTypeNode("nlohmann::json"), isReference: true),
                                new CppParameterNode(new IdentifierNode("value"), new SimpleTypeNode(new IdentifierNode(enumName)), isConst: true, isReference: true),
                            ],
                            body: toJsonBody,
                            documentation: BuildCodecDocumentation(enumName, isToJson: true)),
                        new CppFreeFunctionNode(
                            new IdentifierNode("from_json"),
                            new RawTypeNode("void"),
                            [
                                new CppParameterNode(new IdentifierNode("j"), new RawTypeNode("const nlohmann::json"), isReference: true),
                                new CppParameterNode(new IdentifierNode("value"), new SimpleTypeNode(new IdentifierNode(enumName)), isReference: true),
                            ],
                            body: fromJsonBody,
                            documentation: BuildCodecDocumentation(enumName, isToJson: false)),
                    ]),
                ]),
            ]);

        return (header, source);
    }

    /// <summary>Builds wire converters as out-of-line serializer implementations, keeping enum definitions pure.</summary>
    /// <param name="irEnum">The enum contract.</param>
    /// <returns>The converter, parser, and wire-text function definitions.</returns>
    private IReadOnlyList<CppFreeFunctionNode> BuildWireFunctions(IrEnum irEnum)
    {
        var enumName = _naming.EnumName(irEnum.Name);
        var isInteger = irEnum.BaseType == IrEnumBaseType.Integer;
        var toWire = new CppFreeFunctionNode(
            new IdentifierNode("ToWire"),
            isInteger
                ? new SimpleTypeNode(new IdentifierNode(IntegerWireType(irEnum)), isPrimitive: true)
                : new SimpleTypeNode(new IdentifierNode("std::string_view")),
            [new CppParameterNode(new IdentifierNode("v"), new SimpleTypeNode(new IdentifierNode(enumName)))],
            body: new BlockNode(BuildToWireStatements(irEnum, enumName, isInteger)),
            documentation: StdDocumentation.Create("Returns the original OpenAPI wire value.",
                details: ["@param v The enum value to encode.", "@return The original wire value.", "@throws nlohmann::json::exception The value is not a declared enum member."]));

        var fromWire = new CppFreeFunctionNode(
            new IdentifierNode($"{enumName}FromWire"),
            new GenericTypeNode(new IdentifierNode("std::optional"), [new SimpleTypeNode(new IdentifierNode(enumName))]),
            [new CppParameterNode(
                new IdentifierNode("w"),
                isInteger
                    ? new SimpleTypeNode(new IdentifierNode(IntegerWireType(irEnum)), isPrimitive: true)
                    : new SimpleTypeNode(new IdentifierNode("std::string_view")))],
            body: new BlockNode(BuildFromWireStatements(irEnum, enumName, isInteger)),
            documentation: StdDocumentation.Create("Parses an original OpenAPI wire value.",
                details: ["@param w The wire value to inspect.", "@return The corresponding enum member, or std::nullopt for an unknown value."]));

        var wireText = new CppFreeFunctionNode(
            new IdentifierNode("ToWireText"),
            new SimpleTypeNode(new IdentifierNode("std::string")),
            [new CppParameterNode(new IdentifierNode("v"), new SimpleTypeNode(new IdentifierNode(enumName)))],
            body: new BlockNode(
            [
                new ReturnStatementNode(new CallExpressionNode(
                    StdExpressions.Ident(isInteger ? "std::to_string" : "std::string"),
                    [new CallExpressionNode(StdExpressions.Ident("ToWire"), [StdExpressions.Ident("v")])])),
            ]),
            documentation: StdDocumentation.Create("Formats the original enum wire value as request text.",
                details: ["@param v The enum value to format.", "@return The original wire text.", "@throws nlohmann::json::exception The value is not a declared enum member."]));

        return [toWire, fromWire, wireText];
    }

    /// <summary>Builds conversion branches preserving the enum's exact integer width or string wire value.</summary>
    /// <param name="irEnum">The enum contract.</param>
    /// <param name="enumName">The generated enum type name.</param>
    /// <param name="isInteger">Whether the wire values are integers.</param>
    /// <returns>The conversion statements.</returns>
    private List<StatementNode> BuildToWireStatements(IrEnum irEnum, string enumName, bool isInteger)
    {
        var statements = new List<StatementNode>();
        foreach (var value in irEnum.Values)
        {
            var wire = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? value.Name;
            var memberName = _naming.EnumMemberName(value.Name);
            statements.Add(new IfStatementNode(
                new BinaryExpressionNode(
                    StdExpressions.Ident("v"),
                    BinaryOperator.Equal,
                    BuildScopedMember(enumName, memberName)),
                new BlockNode(
                [
                    isInteger
                        ? new ReturnStatementNode(new CastExpressionNode(
                            new SimpleTypeNode(new IdentifierNode(IntegerWireType(irEnum)), isPrimitive: true),
                            StdExpressions.Ident("v")))
                        : new ReturnStatementNode(new LiteralExpressionNode(wire, LiteralKind.String)),
                ])));
        }

        statements.Add(new ThrowStatementNode(new CallExpressionNode(
            BuildNlohmannOutOfRangeCreate(),
            [new LiteralExpressionNode(1701, LiteralKind.Number),
                new LiteralExpressionNode($"unknown {enumName} enum member", LiteralKind.String),
                StdExpressions.Ident("nullptr")])));
        return statements;
    }

    /// <summary>Builds exact wire comparisons without narrowing 64-bit enum values.</summary>
    /// <param name="irEnum">The enum contract.</param>
    /// <param name="enumName">The generated enum type name.</param>
    /// <param name="isInteger">Whether the wire values are integers.</param>
    /// <returns>The parser statements.</returns>
    private List<StatementNode> BuildFromWireStatements(IrEnum irEnum, string enumName, bool isInteger)
    {
        var statements = new List<StatementNode>();
        for (var i = 0; i < irEnum.Values.Count; i++)
        {
            var value = irEnum.Values[i];
            var wire = Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? value.Name;
            var memberName = _naming.EnumMemberName(value.Name);
            statements.Add(new IfStatementNode(
                new BinaryExpressionNode(
                    StdExpressions.Ident("w"),
                    BinaryOperator.Equal,
                    isInteger
                        ? IntegerWireLiteral(ParseIntegerWire(wire))
                        : new LiteralExpressionNode(wire, LiteralKind.String)),
                new BlockNode([new ReturnStatementNode(BuildScopedMember(enumName, memberName))])));
        }

        statements.Add(new ReturnStatementNode(StdExpressions.Ident("std::nullopt")));
        return statements;
    }

    /// <summary>Builds the JSON exception factory used for unknown wire values and undeclared enum members.</summary>
    /// <returns>The structured exception factory access.</returns>
    private static MemberAccessExpressionNode BuildNlohmannOutOfRangeCreate()
    {
        return new MemberAccessExpressionNode(
            new MemberAccessExpressionNode(
                new MemberAccessExpressionNode(StdExpressions.Ident("nlohmann"), new IdentifierNode("json"), useScopeOperator: true),
                new IdentifierNode("out_of_range"),
                useScopeOperator: true),
            new IdentifierNode("create"),
            useScopeOperator: true);
    }

    /// <summary>Builds a typed enum member access without embedding a source expression string.</summary>
    /// <param name="enumName">The generated enum type name.</param>
    /// <param name="memberName">The generated enum member name.</param>
    /// <returns>The scoped enum member expression.</returns>
    private static MemberAccessExpressionNode BuildScopedMember(string enumName, string memberName)
        => new(StdExpressions.Ident(enumName), new IdentifierNode(memberName), useScopeOperator: true);

    /// <summary>Documents the generated enum JSON boundary.</summary>
    /// <param name="enumName">The generated enum type name.</param>
    /// <param name="isToJson">Whether the codec serializes rather than parses.</param>
    /// <returns>The generated Doxygen documentation.</returns>
    private static CommentNode BuildCodecDocumentation(string enumName, bool isToJson)
        => StdDocumentation.Create(isToJson ? $"Serializes a declared {enumName} wire value." : $"Parses an exact declared {enumName} wire value.",
            details:
            [
                isToJson ? "@param j Receives the JSON wire value." : "@param j The JSON wire value to inspect.",
                isToJson ? "@param value The declared enum member." : "@param value Receives the parsed enum member.",
                "@throws nlohmann::json::exception The JSON type, integer range, or enum value is invalid.",
            ]);

    /// <summary>Selects a representation capable of preserving every declared signed integer wire value.</summary>
    /// <param name="irEnum">The enum contract.</param>
    /// <returns>The exact-width C++ integer type used by the enum and its converters.</returns>
    private static string IntegerWireType(IrEnum irEnum)
    {
        return irEnum.Values.Any(value => ParseIntegerWire(Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? value.Name) is < int.MinValue or > int.MaxValue)
            ? "std::int64_t" : "std::int32_t";
    }

    /// <summary>Parses an integer wire value without silently substituting its declaration ordinal.</summary>
    /// <param name="wire">The original wire value.</param>
    /// <returns>The exact signed 64-bit value.</returns>
    /// <exception cref="NotSupportedException">The wire value is not a supported signed integer.</exception>
    private static long ParseIntegerWire(string wire)
    {
        return long.TryParse(wire, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new NotSupportedException($"C++ integer enum wire value '{wire}' is outside the supported signed 64-bit range.");
    }

    /// <summary>Builds a portable integer literal, including the signed 64-bit minimum without overflowing its token.</summary>
    /// <param name="value">The exact wire integer.</param>
    /// <returns>The C++ constant expression.</returns>
    private static ExpressionNode IntegerWireLiteral(long value)
    {
        if (value == long.MinValue)
            return new BinaryExpressionNode(new LiteralExpressionNode("-9223372036854775807LL", LiteralKind.Number),
                BinaryOperator.Subtract, new LiteralExpressionNode(1, LiteralKind.Number));
        var text = value.ToString(CultureInfo.InvariantCulture);
        return new LiteralExpressionNode(value is < int.MinValue or > int.MaxValue ? text + "LL" : text, LiteralKind.Number);
    }

	}
