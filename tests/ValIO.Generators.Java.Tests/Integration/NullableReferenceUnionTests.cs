using System.Text;
using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Generators.Java.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.Java.Tests.Integration;

/// <summary>Verifies shared nullable normalization produces specific Java reference types rather than Object.</summary>
[TestFixture]
public sealed class NullableReferenceUnionTests
{
    /// <summary>Compiles unmodified generated records and executes nullable JSON round-trips with the caller's Jackson enum codecs.</summary>
    /// <returns>The asynchronous Java compiler and runtime verification.</returns>
    [Test]
    [Category("NativeIntegration")]
    public async Task NullableReferencesRoundTripWithJackson()
    {
        var compiler = Environment.GetEnvironmentVariable("VALIO_JAVAC");
        var dependencies = Environment.GetEnvironmentVariable("VALIO_JAVA_CLASSPATH");
        if (string.IsNullOrWhiteSpace(compiler) || string.IsNullOrWhiteSpace(dependencies))
        {
            Assert.Ignore("Set VALIO_JAVAC and VALIO_JAVA_CLASSPATH for the native Jackson regression.");
            return;
        }
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("CoreIR/nullable-reference-unions-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(input, "json");
        using var harness = new JavaPipelineTestHarness();
        var context = await harness.RunAsync(parsed.Document!);
        var workspace = Path.Combine(Path.GetTempPath(), "valio-java-nullable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var sources = new List<string>();
        foreach (var file in context.GeneratedFiles.Where(file => file.RelativePath.Replace('\\', '/').StartsWith("models/", StringComparison.Ordinal)))
        {
            var path = Path.Combine(workspace, Path.GetFileName(file.RelativePath));
            await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false));
            sources.Add(path);
        }
        var consumer = Path.Combine(workspace, "NullableReferenceConsumer.java");
        await File.WriteAllTextAsync(consumer, OpenApiFixture.Read("NativeClients/NullableReferenceConsumer.java"), new UTF8Encoding(false));
        sources.Add(consumer);
        var classes = Path.Combine(workspace, "classes");
        await GeneratedJavaNativeTests.RunCompiler(compiler,
            new[] { "--release", "21", "-Xlint:all", "-Werror", "-classpath", dependencies, "-d", classes }.Concat(sources), workspace);
        var java = Path.Combine(Path.GetDirectoryName(compiler)!, OperatingSystem.IsWindows() ? "java.exe" : "java");
        await GeneratedJavaNativeTests.RunCompiler(java, ["-classpath", classes + Path.PathSeparator + dependencies, "NullableReferenceConsumer"], workspace);
    }

    /// <summary>Generates the real nullable reference contract and checks model, enum and container identity.</summary>
    /// <returns>The asynchronous generator verification.</returns>
    [Test]
    public async Task NullableReferencesUseSpecificTypes()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("CoreIR/nullable-reference-unions-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(input, "json");
        using var harness = new JavaPipelineTestHarness();
        var context = await harness.RunAsync(parsed.Document!);
        var content = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith("NullableReferences.java", StringComparison.Ordinal)).Content;
        content.Should().Contain("NullableCountry country").And.Contain("NullableState state")
            .And.Contain("NullableCountry optionalCountry").And.Contain("NullableState optionalState")
            .And.Contain("List<NullableCountry>").And.Contain("Map<String, NullableState>");
        content.Should().NotContain("Object");
    }
}
