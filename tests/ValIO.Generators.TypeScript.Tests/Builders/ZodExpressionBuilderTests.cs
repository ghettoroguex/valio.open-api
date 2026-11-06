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

    /// <summary>Verifies model numbers use strict JSON number semantics by default.</summary>
    [Test]
    public void Number_NoConstraints_UsesStrictNumber()
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

    /// <summary>Verifies integers reject fractional values without coercing strings.</summary>
    [Test]
    public void Integer_AddsIntRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Integer, useBigInt: false);
        Render(schema).Should().Contain("z.number()").And.Contain(".int()");
    }

    /// <summary>Verifies int64 validation retains integer semantics when bigint is disabled.</summary>
    [Test]
    public void Long_WithoutBigInt_FallsBackToNumber()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Long, useBigInt: false);
        Render(schema).Should().Be("z.number().int()");
    }

    /// <summary>Verifies response schemas can validate numeric values without parameter coercion.</summary>
    [Test]
    public void Number_WithCoercionDisabled_UsesStrictNumber()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Number, useBigInt: false, coerceNumbers: false);
        Render(schema).Should().Be("z.number()");
    }

    /// <summary>Verifies void does not silently become an unconstrained value.</summary>
    [Test]
    public void Void_UsesVoidSchema()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Void, useBigInt: false);
        Render(schema).Should().Be("z.void()");
    }

    /// <summary>Verifies bigint mode validates bigint values without implicit boundary conversion.</summary>
    [Test]
    public void Long_WithBigInt_UsesStrictBigint()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Long, useBigInt: true);
        Render(schema).Should().Be("z.bigint()");
    }

    /// <summary>Verifies parameter numeric coercion remains available explicitly.</summary>
    [Test]
    public void Number_WithCoercionEnabled_UsesCoercedNumber()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Number, useBigInt: false, coerceNumbers: true);
        Render(schema).Should().Be("z.coerce.number()");
    }

    /// <summary>Verifies UUIDs share the canonical text predicate with other validator libraries.</summary>
    [Test]
    public void Uuid_AddsUuidRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Uuid, useBigInt: false);
        Render(schema).Should().Contain("z.string().refine(").And.Contain("[0-9a-fA-F]{8}");
    }

    /// <summary>Verifies timestamps use the shared RFC 3339 lexical, calendar, and offset checks.</summary>
    [Test]
    public void DateTime_AddsDatetimeWithOffset()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.DateTime, useBigInt: false);
        var rendered = Render(schema);
        rendered.Should().Contain("z.string().refine(").And.Contain("[Tt]").And.Contain("getUTCMinutes()");
    }

    /// <summary>Verifies calendar dates are checked without library-specific ISO interpretation.</summary>
    [Test]
    public void Date_AddsDateRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Date, useBigInt: false);
        Render(schema).Should().Contain("z.string().refine(").And.Contain(".toISOString().slice(");
    }

    /// <summary>Verifies full times require an explicit RFC 3339 timezone.</summary>
    [Test]
    public void Time_AddsTimeRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Time, useBigInt: false);
        Render(schema).Should().Contain("z.string().refine(").And.Contain("[Zz]");
    }

    /// <summary>Verifies Zod 3 string constraints are applied before the shared predicate introduces a ZodEffects wrapper.</summary>
    [Test]
    public void FormattedString_AppliesConstraintsBeforeRefinement()
    {
        var schema = ZodExpressionBuilder.BuildPrimitiveExpression(IrPrimitiveKind.Date, useBigInt: false,
            constraints: new IrPropertyConstraints { MinLength = 10, MaxLength = 10, Pattern = "^2024" });
        var rendered = Render(schema);
        rendered.Should().Contain("z.string().min(10).max(10).regex(").And.Contain(".refine(");
        rendered.IndexOf(".regex(", StringComparison.Ordinal).Should().BeLessThan(rendered.IndexOf(".refine(", StringComparison.Ordinal));
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
