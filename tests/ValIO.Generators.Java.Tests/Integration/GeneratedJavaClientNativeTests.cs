using System.Text;
using FluentAssertions;
using Microsoft.OpenApi;
using NUnit.Framework;
using ValIO.Generators.Java.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.Java.Tests.Integration;

/// <summary>Runs generated Java clients against real OkHttp and Jackson libraries.</summary>
[TestFixture]
[Category("NativeIntegration")]
[NonParallelizable]
public sealed class GeneratedJavaClientNativeTests
{
    /// <summary>Verifies typed JSON, native interceptors, refresh/retry, HTTP errors, headers and Call cancellation.</summary>
    /// <returns>The generated Java consumer compilation and execution.</returns>
    [Test]
    public async Task GeneratedClient_UsesInjectedPipelineAndExposesCancelableCalls()
    {
        var compiler = Environment.GetEnvironmentVariable("VALIO_JAVAC");
        var dependencies = Environment.GetEnvironmentVariable("VALIO_JAVA_CLASSPATH");
        if (string.IsNullOrWhiteSpace(compiler) || string.IsNullOrWhiteSpace(dependencies))
        {
            Assert.Ignore("Set VALIO_JAVAC and VALIO_JAVA_CLASSPATH to run the real Java client integration.");
            return;
        }
        File.Exists(compiler).Should().BeTrue("the configured Java compiler must exist");
        var java = Path.Combine(Path.GetDirectoryName(compiler)!, OperatingSystem.IsWindows() ? "java.exe" : "java");
        File.Exists(java).Should().BeTrue("the configured JDK must contain its runtime");
        foreach (var dependency in dependencies.Split(Path.PathSeparator))
            File.Exists(dependency).Should().BeTrue("every configured classpath entry must be a real dependency jar: {0}", dependency);
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("NativeClients/requests-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(input, "json");
        parsed.Document.Should().NotBeNull();
        (parsed.Diagnostic?.Errors.Select(error => error.Message) ?? []).Should().BeEmpty();
        using var harness = new JavaPipelineTestHarness();
        var context = await harness.RunAsync(parsed.Document!);
        var workspace = Path.Combine(Path.GetTempPath(), $"valio-java-client-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var passed = false;
        try
        {
            var sources = new List<string>();
            foreach (var file in context.GeneratedFiles.Where(file => file.RelativePath.EndsWith(".java", StringComparison.Ordinal)))
            {
                var path = Path.Combine(workspace, Path.GetFileName(file.RelativePath));
                await File.WriteAllTextAsync(path, file.Content, new UTF8Encoding(false));
                sources.Add(path);
            }
            var consumer = Path.Combine(workspace, "JavaClientConsumer.java");
            await File.WriteAllTextAsync(consumer, OpenApiFixture.Read("NativeClients/JavaClientConsumer.java"), new UTF8Encoding(false));
            sources.Add(consumer);
            var classes = Path.Combine(workspace, "classes");
            var arguments = new[] { "--release", "21", "-Xlint:all", "-classpath", dependencies, "-d", classes }.Concat(sources);
            await GeneratedJavaNativeTests.RunCompiler(compiler, arguments, workspace);
            await GeneratedJavaNativeTests.RunCompiler(java, ["-classpath", classes + Path.PathSeparator + dependencies, "JavaClientConsumer"], workspace);
            passed = true;
        }
        finally
        {
            if (passed) Directory.Delete(workspace, recursive: true);
            else TestContext.Out.WriteLine($"Generated Java client integration retained at: {workspace}");
        }
    }
}
