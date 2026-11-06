using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;
using ValIO.Generators.Cpp.Tests.TestHelpers;

namespace ValIO.Generators.Cpp.Tests.Builders.Std;

/// <summary>Checks generated HTTP parameters retain typed values and schema-defined collection encodings.</summary>
[TestFixture]
public sealed class StdParameterWireTests
{
    /// <summary>Checks presence, scalar formatting, and enum conversion belong to the shared request runtime.</summary>
    [Test]
    public void Scalars_UseDirectTypedCallsWithoutGeneratedPresenceOrStringConversion()
    {
        var source = Render([
            Parameter("Enabled", "enabled", Primitive(IrPrimitiveKind.Boolean)),
            Parameter("Count", "count", Primitive(IrPrimitiveKind.Integer)),
            Parameter("Text", "text", Primitive(IrPrimitiveKind.String)),
            Parameter("State", "state", new IrModelReference { ModelName = "State" }),
            Parameter("RequiredValue", "required", new IrNullableType { InnerType = Primitive(IrPrimitiveKind.Integer) }) with { IsRequired = true },
        ], [Parameter("Header", "X-Count", Primitive(IrPrimitiveKind.Integer))]);
        source.Should().Contain("httpRequest.AddQuery(\"enabled\", Enabled);");
        source.Should().Contain("httpRequest.AddQuery(\"count\", Count);");
        source.Should().Contain("httpRequest.AddQuery(\"text\", Text);");
        source.Should().Contain("httpRequest.AddQuery(\"state\", State);");
        source.Should().Contain("httpRequest.AddHeader(\"X-Count\", Header);");
        source.Should().Contain("httpRequest.AddQuery(\"required\", ValIO::RequireParameterValue(\"required\", RequiredValue));");
        source.Should().NotContain("if (Enabled)").And.NotContain("if (Count)").And.NotContain("if (Text)");
        source.Should().NotContain("std::to_string").And.NotContain("? \"true\" : \"false\"");
    }

    /// <summary>Checks every supported array style selects an explicit named runtime encoding.</summary>
    /// <param name="style">The schema parameter style, or a legacy IR default.</param>
    /// <param name="explode">The schema explode value, or its style default.</param>
    /// <param name="expected">The runtime encoding member.</param>
    [TestCase(null, null, "Repeated")]
    [TestCase(IrParameterStyle.Form, true, "Repeated")]
    [TestCase(IrParameterStyle.Form, false, "CommaSeparated")]
    [TestCase(IrParameterStyle.SpaceDelimited, false, "SpaceSeparated")]
    [TestCase(IrParameterStyle.PipeDelimited, false, "PipeSeparated")]
    public void QueryArrays_SelectDeclaredEncoding(IrParameterStyle? style, bool? explode, string expected)
    {
        var source = Render([Parameter("Values", "values", new IrArrayType { ElementType = Primitive(IrPrimitiveKind.Integer) }) with { Style = style, Explode = explode }]);
        source.Should().Contain($"httpRequest.AddQuery(\"values\", Values, ValIO::CollectionEncoding::{expected});");
        source.Should().NotContain("if (Values)");
    }

    /// <summary>Checks header arrays use simple comma-separated values and model leaves retain their JSON formatter.</summary>
    [Test]
    public void HeaderArraysAndModelLeaves_RetainTheirWireRepresentation()
    {
        var source = Render([
            Parameter("Filter", "filter", new IrModelReference { ModelName = "Filter" }),
            Parameter("Filters", "filters", new IrArrayType { ElementType = new IrModelReference { ModelName = "Filter" } }),
        ], [Parameter("Values", "X-Values", new IrArrayType { ElementType = Primitive(IrPrimitiveKind.Boolean) })]);
        source.Should().Contain("httpRequest.AddHeader(\"X-Values\", Values, ValIO::CollectionEncoding::CommaSeparated);");
        source.Should().Contain("httpRequest.AddQuery(\"filter\", Filter, [](const auto& value)");
        source.Should().Contain("return ValIO::Dump(value, ValIO::JsonStringFormat::Plain);");
        source.Should().MatchRegex(@"}\s*, ValIO::CollectionEncoding::Repeated\);");
    }

