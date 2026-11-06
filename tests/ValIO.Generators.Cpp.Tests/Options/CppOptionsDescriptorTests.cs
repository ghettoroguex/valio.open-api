using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Options;

namespace ValIO.Generators.Cpp.Tests.Options;

[TestFixture]
public class CppOptionsDescriptorTests
{
    private readonly CppOptionsDescriptor _descriptor = new();

    private static Dictionary<string, JsonElement> ParseRaw(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private CppGenerationOptions Parse(string json) =>
        (CppGenerationOptions)_descriptor.ParseOptions(ParseRaw(json), out _)!;

    [Test]
    public void EmptyOptions_ReturnsDefaults()
    {
        var opts = (CppGenerationOptions)_descriptor.ParseOptions([], out var errors)!;

        errors.Should().BeEmpty();
        opts.Runtime.Should().Be(CppRuntimeProfile.Ue);
        opts.CxxStandard.Should().Be(17);
        opts.ApiStyle.Should().Be(CppApiStyle.Sync);
        opts.ClientShape.Should().Be(CppClientShape.Class);
        opts.Namespace.Should().BeNull();
        opts.HttpInclude.Should().Be("httplib.h");
        opts.JsonInclude.Should().Be("nlohmann/json.hpp");
        opts.JsonFwdInclude.Should().Be("nlohmann/json_fwd.hpp");
        opts.ErrorModelName.Should().Be("RestApiErrorResponse");
        opts.EmitCmake.Should().BeFalse();
    }

    [TestCase("std", CppRuntimeProfile.Std)]
    [TestCase("STD", CppRuntimeProfile.Std)]
    [TestCase("cpp-httplib", CppRuntimeProfile.Std)]
    [TestCase("httplib", CppRuntimeProfile.Std)]
    [TestCase("ue", CppRuntimeProfile.Ue)]
    [TestCase("unreal", CppRuntimeProfile.Ue)]
    public void Runtime_AcceptsCanonicalValuesAndAliases(string raw, CppRuntimeProfile expected)
        => Parse($$$"""{"runtime":"{{{raw}}}"}""").Runtime.Should().Be(expected);

    [Test]
    public void Runtime_Unknown_AddsError()
    {
        var result = _descriptor.ParseOptions(ParseRaw("""{"runtime":"qt"}"""), out var errors);
        errors.Should().ContainSingle(e => e.Contains("runtime") && e.Contains("qt"));
        result.Should().BeOfType<CppGenerationOptions>();
    }

    [TestCase(17)]
    [TestCase(20)]
    [TestCase(23)]
    public void CxxStandard_AcceptsSupportedNumbers(int standard)
        => Parse($$$"""{"cxxStandard":{{{standard}}}}""").CxxStandard.Should().Be(standard);

    [Test]
    public void CxxStandard_AcceptsStringNumber()
        => Parse("""{"cxxStandard":"20"}""").CxxStandard.Should().Be(20);

    [Test]
    public void CxxStandard_NonNumeric_AddsError()
    {
        _descriptor.ParseOptions(ParseRaw("""{"cxxStandard":true}"""), out var errors);
        errors.Should().ContainSingle(e => e.Contains("cxxStandard"));
    }

    [TestCase(14)]
    [TestCase(16)]
    [TestCase(26)]
    public void Validate_CxxStandard_RejectsUnsupportedValues(int standard)
    {
        var errors = _descriptor.ValidateOptions(new CppGenerationOptions { CxxStandard = standard });
        errors.Should().ContainSingle(e => e.Contains("cxxStandard") && e.Contains("17, 20 or 23"));
    }

    [TestCase("sync", CppApiStyle.Sync)]
    [TestCase("async", CppApiStyle.Async)]
    [TestCase("both", CppApiStyle.Both)]
    public void ApiStyle_AcceptsCanonicalValues(string raw, CppApiStyle expected)
        => Parse($$$"""{"apiStyle":"{{{raw}}}"}""").ApiStyle.Should().Be(expected);

    [Test]
    public void ApiStyle_Unknown_AddsError()
    {
        _descriptor.ParseOptions(ParseRaw("""{"apiStyle":"coroutines"}"""), out var errors);
        errors.Should().ContainSingle(e => e.Contains("apiStyle") && e.Contains("coroutines"));
    }

    [TestCase("class", CppClientShape.Class)]
    [TestCase("interface+impl", CppClientShape.InterfaceImpl)]
    [TestCase("interfaceimpl", CppClientShape.InterfaceImpl)]
    public void ClientShape_AcceptsCanonicalValues(string raw, CppClientShape expected)
        => Parse($$$"""{"clientShape":"{{{raw}}}"}""").ClientShape.Should().Be(expected);

    [Test]
    public void Namespace_IsTrimmed()
        => Parse("""{"namespace":"  Petstore  "}""").Namespace.Should().Be("Petstore");

    [TestCase("Pet Store")]
    [TestCase("1Petstore")]
    [TestCase("petstore.api")]
    public void Validate_Namespace_RejectsNonIdentifiers(string ns)
    {
        var errors = _descriptor.ValidateOptions(new CppGenerationOptions { Namespace = ns });
        errors.Should().ContainSingle(e => e.Contains("namespace"));
    }

    [Test]
    public void EmitCmake_AcceptsBool_AndRejectsOtherTypes()
    {
        Parse("""{"emitCmake":true}""").EmitCmake.Should().BeTrue();
        _descriptor.ParseOptions(ParseRaw("""{"emitCmake":"yes"}"""), out var errors);
        errors.Should().ContainSingle(e => e.Contains("emitCmake"));
    }

    [Test]
    public void IncludePaths_AreOverridable()
    {
        var opts = Parse("""{"httpInclude":"wrappers/http.h","jsonInclude":"wrappers/json.hpp"}""");
        opts.HttpInclude.Should().Be("wrappers/http.h");
        opts.JsonInclude.Should().Be("wrappers/json.hpp");
        opts.JsonFwdInclude.Should().Be("nlohmann/json_fwd.hpp"); // не задан — default
    }

    [Test]
    public void GetOptionDescriptions_CoversAllOptions()
    {
        var names = _descriptor.GetOptionDescriptions().Select(d => d.Name).ToList();

        names.Should().Contain(new[]
        {
            "runtime", "cxxStandard", "apiStyle", "clientShape", "namespace",
            "httpInclude", "jsonInclude", "jsonFwdInclude", "errorModelName", "emitCmake",
        });
    }

    /// <summary>Checks the merged descriptor retains Unreal Engine configuration alongside standalone options.</summary>
    [Test]
    public void UnrealOptions_ArePreservedBySharedDescriptor()
    {
        var options = Parse("""{"moduleApiMacro":"GAME_API","blueprintCategory":"Game","addUnrealEngineIncludesInHeaders":false,"baseAsyncActionClassName":"UCustomAsync"}""");
        options.ModuleApiMacro.Should().Be("GAME_API");
        options.BlueprintCategory.Should().Be("Game");
        options.AddUnrealEngineIncludesInHeaders.Should().BeFalse();
        options.BaseAsyncActionClassName.Should().Be("UCustomAsync");
    }

    /// <summary>Checks definition-only generation has a typed option that is visible in configuration metadata.</summary>
    [Test]
    public void EmitSerializers_ParsesBooleanAndRejectsOtherKinds()
    {
        Parse("""{"emitSerializers":false}""").EmitSerializers.Should().BeFalse();
        _descriptor.ParseOptions(ParseRaw("""{"emitSerializers":"no"}"""), out var errors);
        errors.Should().ContainSingle(error => error.Contains("emitSerializers", StringComparison.Ordinal));
        _descriptor.GetOptionDescriptions().Should().Contain(option => option.Name == "emitSerializers");
    }
}
