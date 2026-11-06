using System.Text.Json;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Models;
using ValIO.Generators.CSharp.Options;
using ValIO.Generators.CSharp.Services;
using ValIO.Generators.CSharp.Steps;

namespace ValIO.Generators.CSharp.Tests;

/// <summary>Checks that options have one typed representation and affect generated output.</summary>
[TestFixture]
public sealed class CSharpOptionsTests
{
    /// <summary>Preserves advanced options from JSON parsing through the generation step.</summary>
    [Test]
    public async Task ParsedOptions_AreConsumedByModelsStep()
    {
        var descriptor = new CSharpOptionsDescriptor();
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
            """{"propertyAccessor":"Init","useRequired":true,"generateEqualityMembers":false,"useSealed":true}""")!;
        var options = descriptor.ParseOptions(raw, out var errors);
        errors.Should().BeEmpty();
        var typed = options.Should().BeOfType<CSharpGenerationOptions>().Subject;
        typed.PropertyAccessor.Should().Be(PropertyAccessorType.Init);
        var context = new GenerationContext
        {
            PluginOptions = typed,
            Configuration = new GenerationConfiguration { Namespace = "Shared.Namespace" },
            IrDocument = new IrDocument
            {
                Metadata = new IrDocumentMetadata(),
                Models = [new IrModel
                {
                    Name = "Item",
                    Properties = [new IrProperty
                    {
                        Name = "Value", OriginalName = "value", IsRequired = true,
                        Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }
                    }]
                }],
                Enums = [], Clients = [],
                TypeDependencies = new IrTypeDependencyGraph
                {
                    Dependencies = new Dictionary<string, IReadOnlyList<string>>(), TopologicalOrder = ["Item"]
                }
            }
        };

        await new CSharpModelsStep(new CSharpNamingConverter(), new CSharpAstRenderer()).ExecuteAsync(context);

        var code = context.GeneratedFiles.Single().Content;
        code.Should().Contain("namespace Shared.Namespace");
        code.Should().Contain("sealed class Item").And.Contain("required string Value { get; init; }");
        code.Should().NotContain("IEquatable");
    }

    /// <summary>Rejects unsupported and malformed options instead of silently ignoring them.</summary>
    /// <param name="json">The invalid or unimplemented option payload.</param>
    [TestCase("{\"modelType\":\"Record\"}")]
    [TestCase("{\"queryParametersStyle\":\"ModelClass\"}")]
    [TestCase("{\"nullableReferenceTypes\":false}")]
    [TestCase("{\"nullableActivity\":false}")]
    [TestCase("{\"clientAccessModifier\":\"Private\"}")]
    [TestCase("{\"generateEqualityMembers\":\"false\"}")]
    [TestCase("{\"propertyAccessor\":\"Unknown\"}")]
    [TestCase("{\"misspelledOption\":true}")]
    public void UnsupportedOptions_ProduceActionableErrors(string json)
    {
        var raw = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

        var options = new CSharpOptionsDescriptor().ParseOptions(raw, out var errors);

        options.Should().BeNull();
        errors.Should().NotBeEmpty();
    }

    /// <summary>Enforces the same unsupported-mode contract for direct AST builder consumers.</summary>
    [Test]
    public void DirectBuilder_RejectsUnsupportedModelKinds()
    {
        var construct = () => new CSharpAstBuilder(new CSharpNamingConverter(), CSharpDocumentFixture.Context([], []),
            new CSharpGenerationOptions { ModelType = ModelType.Record });

        construct.Should().Throw<ArgumentException>().WithMessage("*modelType=Class*");
    }
}
