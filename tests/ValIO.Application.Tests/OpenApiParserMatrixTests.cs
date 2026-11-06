using System.Net.Http;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Parsers.Services;
using ValIO.Tests.Fixtures;

namespace ValIO.Application.Tests;

/// <summary>Exercises the production parser and IR builder against each supported OpenAPI dialect and serialization.</summary>
    /// <returns>The asynchronous test operation.</returns>
[TestFixture]
public sealed class OpenApiParserMatrixTests
{
    /// <summary>Enumerates dialect and serialization pairs, including native YAML rather than JSON disguised as YAML.</summary>
    /// <value>The eight supported source combinations.</value>
    private static IEnumerable<TestCaseData> VersionsAndFormats
    {
        get
        {
            foreach (var version in new[] { "2.0", "3.0.4", "3.1.2", "3.2.0" })
            foreach (var format in new[] { "json", "yaml" })
                yield return new TestCaseData(version, format);
        }
    }

    /// <summary>Restricts composition cases to keywords actually defined by each specification dialect.</summary>
    /// <value>AllOf for all dialects and OneOf/AnyOf for OpenAPI 3.x.</value>
    private static IEnumerable<TestCaseData> CompositionCases
    {
        get
        {
            foreach (var version in new[] { "2.0", "3.0.4", "3.1.2", "3.2.0" })
            foreach (var format in new[] { "json", "yaml" })
            foreach (var composition in version == "2.0" ? new[] { "allOf" } : new[] { "allOf", "oneOf", "anyOf" })
                yield return new TestCaseData(version, format, composition);
        }
    }

    /// <summary>Enumerates heterogeneous nested compositions supported by OpenAPI 3.x.</summary>
    /// <value>Each nesting order in every supported 3.x dialect and source format.</value>
    private static IEnumerable<TestCaseData> NestedCompositionCases
    {
        get
        {
            foreach (var version in new[] { "3.0.4", "3.1.2", "3.2.0" })
            foreach (var format in new[] { "json", "yaml" })
            foreach (var composition in new[] { "anyOf-in-allOf", "allOf-in-oneOf" })
                yield return new TestCaseData(version, format, composition);
        }
    }

    /// <summary>Loads actual temporary files and preserves operation, model, enum, and validation metadata.</summary>
    /// <param name="version">The specification version.</param>
    /// <param name="format">The source serialization format.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCaseSource(nameof(VersionsAndFormats))]
    public async Task FileParser_PreservesContractAcrossVersionsAndFormats(string version, string format)
    {
        var document = await ParseFileAsync($"{version}-contract.{format}");
        var ir = BuildIr(document);
        ir.Metadata.Title.Should().Be("Parser matrix");
        ir.Metadata.ServerUrl.Should().Be("https://api.example.test/v1");
        var model = ir.Models.Single(m => m.Name == "Base");
        model.Properties.Single(p => p.OriginalName == "id").Type.Should().BeOfType<IrPrimitiveType>()
            .Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
        var label = model.Properties.Single(p => p.OriginalName == "label");
        label.Constraints!.MinLength.Should().Be(2);
        label.Constraints.MaxLength.Should().Be(24);
        label.Description.Should().Be("A constrained label.");
        ir.Enums.Single(e => e.Name == "State").Values.Should().HaveCount(2);
        var operation = ir.Clients.SelectMany(c => c.Operations).Single();
        operation.Path.Should().Be("/items");
        operation.HttpMethod.Should().Be("POST");
        operation.Tags.Should().BeEquivalentTo("Items");
        operation.RequestBody!.IsRequired.Should().BeTrue();
        operation.RequestBody.ContentType.Should().Be("application/json");
        operation.Responses.Single(r => r.StatusCode == "200").Description.Should().Be("Accepted item.");
    }

