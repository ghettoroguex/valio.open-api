using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Valibot.Ast.Builders;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>
/// Direct tests for <see cref="ValibotAstBuilder"/> — exercises every
/// primitive/composite shape and verifies the rendered <c>v.string()</c>-style
/// chain matches what we expect.
/// </summary>
[TestFixture]
public class ValibotAstBuilderTests
{
    private ValibotAstBuilder _sut = null!;
    private TypeScriptAstRenderer _renderer = null!;

    [SetUp]
    public void Setup()
    {
        _sut = new ValibotAstBuilder();
        _renderer = new TypeScriptAstRenderer();
    }

    private static IrPrimitiveType Prim(IrPrimitiveKind k) => new() { PrimitiveKind = k };

    private string Render(IrType ir) =>
        _renderer.Visit(_sut.BuildSchemaFromIrType(ir));

    [TestCase(IrPrimitiveKind.String,  "v.string()")]
    [TestCase(IrPrimitiveKind.Number,  "v.number()")]
    [TestCase(IrPrimitiveKind.Boolean, "v.boolean()")]
    [TestCase(IrPrimitiveKind.Any,     "v.unknown()")]
    [TestCase(IrPrimitiveKind.Void,    "v.undefined()")]
    [TestCase(IrPrimitiveKind.Null,    "v.null()")]
    public void Primitives_RenderToExpectedCall(IrPrimitiveKind kind, string expected)
    {
        Render(Prim(kind)).Should().Be(expected);
    }

    [TestCase(IrPrimitiveKind.Integer)]
    [TestCase(IrPrimitiveKind.Long)]
    public void IntegerAndLong_PipeNumberThroughIntegerAction(IrPrimitiveKind kind)
    {
        var rendered = Render(Prim(kind));
        rendered.Should().Contain("v.pipe(").And.Contain("v.number()").And.Contain("v.integer()");
    }

    /// <summary>Verifies each string format uses the validator action matching its wire representation.</summary>
    /// <param name="kind">The formatted primitive kind.</param>
    /// <param name="expectedRefiner">The expected Valibot refinement action.</param>
    [TestCase(IrPrimitiveKind.Uuid,     "v.check(")]
    [TestCase(IrPrimitiveKind.DateTime, "v.check(")]
    [TestCase(IrPrimitiveKind.Date,     "v.check(")]
    [TestCase(IrPrimitiveKind.Time,     "v.check(")]
    public void StringRefinements_PipeStringWithRefiner(IrPrimitiveKind kind, string expectedRefiner)
    {
        var rendered = Render(Prim(kind));
        rendered.Should().Contain("v.pipe(").And.Contain("v.string()").And.Contain(expectedRefiner);
    }

    /// <summary>Verifies date-time validation retains calendar checks and explicit RFC 3339 offset syntax.</summary>
    [Test]
    public void DateTime_UsesTimestampAndCalendarOffsetPredicate()
    {
        var rendered = Render(Prim(IrPrimitiveKind.DateTime));
        rendered.Should().Contain("v.check(")
            .And.Contain("Date.parse(value.slice(")
            .And.Contain(".toISOString().slice(")
            .And.NotContain("v.isoDateTime()")
            .And.NotContain("v.isoTimestamp()");
    }

    [Test]
    public void Array_BecomesVArrayOfInner()
    {
        var ir = new IrArrayType { ElementType = Prim(IrPrimitiveKind.String) };
        Render(ir).Should().Be("v.array(v.string())");
    }

    [Test]
    public void Dictionary_BecomesVRecord()
    {
        var ir = new IrDictionaryType
        {
            KeyType = Prim(IrPrimitiveKind.String),
            ValueType = Prim(IrPrimitiveKind.Integer),
        };
        var rendered = Render(ir);
        // Multi-arg calls get a multiline layout — just check structure.
        rendered.Should().StartWith("v.record(");
        rendered.Should().Contain("v.string()");
        rendered.Should().Contain("v.integer()");
    }

    [Test]
    public void Nullable_BecomesVNullable()
    {
        var ir = new IrNullableType { InnerType = Prim(IrPrimitiveKind.String) };
        Render(ir).Should().Be("v.nullable(v.string())");
    }

    [Test]
    public void Optional_BecomesVOptional()
    {
        var ir = new IrOptionalType { InnerType = Prim(IrPrimitiveKind.String) };
        Render(ir).Should().Be("v.optional(v.string())");
    }

    [Test]
    public void Union_WithMultipleTypes_BecomesVUnionArray()
    {
        var ir = new IrUnionType { Types = [Prim(IrPrimitiveKind.String), Prim(IrPrimitiveKind.Number)] };
        var rendered = Render(ir);
        rendered.Should().StartWith("v.union(");
        rendered.Should().Contain("v.string()").And.Contain("v.number()");
    }

    [Test]
    public void Union_WithSingleType_CollapsesToInner()
    {
        var ir = new IrUnionType { Types = [Prim(IrPrimitiveKind.String)] };
        // Single-element union → just the inner schema, no v.union wrapper.
        Render(ir).Should().Be("v.string()");
    }

    [Test]
    public void Intersection_WithMultipleTypes_BecomesVIntersect()
    {
        var ir = new IrIntersectionType
        {
            Types = [Prim(IrPrimitiveKind.String), Prim(IrPrimitiveKind.Number)]
        };
        Render(ir).Should().StartWith("v.intersect(");
    }

    [Test]
    public void Intersection_WithSingleType_CollapsesToInner()
    {
        var ir = new IrIntersectionType { Types = [Prim(IrPrimitiveKind.String)] };
        Render(ir).Should().Be("v.string()");
    }

    [Test]
    public void ModelReference_RendersAsBareIdent_WhenNotSelfRef()
    {
        var ir = new IrModelReference { ModelName = "Pet" };
        _renderer.Visit(_sut.BuildSchemaFromIrType(ir)).Should().Be("PetSchema");
    }

    [Test]
    public void SelfReference_WrapsInVLazy()
    {
        var ir = new IrModelReference { ModelName = "TreeNode" };
        var rendered = _renderer.Visit(_sut.BuildSchemaFromIrType(ir, currentModelName: "TreeNode"));
        rendered.Should().StartWith("v.lazy(");
        rendered.Should().Contain("TreeNodeSchema");
    }

    [Test]
    public void EmptyModel_BecomesVObjectOfEmptyLiteral()
    {
        var model = new IrModel
        {
            Name = "Empty",
            Properties = Array.Empty<IrProperty>(),
        };
        var rendered = _renderer.Visit(_sut.BuildSchemaFromIrModel(model));
        rendered.Should().Contain("v.object(");
    }

    [Test]
    public void EnumModel_BecomesVPicklistOfValuesArray()
    {
        var e = new IrEnum
        {
            Name = "Status",
            BaseType = IrEnumBaseType.String,
            Values = Array.Empty<IrEnumValue>(),
        };
        var rendered = _renderer.Visit(_sut.BuildEnumSchema(e));
        rendered.Should().Be("v.picklist(StatusValues)");
    }
}
