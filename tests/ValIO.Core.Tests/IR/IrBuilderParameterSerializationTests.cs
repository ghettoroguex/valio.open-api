using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Protects normalized parameter serialization metadata across source versions and reader conversions.</summary>
[TestFixture]
public sealed class IrBuilderParameterSerializationTests
{
    /// <summary>Preserves explicit styles, explicit explode flags, location defaults, and referenced parameter metadata.</summary>
    /// <param name="version">The OpenAPI source version represented by a complete disk fixture.</param>
    /// <returns>The asynchronous parameter contract check.</returns>
    [TestCase("3.0")]
    [TestCase("3.1")]
    [TestCase("3.2")]
    public async Task OpenApi3Parameters_PreserveSerializationDefaultsAndOverrides(string version)
    {
        var operation = (await BuildAsync(version)).Clients.Single().Operations.Single();
        var parameters = operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters)
            .ToDictionary(parameter => parameter.OriginalName, StringComparer.Ordinal);

        AssertSerialization(parameters["pathDefault"], IrParameterStyle.Simple, false);
        AssertSerialization(parameters["pathLabel"], IrParameterStyle.Label, true);
        AssertSerialization(parameters["pathMatrix"], IrParameterStyle.Matrix, false);
        AssertSerialization(parameters["queryDefault"], IrParameterStyle.Form, true);
        AssertSerialization(parameters["queryForm"], IrParameterStyle.Form, true);
        AssertSerialization(parameters["queryCsv"], IrParameterStyle.Form, false);
        AssertSerialization(parameters["querySpaces"], IrParameterStyle.SpaceDelimited, false);
        AssertSerialization(parameters["queryPipes"], IrParameterStyle.PipeDelimited, false);
        AssertSerialization(parameters["queryObject"], IrParameterStyle.DeepObject, true);
        AssertSerialization(parameters["queryReference"], IrParameterStyle.PipeDelimited, false);
        AssertSerialization(parameters["headerDefault"], IrParameterStyle.Simple, false);
        AssertSerialization(parameters["headerExploded"], IrParameterStyle.Simple, true);
        parameters.Should().HaveCount(12);
    }

    /// <summary>Retains the Swagger collection formats that the installed reader converts without ambiguity.</summary>
    /// <param name="wireName">The source parameter name selecting a reader-supported collection format.</param>
    /// <param name="style">The language-independent style expected after Swagger conversion.</param>
    /// <param name="explode">The effective explode flag expected after Swagger conversion.</param>
    /// <returns>The asynchronous source-to-IR serialization check.</returns>
    [TestCase("pathDefault", IrParameterStyle.Simple, false)]
    [TestCase("queryMulti", IrParameterStyle.Form, true)]
    [TestCase("querySpaces", IrParameterStyle.SpaceDelimited, false)]
    [TestCase("queryPipes", IrParameterStyle.PipeDelimited, false)]
    [TestCase("headerDefault", IrParameterStyle.Simple, false)]
    [TestCase("headerCsv", IrParameterStyle.Simple, false)]
    public async Task Swagger2ReaderSupportedCollectionFormats_PreserveAvailableSemantics(
        string wireName, IrParameterStyle style, bool explode)
    {
        var operation = (await BuildAsync("2.0")).Clients.Single().Operations.Single();
        var parameter = operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters)
            .Single(candidate => candidate.OriginalName == wireName);

        AssertSerialization(parameter, style, explode);
        parameter.Type.Should().BeOfType<IrArrayType>();
    }

    /// <summary>Preserves every effective Swagger reader flag without claiming unavailable CSV versus multi distinctions.</summary>
    /// <remarks>
    /// Microsoft.OpenApi 3.5.4 normalizes Swagger query CSV, multi, and omitted collectionFormat identically.
    /// This check covers the IR boundary only; correcting that source-reader loss requires raw parser metadata.
    /// </remarks>
    /// <returns>The asynchronous SDK-boundary preservation check.</returns>
    [Test]
    public async Task Swagger2NormalizedReaderFlags_AreNotReplacedByQueryDefaults()
    {
        var document = await IrTestHarness.LoadAsync(OpenApiFixture.Read("CoreIR/parameter-serialization-2.0.json"));
        var operation = new IrBuilder(new SchemaResolver(document), new NamingConverter())
            .BuildFromOpenApi(document).Clients.Single().Operations.Single();
        var sourceParameters = document.Paths.Single().Value.Operations!.Single().Value.Parameters!;
        var parameters = operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters)
            .ToDictionary(parameter => parameter.OriginalName, StringComparer.Ordinal);

        foreach (var source in sourceParameters)
        {
            parameters[source.Name!].Explode.Should().Be(source.Explode,
                "the IR must retain the effective reader value for {0}", source.Name);
            parameters[source.Name!].Style.Should().NotBeNull("the real source reader always supplies normalized IR metadata");
        }
    }

    /// <summary>Rejects unknown reader styles instead of silently selecting a different wire encoding.</summary>
    /// <returns>The asynchronous unsupported-style check.</returns>
    [Test]
    public async Task UnknownReaderStyle_FailsWithParameterContext()
    {
        var document = await IrTestHarness.LoadAsync(OpenApiFixture.Read("CoreIR/parameter-serialization-3.1.json"));
        var source = (OpenApiParameter)document.Paths.Single().Value.Operations!.Single().Value.Parameters!.First();
        source.Style = (ParameterStyle)int.MaxValue;

        var build = () => new IrBuilder(new SchemaResolver(document), new NamingConverter()).BuildFromOpenApi(document);

        build.Should().Throw<InvalidOperationException>().WithMessage("*pathDefault*unsupported serialization style*");
    }

    /// <summary>Allows existing manually constructed parameter contracts to omit the additive serialization metadata.</summary>
    [Test]
    public void ManuallyConstructedParameter_CanOmitSerializationMetadata()
    {
        var parameter = new IrParameter
        {
            Name = "values",
            Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
            IsRequired = false,
        };

        parameter.Style.Should().BeNull();
        parameter.Explode.Should().BeNull();
    }

    /// <summary>Checks the typed serialization metadata using a parameter's wire identity as diagnostic context.</summary>
    /// <param name="parameter">The normalized source parameter.</param>
    /// <param name="style">The expected serialization style.</param>
    /// <param name="explode">The expected effective explode flag.</param>
    private static void AssertSerialization(IrParameter parameter, IrParameterStyle style, bool explode)
    {
        parameter.Style.Should().Be(style, "serialization style must be preserved for {0}", parameter.OriginalName);
        parameter.Explode.Should().Be(explode, "the explode flag must be preserved for {0}", parameter.OriginalName);
    }

    /// <summary>Builds the IR through the actual reader using the version-specific disk fixture.</summary>
    /// <param name="version">The fixture's source version.</param>
    /// <returns>The normalized operation document.</returns>
    private static async Task<IrDocument> BuildAsync(string version)
    {
        var document = await IrTestHarness.LoadAsync(OpenApiFixture.Read($"CoreIR/parameter-serialization-{version}.json"));
        return new IrBuilder(new SchemaResolver(document), new NamingConverter()).BuildFromOpenApi(document);
    }
}