    /// <summary>Checks unsupported collection contracts fail clearly instead of silently changing their wire meaning.</summary>
    /// <param name="style">An array style with no supported query encoding.</param>
    /// <param name="explode">The unsupported explode setting.</param>
    [TestCase(IrParameterStyle.DeepObject, true)]
    [TestCase(IrParameterStyle.SpaceDelimited, true)]
    [TestCase(IrParameterStyle.Simple, false)]
    public void UnsupportedArrayStyle_ReportsTheContract(IrParameterStyle style, bool explode)
    {
        var parameter = Parameter("Values", "values", new IrArrayType { ElementType = Primitive(IrPrimitiveKind.Integer) }) with { Style = style, Explode = explode };
        var action = () => Render([parameter]);
        action.Should().Throw<NotSupportedException>().WithMessage("*values*unsupported*style*");
    }

    /// <summary>Checks scalar values cannot bypass unsupported object or path serialization styles.</summary>
    /// <param name="style">The serialization style incompatible with a scalar query value.</param>
    [TestCase(IrParameterStyle.DeepObject)]
    [TestCase(IrParameterStyle.Label)]
    [TestCase(IrParameterStyle.Matrix)]
    [TestCase(IrParameterStyle.Cookie)]
    public void UnsupportedScalarStyle_ReportsTheContract(IrParameterStyle style)
    {
        var action = () => Render([Parameter("Value", "value", Primitive(IrPrimitiveKind.String)) with { Style = style }]);
        action.Should().Throw<NotSupportedException>().WithMessage("*value*unsupported query style*");
    }

    /// <summary>Checks object parameter declarations cannot bypass an explicit unsupported deep-object style.</summary>
    [Test]
    public void DeepObjectStyle_ReportsUnsupportedObjectEncoding()
    {
        var action = () => Render([Parameter("Filter", "filter", new IrModelReference { ModelName = "Filter" }) with { Style = IrParameterStyle.DeepObject, Explode = true }]);
        action.Should().Throw<NotSupportedException>().WithMessage("*filter*unsupported query style*DeepObject*");
    }

    /// <summary>Checks parsed object parameters are rejected until their actual form object codec is implemented.</summary>
    [Test]
    public void ExplicitObjectFormStyle_RejectsLegacyJsonSubstitution()
    {
        var action = () => Render([Parameter("Filter", "filter", new IrModelReference { ModelName = "Filter" }) with { Style = IrParameterStyle.Form, Explode = true }]);
        action.Should().Throw<NotSupportedException>().WithMessage("*object parameter*filter*Form*object wire codec is not implemented*");
    }

    /// <summary>Checks nested arrays cannot fall through to a flat collection runtime overload.</summary>
    [Test]
    public void NestedArray_ReportsUnsupportedWireEncoding()
    {
        var action = () => Render([Parameter("Values", "values", new IrArrayType { ElementType = new IrArrayType { ElementType = Primitive(IrPrimitiveKind.Integer) } })]);
        action.Should().Throw<NotSupportedException>().WithMessage("*values*unsupported nested collection*");
    }

    /// <summary>Creates a scalar parameter with optional presence.</summary>
    /// <param name="name">The generated argument identifier.</param>
    /// <param name="wireName">The HTTP parameter name.</param>
    /// <param name="type">The declared value type.</param>
    /// <returns>The parameter contract.</returns>
    private static IrParameter Parameter(string name, string wireName, IrType type) => new() { Name = name, OriginalName = wireName, Type = type, IsRequired = false };

    /// <summary>Creates a primitive IR value.</summary>
    /// <param name="kind">The primitive category.</param>
    /// <returns>The scalar type contract.</returns>
    private static IrPrimitiveType Primitive(IrPrimitiveKind kind) => new() { PrimitiveKind = kind };

    /// <summary>Renders a real synchronous operation against declared model and enum indexes.</summary>
    /// <param name="queries">The query contracts under test.</param>
    /// <param name="headers">The header contracts under test.</param>
    /// <returns>The generated client implementation.</returns>
    private static string Render(IReadOnlyList<IrParameter> queries, IReadOnlyList<IrParameter>? headers = null)
    {
        var context = CppDocumentContextFactory.Create(
            [new IrModel { Name = "Filter", Properties = [] }],
            [new IrEnum { Name = "State", BaseType = IrEnumBaseType.String, Values = [new IrEnumValue { Name = "ready", Value = "ready" }] }]);
        var naming = new StdNamingConvention();
        var builder = new StdClientAstBuilder(new StdCppTypeMapper(naming, context), naming, context);
        var operation = new IrOperation { MethodName = "Browse", HttpMethod = "GET", Path = "/browse", PathParameters = [], QueryParameters = queries, HeaderParameters = headers ?? [], Responses = [new IrResponse { StatusCode = "204" }] };
        var result = builder.Build(new IrClient { Name = "Probe", Operations = [operation] }, "Api", new CppGenerationOptions());
        return new CppAstRenderer().Render(result.ClientSource);
    }
}
