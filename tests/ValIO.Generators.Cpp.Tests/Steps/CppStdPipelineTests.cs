using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.Tests.Steps;

/// <summary>
/// E2E-тесты std-профиля: полный файл-набор, runtime один раз, interface+impl,
/// именованные локали, wire-имена query, независимость вывода от cxxStandard.
/// </summary>
[TestFixture]
public class CppStdPipelineTests
{
    private static IrDocument BuildDocument()
    {
        var order = new IrModel
        {
            Name = "Order",
            Properties =
            [
                new IrProperty
                {
                    OriginalName = "id", Name = "Id",
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Long },
                    IsRequired = true,
                },
                new IrProperty
                {
                    OriginalName = "note", Name = "Note",
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
                    IsRequired = false,
                },
            ],
        };
        var status = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values =
            [
                new IrEnumValue { Name = "placed", Value = "placed" },
                new IrEnumValue { Name = "approved", Value = "approved" },
            ],
        };
        var operation = new IrOperation
        {
            MethodName = "GetOrderById",
            HttpMethod = "GET",
            Path = "/v2/store/order/{orderId}",
            PathParameters =
            [
                new IrParameter
                {
                    Name = "orderId",
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Long },
                    IsRequired = true,
                },
            ],
            QueryParameters =
            [
                new IrParameter
                {
                    Name = "Paging_Offset",
                    OriginalName = "Paging.Offset",
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer },
                    IsRequired = false,
                },
            ],
            HeaderParameters = [],
            Responses =
            [
                new IrResponse { StatusCode = "200", Type = new IrModelReference { ModelName = "Order" } },
            ],
        };

        return new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models = [order],
            Enums = [status],
            Clients = [new IrClient { Name = "Store", Operations = [operation] }],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
                TopologicalOrder = ["Order", "OrderStatus"],
            },
        };
    }

    private static async Task<Dictionary<string, string>> GenerateAsync(IrDocument document, CppGenerationOptions options)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICppAstRenderer, CppAstRenderer>()
            .BuildServiceProvider();
        var pipeline = new CppGeneratorPlugin().CreatePipeline(services);
        var context = new GenerationContext
        {
            IrDocument = document,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = options },
            PluginOptions = options,
        };
        await pipeline.ExecuteAsync(context);
        return context.GeneratedFiles.ToDictionary(f => f.RelativePath, f => f.Content);
    }

    /// <summary>Checks each output layer is emitted exactly once for a complete standalone client.</summary>
    /// <returns>The asynchronous test execution.</returns>
    [Test]
    public async Task StdPipeline_InterfaceImplBoth_ProducesFullLayout()
    {
        var files = await GenerateAsync(BuildDocument(), new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            ClientShape = CppClientShape.InterfaceImpl,
            ApiStyle = CppApiStyle.Both,
            Namespace = "Petstore",
        });

        files.Keys.Should().BeEquivalentTo(new[]
        {
            "Models/Order.h",
            "Enums/OrderStatus.h",
            "Serializers/OrderJson.h",
            "Serializers/OrderJson.cpp",
            "Serializers/OrderStatusJson.h",
            "Serializers/OrderStatusJson.cpp",
            "Runtime/ValIOExpected.h",
            "Runtime/ValIOError.h",
            "Runtime/ValIOHttp.h",
            "Runtime/ValIOHttp.cpp",
            "Runtime/ValIOJson.h",
            "Runtime/ValIOJsonTypes.h",
            "Runtime/ValIOJsonValidation.h",
            "Runtime/ValIOCancellation.h",
            "Runtime/Impl/ValIORequest.inl",
            "Interfaces/IStore.h",
            "Clients/StoreClient.h",
            "Clients/Impl/StoreClient.cpp",
        });

        // Each runtime layer has one canonical output file.
        files.Keys.Count(f => f.StartsWith("Runtime/")).Should().Be(9);
    }

    [Test]
    public async Task StdPipeline_ClassSync_NoInterfaceFile_NoAsync()
    {
        var files = await GenerateAsync(BuildDocument(), new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            ClientShape = CppClientShape.Class,
            ApiStyle = CppApiStyle.Sync,
            Namespace = "Petstore",
        });

        files.Keys.Should().NotContain("Interfaces/IStore.h");
        files["Clients/StoreClient.h"].Should().NotContain("Async");
        files["Clients/StoreClient.h"].Should().Contain("class StoreClient final");
    }

    [Test]
    public async Task StdClient_Implementation_HasNamedLocals_WireQueryName_AndConstDefinitions()
    {
        var files = await GenerateAsync(BuildDocument(), new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            ClientShape = CppClientShape.InterfaceImpl,
            ApiStyle = CppApiStyle.Both,
            Namespace = "Petstore",
        });
        var impl = files["Clients/Impl/StoreClient.cpp"].Replace("\r\n", "\n");

        // Именованные локали — безусловный стиль тел (требование W5).
        impl.Should().Contain("std::string path = std::string(GetOrderByIdPath);");
        impl.Should().Contain("ValIO::ReplacePathParameter(path, \"{orderId}\", ValIO::UrlEncode(std::to_string(OrderId)));");
        impl.Should().Contain("ValIO::Request httpRequest;");
        impl.Should().Contain("const ValIO::Expected<ValIO::Response, ValIO::Error> sendResult = _http->Send(httpRequest, cancellationToken);");
        impl.Should().Contain("const ValIO::Response& response = sendResult.value();");
        impl.Should().Contain("return ValIO::Parse<Order>(response.Body, ValIO::JsonStringFormat::Plain);");

        // wire-имя query-параметра — OriginalName (Paging.Offset), не sanitized.
        impl.Should().Contain("httpRequest.AddQuery(\"Paging.Offset\", PagingOffset);");

        // Определение константное и без повторных default-аргументов.
        impl.Should().Contain("StoreClient::GetOrderById(std::int64_t OrderId, std::optional<std::int32_t> PagingOffset, ValIO::CancellationToken cancellationToken) const");
        impl.Should().NotContain("= std::nullopt) const");

        // Интерфейс — чистый: методы pure virtual + виртуальный деструктор.
        var interfaceHeader = files["Interfaces/IStore.h"].Replace("\r\n", "\n");
        interfaceHeader.Should().Contain("virtual ~IStore() = default;");
        interfaceHeader.Should().Contain("virtual ValIO::Expected<Order, ValIO::Error> GetOrderById(std::int64_t OrderId, std::optional<std::int32_t> PagingOffset = std::nullopt, ValIO::CancellationToken cancellationToken = {}) const = 0;");
        interfaceHeader.Should().Contain("std::future<ValIO::Expected<Order, ValIO::Error>> GetOrderByIdAsync");
        interfaceHeader.Should().NotContainAny(["ValIO::HttpClient", "httplib", "ValIOJson", "nlohmann"]);

        // Клиентский хедер не тянет полный nlohmann (только сериализаторные .cpp).
        files["Clients/StoreClient.h"].Should().NotContain("nlohmann");
        files["Clients/Impl/StoreClient.cpp"].Should().Contain("Runtime/ValIOJson.h");
    }

    [Test]
    public async Task StdPipeline_OutputIsIndependentOfCxxStandard()
    {
        var document = BuildDocument();
        var baseline = await GenerateAsync(document, new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            ClientShape = CppClientShape.InterfaceImpl,
            ApiStyle = CppApiStyle.Both,
            Namespace = "Petstore",
            CxxStandard = 17,
        });

        foreach (var standard in new[] { 20, 23 })
        {
            var output = await GenerateAsync(BuildDocument(), new CppGenerationOptions
            {
                Runtime = CppRuntimeProfile.Std,
                ClientShape = CppClientShape.InterfaceImpl,
                ApiStyle = CppApiStyle.Both,
                Namespace = "Petstore",
                CxxStandard = standard,
            });
            output.Should().BeEquivalentTo(baseline,
                $"вывод не должен зависеть от cxxStandard={standard} — переключатель стандарта живёт в Runtime/ValIOExpected.h");
        }
    }

    [Test]
    public async Task UeSteps_SuppressedUnderStdProfile()
    {
        var files = await GenerateAsync(BuildDocument(), new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            Namespace = "Petstore",
        });

        files.Keys.Should().NotContain(f => f.StartsWith("Public/") || f.StartsWith("Private/"));
    }

    /// <summary>Checks generated CMake separates layers and reuses canonical dependency targets supplied by a superproject.</summary>
    /// <returns>The asynchronous test execution.</returns>
    [Test]
    public async Task EmitCmake_AddsRootCMakeLists_WithTargetStandard()
    {
        var withoutCmake = await GenerateAsync(BuildDocument(), new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            Namespace = "Petstore",
            EmitCmake = false,
        });
        withoutCmake.Keys.Should().NotContain("CMakeLists.txt");

        var withCmake = await GenerateAsync(BuildDocument(), new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            Namespace = "Petstore",
            EmitCmake = true,
            CxxStandard = 20,
        });
        withCmake.Keys.Should().Contain("CMakeLists.txt");
        withCmake["CMakeLists.txt"].Replace("\r\n", "\n").Should()
            .Contain("set(CMAKE_CXX_STANDARD 20)")
            .And.Contain("target_compile_features(valio_models INTERFACE cxx_std_20)")
            .And.Contain("find_package(httplib REQUIRED)")
            .And.Contain("find_package(nlohmann_json REQUIRED)")
            .And.Contain("if(NOT TARGET nlohmann_json::nlohmann_json)\n        find_package(nlohmann_json REQUIRED)\n    endif()")
            .And.Contain("if(NOT TARGET httplib::httplib)\n        find_package(httplib REQUIRED)\n    endif()")
            .And.Contain("add_library(valio_models INTERFACE)")
            .And.Contain("add_library(valio_serializers STATIC ${VALIO_SERIALIZER_SOURCES})")
            .And.Contain("add_library(valio_clients STATIC ${VALIO_CLIENT_SOURCES}")
            .And.Contain("PRIVATE httplib::httplib")
            .And.Contain("project(ValIO_Petstore LANGUAGES CXX)");
    }
}
