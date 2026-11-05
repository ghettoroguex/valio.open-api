using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Zod.Ast.Builders;
using ValIO.Generators.TypeScript.Zod.Ast.Nodes;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>
/// Tests for <see cref="ZodExpressionBuilder.BuildPrimitiveExpression"/> —
/// the pure builder that produces a Zod schema chain (z.string().uuid().min(…))
/// from an IR primitive + constraints. Rendered through the TS renderer to
/// keep assertions human-readable.
/// </summary>
[TestFixture]
public class ZodExpressionBuilderTests
{
    private TypeScriptAstRenderer _renderer = null!;

    [SetUp]
    public void Setup() => _renderer = new TypeScriptAstRenderer();

    private string Render(ZodExpressionSchemaNode node) =>
        _renderer.Visit(node.Expression);

    [Test]
    public void String_NoConstraints_BareStringCall()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.String, useBigInt: false);
        Render(schema).Should().Be("z.string()");
    }

    [Test]
    public void Boolean_NoConstraints_BareBooleanCall()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Boolean, useBigInt: false);
        Render(schema).Should().Be("z.boolean()");
    }

    /// <summary>Checks that number schemas do not coerce input.</summary>
    [Test]
    public void Number_NoConstraints_StrictNumber()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Number, useBigInt: false);
        Render(schema).Should().Be("z.number()");
    }

    [Test]
    public void Null_BareNullCall()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Null, useBigInt: false);
        Render(schema).Should().Be("z.null()");
    }

    /// <summary>Checks integer validation without input coercion.</summary>
    [Test]
    public void Integer_AddsIntRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Integer, useBigInt: false);
        Render(schema).Should().Contain("z.number()").And.Contain(".int()");
    }

    /// <summary>Checks that long values use a strict number when bigint is disabled.</summary>
    [Test]
    public void Long_WithoutBigInt_FallsBackToNumber()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Long, useBigInt: false);
        Render(schema).Should().Be("z.number().int()");
    }

    /// <summary>Checks that bigint values are not coerced.</summary>
    [Test]
    public void Long_WithBigInt_UsesStrictBigint()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Long, useBigInt: true);
        Render(schema).Should().Be("z.bigint()");
    }

    [Test]
    public void Uuid_AddsUuidRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Uuid, useBigInt: false);
        Render(schema).Should().Be("z.string().uuid()");
    }

    [Test]
    public void DateTime_AddsDatetimeWithOffset()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.DateTime, useBigInt: false);
        var rendered = Render(schema);
        rendered.Should().Contain("z.string()").And.Contain(".datetime(").And.Contain("offset: true");
    }

    [Test]
    public void Date_AddsDateRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Date, useBigInt: false);
        Render(schema).Should().Be("z.string().date()");
    }

    [Test]
    public void Time_AddsTimeRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Time, useBigInt: false);
        Render(schema).Should().Be("z.string().time()");
    }

    [Test]
    public void Any_FallsBackToZAny()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Any, useBigInt: false);
        Render(schema).Should().Be("z.any()");
    }

    [Test]
    public void String_WithMinLength_AddsMinLiteral()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(
            IrPrimitiveKind.String, useBigInt: false,
            constraints: new IrPropertyConstraints { MinLength = 3 });
        Render(schema).Should().Contain(".min(3)");
    }

    [Test]
    public void String_WithPattern_AddsRegex()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(
            IrPrimitiveKind.String, useBigInt: false,
            constraints: new IrPropertyConstraints { Pattern = "^a-z+$" });
        var rendered = Render(schema);
        rendered.Should().Contain(".regex(").And.Contain("new RegExp(");
        rendered.Should().Contain("^a-z+$");
    }

    [Test]
    public void Number_WithMinMax_AddsBothChains()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(
            IrPrimitiveKind.Number, useBigInt: false,
            constraints: new IrPropertyConstraints { Min = 0, Max = 100 });
        var rendered = Render(schema);
        rendered.Should().Contain(".min(0)").And.Contain(".max(100)");
    }

    [Test]
    public void Number_WithExclusiveBounds_UsesGtAndLt()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(
            IrPrimitiveKind.Number, useBigInt: false,
            constraints: new IrPropertyConstraints { ExclusiveMin = 1, ExclusiveMax = 10 });
        var rendered = Render(schema);
        rendered.Should().Contain(".gt(1)").And.Contain(".lt(10)");
    }

    [Test]
    public void ConstraintsRef_SubstitutesObjectRefs_InsteadOfLiterals()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(
            IrPrimitiveKind.String, useBigInt: false,
            constraints: new IrPropertyConstraints { MinLength = 3, MaxLength = 99 },
            constraintsRef: "UserConstraints.email");
        var rendered = Render(schema);
        rendered.Should().Contain("UserConstraints.email.minLength");
        rendered.Should().Contain("UserConstraints.email.maxLength");
        rendered.Should().NotContain(".min(3)"); // would mean literal substituted instead of ref
    }

    [Test]
    public void String_WithNoConstraintsField_KeepsBareChain()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.String, useBigInt: false);
        Render(schema).Should().NotContain(".min");
    }
}
