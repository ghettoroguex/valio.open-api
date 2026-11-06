using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.Java.Tests.TestHelpers;

namespace ValIO.Generators.Java.Tests.Steps;

[TestFixture]
public class OkHttpClientStepTests
{
    private JavaPipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new JavaPipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    [Test]
    public async Task EmitsClientWithConstructorInjection()
    {
        var doc = await JavaPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var clientFiles = ctx.GeneratedFiles
            .Where(f => f.RelativePath.Replace('\\', '/').StartsWith("api/", StringComparison.Ordinal))
            .ToList();
        clientFiles.Should().NotBeEmpty();

        var content = clientFiles[0].Content;
        content.Should().Contain("import okhttp3.OkHttpClient;");
        content.Should().Contain("import okhttp3.HttpUrl;");
        content.Should().Contain("import okhttp3.Request;");
        content.Should().Contain("import okhttp3.Response;");
        content.Should().Contain("import com.fasterxml.jackson.databind.ObjectMapper;");
        content.Should().Contain("private final OkHttpClient client;");
        content.Should().Contain("private final HttpUrl baseUrl;");
        content.Should().Contain("private final ObjectMapper mapper;");
    }

    [Test]
    public async Task MethodsDeclareThrowsIOException()
    {
        var doc = await JavaPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var clientFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').StartsWith("api/", StringComparison.Ordinal));
        clientFile.Content.Should().Contain("throws IOException");
    }

    [Test]
    public async Task UsesCamelCaseMethodNames_FromOperationId()
    {
        var doc = await JavaPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var clientFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').StartsWith("api/", StringComparison.Ordinal));
        clientFile.Content.Should().Contain("getUsers(");
        clientFile.Content.Should().Contain("getUserById(");
    }

    /// <summary>Encodes parameters within individual URL segments instead of treating their slashes as separators.</summary>
    /// <returns>The generated URL construction assertions.</returns>
    [Test]
    public async Task PathParametersAreInterpolatedViaStringConcat()
    {
        var doc = await JavaPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var clientFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').StartsWith("api/", StringComparison.Ordinal));
        // Each declared route segment is encoded separately by the native HTTP library.
        clientFile.Content.Should().Contain("addPathSegment(\"users\")");
        clientFile.Content.Should().Contain("addPathSegment((\"\" + String.valueOf(id)))");
        clientFile.Content.Should().Contain("String.valueOf(id)");
    }

    [Test]
    public async Task UsesTryWithResourcesForResponse()
    {
        var doc = await JavaPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var clientFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').StartsWith("api/", StringComparison.Ordinal));
        clientFile.Content.Should().Contain("try (Response response = client.newCall(request).execute())");
    }

    [Test]
    public async Task DeserializesModelResponse_ViaObjectMapper()
    {
        var doc = await JavaPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var ctx = await _harness.RunAsync(doc);

        var clientFile = ctx.GeneratedFiles.First(f =>
            f.RelativePath.Replace('\\', '/').StartsWith("api/", StringComparison.Ordinal));
        clientFile.Content.Should().Contain("mapper.readValue(response.body().byteStream(), User.class)");
    }
}
