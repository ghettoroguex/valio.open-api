using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Tests.Builders.Std;

/// <summary>Guards C++ contract semantics previously missed by the single-enum smoke document.</summary>
[TestFixture]
public sealed class StdContractRegressionTests
{
    /// <summary>Checks integer enums preserve the complete signed 64-bit range in declarations and codec signatures.</summary>
    [Test]
    public void Enum_WideIntegerValuesUsePortableInt64Constants()
    {
        var irEnum = new IrEnum
        {
            Name = "Wide", BaseType = IrEnumBaseType.Integer,
            Values = [new IrEnumValue { Name = "minimum", Value = long.MinValue }, new IrEnumValue { Name = "maximum", Value = long.MaxValue }],
        };
        var builder = new StdEnumAstBuilder(new StdNamingConvention());
        var renderer = new CppAstRenderer();
        var definition = renderer.Render(builder.Build(irEnum, "Api"));
        var (header, _) = builder.BuildJson(irEnum, "Api", new CppGenerationOptions());

        definition.Should().Contain("enum class Wide : std::int64_t");
        definition.Should().Contain("Minimum = -9223372036854775807LL - 1");
        definition.Should().Contain("Maximum = 9223372036854775807LL");
        renderer.Render(header).Should().Contain("WideFromWire(std::int64_t w)");
    }

    /// <summary>Checks unsupported integer values produce diagnostics rather than fabricated ordinal wire values.</summary>
    [Test]
    public void Enum_InvalidIntegerWireValueFailsExplicitly()
    {
        var irEnum = new IrEnum { Name = "Bad", BaseType = IrEnumBaseType.Integer, Values = [new IrEnumValue { Name = "bad", Value = "9223372036854775808" }] };
        var build = () => new StdEnumAstBuilder(new StdNamingConvention()).Build(irEnum, "Api");
        build.Should().Throw<NotSupportedException>().WithMessage("*9223372036854775808*signed 64-bit*");
    }

    /// <summary>Rejects composed model alternatives whose constraints cannot be represented by an ordinary C++ struct.</summary>
    /// <param name="kind">The unsupported composition kind.</param>
    [TestCase(IrCompositionKind.OneOf)]
    [TestCase(IrCompositionKind.AnyOf)]
    public void Model_AlternativeCompositionFailsExplicitly(IrCompositionKind kind)
    {
        var model = new IrModel
        {
            Name = "Choice", Properties = [],
            Composition = new IrComposition { Kind = kind, Types = [new IrModelReference { ModelName = "Variant" }] },
        };
        var naming = new StdNamingConvention();
        var context = CreateContext([model]);
        var builder = new StdModelAstBuilder(new StdCppTypeMapper(naming, context), naming, context);
        var build = () => builder.Build(model, "Api");
        build.Should().Throw<NotSupportedException>().WithMessage("*Choice*typed variant codec*");
    }

