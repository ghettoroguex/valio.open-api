using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Protects source identifiers and wire identities from lossy parameter normalization.</summary>
[TestFixture]
public sealed class IrBuilderParameterIdentityTests
{
    /// <summary>Declares case-sensitive, sanitized, and cross-location identifier collisions.</summary>
    private static readonly string CollisionDocument = OpenApiFixture.Read("CoreIR/parameter-identities.json");

    /// <summary>Ensures distinct wire locations, case, and punctuation remain present in the IR.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task DistinctWireParameters_ArePreservedWithDistinctSourceIdentifiers()
    {
        var ir = await BuildAsync(CollisionDocument);
        var operation = ir.Clients.Single().Operations.Single();

        operation.PathParameters.Select(parameter => parameter.OriginalName).Should().Equal("item-id", "ID");
        operation.QueryParameters.Select(parameter => parameter.OriginalName)
            .Should().Equal("ID", "id", "filter.name", "filter-name", "filter_name");
        operation.HeaderParameters.Single().OriginalName.Should().Be("item-id");

        var parameters = operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters);
        var converter = new NamingConverter();
        parameters.Select(parameter => converter.ToCamelCase(parameter.Name)).Should().OnlyHaveUniqueItems();
        operation.QueryParameters.Single(parameter => parameter.OriginalName == "ID")
            .Type.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Integer);
    }

    /// <summary>Ensures diagnostics are observational and never change generated parameter identities.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OptionalDiagnostics_DoNotChangeParameterNormalization()
    {
        var warnings = new List<string>();
        var withDiagnostics = await BuildAsync(CollisionDocument, warnings);
        var withoutDiagnostics = await BuildAsync(CollisionDocument);

        withDiagnostics.Clients.Should().BeEquivalentTo(withoutDiagnostics.Clients);
        warnings.Should().NotBeEmpty();
        warnings.Should().OnlyContain(warning => warning.Contains("identifier", StringComparison.Ordinal));
    }

    /// <summary>Ensures operation overrides respect case-sensitive query names and case-insensitive header names.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task OperationOverrides_UseWireLocationAndCorrectCaseRules()
    {
        var json = OpenApiFixture.Read("CoreIR/parameter-overrides.json");

        var operation = (await BuildAsync(json)).Clients.Single().Operations.Single();
        operation.PathParameters.Select(parameter => parameter.OriginalName).Should().Equal("id", "ID");
        operation.QueryParameters.Select(parameter => parameter.OriginalName).Should().Equal("Limit", "limit");
        operation.QueryParameters[0].Type.Should().BeOfType<IrPrimitiveType>()
            .Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
        operation.QueryParameters[1].Type.Should().BeOfType<IrPrimitiveType>()
            .Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Integer);
        operation.HeaderParameters.Should().ContainSingle().Which.OriginalName.Should().Be("x-trace");
        operation.HeaderParameters[0].Type.Should().BeOfType<IrPrimitiveType>()
            .Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Integer);
    }

    /// <summary>Ensures repeated declarations are collapsed identically with and without a diagnostic collection.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task DuplicateWireDeclarations_AreDiagnosedAndKeepLastDefinition()
    {
        var json = OpenApiFixture.Read("CoreIR/parameter-duplicates.json");

        var warnings = new List<string>();
        var withDiagnostics = await BuildAsync(json, warnings);
        var withoutDiagnostics = await BuildAsync(json);
        withDiagnostics.Clients.Should().BeEquivalentTo(withoutDiagnostics.Clients);
        var parameter = withDiagnostics.Clients.Single().Operations.Single().QueryParameters.Should().ContainSingle().Which;
        parameter.Type.Should().BeOfType<IrPrimitiveType>().Which.PrimitiveKind.Should().Be(IrPrimitiveKind.Integer);
        warnings.Should().ContainSingle().Which.Should().Contain("duplicate query parameter 'limit'");
    }

    /// <summary>Builds a fresh IR through the real OpenAPI reader and an optional diagnostic sink.</summary>
    /// <param name="json">The source OpenAPI JSON.</param>
    /// <param name="warnings">An optional diagnostic collection.</param>
    /// <returns>The translated document.</returns>
    private static async Task<IrDocument> BuildAsync(string json, ICollection<string>? warnings = null)
    {
        var document = await IrTestHarness.LoadAsync(json);
        return new IrBuilder(new SchemaResolver(document), new NamingConverter()).BuildFromOpenApi(document, warnings);
    }
}
