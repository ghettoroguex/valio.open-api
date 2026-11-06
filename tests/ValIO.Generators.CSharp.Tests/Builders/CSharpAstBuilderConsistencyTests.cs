using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Nodes;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Models;
using ValIO.Generators.CSharp.Options;
using ValIO.Generators.CSharp.Services;

namespace ValIO.Generators.CSharp.Tests.Builders;

/// <summary>Exercises consistent C# type, inheritance, literal, and nullable projections.</summary>
[TestFixture]
public sealed class CSharpAstBuilderConsistencyTests
{
    /// <summary>Renders syntax nodes for focused source assertions.</summary>
    private readonly CSharpAstRenderer _renderer = new();

    /// <summary>Ensures direct builder consumers cannot diverge from the plugin's type mapper.</summary>
    /// <param name="kind">The normalized primitive kind to compare.</param>
    [TestCase(IrPrimitiveKind.String)]
    [TestCase(IrPrimitiveKind.Integer)]
    [TestCase(IrPrimitiveKind.Long)]
    [TestCase(IrPrimitiveKind.Number)]
    [TestCase(IrPrimitiveKind.Boolean)]
    [TestCase(IrPrimitiveKind.Date)]
    [TestCase(IrPrimitiveKind.DateTime)]
    [TestCase(IrPrimitiveKind.Time)]
    [TestCase(IrPrimitiveKind.Uuid)]
    [TestCase(IrPrimitiveKind.Binary)]
    [TestCase(IrPrimitiveKind.ByteArray)]
    [TestCase(IrPrimitiveKind.File)]
    [TestCase(IrPrimitiveKind.Any)]
    [TestCase(IrPrimitiveKind.Void)]
    [TestCase(IrPrimitiveKind.Null)]
    public void PrimitiveMapping_AllEntryPointsAgree(IrPrimitiveKind kind)
    {
        var type = new IrPrimitiveType { PrimitiveKind = kind };
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));

        _renderer.Visit(builder.BuildType(type)).Should()
            .Be(_renderer.Visit(new CSharpIrTypeMapper().MapType(type)));
    }

    /// <summary>Represents the OpenAPI null type with a nullable C# reference.</summary>
    [Test]
    public void NullPrimitive_AllowsTheOnlySchemaValue()
    {
        var type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Null };

        _renderer.Visit(new CSharpIrTypeMapper().MapType(type)).Should().Be("object?");
    }

    /// <summary>Rejects numeric enum values that cannot be emitted faithfully as C# enum constants.</summary>
    /// <param name="value">The fractional or out-of-range schema value.</param>
    [TestCase("1.5")]
    [TestCase("9223372036854775808")]
    [TestCase("18446744073709551615")]
    public void InvalidNumericEnum_DoesNotSilentlyAssignAnOrdinal(string value)
    {
        var model = new IrEnum
        {
            Name = "InvalidCode", BaseType = IrEnumBaseType.Integer,
            Values = [new IrEnumValue { Name = "Value", Value = value }]
        };
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));

        var build = () => builder.BuildEnum(model);

        build.Should().Throw<NotSupportedException>().WithMessage("*signed 64-bit integer*");
    }

    /// <summary>Preserves keyword-shaped string values instead of emitting executable syntax.</summary>
    /// <param name="value">A schema default which is ordinary text.</param>
    [TestCase("default")]
    [TestCase("default!")]
    public void StringDefault_KeywordText_RemainsQuoted(string value)
    {
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));
        var property = Property("value", "Value") with
        {
            Constraints = new IrPropertyConstraints { DefaultValue = value }
        };

        _renderer.Render(builder.BuildProperty(property)).Should()
            .Contain($"= \"{value}\";");
        _renderer.Visit(new LiteralExpressionNode(value, LiteralKind.String))
            .Should().Be($"\"{value}\"");
    }

    /// <summary>Rejects numeric defaults that would change meaning or fail C# compilation.</summary>
    /// <param name="kind">The emitted numeric kind.</param>
    /// <param name="value">The out-of-range, fractional, or non-finite schema value.</param>
    [TestCase(IrPrimitiveKind.Integer, "2147483648")]
    [TestCase(IrPrimitiveKind.Integer, "-2147483649")]
    [TestCase(IrPrimitiveKind.Integer, "1.5")]
    [TestCase(IrPrimitiveKind.Long, "9223372036854775808")]
    [TestCase(IrPrimitiveKind.Long, "18446744073709551615")]
    [TestCase(IrPrimitiveKind.Number, "NaN")]
    [TestCase(IrPrimitiveKind.Number, "Infinity")]
    [TestCase(IrPrimitiveKind.Number, "1e500")]
    public void InvalidNumericDefault_DoesNotRoundOrOverflow(IrPrimitiveKind kind, string value)
    {
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));
        var property = Property("value", "Value") with
        {
            Type = new IrPrimitiveType { PrimitiveKind = kind },
            Constraints = new IrPropertyConstraints { DefaultValue = value }
        };

        builder.BuildProperty(property).DefaultValue.Should().BeNull();
    }

    /// <summary>Separates contextual default values from literal text and preserves clone metadata.</summary>
    [Test]
    public void DefaultExpression_RendersTypedValueAndClonesItsState()
    {
        var expression = new DefaultExpressionNode(
            suppressNullableWarning: true,
            type: new SimpleTypeNode(new IdentifierNode("string")),
            metadata: new Dictionary<string, object> { ["origin"] = "initializer" });

        var clone = (DefaultExpressionNode)expression.Clone();

        _renderer.Visit(expression).Should().Be("default(string)!");
        _renderer.Visit(clone).Should().Be("default(string)!");
        clone.Should().NotBeSameAs(expression);
        clone.Type.Should().NotBeSameAs(expression.Type);
        clone.Children.Should().ContainSingle().Which.Should().BeSameAs(clone.Type);
        clone.Metadata["origin"].Should().Be("initializer");
    }

    /// <summary>Emits a single nullable marker for already-nullable optional inputs.</summary>
    [Test]
    public void NullableOptionalParameters_AvoidDuplicateNullableMarkers()
    {
        var nullableInteger = new IrNullableType
        {
            InnerType = new IrNullableType
            {
                InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer }
            }
        };
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));
        var operation = new IrOperation
        {
            MethodName = "GetItems",
            HttpMethod = "GET",
            Path = "/items",
            PathParameters = [],
            QueryParameters = [new IrParameter { Name = "limit", Type = nullableInteger, IsRequired = false }],
            HeaderParameters = [new IrParameter { Name = "revision", Type = nullableInteger, IsRequired = false }],
            RequestBody = new IrRequestBody { Type = nullableInteger, IsRequired = false },
            Responses = [new IrResponse { StatusCode = "204" }]
        };

        var code = _renderer.Render(builder.BuildMethod(operation));

        code.Should().Contain("int? limit").And.Contain("int? revision").And.Contain("int? request");
        code.Should().NotContain("int??");
        code.Should().Contain("CancellationToken cancellationToken = default");
    }

    /// <summary>Includes every multiple-allOf base property and compares nullable fields safely.</summary>
    [Test]
    public void MultipleAllOfBases_FlattenAllStateWithoutDuplicateEqualityChecks()
    {
        var first = new IrModel { Name = "Named", Properties = [Property("name", "Name")] };
        var second = new IrModel { Name = "Tagged", Properties = [Property("tag", "Tag")] };
        var combined = new IrModel
        {
            Name = "Combined",
            Properties = [Property("name", "Name"), Property("own", "Own")],
            Composition = AllOf("Named", "Tagged")
        };
        var builder = BuilderWithModels(first, second, combined);

        var code = _renderer.Render(builder.BuildCompilationUnit(combined, "Generated"));

        code.Should().Contain("class Combined : IEquatable<Combined>");
        code.Should().Contain("public string Name").And.Contain("public string Tag").And.Contain("public string Own");
        code.Should().Contain("object.Equals(this.Tag, other.Tag)");
        code.Split("hashCode.Add(this.Name)").Should().HaveCount(2);
        code.Should().NotContain("this.Tag.Equals");
        code.Should().Contain("using System.Text.Json.Serialization;");
    }

    /// <summary>Keeps an intermediate generated base open while sealing terminal classes.</summary>
    [Test]
    public void UseSealed_DoesNotSealIntermediateBases()
    {
        var root = new IrModel { Name = "Root", Properties = [Property("id", "Id")] };
        var middle = new IrModel { Name = "Middle", Properties = [], Composition = AllOf("Root") };
        var leaf = new IrModel { Name = "Leaf", Properties = [], Composition = AllOf("Middle") };
        var builder = BuilderWithModels(root, middle, leaf);

        builder.BuildClass(root).IsSealed.Should().BeFalse();
        builder.BuildClass(middle).IsSealed.Should().BeFalse();
        builder.BuildClass(leaf).IsSealed.Should().BeTrue();
    }

    /// <summary>Rejects composed alternatives before they can disappear from generated classes.</summary>
    [Test]
    public void NestedComposition_RequiresAnExplicitValueRepresentation()
    {
        var model = new IrModel
        {
            Name = "Choice", Properties = [],
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = [new IrUnionType { Types = [new IrModelReference { ModelName = "First" }, new IrModelReference { ModelName = "Second" }] }]
            }
        };
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));

        var build = () => builder.BuildClass(model);

        build.Should().Throw<NotSupportedException>().WithMessage("*composed value representation*");
    }

    /// <summary>Prefers a typed success body even when a bodyless or wildcard status appears first.</summary>
    [Test]
    public void SuccessResponseSelection_DoesNotDependOnStatusOrder()
    {
        var operation = OperationWithResponses(
            new IrResponse { StatusCode = "204" },
            new IrResponse { StatusCode = "2XX", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer } });
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));

        _renderer.Render(builder.BuildMethod(operation)).Should().Contain("Task<int> GetItem");
    }

    /// <summary>Reports incompatible result types before generating a misleading C# signature.</summary>
    [Test]
    public void DifferentSuccessTypes_RequireAnExplicitResultUnion()
    {
        var operation = OperationWithResponses(
            new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer } },
            new IrResponse { StatusCode = "201", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } });
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));

        var build = () => builder.BuildMethod(operation);

        build.Should().Throw<NotSupportedException>().WithMessage("*explicit result union*");
    }

    /// <summary>Preserves raw HTTP errors without interpreting arbitrary payloads as the first error schema.</summary>
    [Test]
    public void ErrorResponses_DoNotDeserializeUnrelatedPayloads()
    {
        var operation = OperationWithResponses(
            new IrResponse { StatusCode = "204" },
            new IrResponse { StatusCode = "400", Type = new IrModelReference { ModelName = "ValidationError" } },
            new IrResponse { StatusCode = "500", Type = new IrModelReference { ModelName = "ServerError" } });
        var builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));

        var code = _renderer.Render(builder.BuildMethod(operation));

        code.Should().NotContain("JsonSerializer.Deserialize");
        code.Should().Contain("throw new ApiException");
        code.Should().Contain("responseContent");
    }

    /// <summary>Creates a bodyless GET operation with a specified response set.</summary>
    /// <param name="responses">The statuses and body types to select.</param>
    /// <returns>The normalized operation.</returns>
    private static IrOperation OperationWithResponses(params IrResponse[] responses) => new()
    {
        MethodName = "GetItem", HttpMethod = "GET", Path = "/item",
        PathParameters = [], QueryParameters = [], HeaderParameters = [], Responses = responses
    };

    /// <summary>Builds a model lookup with sealing enabled for inheritance regressions.</summary>
    /// <param name="models">The models available to inheritance resolution.</param>
    /// <returns>A builder with the complete lookup.</returns>
    private static CSharpAstBuilder BuilderWithModels(params IrModel[] models) =>
        new(new CSharpNamingConverter(), CSharpDocumentFixture.Context(models, []),
            new CSharpGenerationOptions { UseSealed = true });

    /// <summary>Creates a string property with a distinct wire name.</summary>
    /// <param name="wireName">The source schema property name.</param>
    /// <param name="name">The emitted C# property identifier.</param>
    /// <returns>The property used in model regressions.</returns>
    private static IrProperty Property(string wireName, string name) => new()
    {
        Name = name,
        OriginalName = wireName,
        Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
        IsRequired = true
    };

    /// <summary>Creates an allOf composition from named schema references.</summary>
    /// <param name="names">The referenced model names.</param>
    /// <returns>The composition preserving the source order.</returns>
    private static IrComposition AllOf(params string[] names) => new()
    {
        Kind = IrCompositionKind.AllOf,
        Types = names.Select(name => (IrType)new IrModelReference { ModelName = name }).ToArray()
    };
}
