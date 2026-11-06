using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Steps;

/// <summary>
/// Regression tests for «проводные» имена query-параметров в TypeScript-клиентах.
/// ASP.NET биндит вложенную модель (<c>PagingQuery Paging</c>) по точечным именам
/// <c>Paging.Offset</c>/<c>Paging.Take</c>. Точка недопустима в TS-идентификаторе, поэтому свойство
/// параметров sanitized до <c>Paging_Offset</c> — но ключ, уходящий в query-string, обязан остаться
/// исходным, иначе сервер параметр не видит и постранично листать нечем.
/// Аналог <c>CSharpAstBuilderQueryParametersTests</c> для C#-генератора.
/// </summary>
[TestFixture]
public class NestedQueryParamNamesTests
{
    private PipelineTestHarness _harness = null!;

    [OneTimeSetUp]
    public void Setup() => _harness = new PipelineTestHarness();

    [OneTimeTearDown]
    public void Teardown() => _harness.Dispose();

    /// <summary>
    /// Документ с операцией, повторяющей публичный lookup городов PIN: необязательный плоский фильтр
    /// плюс обязательные параметры вложенной модели пагинации с точками в именах.
    /// </summary>
    private const string NestedPagingSchema = """
    {
      "openapi": "3.0.1",
      "info": { "title": "Handbook", "version": "v1" },
      "paths": {
        "/api/public/v1/handbook/cities": {
          "get": {
            "operationId": "LookupCities",
            "parameters": [
              { "name": "CountryId", "in": "query", "required": false, "schema": { "type": "integer" } },
              { "name": "Paging.Offset", "in": "query", "required": true, "schema": { "type": "integer" } },
              { "name": "Paging.Take", "in": "query", "required": true, "schema": { "type": "integer" } }
            ],
            "responses": {
              "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "object" } } } }
            }
          }
        }
      }
    }
    """;

    /// <summary>Документ без вложенных параметров — контроль на отсутствие регрессии.</summary>
    private const string FlatQuerySchema = """
    {
      "openapi": "3.0.1",
      "info": { "title": "Handbook", "version": "v1" },
      "paths": {
        "/api/public/v1/handbook/countries": {
          "get": {
            "operationId": "LookupCountries",
            "parameters": [
              { "name": "Search", "in": "query", "required": false, "schema": { "type": "string" } }
            ],
            "responses": {
              "200": { "description": "OK", "content": { "application/json": { "schema": { "type": "object" } } } }
            }
          }
        }
      }
    }
    """;

    /// <summary>
    /// Прогоняет пайплайн с валидацией zod4 — так сконфигурированы все 50 PIN-проектов, поэтому
    /// значения читаются из <c>parsedQuery</c>, а не из сырого <c>params</c>.
    /// </summary>
    private async Task<string> GenerateAsync(string schemaJson, string transport, string fileSuffix)
    {
        var doc = await PipelineTestHarness.LoadDocumentFromJsonAsync(schemaJson);
        var ctx = await _harness.RunAsync(doc, new TypeScriptGenerationOptions
        {
            Transports = [transport],
            ValidationLibrary = "zod4",
        });

        return ctx.GeneratedFiles
            .First(f => f.RelativePath.EndsWith(fileSuffix, StringComparison.Ordinal))
            .Content;
    }

    [Test]
    public async Task AxiosClient_NestedQueryParam_SendsDottedWireKey()
    {
        var content = await GenerateAsync(NestedPagingSchema, "axios", ".axios.ts");

        // Ключ в params — исходное точечное имя, значение читается по sanitized-свойству.
        content.Should().Contain("'Paging.Offset': parsedQuery.Paging_Offset");
        content.Should().Contain("'Paging.Take': parsedQuery.Paging_Take");
    }

    [Test]
    public async Task AxiosClient_NestedQueryParam_DoesNotPassQueryObjectThroughVerbatim()
    {
        var content = await GenerateAsync(NestedPagingSchema, "axios", ".axios.ts");

        // Передача объекта целиком отправила бы ключи с подчёркиванием — именно этот дефект и правим.
        content.Should().NotContain("params: parsedQuery");
    }

    /// <summary>Preserves unchanged query wire names through the same serializer used for dotted names.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task AxiosClient_FlatQueryParams_PreserveWireNames()
    {
        var content = await GenerateAsync(FlatQuerySchema, "axios", ".axios.ts");

        // The canonical object serializer also preserves identifiers that do not need sanitization.
        content.Should().Contain("params: {");
        content.Should().Contain("Search: parsedQuery.Search");
    }

    [Test]
    public async Task NextjsClient_NestedQueryParam_SetsDottedWireKey()
    {
        var content = await GenerateAsync(NestedPagingSchema, "nextjs", ".nextjs.ts");

        content.Should().Contain("\"Paging.Offset\"");
        content.Should().Contain("String(parsedQuery.Paging_Offset)");
        // Ключ с подчёркиванием сервер не биндит.
        content.Should().NotContain("query.set(\n            \"Paging_Offset\"");
        content.Should().NotContain("\"Paging_Offset\",");
    }

    [Test]
    public async Task FetchClient_NestedQueryParam_SetsDottedWireKey()
    {
        var content = await GenerateAsync(NestedPagingSchema, "fetch", ".fetch.ts");

        content.Should().Contain("\"Paging.Offset\"");
        content.Should().Contain("String(parsedQuery.Paging_Offset)");
        content.Should().NotContain("\"Paging_Offset\",");
    }

    [Test]
    public async Task FlatQueryParam_WireKeyUnchanged()
    {
        var content = await GenerateAsync(FlatQuerySchema, "nextjs", ".nextjs.ts");

        // OriginalName по умолчанию равен Name — обычные параметры не регрессируют.
        content.Should().Contain("\"Search\"");
    }
}
