using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders.Std;

/// <summary>
/// Builds ADL JSON codecs that preserve required keys, nullable values, and optional presence.
/// </summary>
public sealed class StdModelJsonAstBuilder : AstBuilderBase
{
    /// <summary>Names model fields and serializer files.</summary>
    private readonly StdNamingConvention _naming;
    /// <summary>Maps the types read by generated JSON codecs.</summary>
    private readonly StdCppTypeMapper _typeMapper;

    /// <summary>Creates a codec builder sharing the model's naming and type mapping.</summary>
    /// <param name="typeMapper">The standalone type mapper.</param>
    /// <param name="naming">The generated naming convention.</param>
    /// <param name="documentContext">The document's shared immutable declaration indexes.</param>
    /// <exception cref="ArgumentNullException">A required service is missing.</exception>
    public StdModelJsonAstBuilder(StdCppTypeMapper typeMapper, StdNamingConvention naming, IrDocumentContext documentContext)
        : base(documentContext)
    {
        _typeMapper = typeMapper ?? throw new ArgumentNullException(nameof(typeMapper));
        _naming = naming ?? throw new ArgumentNullException(nameof(naming));
    }

    /// <summary>Builds declarations and definitions for one model's JSON conversion.</summary>
    /// <param name="model">The model contract.</param>
    /// <param name="rootNamespace">The generated namespace.</param>
    /// <param name="options">The configured JSON include paths.</param>
    /// <param name="warnings">The optional generation diagnostic sink.</param>
    /// <returns>The serializer header and source ASTs.</returns>
    /// <exception cref="NotSupportedException">The model requires unsupported recursive ownership or composition codecs.</exception>
    public (CppHeaderFileNode Header, CppSourceFileNode Source) Build(
        IrModel model,
        string rootNamespace,
        CppGenerationOptions options,
        ICollection<string>? warnings = null)
    {
        var structName = _naming.StructName(model.Name);
        CppTypeReferenceCollector.EnsureSupportedComposition(model, ModelsByName);
        var plan = IrInheritancePlan.Resolve(model, ModelsByName);
        CppTypeReferenceCollector.EnsureNonRecursive(model, plan.EffectiveProperties, EnumNames);
        var properties = plan.EffectiveProperties;

        var header = new CppHeaderFileNode(
            fileName: _naming.SerializerHeaderPath(model.OriginalName),
            includes:
            [
                new CppIncludeDirectiveNode(options.JsonFwdInclude, isSystemInclude: true),
                new CppIncludeDirectiveNode(_naming.ModelHeaderPath(model.OriginalName)),
            ],
            declarations:
            [
                new NamespaceDeclarationNode(new IdentifierNode(rootNamespace),
                [
                    BuildFunctionDeclaration(structName, isToJson: true),
                    BuildFunctionDeclaration(structName, isToJson: false),
                ]),
            ]);

        var source = new CppSourceFileNode(
            fileName: _naming.SerializerSourcePath(model.OriginalName),
            includes: CollectSourceIncludes(model, plan, properties, EnumNames, options),
            sections:
            [
                new CppSourceSection(members:
                [
                    new NamespaceDeclarationNode(new IdentifierNode(rootNamespace),
                    [
                        BuildFunctionDefinition(structName, isToJson: true, body: BuildToJsonBody(plan, properties)),
                        BuildFunctionDefinition(structName, isToJson: false, body: BuildFromJsonBody(plan, properties)),
                    ]),
                ]),
            ]);

        return (header, source);
    }

    /// <summary>Declares one ADL conversion function.</summary>
    /// <param name="structName">The generated model name.</param>
    /// <param name="isToJson">Whether the function serializes rather than parses.</param>
    /// <returns>The function declaration.</returns>
    private CppFreeFunctionNode BuildFunctionDeclaration(string structName, bool isToJson)
        => new(
            new IdentifierNode(isToJson ? "to_json" : "from_json"),
            new RawTypeNode("void"),
            BuildParameters(structName, isToJson),
            isDeclarationOnly: true,
            documentation: BuildDocumentation(structName, isToJson));

    /// <summary>Defines one ADL conversion function.</summary>
    /// <param name="structName">The generated model name.</param>
    /// <param name="isToJson">Whether the function serializes rather than parses.</param>
    /// <param name="body">The conversion statements.</param>
    /// <returns>The function definition.</returns>
    private CppFreeFunctionNode BuildFunctionDefinition(string structName, bool isToJson, BlockNode body)
        => new(
            new IdentifierNode(isToJson ? "to_json" : "from_json"),
            new RawTypeNode("void"),
            BuildParameters(structName, isToJson),
            body: body,
            documentation: BuildDocumentation(structName, isToJson));

    /// <summary>Documents the JSON boundary contract and its validation failures.</summary>
    /// <param name="structName">The generated DTO type.</param>
    /// <param name="isToJson">Whether the function writes rather than reads JSON.</param>
    /// <returns>The generated Doxygen documentation.</returns>
    private static CommentNode BuildDocumentation(string structName, bool isToJson)
        => StdDocumentation.Create(
            isToJson ? $"Serializes {structName} using its declared OpenAPI wire formats." : $"Parses and validates the JSON representation of {structName}.",
            details:
            [
                isToJson ? "@param j Receives the serialized JSON object." : "@param j The JSON object to decode.",
                isToJson ? "@param value The DTO to serialize." : "@param value Receives the decoded DTO.",
                "@throws nlohmann::json::exception A required key, value type, number range, enum, or string format is invalid.",
            ]);