    /// <summary>Rejects nested allOf alternatives instead of flattening only the reference members.</summary>
    [Test]
    public void Model_NestedAlternativeInsideAllOfFailsExplicitly()
    {
        var model = new IrModel
        {
            Name = "Mixed", Properties = [],
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = [new IrUnionType { Types = [new IrModelReference { ModelName = "First" }, new IrModelReference { ModelName = "Second" }] }],
            },
        };
        var naming = new StdNamingConvention();
        var context = CreateContext([model]);
        var builder = new StdModelAstBuilder(new StdCppTypeMapper(naming, context), naming, context);
        var build = () => builder.Build(model, "Api");
        build.Should().Throw<NotSupportedException>().WithMessage("*Mixed*nested non-reference allOf*codec*");
    }

    /// <summary>Checks optional presence and nullable values retain separate storage and codec operations.</summary>
    [Test]
    public void Model_PresenceAndNullabilityRemainIndependent()
    {
        var nullable = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } };
        var model = new IrModel
        {
            Name = "Probe",
            Properties =
            [
                new IrProperty { Name = "RequiredNullable", OriginalName = "required_null", IsRequired = true, Type = nullable },
                new IrProperty { Name = "OptionalNullable", OriginalName = "optional_null", IsRequired = false, Type = nullable },
            ],
        };
        var naming = new StdNamingConvention();
        var context = CreateContext([model]);
        var mapper = new StdCppTypeMapper(naming, context);
        var renderer = new CppAstRenderer();

        var header = renderer.Render(new StdModelAstBuilder(mapper, naming, context).Build(model, "Api"));
        var (_, codec) = new StdModelJsonAstBuilder(mapper, naming, context).Build(model, "Api", new CppGenerationOptions());
        var source = renderer.Render(codec);

        header.Should().Contain("std::optional<std::string> RequiredNullable;");
        header.Should().Contain("std::optional<std::optional<std::string>> OptionalNullable;");
        source.Should().Contain("value.RequiredNullable = ValIO::ReadJson<std::optional<std::string>>(j.at(\"required_null\"), ValIO::JsonStringFormat::Plain);");
        source.Should().NotContain("j.contains(\"required_null\")");
        source.Should().Contain("j[\"required_null\"] = ValIO::WriteJson(value.RequiredNullable, ValIO::JsonStringFormat::Plain);");
        source.Should().Contain("value.OptionalNullable.emplace(ValIO::ReadJson<std::optional<std::string>>(j.at(\"optional_null\"), ValIO::JsonStringFormat::Plain));");
        source.Should().Contain("value.OptionalNullable.reset();");
        source.Should().Contain("j = nlohmann::json::object();");
    }

    /// <summary>Checks path placeholders bind wire names even when parameters are reversed or repeated.</summary>
    [Test]
    public void Client_PathParametersResolveByWireName()
    {
        var operation = new IrOperation
        {
            MethodName = "GetPair", HttpMethod = "GET", Path = "/{second-wire}/{first-wire}/{second-wire}",
            PathParameters =
            [
                new IrParameter { Name = "First", OriginalName = "first-wire", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer } },
                new IrParameter { Name = "Second", OriginalName = "second-wire", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer } },
            ],
            QueryParameters = [], HeaderParameters = [], Responses = [],
        };

        var source = RenderClient(operation, CppApiStyle.Sync).Source;
        source.Should().Contain("ValIO::ReplacePathParameter(path, \"{second-wire}\", ValIO::UrlEncode(std::to_string(Second)));");
        source.Should().Contain("ValIO::ReplacePathParameter(path, \"{first-wire}\", ValIO::UrlEncode(std::to_string(First)));");
        source.Split("ReplacePathParameter(path, \"{second-wire}\"", StringSplitOptions.None).Should().HaveCount(2);
    }

    /// <summary>Checks async-only clients retain a private implementation and own async transport state.</summary>
    [Test]
    public void Client_AsyncOnlyHasOwnedWorkerAndPrivateSyncImplementation()
    {
        var operation = new IrOperation
        {
            MethodName = "Fetch", HttpMethod = "GET", Path = "/probe",
            PathParameters = [], QueryParameters = [], HeaderParameters = [], Responses = [],
        };
        var result = RenderClient(operation, CppApiStyle.Async);

        result.Header.Should().Contain("private:");
        result.Header.Should().Contain("Fetch(ValIO::CancellationToken cancellationToken = {}) const;");
        result.Header.Should().Contain("FetchAsync(ValIO::CancellationToken cancellationToken = {}) const;");
        result.Source.Should().Contain("ProbeClient::Fetch(ValIO::CancellationToken cancellationToken) const");
        result.Source.Should().Contain("[http = _http, cancellationToken]");
        result.Source.Should().Contain("ProbeClient worker = ProbeClient(http);");
        result.Source.Should().Contain("return worker.Fetch(cancellationToken);");
        result.Source.Should().NotContain("[this");
    }

    /// <summary>Checks cloning retains the linkage needed for generated header-defined converters.</summary>
    [Test]
    public void FreeFunction_ClonePreservesInlineLinkage()
    {
        var node = new CppFreeFunctionNode(new IdentifierNode("Wire"), new RawTypeNode("void"), body: new BlockNode([]), isInline: true);
        var clone = (CppFreeFunctionNode)node.Clone();
        clone.IsInline.Should().BeTrue();
        new CppAstRenderer().Render(clone).Should().Contain("inline void Wire()");
    }

    /// <summary>Checks injected transport ownership and constructor initialization avoid redundant transport construction.</summary>
    [Test]
    public void Client_SharedTransportConstructorInitializesExactlyOnce()
    {
        var operation = EmptyOperation();
        var result = RenderClient(operation, CppApiStyle.Both);
        result.Header.Should().Contain("ProbeClient(std::shared_ptr<ValIO::HttpClient> http);");
        result.Source.Should().Contain("ProbeClient::ProbeClient(const ValIO::HttpConfig& config) : _http(std::make_shared<ValIO::HttpClient>(config))");
        result.Source.Should().Contain("ProbeClient::ProbeClient(std::shared_ptr<ValIO::HttpClient> http) : _http(std::move(http))");
        result.Source.Should().Contain("throw std::invalid_argument(\"HTTP client must not be null\");");
        result.Source.Should().NotContain("_config");
    }

    /// <summary>Checks checked request serialization returns an expected failure without broadly catching unrelated exceptions.</summary>
    [Test]
    public void Client_ConvertsKnownSerializationFailuresToExpectedErrors()
    {
        var result = RenderClient(EmptyOperation(), CppApiStyle.Both);
        result.Source.Should().Contain("catch (const nlohmann::json::exception& exception)");
        result.Source.Should().Contain("catch (const std::invalid_argument& exception)");
        result.Source.Should().Contain("error.What = ValIO::Error::Code::Serialization;");
        result.Source.Should().Contain("error.Message = exception.what();");
        result.Source.Should().NotContain("catch (const std::exception&");
    }

    /// <summary>Checks declarations always include Doxygen, source descriptions, parameters, result ownership, and route constants.</summary>
    [Test]
    public void Client_DocumentsPublicContractAndEscapesCommentTerminators()
    {
        var operation = EmptyOperation() with { Summary = "Probe summary */ declaration", Description = "Detailed contract" };
        var result = RenderClient(operation, CppApiStyle.Both);
        result.Header.Should().Contain("@brief HTTP implementation for Probe.");
        result.Header.Should().Contain("@brief Probe summary * / declaration");
        result.Header.Should().Contain("@details Detailed contract");
        result.Header.Should().Contain("@param http A non-null shared HTTP client");
        result.Header.Should().Contain("@return A future retaining the shared transport and copied arguments");
        result.Header.Should().Contain("inline static constexpr std::string_view FetchPath = \"/probe\";");
        result.Source.Should().NotContain("std::string path = \"/probe\"");
    }

    /// <summary>Checks bodyless success does not accidentally adopt an unrelated default error payload.</summary>
    [Test]
    public void Client_NoContentDoesNotUseDefaultErrorSchema()
    {
        var operation = EmptyOperation() with
        {
            Responses = [new IrResponse { StatusCode = "204" },
                new IrResponse { StatusCode = "default", Type = new IrModelReference { ModelName = "ErrorBody" } }],
        };
        var result = RenderClient(operation, CppApiStyle.Sync);
        result.Header.Should().Contain("ValIO::Expected<void, ValIO::Error>");
        result.Source.Should().NotContain("ValIO::Parse<ErrorBody>");
    }

    /// <summary>Checks mixed typed and bodyless successes have stable optional results in every response declaration order.</summary>
    /// <param name="reverse">Whether the bodyless declaration appears first.</param>
    [TestCase(false)]
    [TestCase(true)]
    public void Client_MixedSuccessfulBodiesUseOptionalPayload(bool reverse)
    {
        var typed = new IrResponse { StatusCode = "200", Type = new IrModelReference { ModelName = "Value" } };
        var empty = new IrResponse { StatusCode = "204" };
        var operation = EmptyOperation() with { Responses = reverse ? [empty, typed] : [typed, empty] };
        var result = RenderClient(operation, CppApiStyle.Both);
        result.Header.Should().Contain("ValIO::Expected<std::optional<Value>, ValIO::Error>");
        result.Source.Should().Contain("return std::optional<Value>();");
        result.Source.Should().Contain("return ValIO::Parse<std::optional<Value>>(response.Body, ValIO::JsonStringFormat::Plain);");
        result.Source.Should().Contain("response.StatusCode < 200 || response.StatusCode >= 300");
    }

    /// <summary>Checks HEAD never attempts to decode a payload even when its response schema lists one.</summary>
    [Test]
    public void Client_HeadIsAlwaysBodyless()
    {
        var operation = EmptyOperation() with { HttpMethod = "HEAD", Responses =
            [new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }] };
        var result = RenderClient(operation, CppApiStyle.Sync);
        result.Header.Should().Contain("ValIO::Expected<void, ValIO::Error>");
        result.Source.Should().NotContain("ValIO::Parse<");
    }

    /// <summary>Checks an exact payload response overrides a bodyless successful range.</summary>
    [Test]
    public void Client_ExactResponseTakesPrecedenceOverSuccessfulRange()
    {
        var operation = EmptyOperation() with { Responses =
            [new IrResponse { StatusCode = "2XX" },
             new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }] };
        var result = RenderClient(operation, CppApiStyle.Sync);
        result.Header.Should().Contain("ValIO::Expected<std::optional<bool>, ValIO::Error>");
        result.Source.Should().Contain("response.StatusCode != 200");
        result.Source.Should().Contain("return ValIO::Parse<std::optional<bool>>(response.Body, ValIO::JsonStringFormat::Plain);");
    }

    /// <summary>Checks unsupported heterogeneous successes cannot silently adopt the first declared schema.</summary>
    [Test]
    public void Client_DistinctSuccessfulSchemasFailExplicitly()
    {
        var operation = EmptyOperation() with { Responses =
            [new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } },
             new IrResponse { StatusCode = "201", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } }] };
        var action = () => RenderClient(operation, CppApiStyle.Sync);
        action.Should().Throw<NotSupportedException>().WithMessage("*Fetch*heterogeneous successful response schemas*");
    }

    /// <summary>Creates a minimal operation for constructor, documentation, and decoding regressions.</summary>
    /// <returns>The bodyless probe operation.</returns>
    private static IrOperation EmptyOperation() => new()
    {
        MethodName = "Fetch", HttpMethod = "GET", Path = "/probe",
        PathParameters = [], QueryParameters = [], HeaderParameters = [], Responses = [],
    };

    /// <summary>Renders a minimal client for a focused operation regression.</summary>
    /// <param name="operation">The operation under test.</param>
    /// <param name="style">The client execution style.</param>
    /// <returns>The client header and implementation text.</returns>
    private static (string Header, string Source) RenderClient(IrOperation operation, CppApiStyle style)
    {
        var naming = new StdNamingConvention();
        var context = CreateContext([]);
        var builder = new StdClientAstBuilder(new StdCppTypeMapper(naming, context), naming, context);
        var result = builder.Build(new IrClient { Name = "Probe", Operations = [operation] }, "Api", new CppGenerationOptions { ApiStyle = style });
        var renderer = new CppAstRenderer();
        return (renderer.Render(result.ClientHeader), renderer.Render(result.ClientSource));
    }
    /// <summary>Creates a finalized document context shared by all services in a focused regression.</summary>
    /// <param name="models">The scenario's model declarations.</param>
    /// <returns>The immutable declaration indexes.</returns>
    private static IrDocumentContext CreateContext(IReadOnlyList<IrModel> models) => new(new IrDocument
    {
        Metadata = new IrDocumentMetadata(), Models = models, Enums = [], Clients = [],
        TypeDependencies = new IrTypeDependencyGraph { Dependencies = new Dictionary<string, IReadOnlyList<string>>(), TopologicalOrder = [] },
    });

}
