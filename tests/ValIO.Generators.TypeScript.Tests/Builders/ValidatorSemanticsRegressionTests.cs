using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Generators.TypeScript.Valibot.Ast.Builders;
using ValIO.Generators.TypeScript.TypeBox.Ast.Builders;
using ValIO.Generators.TypeScript.Zod.Ast.Builders;
using ValIO.Generators.TypeScript.Zod.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>Guards validation semantics where IR wrappers and composite schemas cross AST boundaries.</summary>
[TestFixture]
public sealed class ValidatorSemanticsRegressionTests
{
    /// <summary>Verifies that key presence and null acceptance remain independent for each validator.</summary>
    /// <param name="validator">The validator under test.</param>
    /// <param name="required">Whether the property must be present.</param>
    /// <param name="nullable">Whether explicit null is allowed.</param>
    /// <param name="expected">The expected property schema.</param>
    [TestCase("zod", true, false, "value: z.string()")]
    [TestCase("zod", true, true, "value: z.string().nullable()")]
    [TestCase("zod", false, false, "value: z.string().optional()")]
    [TestCase("zod", false, true, "value: z.string().nullable().optional()")]
    [TestCase("valibot", true, false, "value: v.string()")]
    [TestCase("valibot", true, true, "value: v.nullable(v.string())")]
    [TestCase("valibot", false, false, "value: v.optional(v.string())")]
    [TestCase("valibot", false, true, "value: v.optional(v.nullable(v.string()))")]
    public void ModelProperty_PreservesPresenceAndNullability(string validator, bool required, bool nullable, string expected)
    {
        IrType type = Primitive(IrPrimitiveKind.String);
        if (nullable)
            type = new IrNullableType { InnerType = type };
        var model = Model(new IrProperty { Name = "value", OriginalName = "value", Type = type, IsRequired = required });

        var content = validator == "zod"
            ? new ZodAstRenderer().RenderSchema(ZodBuilder().BuildZodSchemaFromIrModel(model))
            : new TypeScriptAstRenderer().Visit(new ValibotAstBuilder().BuildSchemaFromIrModel(model));

        content.Should().Contain(expected).And.NotContain("nullish");
    }

    /// <summary>Verifies that dictionary values are validated and recursive values retain lazy references.</summary>
    [Test]
    public void ZodDictionary_ValidatesKeysAndRecursiveValues()
    {
        var dictionary = new IrDictionaryType
        {
            KeyType = Primitive(IrPrimitiveKind.String),
            ValueType = new IrModelReference { ModelName = "Node" }
        };
        var schema = ZodBuilder().BuildZodSchemaFromIrType(dictionary, currentModelName: "Node");

        var content = new ZodAstRenderer().RenderSchema(schema);

        content.Should().Contain("z.record(").And.Contain("z.string()").And.Contain("z.lazy(() => NodeSchema)");
        content.Should().NotContain("z.any()");
        var call = ((ValIO.Generators.TypeScript.Zod.Ast.Nodes.ZodExpressionSchemaNode)schema).Expression
            .Should().BeOfType<CallExpressionNode>().Subject;
        call.Arguments.Should().HaveCount(2, "both Zod versions accept the explicit key/value record overload");
    }

    /// <summary>Verifies that more than two allOf arms use nested binary intersections.</summary>
    [Test]
    public void ZodIntersection_WithThreeArms_UsesAndForRemainingArm()
    {
        var intersection = new IrIntersectionType
        {
            Types = [new IrModelReference { ModelName = "A" }, new IrModelReference { ModelName = "B" }, new IrModelReference { ModelName = "C" }]
        };

        var content = new ZodAstRenderer().RenderSchema(ZodBuilder().BuildZodSchemaFromIrType(intersection));

        content.Should().Be("z.intersection(ASchema, BSchema).and(CSchema)");
    }

    /// <summary>Verifies numeric enums use literal schemas supported by Zod 3 and Zod 4.</summary>
    /// <param name="count">The number of numeric enum values.</param>
    /// <param name="expected">The expected cardinality-aware schema.</param>
    [TestCase(0, "z.never()")]
    [TestCase(1, "z.literal(1)")]
    [TestCase(2, "z.union([z.literal(1), z.literal(2)])")]
    public void ZodNumericEnum_UsesLiteralSchemas(int count, string expected)
    {
        var enumeration = new IrEnum
        {
            Name = "Status", BaseType = IrEnumBaseType.Integer,
            Values = Enumerable.Range(1, count).Select(value => new IrEnumValue { Name = $"Value{value}", Value = value }).ToArray()
        };

        var content = new ZodAstRenderer().VisitZodEnum(ZodBuilder().BuildZodEnum(enumeration));

        content.Should().Be(expected);
    }