    /// <summary>Preserves model composition and request/response types through the real parser and IR conversion.</summary>
    /// <param name="version">The specification version.</param>
    /// <param name="format">The source serialization format.</param>
    /// <param name="composition">A composition keyword supported by the selected dialect.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCaseSource(nameof(CompositionCases))]
    public async Task Composition_PreservesModelAndEndpointSemantics(string version, string format, string composition)
    {
        var document = await ParseFileAsync($"{version}-{composition}.{format}");
        var ir = BuildIr(document);
        var model = ir.Models.Single(m => m.Name == "Choice");
        var expectedKind = composition switch
        {
            "allOf" => IrCompositionKind.AllOf,
            "oneOf" => IrCompositionKind.OneOf,
            _ => IrCompositionKind.AnyOf
        };
        model.Composition!.Kind.Should().Be(expectedKind);
        model.Composition.Types.OfType<IrModelReference>().Select(r => r.ModelName)
            .Should().Contain("Base");
        if (composition == "allOf")
        {
            model.Properties.Select(p => p.OriginalName).Should().Contain("extra").And.NotContain("id");
        }
        else
        {
            model.Composition.Types.OfType<IrModelReference>().Select(r => r.ModelName)
                .Should().BeEquivalentTo("Base", "Alternative");
        }
        if (composition == "oneOf")
        {
            model.Composition.DiscriminatorPropertyName.Should().Be("kind");
            model.Composition.DiscriminatorMapping.Should().Contain("base", "Base");
            model.Composition.DiscriminatorMapping.Should().Contain("alternative", "Alternative");
        }

        var operation = ir.Clients.SelectMany(c => c.Operations).Single();
        AssertEndpointComposition(operation.RequestBody!.Type, composition);
        AssertEndpointComposition(operation.Responses.Single(r => r.StatusCode == "200").Type!, composition);
        ir.Models.Select(m => m.Name).Should().OnlyHaveUniqueItems();
    }

    /// <summary>Normalizes nullable request and response schemas without confusing nullability with requiredness.</summary>
    /// <param name="version">The specification version.</param>
    /// <param name="format">The source serialization format.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCaseSource(nameof(VersionsAndFormats))]
    public async Task NullableBodies_PreserveRequirednessAndScalarType(string version, string format)
    {
        var document = await ParseFileAsync($"{version}-nullable.{format}");
        var operation = BuildIr(document).Clients.SelectMany(c => c.Operations).Single();
        operation.RequestBody!.IsRequired.Should().BeTrue();
        foreach (var type in new[] { operation.RequestBody.Type, operation.Responses.Single(r => r.StatusCode == "200").Type })
            type.Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrPrimitiveType>()
                .Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
    }

    /// <summary>Retains mixed composition boundaries in model properties, declarations, and endpoint schemas.</summary>
    /// <param name="version">The specification version.</param>
    /// <param name="format">The source serialization format.</param>
    /// <param name="composition">The order of the nested composition keywords.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCaseSource(nameof(NestedCompositionCases))]
    public async Task NestedCompositions_PreserveModelPropertyAndEndpointBoundaries(string version, string format, string composition)
    {
        var ir = BuildIr(await ParseFileAsync($"{version}-{composition}.{format}"));
        var operation = ir.Clients.SelectMany(c => c.Operations).Single();
        var property = ir.Models.Single(m => m.Name == "Container").Properties.Single(p => p.OriginalName == "choice");
        foreach (var type in new[] { property.Type, operation.RequestBody!.Type, operation.Responses.Single(r => r.StatusCode == "200").Type! })
            AssertNestedComposition(type, composition);

        var declaration = ir.Models.Single(m => m.Name == "Choice");
        if (composition == "anyOf-in-allOf")
        {
            declaration.Composition!.Kind.Should().Be(IrCompositionKind.AllOf);
            declaration.Composition.Types.OfType<IrUnionType>().Should().Contain(t => t.Kind == IrUnionKind.AnyOf);
        }
        else
        {
            declaration.Composition!.Kind.Should().Be(IrCompositionKind.OneOf);
            declaration.Composition.Types.OfType<IrIntersectionType>().Should().NotBeEmpty();
        }
    }

