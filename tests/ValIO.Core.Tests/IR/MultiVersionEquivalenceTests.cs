using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;

namespace ValIO.Core.Tests.IR;

/// <summary>
/// Verifies equivalent nullable IR across Swagger 2.0 and OpenAPI 3.0, 3.1, and 3.2.
/// Reader failures fail the tests instead of silently removing a dialect from coverage.
/// </summary>
[TestFixture]
public class MultiVersionEquivalenceTests
{
    /// <summary>Lists the embedded specification dialects that every test must support.</summary>
    private static readonly string[] Versions = { "2.0", "3.0", "3.1", "3.2" };

    /// <summary>Builds the selected embedded contract without suppressing reader or IR failures.</summary>
    /// <param name="version">The specification dialect identifying the embedded fixture.</param>
    /// <returns>The complete IR for the selected contract.</returns>
    private static Task<IrDocument> BuildAsync(string version) => IrTestHarness.BuildIrAsync(
        OpenApiFragments.LoadEmbedded($"multiversion/account-{version}.json"));

    /// <summary>Requires every supported dialect to expose its declared account model.</summary>
    /// <param name="version">The specification dialect under test.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCaseSource(nameof(Versions))]
    public async Task Reader_ParsesVersion(string version)
    {
        var ir = await BuildAsync(version);
        ir.Models.Select(m => m.Name).Should().Contain("Account");
    }

    /// <summary>Normalizes each dialect's nullable scalar spelling into the same IR type.</summary>
    /// <param name="version">The specification dialect under test.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCaseSource(nameof(Versions))]
    public async Task NullableScalar_EquivalentAcrossDialects(string version)
    {
        var ir = await BuildAsync(version);
        var nickname = ir.Models.Single(m => m.Name == "Account")
            .Properties.Single(p => p.OriginalName == "nickname");

        nickname.Type.Should().BeOfType<IrNullableType>()
            .Which.InnerType.Should().BeOfType<IrPrimitiveType>()
            .Which.PrimitiveKind.Should().Be(IrPrimitiveKind.String);
    }

    /// <summary>Preserves the referenced model when a dialect explicitly permits null.</summary>
    /// <param name="version">The specification dialect under test.</param>
    /// <returns>The asynchronous test operation.</returns>
    [TestCaseSource(nameof(Versions))]
    public async Task NullableRefWrapper_EquivalentAcrossDialects(string version)
    {
        var ir = await BuildAsync(version);
        var envelope = ir.Models.Single(m => m.Name == "Holder")
            .Properties.Single(p => p.OriginalName == "envelope");

        envelope.Type.Should().BeOfType<IrNullableType>()
            .Which.InnerType.Should().BeOfType<IrModelReference>()
            .Which.ModelName.Should().Be("Account");
    }
}