    /// <summary>Verifies explicit optional wrappers do not accept null.</summary>
    [Test]
    public void ZodOptionalType_DoesNotAddNullability()
    {
        var type = new IrOptionalType { InnerType = Primitive(IrPrimitiveKind.String) };

        var content = new ZodAstRenderer().RenderSchema(ZodBuilder().BuildZodSchemaFromIrType(type));

        content.Should().Be("z.string().optional()");
    }

    /// <summary>Verifies the Zod renderer uses the shared TypeScript literal escaping rules.</summary>
    [Test]
    public void ZodDescription_EscapesQuotesAndBackslashes()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.String, useBigInt: false,
            description: "A \"quoted\" path C:\\data");

        var content = new ZodAstRenderer().RenderSchema(schema);

        content.Should().Be("z.string().describe(\"A \\\"quoted\\\" path C:\\\\data\")");
    }

    /// <summary>Verifies value constraints survive nullable and optional wrapper composition.</summary>
    [Test]
    public void ValibotConstraints_SurviveNullableOptionalWrappers()
    {
        var type = new IrOptionalType { InnerType = new IrNullableType { InnerType = Primitive(IrPrimitiveKind.String) } };

        var content = new TypeScriptAstRenderer().Visit(new ValibotAstBuilder().BuildSchemaFromIrType(type,
            constraints: new IrPropertyConstraints { MinLength = 3 }));

        content.Should().Contain("v.optional(v.nullable(v.pipe(").And.Contain("v.minLength(3)");
    }

    /// <summary>Verifies binary validator outputs match generated Blob, File, and Uint8Array types.</summary>
    /// <param name="validator">The selected validation library.</param>
    /// <param name="kind">The binary primitive kind.</param>
    /// <param name="expected">The expected runtime schema fragment.</param>
    [TestCase("zod", IrPrimitiveKind.Binary, "z.instanceof(Blob)")]
    [TestCase("zod", IrPrimitiveKind.File, "z.instanceof(File)")]
    [TestCase("zod", IrPrimitiveKind.ByteArray, "z.instanceof(Uint8Array)")]
    [TestCase("valibot", IrPrimitiveKind.Binary, "v.instance(Blob)")]
    [TestCase("valibot", IrPrimitiveKind.File, "v.instance(File)")]
    [TestCase("valibot", IrPrimitiveKind.ByteArray, "v.instance(Uint8Array)")]
    [TestCase("typebox", IrPrimitiveKind.Binary, "Type.Unsafe<Blob>")]
    [TestCase("typebox", IrPrimitiveKind.File, "Type.Unsafe<File>")]
    [TestCase("typebox", IrPrimitiveKind.ByteArray, "Type.Uint8Array()")]
    public void BinaryPrimitive_PreservesRuntimeType(string validator, IrPrimitiveKind kind, string expected)
    {
        var type = Primitive(kind);
        var content = validator switch
        {
            "zod" => new ZodAstRenderer().RenderSchema(ZodBuilder().BuildZodSchemaFromIrType(type)),
            "valibot" => new TypeScriptAstRenderer().Visit(new ValibotAstBuilder().BuildSchemaFromIrType(type)),
            _ => new TypeScriptAstRenderer().Visit(new TypeBoxAstBuilder().BuildSchemaFromIrType(type))
        };
        content.Should().Contain(expected);
    }

    /// <summary>Creates a primitive type for schema-level regression scenarios.</summary>
    /// <param name="kind">The primitive kind.</param>
    /// <returns>The IR primitive.</returns>
    private static IrPrimitiveType Primitive(IrPrimitiveKind kind) => new() { PrimitiveKind = kind };

    /// <summary>Creates the schema builder with an explicit partial document and neutral type projection.</summary>
    /// <returns>The Zod builder under test.</returns>
    private static ZodAstBuilder ZodBuilder() => new(IrDocumentContexts.Create(), new NamingConverter(),
        _ => new RawTypeNode("unknown"));

    /// <summary>Creates a model with the supplied properties.</summary>
    /// <param name="properties">The properties to validate.</param>
    /// <returns>The regression model.</returns>
    private static IrModel Model(params IrProperty[] properties) => new() { Name = "Example", Properties = properties };
}
