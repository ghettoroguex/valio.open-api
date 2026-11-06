using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Axios.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Integration;

/// <summary>Проверяет независимую необязательность при передаче, настроенный Axios и смешанные ответы без тела.</summary>
[TestFixture]
public sealed class IdentityClientContractTests
{
    /// <summary>Объявления модели и запроса сохраняют все четыре сочетания наличия и допустимости null.</summary>
    [Test]
    public void ModelsAndParametersKeepPresenceSeparateFromNull()
    {
        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create(), new NamingConverter());
        var renderer = new TypeScriptAstRenderer([new AxiosNodeRenderer()]);
        var text = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
        var nullable = new IrNullableType { InnerType = text };
        var model = new IrModel
        {
            Name = "Example",
            Properties =
            [
                Property("requiredValue", text, true),
                Property("requiredNullable", nullable, true),
                Property("optionalValue", text, false),
                Property("optionalNullable", nullable, false),
                Property("nestedOptional", new IrNullableType { InnerType = new IrOptionalType { InnerType = text } }, true)
            ]
        };
        var modelCode = renderer.Render(builder.BuildSourceFile(model));
        modelCode.Should().Contain("requiredValue: string;");
        modelCode.Should().Contain("requiredNullable: string | null;");
        modelCode.Should().Contain("optionalValue?: string;");
        modelCode.Should().Contain("optionalNullable?: string | null;");
        modelCode.Should().Contain("nestedOptional?: string | null;");
        var operation = Operation([new IrResponse { StatusCode = "204", Type = null }]) with
        {
            QueryParameters =
            [
                new IrParameter { Name = "requiredNullable", IsRequired = true, Type = nullable },
                new IrParameter { Name = "optionalValue", IsRequired = false, Type = text }
            ]
        };
        var paramsBuilder = new TypeScriptParamsAstBuilder(IrDocumentContexts.Create(), new NamingConverter(), builder.BuildType);
        var paramsCode = renderer.Render(paramsBuilder.BuildParamsSourceFile(operation, null, null, operation.MethodName));
        paramsCode.Should().Contain("requiredNullable: string | null;");
        paramsCode.Should().Contain("optionalValue?: string;");
    }

    /// <summary>AllOf сохраняет ограничение базового типа, когда локальное объявление менее строгое.</summary>
    [Test]
    public void AllOfDoesNotRelaxInheritedRequiredProperty()
    {
        var text = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
        var parent = new IrModel { Name = "Base", Properties = [Property("id", text, true)] };
        var child = new IrModel
        {
            Name = "Child",
            Properties = [Property("id", new IrNullableType { InnerType = text }, false)],
            Composition = new IrComposition { Kind = IrCompositionKind.AllOf, Types = [new IrModelReference { ModelName = "Base" }] }
        };
        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create([parent, child]), new NamingConverter());
        var renderer = new TypeScriptAstRenderer();
        renderer.Render(builder.BuildSourceFile(parent)).Should().Contain("id: string;");
        var derived = renderer.Render(builder.BuildSourceFile(child));
        derived.Should().Contain("interface Child extends Base");
        derived.Should().NotContain("id?:");
    }

    /// <summary>AllOf ужесточает необязательное допускающее null базовое свойство и сохраняет оба ограничения значения.</summary>
    [Test]
    public void AllOfNarrowsPresenceAndIntersectsConflictingValueTypes()
    {
        var text = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
        var number = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer };
        var parent = new IrModel { Name = "Base", Properties = [Property("id", new IrNullableType { InnerType = text }, false)] };
        var child = new IrModel
        {
            Name = "Child", Properties = [Property("id", number, true)],
            Composition = new IrComposition { Kind = IrCompositionKind.AllOf, Types = [new IrModelReference { ModelName = "Base" }] }
        };
        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create([parent, child]), new NamingConverter());
        var code = new TypeScriptAstRenderer().Render(builder.BuildSourceFile(child));
        code.Should().Contain("id: (string | null) & number;");
        code.Should().NotContain("id?:");
    }

    /// <summary>Общий тестовый пример allOf сохраняет унаследованные и локальные ограничения в сгенерированных объявлениях.</summary>
    /// <returns>Задача, завершающаяся после проверки результата тестового примера allOf.</returns>
    [Test]
    public async Task AllOfFixtureGeneratesRequiredNonNullIntersection()
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentAsync("allof-narrowing.json");
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["axios"] });
        var child = context.GeneratedFiles.Single(file => file.RelativePath.Replace('\\', '/')
            .EndsWith("models/child/child.ts", StringComparison.Ordinal)).Content;
        child.Should().Contain("interface Child extends Base");
        child.Should().Contain("id: (string | null) & string;");
        child.Should().NotContain("id?:");
    }

    /// <summary>Axios принимает явно настроенный клиент после сигнала отмены и по умолчанию использует настроенный экземпляр.</summary>
    [Test]
    public void AxiosUsesCallerClientOrConfiguredInstance()
    {
        var options = new TypeScriptGenerationOptions
        {
            Transports = ["axios"],
            Axios = new TypeScriptAxiosOptions { CustomInstancePath = "@shared/api/api-mutator.ts", CustomInstanceName = "apiMutator" }
        };
        var code = new TypeScriptAstRenderer([new AxiosNodeRenderer()]).Render(new TypeScriptAstBuilder(IrDocumentContexts.Create(), new NamingConverter(), options)
            .BuildSourceFileForOperation(Operation([new IrResponse { StatusCode = "204", Type = null }]), transport: "axios"));
        code.Should().Contain("from \"@shared/api/api-mutator\"");
        code.Should().Contain("signal?: AbortSignal");
        code.Should().Contain("client?: AxiosInstance");
        code.IndexOf("signal?: AbortSignal", StringComparison.Ordinal).Should().BeLessThan(code.IndexOf("client?: AxiosInstance", StringComparison.Ordinal));
        code.Should().Contain("client ?? axiosDefaults.apiMutator");
        code.Should().Contain("axiosInstance.request");
    }

    /// <summary>Документированные успешные ответы без тела представлены в результате и не разбираются fetch как JSON.</summary>
    /// <param name="transport">Нативный HTTP-транспорт для обработки успешных ответов.</param>
    [TestCase("axios")]
    [TestCase("fetch")]
    public void MixedResponsesIncludeBodylessSuccess(string transport)
    {
        var operation = Operation([
            new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } },
            new IrResponse { StatusCode = "204", Type = null }
        ]);
        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create(), new NamingConverter(), new TypeScriptGenerationOptions { Transports = [transport] });
        var renderer = new TypeScriptAstRenderer([new AxiosNodeRenderer()]);
        var code = renderer.Render(builder.BuildSourceFileForOperation(operation, transport: transport));
        var results = renderer.Render(new TypeScriptResultsAstBuilder(IrDocumentContexts.Create(), new NamingConverter(), builder.BuildType)
            .BuildResultsSourceFile(operation, null, null, operation.MethodName));
        results.Should().Contain("ExampleResult204");
        results.Should().Contain("body: undefined;");
        code.Should().Contain("body: undefined");
        if (transport == "fetch")
            code.IndexOf("204", StringComparison.Ordinal).Should().BeLessThan(code.IndexOf("response.json", StringComparison.Ordinal));
    }

    /// <summary>Разделители сгенерированного клиента не содержат пробелов, а строковые литералы сохраняют свои пробелы.</summary>
    /// <param name="transport">Выбранный HTTP-транспорт.</param>
    [TestCase("axios")]
    [TestCase("fetch")]
    public void ClientBlankLinesDoNotContainWhitespace(string transport)
    {
        var operation = Operation([new IrResponse { StatusCode = "204", Type = null }]) with { Path = "/  literal  " };
        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create(), new NamingConverter(), new TypeScriptGenerationOptions { Transports = [transport] });
        var code = new TypeScriptAstRenderer([new AxiosNodeRenderer()]).Render(builder.BuildSourceFileForOperation(operation, transport: transport));
        code.Split('\n').Select(line => line.TrimEnd('\r')).Should().NotContain(line => line.Length > 0 && string.IsNullOrWhiteSpace(line));
        code.Should().Contain("/  literal  ");
        code.Should().EndWith(Environment.NewLine);
        code.Should().NotEndWith(Environment.NewLine + Environment.NewLine);
    }

    /// <summary>Ответы исключительно без тела сохраняют возвращаемый тип void и не читают JSON через fetch.</summary>
    [Test]
    public void FetchVoidDoesNotReadJson()
    {
        var operation = Operation([new IrResponse { StatusCode = "204", Type = null }]);
        var code = new TypeScriptAstRenderer([new AxiosNodeRenderer()]).Render(new TypeScriptAstBuilder(IrDocumentContexts.Create(), new NamingConverter())
            .BuildSourceFileForOperation(operation, transport: "fetch"));
        code.Should().Contain("Promise<void>");
        code.Should().NotContain("response.json");
    }

    /// <summary>Создаёт свойство модели для матрицы наличия и допустимости null.</summary>
    /// <param name="name">Имя свойства.</param>
    /// <param name="type">Тип значения свойства.</param>
    /// <param name="required">Является ли свойство обязательным.</param>
    /// <returns>Настроенное свойство IR.</returns>
    private static IrProperty Property(string name, IrType type, bool required) => new()
    {
        Name = name, OriginalName = name, Type = type, IsRequired = required
    };

    /// <summary>Создаёт операцию с явно документированными ответами.</summary>
    /// <param name="responses">Документированные ответы операции.</param>
    /// <returns>Настроенная операция.</returns>
    private static IrOperation Operation(IReadOnlyList<IrResponse> responses) => new()
    {
        MethodName = "Example", Path = "/example", HttpMethod = "GET",
        PathParameters = [], QueryParameters = [], HeaderParameters = [], RequestBody = null, Responses = responses
    };
}
