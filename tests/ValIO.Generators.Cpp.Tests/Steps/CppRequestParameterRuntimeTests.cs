using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Steps;

namespace ValIO.Generators.Cpp.Tests.Steps;

/// <summary>Guards the boundary between public HTTP contracts, visible parameter templates, and compiled runtime bodies.</summary>
[TestFixture]
public sealed class CppRequestParameterRuntimeTests
{
    /// <summary>Checks ordinary request operations and transport construction live in the implementation translation unit.</summary>
    /// <returns>The asynchronous runtime generation assertion.</returns>
    [Test]
    public async Task OrdinaryMethods_AreDeclaredInHeaderAndDefinedInSource()
    {
        var files = await Generate();
        var header = files["Runtime/ValIOHttp.h"];
        var source = files["Runtime/ValIOHttp.cpp"];
        header.Should().Contain("void SetJsonBody(std::string body);").And.Contain("HttpClient();");
        header.Should().NotContain("emplace_back(").And.NotContain("FormFields.clear()").And.NotContain("HttpClient() :");
        foreach (var method in new[] { "AddQuery", "AddHeader", "AddFormField", "SetJsonBody" })
            source.Should().Contain($"void Request::{method}(");
        foreach (var method in new[] { "AddQuery", "AddHeader", "AddFormField" })
        {
            source.Should().Contain($"\nvoid Request::{method}(std::string name, std::string value)");
            source.Should().Contain($"\nvoid Request::{method}(std::string name, const std::string_view value)");
            source.Should().Contain($"\nvoid Request::{method}(std::string name, const char* value)");
            source.Should().Contain($"\nvoid Request::{method}(std::string name, const bool value)");
        }
        source.Should().Contain("HttpClient::HttpClient() : HttpClient(HttpConfig{}) {}");
    }

    /// <summary>Checks scalar template support remains visible without importing JSON, transport, or provider dependencies.</summary>
    /// <returns>The asynchronous runtime generation assertion.</returns>
    [Test]
    public async Task ParameterTemplates_AreSeparateAndDependencyIndependent()
    {
        var files = await Generate();
        files["Runtime/ValIOHttp.h"].Should().Contain("#include \"Impl/ValIORequest.inl\"");
        var templates = files["Runtime/Impl/ValIORequest.inl"];
        templates.Should().Contain("RequireParameterValue").And.Contain("Request::AddQuery").And.Contain("Request::AddHeader");
        templates.Should().NotContainAny(["nlohmann", "httplib", "ValIOJson", "ValIOUuid", "ValIOTemporal", "std::to_chars"]);
        files["Runtime/ValIOHttp.h"].Should().NotContainAny(["nlohmann", "#include <httplib", "ValIOJson"]);
    }

    /// <summary>Checks the cancellation signal's ordinary shared-state constructor is compiled instead of inlined in its contract.</summary>
    /// <returns>The asynchronous runtime generation assertion.</returns>
    [Test]
    public async Task CancellationStateConstructor_IsOutOfLine()
    {
        var files = await Generate();
        files["Runtime/ValIOCancellation.h"].Should().NotContain(": _state(");
        files["Runtime/ValIOHttp.cpp"].Should().Contain("CancellationToken::CancellationToken(std::shared_ptr<detail::CancellationState> state) noexcept");
    }

    /// <summary>Generates the actual production runtime for one standalone HTTP client.</summary>
    /// <returns>Runtime output indexed by its canonical relative file path.</returns>
    private static async Task<IReadOnlyDictionary<string, string>> Generate()
    {
        var options = new CppGenerationOptions { Runtime = CppRuntimeProfile.Std };
        var context = new GenerationContext
        {
            IrDocument = new IrDocument
            {
                Metadata = new IrDocumentMetadata(), Models = [], Enums = [],
                Clients = [new IrClient { Name = "Probe", Operations = [] }],
                TypeDependencies = new IrTypeDependencyGraph
                {
                    Dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal), TopologicalOrder = [],
                },
            },
            PluginOptions = options,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = options },
        };
        await new CppStdRuntimeStep(new CppAstRenderer()).ExecuteAsync(context);
        return context.GeneratedFiles.ToDictionary(file => file.RelativePath, file => file.Content, StringComparer.Ordinal);
    }
}
