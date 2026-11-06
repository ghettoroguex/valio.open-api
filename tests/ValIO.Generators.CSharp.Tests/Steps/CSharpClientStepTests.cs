using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Services;
using ValIO.Generators.CSharp.Steps;

namespace ValIO.Generators.CSharp.Tests.Steps;

/// <summary>
/// Regression tests for <see cref="CSharpClientStep"/>: generated clients throw
/// <c>ApiException</c>, so the step must emit its declaration exactly once per project when at
/// least one client is generated — otherwise the output does not compile (type not declared
/// anywhere).
/// </summary>
[TestFixture]
public class CSharpClientStepTests
{
    private CSharpClientStep _step = null!;

    [SetUp]
    public void Setup()
    {
        _step = new CSharpClientStep(new CSharpNamingConverter(), new CSharpAstRenderer());
    }

    private static IrClient MinimalClient(string name) => new()
    {
        Name = name,
        Operations =
        [
            new IrOperation
            {
                MethodName = "GetItems",
                HttpMethod = "GET",
                Path = "/items",
                PathParameters = [],
                QueryParameters = [],
                HeaderParameters = [],
                Responses = [new IrResponse { StatusCode = "200" }],
            },
        ],
    };

    private static IrDocument DocumentWithClients(params IrClient[] clients) => new()
    {
        Metadata = new IrDocumentMetadata(),
        Models = [],
        Enums = [],
        Clients = clients,
        TypeDependencies = new IrTypeDependencyGraph
        {
            Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
            TopologicalOrder = [],
        },
    };

    [Test]
    public async Task ClientsPresent_EmitsApiExceptionExactlyOnce()
    {
        var context = new GenerationContext
        {
            IrDocument = DocumentWithClients(MinimalClient("Pets"), MinimalClient("Owners")),
            Configuration = new GenerationConfiguration { Namespace = "Test.Ns" },
        };

        await _step.ExecuteAsync(context);

        var apiExceptionFiles = context.GeneratedFiles
            .Where(f => f.Content.Contains("class ApiException"))
            .ToList();

        apiExceptionFiles.Should().HaveCount(1, "ApiException must be declared exactly once even with multiple clients");
        apiExceptionFiles[0].Content.Should().Contain(": ApplicationException");
        apiExceptionFiles[0].Content.Should().Contain("namespace Test.Ns");
    }

    [Test]
    public async Task NoClients_DoesNotEmitApiException()
    {
        var context = new GenerationContext
        {
            IrDocument = DocumentWithClients(),
            Configuration = new GenerationConfiguration { Namespace = "Test.Ns" },
        };

        await _step.ExecuteAsync(context);

        context.GeneratedFiles.Should().NotContain(f => f.Content.Contains("class ApiException"));
    }

    /// <summary>
    /// Verifies that multipart support is emitted once and only when a generated operation requires it.
    /// </summary>
    [Test]
    public async Task MultipartClient_EmitsInvariantContentFactoryExactlyOnce()
    {
        var multipartOperation = MinimalClient("Logos").Operations[0] with
        {
            HttpMethod = "POST",
            RequestBody = new IrRequestBody
            {
                Type = new IrModelReference { ModelName = "ReplaceLogoForm" },
                IsRequired = true,
                ContentType = "multipart/form-data",
            },
        };
        var client = MinimalClient("Logos") with { Operations = [multipartOperation] };
        var context = new GenerationContext
        {
            IrDocument = DocumentWithClients(client),
            Configuration = new GenerationConfiguration { Namespace = "Test.Ns" },
        };

        await _step.ExecuteAsync(context);

        var helpers = context.GeneratedFiles
            .Where(file => file.RelativePath == "Api/MultipartFormDataContentFactory.cs")
            .ToList();
        helpers.Should().ContainSingle();
        helpers[0].Content.Should().Contain("namespace Test.Ns;");
        helpers[0].Content.Should().Contain("case byte[] bytes:");
        context.GeneratedFiles.Single(file => file.RelativePath == "Api/HttpWireValueFormatter.cs")
            .Content.Should().Contain("bool boolean => boolean ? \"true\" : \"false\"");
        context.GeneratedFiles.Single(file => file.RelativePath == "Api/HttpWireValueFormatter.cs").Content.Should().Contain("DateTimeOffset dateTimeOffset => dateTimeOffset.ToString(\"O\"");
    }
}
