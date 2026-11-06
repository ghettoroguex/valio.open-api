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
/// Regression tests for query-string building in <see cref="CSharpAstBuilder"/>:
/// - optional (nullable) query parameters must not be appended to the URL unconditionally,
///   otherwise a null value throws (ArgumentNullException/NullReferenceException) or literally
///   serializes as "null" in the request;
/// - array query parameters must expand into repeated key=value pairs (form/explode=true style),
///   not <c>.ToString()</c> the collection into a single garbage value.
/// </summary>
[TestFixture]
public class CSharpAstBuilderQueryParametersTests
{
    /// <summary>Builds HTTP operation syntax for the query regression cases.</summary>
    private CSharpAstBuilder _builder = null!;
    /// <summary>Renders the syntax used by query source assertions.</summary>
    private CSharpAstRenderer _renderer = null!;

    /// <summary>Creates fresh builder and renderer instances.</summary>
    [SetUp]
    public void Setup()
    {
        _builder = new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []));
        _renderer = new CSharpAstRenderer();
    }

    /// <summary>Creates a parameter retaining its wire name.</summary>
    /// <param name="name">The source and wire name.</param>
    /// <param name="type">The normalized parameter type.</param>
    /// <param name="isRequired">Whether the parameter must be supplied.</param>
    /// <returns>The parameter metadata.</returns>
    private static IrParameter Param(string name, IrType type, bool isRequired) =>
        new() { Name = name, Type = type, IsRequired = isRequired };

    /// <summary>Creates a GET operation with the specified query arguments.</summary>
    /// <param name="queryParams">The query parameters in source order.</param>
    /// <returns>The operation metadata.</returns>
    private static IrOperation Operation(params IrParameter[] queryParams) => new()
    {
        MethodName = "GetItems",
        HttpMethod = "GET",
        Path = "/items",
        PathParameters = [],
        QueryParameters = queryParams,
        HeaderParameters = [],
        Responses = [new IrResponse { StatusCode = "200" }],
    };

    /// <summary>Gets the string scalar used in query cases.</summary>
    /// <value>The normalized string type.</value>
    private static IrPrimitiveType StringType => new() { PrimitiveKind = IrPrimitiveKind.String };
    /// <summary>Gets the UUID scalar used in query cases.</summary>
    /// <value>The normalized UUID type.</value>
    private static IrPrimitiveType UuidType => new() { PrimitiveKind = IrPrimitiveKind.Uuid };

    /// <summary>Verifies optionalscalarqueryparam isguardedbynullcheck.</summary>
    [Test]
    public void OptionalScalarQueryParam_IsGuardedByNullCheck()
    {
        var op = Operation(Param("search", StringType, isRequired: false));
        var code = _renderer.Render(_builder.BuildMethod(op));

        code.Should().Contain("if (search != null)");
        code.Should().Contain("queryParts.Add(\"search=\" + Uri.EscapeDataString(HttpWireValueFormatter.Format(search)))");
    }

    /// <summary>Verifies requiredscalarqueryparam isaddedunconditionally.</summary>
    [Test]
    public void RequiredScalarQueryParam_IsAddedUnconditionally()
    {
        var op = Operation(Param("id", UuidType, isRequired: true));
        var code = _renderer.Render(_builder.BuildMethod(op));

        code.Should().NotContain("if (id != null)");
        code.Should().Contain("queryParts.Add(\"id=\" + Uri.EscapeDataString(HttpWireValueFormatter.Format(id)))");
    }

    /// <summary>Verifies arrayqueryparam expandstorepeatedkeyvaluepairs.</summary>
    [Test]
    public void ArrayQueryParam_ExpandsToRepeatedKeyValuePairs()
    {
        var arrayType = new IrArrayType { ElementType = UuidType };
        var op = Operation(Param("projectIds", arrayType, isRequired: false));
        var code = _renderer.Render(_builder.BuildMethod(op));

        code.Should().Contain("if (projectIds != null)");
        code.Should().Contain("foreach (var item in projectIds)");
        code.Should().Contain("queryParts.Add(\"projectIds=\" + Uri.EscapeDataString(HttpWireValueFormatter.Format(item)))");
    }

    /// <summary>Verifies arrayofstrings escapeseachitem.</summary>
    [Test]
    public void ArrayOfStrings_EscapesEachItem()
    {
        var arrayType = new IrArrayType { ElementType = StringType };
        var op = Operation(Param("tags", arrayType, isRequired: true));
        var code = _renderer.Render(_builder.BuildMethod(op));

        code.Should().Contain("foreach (var item in tags)");
        code.Should().Contain("queryParts.Add(\"tags=\" + Uri.EscapeDataString(HttpWireValueFormatter.Format(item)))");
        // Required array — no outer null guard.
        code.Should().NotContain("if (tags != null)");
    }

    /// <summary>Verifies queryparts onlyappendedtourlwhennonempty.</summary>
    [Test]
    public void QueryParts_OnlyAppendedToUrlWhenNonEmpty()
    {
        var op = Operation(Param("search", StringType, isRequired: false));
        var code = _renderer.Render(_builder.BuildMethod(op));

        code.Should().Contain("List<string> queryParts = new List<string>();");
        code.Should().Contain("if (queryParts.Count > 0)");
        code.Should().Contain("urlBuilder.Append(string.Join(\"&\", queryParts))");
    }

    /// <summary>Verifies noqueryparameters doesnotemitquerypartsscaffolding.</summary>
    [Test]
    public void NoQueryParameters_DoesNotEmitQueryPartsScaffolding()
    {
        var op = Operation();
        var code = _renderer.Render(_builder.BuildMethod(op));

        code.Should().NotContain("queryParts");
    }

    /// <summary>Verifies queryparamwithdottedwirename usesoriginalnameasurlkeybutsanitizedidentifier.</summary>
    [Test]
    public void QueryParamWithDottedWireName_UsesOriginalNameAsUrlKeyButSanitizedIdentifier()
    {
        // ASP.NET биндит вложенную модель (PagingQuery) по точечным именам Paging.Offset/Paging.Take.
        // Sanitized-идентификатор метода — Paging_Offset (точка недопустима в имени параметра C#),
        // но ключ в URL обязан остаться исходным Paging.Offset, иначе сервер параметр не увидит.
        var op = Operation(new IrParameter
        {
            Name = "Paging_Offset",
            OriginalName = "Paging.Offset",
            Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer },
            IsRequired = true,
        });

        var code = _renderer.Render(_builder.BuildMethod(op));

        // Ключ в URL — исходное «проводное» имя (с точкой)…
        code.Should().Contain("queryParts.Add(\"Paging.Offset=\" + Uri.EscapeDataString(HttpWireValueFormatter.Format(Paging_Offset)))");
        // …а НЕ sanitized-идентификатор с подчёркиванием.
        code.Should().NotContain("\"Paging_Offset=\"");
    }

    /// <summary>Verifies queryparam withoutexplicitoriginalname fallsbacktoname.</summary>
    [Test]
    public void QueryParam_WithoutExplicitOriginalName_FallsBackToName()
    {
        // OriginalName по умолчанию равен Name — обычные (без точки) параметры не регрессируют.
        var op = Operation(Param("countryId", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer }, isRequired: false));
        var code = _renderer.Render(_builder.BuildMethod(op));

        code.Should().Contain("queryParts.Add(\"countryId=\" + Uri.EscapeDataString(HttpWireValueFormatter.Format(countryId)))");
    }
}
