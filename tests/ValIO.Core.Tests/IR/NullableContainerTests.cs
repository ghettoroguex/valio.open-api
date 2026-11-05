using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;

namespace ValIO.Core.Tests.IR;

/// <summary>Checks that container nullability remains distinct from element and property optionality.</summary>
[TestFixture]
public sealed class NullableContainerTests
{
    /// <summary>Preserves nullable arrays and dictionaries in both OpenAPI dialects.</summary>
    /// <param name="version">The OpenAPI version under test.</param>
    /// <param name="arraySchema">The nullable array schema.</param>
    /// <param name="dictionarySchema">The nullable dictionary schema.</param>
    /// <returns>A task that completes after nullable containers are checked in the parsed document.</returns>
    [TestCase("3.0.3", "{\"type\":\"array\",\"nullable\":true,\"items\":{\"type\":\"string\"}}", "{\"type\":\"object\",\"nullable\":true,\"additionalProperties\":{\"type\":\"string\"}}")]
    [TestCase("3.1.0", "{\"type\":[\"array\",\"null\"],\"items\":{\"type\":\"string\"}}", "{\"type\":[\"object\",\"null\"],\"additionalProperties\":{\"type\":\"string\"}}")]
    public async Task Nullable_containers_keep_their_inner_type(string version, string arraySchema, string dictionarySchema)
    {
        var holder = "{\"type\":\"object\",\"required\":[\"items\"],\"properties\":{\"items\":" + arraySchema + ",\"lookup\":" + dictionarySchema + "}}";
        var model = await IrTestHarness.BuildSingleModelAsync("Holder", holder, version);
        var items = model.Properties.Single(p => p.OriginalName == "items");
        items.IsRequired.Should().BeTrue();
        items.Type.Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrArrayType>();
        var lookup = model.Properties.Single(p => p.OriginalName == "lookup");
        lookup.IsRequired.Should().BeFalse();
        lookup.Type.Should().BeOfType<IrNullableType>().Which.InnerType.Should().BeOfType<IrDictionaryType>();
    }
}
