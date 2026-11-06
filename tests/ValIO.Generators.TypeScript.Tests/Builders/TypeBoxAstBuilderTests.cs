using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.TypeBox.Ast.Builders;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>
/// Direct tests for <see cref="TypeBoxAstBuilder"/> — every primitive,
/// composite, and the Type.Recursive self-reference shape.
/// </summary>
[TestFixture]
public class TypeBoxAstBuilderTests
{
    private TypeBoxAstBuilder _sut = null!;
    private TypeScriptAstRenderer _renderer = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new TypeBoxAstBuilder();
        _renderer = new TypeScriptAstRenderer();
    }

    private static IrPrimitiveType Prim(IrPrimitiveKind k) => new() { PrimitiveKind = k };

    private string Render(IrType ir) =>
        _renderer.Visit(_sut.BuildSchemaFromIrType(ir));

    [TestCase(IrPrimitiveKind.String,  "Type.String()")]
    [TestCase(IrPrimitiveKind.Number,  "Type.Number()")]
    [TestCase(IrPrimitiveKind.Boolean, "Type.Boolean()")]
    [TestCase(IrPrimitiveKind.Void,    "Type.Void()")]
    [TestCase(IrPrimitiveKind.Null,    "Type.Null()")]
    [TestCase(IrPrimitiveKind.Any,     "Type.Unknown()")]
    [TestCase(IrPrimitiveKind.Integer, "Type.Integer()")]
    [TestCase(IrPrimitiveKind.Long,    "Type.Integer()")]
    public void Primitives_RenderToTypeCall(IrPrimitiveKind kind, string expected)
    {
        Render(Prim(kind)).Should().Be(expected);
    }

    [Test]
    public void StringWithUuid_AddsFormatOption()
    {
        var rendered = Render(Prim(IrPrimitiveKind.Uuid));
        rendered.Should().Contain("Type.String(").And.Contain("format").And.Contain("uuid");
    }

    [Test]
    public void StringWithDateTime_FormatIsDateTime()
    {
        var rendered = Render(Prim(IrPrimitiveKind.DateTime));
        rendered.Should().Contain("format").And.Contain("date-time");
    }

    /// <summary>Verifies binary values use a registered Blob schema matching their generated runtime type.</summary>
    [Test]
    public void Binary_UsesRegisteredBlobInstanceSchema()
    {
        var rendered = Render(Prim(IrPrimitiveKind.Binary));
        rendered.Should().Contain("TypeRegistry.Set(")
            .And.Contain("\"ValIO.Blob\"")
            .And.Contain("Blob.prototype.isPrototypeOf(Object(value))")
            .And.Contain("Type.Unsafe<Blob>(Object.fromEntries([[Kind, \"ValIO.Blob\"]]))");
        rendered.Should().NotContain("Type.String(").And.NotContain("contentEncoding");
    }

    [Test]
    public void Array_BecomesTypeArrayOfInner()
    {
        var ir = new IrArrayType { ElementType = Prim(IrPrimitiveKind.String) };
        Render(ir).Should().Be("Type.Array(Type.String())");
    }

    [Test]
    public void Dictionary_BecomesTypeRecord()
    {
        var ir = new IrDictionaryType
        {
            KeyType = Prim(IrPrimitiveKind.String),
            ValueType = Prim(IrPrimitiveKind.Number),
        };
        var rendered = Render(ir);
        rendered.Should().StartWith("Type.Record(");
        rendered.Should().Contain("Type.String()");
        rendered.Should().Contain("Type.Number()");
    }

    [Test]
    public void Nullable_BecomesUnionWithTypeNull()
    {
        var ir = new IrNullableType { InnerType = Prim(IrPrimitiveKind.String) };
        var rendered = Render(ir);
        rendered.Should().StartWith("Type.Union(");
        rendered.Should().Contain("Type.String()");
        rendered.Should().Contain("Type.Null()");
    }

    [Test]
    public void Optional_BecomesTypeOptional()
    {
        var ir = new IrOptionalType { InnerType = Prim(IrPrimitiveKind.String) };
        Render(ir).Should().Be("Type.Optional(Type.String())");
    }

    [Test]
    public void Union_RendersTypeUnionArray()
    {
        var ir = new IrUnionType { Types = [Prim(IrPrimitiveKind.String), Prim(IrPrimitiveKind.Number)] };
        var rendered = Render(ir);
        rendered.Should().StartWith("Type.Union(");
        rendered.Should().Contain("Type.String()").And.Contain("Type.Number()");
    }

    [Test]
    public void Intersection_RendersTypeIntersect()
    {
        var ir = new IrIntersectionType
        {
            Types = [Prim(IrPrimitiveKind.String), Prim(IrPrimitiveKind.Number)]
        };
        Render(ir).Should().StartWith("Type.Intersect(");
    }

    [Test]
    public void ModelReference_RendersAsBareSchemaIdent()
    {
        var ir = new IrModelReference { ModelName = "Pet" };
        _renderer.Visit(_sut.BuildSchemaFromIrType(ir)).Should().Be("PetSchema");
    }

    [Test]
    public void SelfReference_UsesThisInsideTypeRecursive()
    {
        var ir = new IrModelReference { ModelName = "TreeNode" };
        var rendered = _renderer.Visit(_sut.BuildSchemaFromIrType(ir, currentModelName: "TreeNode"));
        rendered.Should().Be("This");
    }

    [Test]
    public void EnumSchema_RendersUnionOverReusedValuesArray()
    {
        var e = new IrEnum
        {
            Name = "Status",
            BaseType = IrEnumBaseType.String,
            Values =
            [
                new IrEnumValue { Name = "ON",  Value = "on" },
                new IrEnumValue { Name = "OFF", Value = "off" },
            ],
        };
        var rendered = _renderer.Visit(_sut.BuildEnumSchema(e));
        // Reuses the model's StatusValues array instead of inlining literals:
        // Type.Union(StatusValues.map((value) => Type.Literal(value))).
        rendered.Should().StartWith("Type.Union(");
        rendered.Should().Contain("StatusValues.map(");
        rendered.Should().Contain("Type.Literal(value)");
        rendered.Should().NotContain("Type.Literal(\"on\")");
    }

    [Test]
    public void ConstraintsOnNumber_MinAddedAsOption()
    {
        var rendered = _renderer.Visit(_sut.BuildSchemaFromIrType(
            Prim(IrPrimitiveKind.Number),
            constraints: new IrPropertyConstraints { Min = 0 }));
        rendered.Should().Contain("Type.Number(");
        rendered.Should().Contain("0");
    }

    [Test]
    public void EmptyModel_BecomesTypeObject()
    {
        var model = new IrModel
        {
            Name = "Empty",
            Properties = Array.Empty<IrProperty>(),
        };
        var rendered = _renderer.Visit(_sut.BuildSchemaFromIrModel(model));
        rendered.Should().Contain("Type.Object(");
    }
}
