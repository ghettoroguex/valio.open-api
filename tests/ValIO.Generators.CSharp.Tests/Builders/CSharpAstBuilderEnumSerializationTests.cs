using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Services;

namespace ValIO.Generators.CSharp.Tests.Builders;

/// <summary>
/// Regression tests for string-enum round-tripping through <c>System.Text.Json</c>.
/// <c>[EnumMember(Value = "...")]</c> alone is ignored by <c>System.Text.Json</c> — without
/// <c>[JsonConverter(typeof(JsonStringEnumConverter))]</c> on the enum type, values serialize as
/// numbers, and without <c>[JsonStringEnumMemberName]</c> per member, the wire value produced/expected
/// (e.g. "in_progress") diverges from the PascalCase member name ("InProgress").
/// </summary>
[TestFixture]
public class CSharpAstBuilderEnumSerializationTests
{
    private CSharpAstBuilder _builder = null!;
    private CSharpAstRenderer _renderer = null!;

    /// <summary>Creates a renderer and a builder with the explicit fragment document context.</summary>
    [SetUp]
    public void Setup()
    {
        _builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));
        _renderer = new CSharpAstRenderer();
    }

    [Test]
    public void StringEnum_HasJsonStringEnumConverterOnType()
    {
        var enumIr = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values = [new IrEnumValue { Name = "InProgress", Value = "in_progress" }],
        };

        var code = _renderer.Render(_builder.BuildCompilationUnit(enumIr, "Test.Ns"));

        code.Should().Contain("using System.Text.Json.Serialization;");
        code.Should().Contain("[JsonConverter(typeof(JsonStringEnumConverter))]");
    }

    [Test]
    public void StringEnum_MemberKeepsOriginalWireValue_ViaJsonStringEnumMemberName()
    {
        var enumIr = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values =
            [
                new IrEnumValue { Name = "InProgress", Value = "in_progress" },
                new IrEnumValue { Name = "Done", Value = "done" },
            ],
        };

        var code = _renderer.Render(_builder.BuildCompilationUnit(enumIr, "Test.Ns"));

        code.Should().Contain("[JsonStringEnumMemberName(\"in_progress\")]");
        code.Should().Contain("[JsonStringEnumMemberName(\"done\")]");
    }

    [Test]
    public void StringEnum_StillEmitsEnumMemberForBackwardCompatibility()
    {
        var enumIr = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values = [new IrEnumValue { Name = "InProgress", Value = "in_progress" }],
        };

        var code = _renderer.Render(_builder.BuildCompilationUnit(enumIr, "Test.Ns"));

        code.Should().Contain("[EnumMember(Value = \"in_progress\")]");
    }

    [Test]
    public void IntegerEnum_HasNoJsonStringEnumAttributes()
    {
        var enumIr = new IrEnum
        {
            Name = "Priority",
            BaseType = IrEnumBaseType.Integer,
            Values = [new IrEnumValue { Name = "Low", Value = 0 }],
        };

        var code = _renderer.Render(_builder.BuildCompilationUnit(enumIr, "Test.Ns"));

        code.Should().NotContain("JsonStringEnumConverter");
        code.Should().NotContain("JsonStringEnumMemberName");
        code.Should().NotContain("System.Text.Json.Serialization");
    }
}
