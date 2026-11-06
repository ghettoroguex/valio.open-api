using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Separates request-body presence from required fields within the body schema.</summary>
[TestFixture]
public sealed class IrBuilderRequestBodyPresenceTests
{
    /// <summary>Preserves optional and required body declarations while retaining the same required inner field.</summary>
    /// <param name="version">The OpenAPI dialect whose explicit request-body fixture is translated.</param>
    /// <returns>The asynchronous assertion operation.</returns>
    [TestCase("2.0")]
    [TestCase("3.0")]
    [TestCase("3.1")]
    [TestCase("3.2")]
    public async Task RequestBodyPresence_DependsOnBodyDeclarationRatherThanRequiredModelFields(string version)
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read($"CoreIR/request-body-presence-{version}.json"));
        var operations = ir.Clients.SelectMany(client => client.Operations).ToDictionary(operation => operation.Path);
        var omitted = operations["/optional-omitted"].RequestBody!;
        var optional = operations["/optional-explicit"].RequestBody!;
        var required = operations["/required"].RequestBody!;

        omitted.IsRequired.Should().BeFalse("an omitted body-required flag defaults to optional");
        optional.IsRequired.Should().BeFalse("required inner fields constrain a supplied body only");
        required.IsRequired.Should().BeTrue("the body itself explicitly requires presence");

        foreach (var body in new[] { omitted, optional, required })
        {
            body.Type.Should().BeOfType<IrModelReference>().Which.ModelName.Should().Be("Payload");
            body.ContentType.Should().Be("application/json");
        }

        var model = ir.Models.Single(candidate => candidate.Name == "Payload");
        model.Properties.Single(property => property.OriginalName == "id").IsRequired.Should().BeTrue();
        model.Properties.Single(property => property.OriginalName == "note").IsRequired.Should().BeFalse();
    }
}