    /// <summary>Rejects diagnostic errors even when the reader returns a partially populated document.</summary>
    /// <param name="format">The source serialization format.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCase("json")]
    [TestCase("yaml")]
    public async Task InvalidStructure_DoesNotReturnPartialDocument(string format)
    {
        Func<Task> parse = async () => await ParseFileAsync($"invalid-info.{format}");
        await parse.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to parse OpenAPI document*");
    }

    /// <summary>Rejects unresolved local references before a generator can silently weaken their types.</summary>
    /// <param name="format">The source serialization format.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCase("json")]
    [TestCase("yaml")]
    public async Task MissingReference_IsReportedAsError(string format)
    {
        Func<Task> parse = async () => await ParseFileAsync($"missing-reference.{format}");
        await parse.Should().ThrowAsync<InvalidOperationException>().WithMessage("*DoesNotExist*");
    }

    /// <summary>Allows operation tag names without requiring optional root-level metadata declarations.</summary>
    /// <param name="format">The source serialization format.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCase("json")]
    [TestCase("yaml")]
    public async Task UndeclaredOperationTags_DoNotBecomeUnresolvedReferences(string format)
    {
        var document = await ParseFileAsync($"undeclared-operation-tags.{format}");
        document.Tags.Should().BeNullOrEmpty();
        var operation = BuildIr(document).Clients.SelectMany(c => c.Operations).Single();
        operation.Tags.Should().BeEquivalentTo("Orders");
        operation.Responses.Single(r => r.StatusCode == "200").Type.Should()
            .BeOfType<IrModelReference>().Which.ModelName.Should().Be("Order");
    }

    /// <summary>Rejects malformed syntax in both readers.</summary>
    /// <param name="format">The requested reader.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCase("json")]
    [TestCase("yaml")]
    public async Task MalformedInput_IsRejected(string format)
    {
        var source = OpenApiFixture.Read($"Parser/malformed.{format}");
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        Func<Task> parse = async () => await CreateParser().ParseFromStreamAsync(stream, format);
        await parse.Should().ThrowAsync<Exception>();
    }

    /// <summary>Does not treat an AsyncAPI description as an OpenAPI HTTP contract.</summary>
    /// <param name="format">The serialization format.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCase("json")]
    [TestCase("yaml")]
    public async Task OtherProtocolDocument_IsRejected(string format)
    {
        Func<Task> parse = async () => await ParseFileAsync($"asyncapi.{format}");
        await parse.Should().ThrowAsync<Exception>();
    }