    /// <summary>Builds the JSON and model reference parameters for ADL conversions.</summary>
    /// <param name="structName">The generated model name.</param>
    /// <param name="isToJson">Whether the model reference must be const.</param>
    /// <returns>The two conversion parameters.</returns>
    private static CppParameterNode[] BuildParameters(string structName, bool isToJson)
        =>
        [
            new CppParameterNode(
                new IdentifierNode("j"),
                new RawTypeNode(isToJson ? "nlohmann::json" : "const nlohmann::json"),
                isReference: true),
            new CppParameterNode(
                new IdentifierNode("value"),
                new RawTypeNode(isToJson ? $"const {structName}&" : $"{structName}&")),
        ];

    /// <summary>Serializes a complete object, omitting only absent optional keys.</summary>
    /// <param name="plan">The resolved inheritance strategy.</param>
    /// <param name="properties">The fields declared by this model.</param>
    /// <returns>The serialization body.</returns>
    private BlockNode BuildToJsonBody(IrInheritancePlan plan, IReadOnlyList<IrProperty> properties)
    {
        var statements = new List<StatementNode>
        {
            new ExpressionStatementNode(new BinaryExpressionNode(
                StdExpressions.Ident("j"), BinaryOperator.Assign,
                new CallExpressionNode(StdExpressions.Ident("nlohmann::json::object")), isAssignment: true)),
            new ExpressionStatementNode(new CastExpressionNode(new RawTypeNode("void"), StdExpressions.Ident("value"))),
        };

        if (plan.UseInheritance)
        {
            statements.Add(new ExpressionStatementNode(new CallExpressionNode(
                StdExpressions.Ident("to_json"),
                [
                    StdExpressions.Ident("j"),
                    new CastExpressionNode(
                        new RawTypeNode($"const {_naming.StructName(plan.BaseModelName!)}&"),
                        StdExpressions.Ident("value")),
                ])));
        }

        foreach (var property in properties)
        {
            var field = _naming.PropertyName(property.Name);
            var wire = property.OriginalName;
            var fieldAccess = new MemberAccessExpressionNode(StdExpressions.Ident("value"), new IdentifierNode(field));

            if (StdPropertyTypes.IsOptional(property))
            {
                statements.Add(new IfStatementNode(
                    fieldAccess,
                    new BlockNode(
                    [
                        new ExpressionStatementNode(new BinaryExpressionNode(
                            JsonKey(wire),
                            BinaryOperator.Assign,
                            BuildWriteCall(property.Type, new UnaryExpressionNode(UnaryOperator.Dereference, fieldAccess)),
                            isAssignment: true)),
                    ])));
            }
            else
            {
                statements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
                    JsonKey(wire),
                    BinaryOperator.Assign,
                    BuildWriteCall(property.Type, fieldAccess),
                    isAssignment: true)));
            }
        }

        return new BlockNode(statements);
    }

    /// <summary>Reads required keys and replaces optional state even when reusing a model instance.</summary>
    /// <param name="plan">The resolved inheritance strategy.</param>
    /// <param name="properties">The fields declared by this model.</param>
    /// <returns>The deserialization body.</returns>
    private BlockNode BuildFromJsonBody(IrInheritancePlan plan, IReadOnlyList<IrProperty> properties)
    {
        var statements = new List<StatementNode>
        {
            new ExpressionStatementNode(new CastExpressionNode(new RawTypeNode("void"),
                new CallExpressionNode(new MemberAccessExpressionNode(StdExpressions.Ident("j"), new IdentifierNode("get_ref")),
                    [], genericTypeName: "const nlohmann::json::object_t&"))),
            new ExpressionStatementNode(new CastExpressionNode(new RawTypeNode("void"), StdExpressions.Ident("value"))),
        };

        if (plan.UseInheritance)
        {
            statements.Add(new ExpressionStatementNode(new CallExpressionNode(
                StdExpressions.Ident("from_json"),
                [
                    StdExpressions.Ident("j"),
                    new CastExpressionNode(
                        new RawTypeNode($"{_naming.StructName(plan.BaseModelName!)}&"),
                        StdExpressions.Ident("value")),
                ])));
        }

        foreach (var property in properties)
        {
            var field = _naming.PropertyName(property.Name);
            var wire = property.OriginalName;
            var fieldAccess = new MemberAccessExpressionNode(StdExpressions.Ident("value"), new IdentifierNode(field));

            if (StdPropertyTypes.IsOptional(property))
            {
                var innerRendered = StdTypeText.Render(_typeMapper.MapType(StdPropertyTypes.ValueType(property)));
                statements.Add(new IfStatementNode(
                    BuildContainsCall(wire),
                    new BlockNode(
                    [
                        new ExpressionStatementNode(new CallExpressionNode(
                            new MemberAccessExpressionNode(fieldAccess, new IdentifierNode("emplace")),
                            [
                            BuildReadCall(wire, innerRendered, property.Type),
                            ])),
                    ]),
                    new BlockNode(
                    [
                        new ExpressionStatementNode(new CallExpressionNode(
                            new MemberAccessExpressionNode(fieldAccess, new IdentifierNode("reset")))),
                    ])));
            }
            else
            {
                statements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
                    fieldAccess, BinaryOperator.Assign,
                    BuildReadCall(wire, StdTypeText.Render(_typeMapper.MapType(StdPropertyTypes.ValueType(property))), property.Type),
                    isAssignment: true)));
            }
        }

        return new BlockNode(statements);
    }

    /// <summary>Builds assignment access for an original JSON key.</summary>
    /// <param name="wire">The original schema property name.</param>
    /// <returns>The JSON subscript expression.</returns>
    private static IndexAccessExpressionNode JsonKey(string wire)
        => new(
            StdExpressions.Ident("j"),
            new LiteralExpressionNode(wire, LiteralKind.String));

    /// <summary>Builds checked access that rejects missing required keys.</summary>
    /// <param name="wire">The original schema property name.</param>
    /// <returns>The checked JSON access expression.</returns>
    private static CallExpressionNode BuildAtCall(string wire)
        => new(
            new MemberAccessExpressionNode(StdExpressions.Ident("j"), new IdentifierNode("at")),
            [new LiteralExpressionNode(wire, LiteralKind.String)]);

    /// <summary>Builds the presence check for an optional property.</summary>
    /// <param name="wire">The original schema property name.</param>
    /// <returns>The JSON key presence expression.</returns>
    private static CallExpressionNode BuildContainsCall(string wire)
        => new(
            new MemberAccessExpressionNode(StdExpressions.Ident("j"), new IdentifierNode("contains")),
            [new LiteralExpressionNode(wire, LiteralKind.String)]);

    /// <summary>Builds a checked JSON conversion with recursive numeric and wire-format validation.</summary>
    /// <param name="wire">The original schema property name.</param>
    /// <param name="targetType">The rendered destination type.</param>
    /// <param name="schemaType">The schema type determining its leaf wire format.</param>
    /// <returns>The field conversion expression.</returns>
    private static CallExpressionNode BuildReadCall(string wire, string targetType, IrType schemaType)
        => new(
            StdExpressions.Ident("ValIO::ReadJson"),
            [BuildAtCall(wire), StdJsonWireFormat.BuildArgument(schemaType)],
            genericTypeName: targetType);

    /// <summary>Builds serialization that preserves declared base64 and string wire formats.</summary>
    /// <param name="schemaType">The schema type determining its leaf wire format.</param>
    /// <param name="value">The DTO field value.</param>
    /// <returns>The checked serialization expression.</returns>
    private static CallExpressionNode BuildWriteCall(IrType schemaType, ExpressionNode value)
        => new(StdExpressions.Ident("ValIO::WriteJson"), [value, StdJsonWireFormat.BuildArgument(schemaType)]);

    /// <summary>
    /// Collects JSON runtime support and ADL declarations for all referenced model and enum codecs.
    /// </summary>
    /// <param name="model">The current model.</param>
    /// <param name="plan">The resolved inheritance strategy.</param>
    /// <param name="properties">The generated model fields.</param>
    /// <param name="enumNames">The document's enum names.</param>
    /// <param name="options">The configured JSON include paths.</param>
    /// <returns>The ordered serializer source includes.</returns>
    private List<CppIncludeDirectiveNode> CollectSourceIncludes(
        IrModel model,
        IrInheritancePlan plan,
        IReadOnlyList<IrProperty> properties,
        IReadOnlySet<string> enumNames,
        CppGenerationOptions options)
    {
        var referencedModels = new HashSet<string>();
        var referencedEnums = new HashSet<string>();
        var orig = CppTypeReferenceCollector.BuildOriginalNameMap(model, plan.EffectiveProperties);

        if (plan.UseInheritance)
            referencedModels.Add(plan.BaseModelName!);

        foreach (var property in properties)
            CppTypeReferenceCollector.Collect(property.Type, referencedModels, referencedEnums, enumNames);

        var includes = new List<CppIncludeDirectiveNode>
        {
            new(_naming.SerializerHeaderPath(model.OriginalName)),
            new(options.JsonInclude, isSystemInclude: true),
            new("Runtime/ValIOJsonTypes.h"),
            new("Runtime/ValIOJsonValidation.h"),
        };

        foreach (var refModel in referencedModels.Where(m => m != model.Name).OrderBy(x => x, StringComparer.Ordinal))
            includes.Add(new CppIncludeDirectiveNode(_naming.SerializerHeaderPath(orig.GetValueOrDefault(refModel, refModel))));

        foreach (var refEnum in referencedEnums.OrderBy(x => x, StringComparer.Ordinal))
            includes.Add(new CppIncludeDirectiveNode(_naming.SerializerHeaderPath(orig.GetValueOrDefault(refEnum, refEnum))));

        return includes;
    }
	}
