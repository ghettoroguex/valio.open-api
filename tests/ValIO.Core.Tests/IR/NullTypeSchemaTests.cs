using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>
/// Покрывает OpenAPI 3.1 «только null» ({"type":"null"}) как самостоятельный тип свойства
/// (конверт success/error) и диагностический контекст в сообщениях об ошибках построения IR.
/// </summary>
[TestFixture]
public class NullTypeSchemaTests
{
    // Standalone {"type":"null"} свойство не должно падать и должно маппиться в IrPrimitiveKind.Null.
    [Test]
    public async Task StandaloneNullTypeProperty_MapsToNullPrimitive()
    {
        var model = await IrTestHarness.BuildSingleModelAsync(
            "Envelope",
            """{"type":"object","required":["result","error"],"properties":{"result":{"type":"object","properties":{"id":{"type":"string"}}},"error":{"type":"null"}}}""",
            "3.1.0");

        var error = model.Properties.Single(p => p.OriginalName == "error");
        error.Type.Should().BeOfType<IrPrimitiveType>()
            .Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Null);
    }

    /// <summary>Preserves nullable normalization when an anyOf contains an explicit null alternative.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task NullInsideAnyOf_StaysNullable()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/null-anyof-3.1.json"));
        var model = ir.Models.Single(candidate => candidate.Name == "M");

        var capacity = model.Properties.Single(p => p.OriginalName == "capacity");
        capacity.Type.Should().BeOfType<IrNullableType>();
    }

    // Некорректная схема сообщает endpoint + операцию + ответ + путь свойства в тексте ошибки.
    [Test]
    public async Task MalformedSchema_ErrorMessageIncludesLocationContext()
    {
        const string doc = """
        {
          "openapi":"3.1.0",
          "info":{"title":"T","version":"1.0.0"},
          "paths":{
            "/api/apartments":{
              "get":{
                "operationId":"getApartments",
                "responses":{
                  "200":{
                    "description":"ok",
                    "content":{"application/json":{"schema":{"type":"object","properties":{"data":{"type":"array"}}}}}
                  }
                }
              }
            }
          }
        }
        """;

        var act = async () => await IrTestHarness.BuildIrAsync(doc);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should()
                .Contain("getApartments").And
                .Contain("/api/apartments").And
                .Contain("response 200").And
                .Contain("data");
    }
}
