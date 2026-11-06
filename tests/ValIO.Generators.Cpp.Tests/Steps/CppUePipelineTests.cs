using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp;
using ValIO.Generators.Cpp.Ast.Builders;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.Tests.Steps;

/// <summary>
/// Регрессионный замок рефакторинга «фасад → шаги»: пайплайн UE-профиля
/// обязан давать тот же набор файлов и то же содержимое побайтово, что и
/// монолитный CppAstBuilder.Build на одном и том же IrDocument.
/// </summary>
[TestFixture]
public class CppUePipelineTests
{
    private static IrProperty Prop(string originalName, string pascalName, IrPrimitiveKind kind, bool required = true) => new()
    {
        OriginalName = originalName,
        Name = pascalName,
        Type = new IrPrimitiveType { PrimitiveKind = kind },
        IsRequired = required,
    };

    private static IrDocument BuildDocument()
    {
        var model = new IrModel
        {
            Name = "BuildingModel",
            Properties =
            [
                Prop("buildingId", "BuildingId", IrPrimitiveKind.Long),
                Prop("displayName", "DisplayName", IrPrimitiveKind.String, required: false),
            ],
        };

        var statusEnum = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values =
            [
                new IrEnumValue { Name = "Placed", Value = "placed" },
                new IrEnumValue { Name = "Approved", Value = "approved" },
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
            QueryParameters = [],
            HeaderParameters = [],
            Responses =
            [
                new IrResponse
                {
                    StatusCode = "200",
                    Type = new IrModelReference { ModelName = "BuildingModel" },
                },
                new IrResponse { StatusCode = "default" },
            ],
        };

        return new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models = [model],
            Enums = [statusEnum],
            Clients = [new IrClient { Name = "Store", Operations = [operation] }],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
                TopologicalOrder = ["BuildingModel", "OrderStatus"],
            },
        };
    }

    /// <summary>Renders the document-bound facade as the pipeline parity baseline.</summary>
    /// <param name="document">The exact document retained by the facade context.</param>
    /// <returns>The generated file contents indexed by relative path.</returns>
    private static Dictionary<string, string> RenderViaFacade(IrDocument document)
    {
        var facade = new CppAstBuilder(new CppGenerationOptions(), new IrDocumentContext(document));
        var build = facade.Build(document);
        var renderer = new CppAstRenderer();
        return build.Files.ToDictionary(kv => kv.Key, kv => renderer.Render(kv.Value));
    }

    private static async Task<Dictionary<string, string>> RenderViaPipeline(IrDocument document, object? pluginOptions)
    {
        var services = new ServiceCollection()
            .AddSingleton<ICppAstRenderer, CppAstRenderer>()
            .BuildServiceProvider();

        var pipeline = new CppGeneratorPlugin().CreatePipeline(services);
        var context = new GenerationContext
        {
            IrDocument = document,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = pluginOptions },
            PluginOptions = pluginOptions,
        };
        await pipeline.ExecuteAsync(context);
        return context.GeneratedFiles.ToDictionary(f => f.RelativePath, f => f.Content);
    }

    [Test]
    public async Task Pipeline_WithoutOptions_MatchesFacadeByteForByte()
        => await AssertPipelineMatchesFacade(null);

    [Test]
    public async Task Pipeline_WithUeOptions_MatchesFacadeByteForByte()
        => await AssertPipelineMatchesFacade(new CppGenerationOptions { Runtime = CppRuntimeProfile.Ue });

    private static async Task AssertPipelineMatchesFacade(object? pluginOptions)
    {
        var document = BuildDocument();
        var baseline = RenderViaFacade(document);
        var pipeline = await RenderViaPipeline(document, pluginOptions);

        pipeline.Keys.Should().BeEquivalentTo(baseline.Keys, "набор файлов должен совпадать с фасадом");
        foreach (var (path, expected) in baseline)
            pipeline[path].Should().Be(expected, $"содержимое {path} должно совпадать побайтово");

        // Контракт раскладки: модели, enum и 4 файла на операцию.
        baseline.Keys.Should().Contain(new[]
        {
            "Public/Models/BuildingModel.h",
            "Public/Enums/OrderStatus.h",
            "Public/Endpoints/GetOrderById/GetOrderByIdParams.h",
            "Public/Endpoints/GetOrderById/GetOrderByIdResult.h",
            "Public/Endpoints/GetOrderById/GetOrderById.h",
            "Private/Endpoints/GetOrderById.cpp",
        });
    }

    [Test]
    public async Task StdProfile_SuppressesAllUeSteps()
    {
        var document = BuildDocument();
        var pipeline = await RenderViaPipeline(
            document, new CppGenerationOptions { Runtime = CppRuntimeProfile.Std });

        // UE-шаги отключены (Public//Private — раскладка UE-профиля);
        // std-шаги (Models/Enums/Runtime/Clients) работают начиная с W5.
        pipeline.Keys.Should().NotContain(path => path.StartsWith("Public/") || path.StartsWith("Private/"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GenerateModels_Toggle_SkipsOrEmitsModelFile(bool generateModels)
    {
        var document = BuildDocument();
        var services = new ServiceCollection()
            .AddSingleton<ICppAstRenderer, CppAstRenderer>()
            .BuildServiceProvider();
        var pipeline = new CppGeneratorPlugin().CreatePipeline(services);
        var context = new GenerationContext
        {
            IrDocument = document,
            Configuration = new GenerationConfiguration { GenerateModels = generateModels },
        };
        await pipeline.ExecuteAsync(context);

        var hasModel = context.GeneratedFiles.Any(f => f.RelativePath == "Public/Models/BuildingModel.h");
        hasModel.Should().Be(generateModels);
    }
}