    /// <summary>Sniffs a forward-only stream and preserves caller ownership of that stream.</summary>
    /// <param name="format">The actual serialization format, omitted from the parser call.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCase("json")]
    [TestCase("yaml")]
    public async Task AutoDetection_SupportsForwardOnlyStreams(string format)
    {
        await using var stream = new ForwardOnlyStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read($"Parser/3.2.0-contract.{format}")));
        var document = await CreateParser().ParseFromStreamAsync(stream, null);
        document.Info.Title.Should().Be("Parser matrix");
        stream.CanRead.Should().BeTrue();
    }

    /// <summary>Sniffs unread content at the caller's current position instead of resetting to unrelated prefix bytes.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Test]
    public async Task AutoDetection_PreservesStreamOffsetAndOwnership()
    {
        var source = "prefix" + OpenApiFixture.Read("Parser/3.2.0-contract.json");
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(source));
        stream.Position = Encoding.UTF8.GetByteCount("prefix");
        var document = await CreateParser().ParseFromStreamAsync(stream, null);
        document.Info.Title.Should().Be("Parser matrix");
        stream.CanRead.Should().BeTrue();
    }

    /// <summary>Rejects serializers for other protocols explicitly.</summary>
    /// <returns>The asynchronous test operation.</returns>
    [Test]
    public async Task UnsupportedFormat_HasClearFailure()
    {
        await using var stream = new MemoryStream();
        Func<Task> parse = async () => await CreateParser().ParseFromStreamAsync(stream, "protobuf");
        await parse.Should().ThrowAsync<NotSupportedException>().WithMessage("*protobuf*");
    }

    /// <summary>Checks the normalized endpoint type without imposing generator-specific representation.</summary>
    /// <param name="type">The request or response IR type.</param>
    /// <param name="composition">The source composition keyword.</param>
    private static void AssertEndpointComposition(IrType type, string composition)
    {
        if (composition == "allOf")
            type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Base");
        else
        {
            var union = type.Should().BeOfType<IrUnionType>().Subject;
            union.Kind.Should().Be(composition == "oneOf" ? IrUnionKind.OneOf : IrUnionKind.AnyOf);
            union.Types.OfType<IrModelReference>().Select(r => r.ModelName)
                .Should().BeEquivalentTo("Base", "Alternative");
        }
    }

    /// <summary>Checks that normalization does not distribute or erase heterogeneous schema combinations.</summary>
    /// <param name="type">The normalized property, request, or response type.</param>
    /// <param name="composition">The expected nesting order.</param>
    private static void AssertNestedComposition(IrType type, string composition)
    {
        if (composition == "anyOf-in-allOf")
        {
            var intersection = type.Should().BeOfType<IrIntersectionType>().Subject;
            intersection.Types.OfType<IrModelReference>().Select(r => r.ModelName).Should().ContainSingle().Which.Should().Be("Extra");
            var union = intersection.Types.OfType<IrUnionType>().Single();
            union.Kind.Should().Be(IrUnionKind.AnyOf);
            union.Types.OfType<IrModelReference>().Select(r => r.ModelName).Should().BeEquivalentTo("Base", "Alternative");
        }
        else
        {
            var union = type.Should().BeOfType<IrUnionType>().Subject;
            union.Kind.Should().Be(IrUnionKind.OneOf);
            union.Types.OfType<IrModelReference>().Select(r => r.ModelName).Should().ContainSingle().Which.Should().Be("Alternative");
            union.Types.OfType<IrIntersectionType>().Single().Types.OfType<IrModelReference>()
                .Select(r => r.ModelName).Should().BeEquivalentTo("Base", "Extra");
        }
    }

    /// <summary>Builds a language-independent contract from the production parser result.</summary>
    /// <param name="document">The parsed OpenAPI description.</param>
    /// <returns>The complete normalized IR.</returns>
    private static IrDocument BuildIr(OpenApiDocument document) =>
        new IrBuilder(new SchemaResolver(document), new NamingConverter()).BuildFromOpenApi(document);

    /// <summary>Creates a production parser with no remote calls for local-source tests.</summary>
    /// <returns>The parser under test.</returns>
    private static OpenApiDocumentParser CreateParser() => new(new UnusedHttpClientFactory(), NullLogger<OpenApiDocumentParser>.Instance);

    /// <summary>Copies an explicit source fixture to a temporary file and exercises the public file entry point.</summary>
    /// <param name="fixtureName">The JSON or YAML fixture name inside the Parser fixture directory.</param>
    /// <returns>The parsed document.</returns>
    private static async Task<OpenApiDocument> ParseFileAsync(string fixtureName)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"valio-parser-{Guid.NewGuid():N}{Path.GetExtension(fixtureName)}");
        try
        {
            await File.WriteAllTextAsync(filePath, OpenApiFixture.Read($"Parser/{fixtureName}"));
            return await CreateParser().ParseFromFileAsync(filePath);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    /// <summary>Fails immediately if a local fixture unexpectedly attempts a remote request.</summary>
    private sealed class UnusedHttpClientFactory : IHttpClientFactory
    {
        /// <inheritdoc/>
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("Local parser fixtures must not use HTTP.");
    }

    /// <summary>Models the forward-only response streams returned by HTTP content.</summary>
    private sealed class ForwardOnlyStream : MemoryStream
    {
        /// <summary>Initializes a readable source whose position cannot be queried or changed.</summary>
        /// <param name="content">The source bytes.</param>
        public ForwardOnlyStream(byte[] content) : base(content) { }

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
