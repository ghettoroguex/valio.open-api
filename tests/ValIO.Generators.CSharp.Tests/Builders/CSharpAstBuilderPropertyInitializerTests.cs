using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Services;

namespace ValIO.Generators.CSharp.Tests.Builders;

/// <summary>
/// Regression tests for CS8618 ("non-nullable property must contain a non-null value when exiting
/// constructor") on generated models in projects with <c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c>.
/// Also covers a property's default-value initializer actually being rendered — previously
/// <c>CSharpAstRenderer.RenderProperty</c> silently dropped it.
/// </summary>
[TestFixture]
public class CSharpAstBuilderPropertyInitializerTests
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
    public void NonNullableReferenceProperty_GetsDefaultBangInitializer()
    {
        var property = new IrProperty
        {
            OriginalName = "name",
            Name = "Name",
            Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
            IsRequired = true,
        };

        var code = _renderer.Render(_builder.BuildProperty(property));

        code.Should().Contain("public string Name { get; set; } = default!;");
    }

    [Test]
    public void NullableProperty_NoDefaultBangNeeded()
    {
        var property = new IrProperty
        {
            OriginalName = "nickname",
            Name = "Nickname",
            Type = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } },
            IsRequired = false,
        };

        var code = _renderer.Render(_builder.BuildProperty(property));

        code.Should().Contain("public string? Nickname { get; set; }");
        code.Should().NotContain("default!");
    }

    [Test]
    public void NonNullableArrayProperty_AlsoGetsDefaultBangInitializer()
    {
        var property = new IrProperty
        {
            OriginalName = "tags",
            Name = "Tags",
            Type = new IrArrayType { ElementType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } },
            IsRequired = true,
        };

        var code = _renderer.Render(_builder.BuildProperty(property));

        code.Should().Contain("= default!;");
    }
}
