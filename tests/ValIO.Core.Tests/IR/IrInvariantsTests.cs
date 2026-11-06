using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR.Types;
using ValIO.Core.Tests.TestHelpers;
using ValIO.Tests.Fixtures;

namespace ValIO.Core.Tests.IR;

/// <summary>Guards shared IR composition cardinality and inline-model reachability across all generators.</summary>
[TestFixture]
public class IrInvariantsTests
{
    /// <summary>Ensures normalized IR compositions never retain a redundant single-member wrapper.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task BuiltIr_HasNoSingleArmIntersectionOrUnion()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/composition-invariants-3.0.json"));

        foreach (var type in IrTypeWalker.AllTypes(ir))
        {
            if (type is IrIntersectionType i)
                i.Types.Count.Should().BeGreaterThan(1, "an IrIntersectionType must never have <2 members");
            if (type is IrUnionType u)
                u.Types.Count.Should().BeGreaterThan(1, "an IrUnionType must never have <2 members");
        }
    }

    /// <summary>Ensures every allocated inline model is referenced by another declaration.</summary>
    /// <returns>The asynchronous assertion operation.</returns>
    [Test]
    public async Task BuiltIr_HasNoOrphanModels()
    {
        var ir = await IrTestHarness.BuildIrAsync(OpenApiFixture.Read("CoreIR/composition-invariants-3.0.json"));

        // Every model is either reachable from another model (dependency edge) or a top-level
        // component schema. Phase C must not leave behind inline models with no incoming reference.
        var referenced = new HashSet<string>();
        foreach (var kvp in ir.TypeDependencies.Dependencies)
            foreach (var dep in kvp.Value)
                referenced.Add(dep);

        // Inline models are named like "<Prop>InlineModel"/"InlineModelN"; assert none are orphaned.
        var inlineModels = ir.Models.Where(m => m.Name.Contains("InlineModel", StringComparison.Ordinal));
        foreach (var inline in inlineModels)
            referenced.Should().Contain(inline.Name,
                $"inline model '{inline.Name}' must be referenced by something (no orphans after Phase C)");
    }
}
