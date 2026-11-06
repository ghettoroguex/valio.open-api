using System.Text;
using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.Cpp.Tests.Builders.Std;

/// <summary>Checks body presence and value nullability remain independent in generated C++ operation contracts.</summary>
[TestFixture]
public sealed class StdOptionalRequestBodyTests
{
    /// <summary>Checks optional JSON bodies have an outer presence wrapper even when their model contains required properties.</summary>
    /// <returns>The asynchronous fixture translation and assertions.</returns>
    [Test]
    public async Task OptionalObjectBody_HasDefaultPresenceWrapperAndGuardedSerialization()
    {
        var document = await LoadAsync();
        var operation = document.Clients.Single().Operations.Single(candidate => candidate.MethodName == "PostOptionalObject");
        operation.RequestBody!.IsRequired.Should().BeFalse();
        document.Models.Single(model => model.Name == "OptionalPayload").Properties.Single(property => property.OriginalName == "id").IsRequired.Should().BeTrue();
        var (header, source) = Render(document);
        header.Should().Contain("PostOptionalObject(const std::optional<OptionalPayload>& request = std::nullopt, ValIO::CancellationToken cancellationToken = {}) const");
        source.Should().Contain("if (request)");
        source.Should().Contain("ValIO::Dump(*request, ValIO::JsonStringFormat::Plain)");
        source.Should().Contain("return worker.PostOptionalObject(request, cancellationToken);");
    }

    /// <summary>Checks explicit JSON null is represented by an empty inner optional inside a present outer optional.</summary>
    /// <returns>The asynchronous fixture translation and assertions.</returns>
    [Test]
    public async Task OptionalNullableBody_RetainsTwoIndependentStates()
    {
        var document = await LoadAsync();
        var operation = document.Clients.Single().Operations.Single(candidate => candidate.MethodName == "PostOptionalNullable");
        operation.RequestBody!.Type.Should().BeOfType<IrNullableType>();
        var (header, source) = Render(document);
        header.Should().Contain("PostOptionalNullable(const std::optional<std::optional<OptionalPayload>>& request = std::nullopt");
        header.Should().Contain("PostRequiredNullable(const std::optional<OptionalPayload>& request, ValIO::CancellationToken cancellationToken = {}) const");
        source.Should().Contain("ValIO::Dump(*request, ValIO::JsonStringFormat::Plain)");
        header.Should().Contain("An empty outer optional omits the request body and its content type");
    }

    /// <summary>Checks optional form and raw binary payload construction unwraps the presence state before accessing its value.</summary>
    /// <returns>The asynchronous fixture translation and assertions.</returns>
    [Test]
    public async Task OptionalFormMultipartAndBinary_AccessPresentPayload()
    {
        var document = await LoadAsync();
        var (header, source) = Render(document);
        header.Should().Contain("PostOptionalForm(const std::optional<OptionalPayload>& request = std::nullopt");
        header.Should().Contain("PostOptionalMultipart(const std::optional<OptionalPayload>& request = std::nullopt");
        header.Should().Contain("PostOptionalBinary(const std::optional<std::vector<std::uint8_t>>& request = std::nullopt");
        source.Should().Contain("httpRequest.AddFormField(\"id\", (*request).Id);");
        source.Should().Contain("httpRequest.Body.assign((*request).begin(), (*request).end());");
        source.Should().Contain("httpRequest.ContentType = \"application/octet-stream\";");
    }

    /// <summary>Loads an explicit disk fixture and translates it through the actual OpenAPI and IR readers.</summary>
    /// <returns>The complete optional-body IR.</returns>
    private static async Task<IrDocument> LoadAsync()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("Cpp/optional-request-bodies-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(stream, "json");
        parsed.Diagnostic!.Errors.Should().BeEmpty();
        var document = parsed.Document!;
        return new IrBuilder(new SchemaResolver(document), new NamingConverter()).BuildFromOpenApi(document);
    }

    /// <summary>Renders the real client header and implementation for a finalized document.</summary>
    /// <param name="document">The parsed fixture document.</param>
    /// <returns>The generated declarations and definitions.</returns>
    private static (string Header, string Source) Render(IrDocument document)
    {
        var context = new IrDocumentContext(document);
        var naming = new StdNamingConvention();
        var builder = new StdClientAstBuilder(new StdCppTypeMapper(naming, context), naming, context);
        var result = builder.Build(document.Clients.Single(), "Petstore", new CppGenerationOptions { ApiStyle = CppApiStyle.Both });
        var renderer = new CppAstRenderer();
        return (renderer.Render(result.ClientHeader), renderer.Render(result.ClientSource));
    }
}
