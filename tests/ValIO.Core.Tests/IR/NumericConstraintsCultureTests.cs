using System.Globalization;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Tests.TestHelpers;

namespace ValIO.Core.Tests.IR;

/// <summary>Checks that OpenAPI numeric bounds do not depend on the process culture.</summary>
[TestFixture]
public sealed class NumericConstraintsCultureTests
{
    /// <summary>Parses all fractional bounds under cultures with different decimal separators.</summary>
    /// <param name="culture">The current culture used while building the IR.</param>
    /// <returns>A task that completes after all bounds have been checked.</returns>
    [TestCase("en-US")]
    [TestCase("ru-RU")]
    public async Task FractionalBoundsUseInvariantOpenApiSyntax(string culture)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            const string schema = "{\"type\":\"object\",\"properties\":{" +
                "\"range\":{\"type\":\"number\",\"minimum\":0.5,\"maximum\":4.5}," +
                "\"exclusive\":{\"type\":\"number\",\"exclusiveMinimum\":1.5,\"exclusiveMaximum\":3.5}}}";
            var model = await IrTestHarness.BuildSingleModelAsync("Holder", schema, "3.1.0");
            var range = model.Properties.Single(p => p.OriginalName == "range").Constraints;
            range.Should().NotBeNull();
            range!.Min.Should().Be(0.5m);
            range.Max.Should().Be(4.5m);
            var exclusive = model.Properties.Single(p => p.OriginalName == "exclusive").Constraints;
            exclusive.Should().NotBeNull();
            exclusive!.ExclusiveMin.Should().Be(1.5m);
            exclusive.ExclusiveMax.Should().Be(3.5m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }
}
