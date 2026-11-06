using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.Plugins;

namespace ValIO.Core.Tests.Plugins;

/// <summary>Verifies unambiguous and atomic generator identifier registration.</summary>
[TestFixture]
public class GeneratorRegistryTests
{
    /// <summary>Ensures primary identifiers and aliases use the same case-insensitive lookup rules.</summary>
    [Test]
    public void FindPlugin_ResolvesIdentifiersAndAliasesCaseInsensitively()
    {
        var cpp = new TestPlugin("cpp", "c++", "cpp-std");
        var typescript = new TestPlugin("typescript", "ts");

        var registry = new GeneratorRegistry([cpp, typescript]);

        registry.FindPlugin("CPP").Should().BeSameAs(cpp);
        registry.FindPlugin("C++").Should().BeSameAs(cpp);
        registry.FindPlugin("CPP-STD").Should().BeSameAs(cpp);
        registry.FindPlugin("TypeScript").Should().BeSameAs(typescript);
        registry.FindPlugin("TS").Should().BeSameAs(typescript);
        registry.FindPlugin("unknown").Should().BeNull();
        registry.AllPlugins.Should().Equal(cpp, typescript);
    }

    /// <summary>Ensures a plugin may list its own identifier and repeated aliases without colliding with itself.</summary>
    [Test]
    public void Constructor_AllowsOwnIdentifierAndRepeatedAliases()
    {
        var cpp = new TestPlugin("cpp", "cpp", "CPP", "c++", "C++");

        var registry = new GeneratorRegistry([cpp]);

        registry.FindPlugin("cpp").Should().BeSameAs(cpp);
        registry.FindPlugin("C++").Should().BeSameAs(cpp);
    }

    /// <summary>Ensures repeated registration of the same plugin instance remains supported.</summary>
    [Test]
    public void Constructor_AllowsRepeatedPluginInstance()
    {
        var cpp = new TestPlugin("cpp", "c++");

        var registry = new GeneratorRegistry([cpp, cpp]);

        registry.FindPlugin("CPP").Should().BeSameAs(cpp);
        registry.FindPlugin("c++").Should().BeSameAs(cpp);
        registry.AllPlugins.Should().Equal(cpp, cpp);
    }

    /// <summary>Ensures primary identifiers and aliases cannot shadow a different plugin in either direction.</summary>
    /// <param name="firstId">The first plugin's primary identifier.</param>
    /// <param name="firstAlias">The first plugin's alias.</param>
    /// <param name="secondId">The second plugin's primary identifier.</param>
    /// <param name="secondAlias">The second plugin's alias.</param>
    /// <param name="conflictingIdentifier">The conflicting spelling reported by the second plugin.</param>
    [TestCase("cpp", "c++", "CPP", "other", "CPP")]
    [TestCase("cpp", "shared", "typescript", "SHARED", "SHARED")]
    [TestCase("cpp", "typescript", "TypeScript", "ts", "TypeScript")]
    [TestCase("cpp", "c++", "typescript", "CPP", "CPP")]
    public void Constructor_RejectsCollisionsBetweenDistinctPlugins(
        string firstId,
        string firstAlias,
        string secondId,
        string secondAlias,
        string conflictingIdentifier)
    {
        var first = new TestPlugin(firstId, firstAlias);
        var second = new TestPlugin(secondId, secondAlias);

        Action create = () => new GeneratorRegistry([first, second]);

        create.Should().Throw<InvalidOperationException>()
            .WithMessage($"*'{conflictingIdentifier}'*'{firstId}'*'{secondId}'*");
    }

    /// <summary>Ensures an alias collision rejects the whole registry and leaves no successful partial result.</summary>
    [Test]
    public void Constructor_RejectsWholeRegistrationWhenLaterAliasCollides()
    {
        var first = new TestPlugin("cpp", "shared");
        var second = new TestPlugin("typescript", "ts", "SHARED");
        GeneratorRegistry? registry = null;

        Action create = () => registry = new GeneratorRegistry([first, second]);

        create.Should().Throw<InvalidOperationException>();
        registry.Should().BeNull();
        new GeneratorRegistry([first]).FindPlugin("shared").Should().BeSameAs(first);
        new GeneratorRegistry([second]).FindPlugin("ts").Should().BeSameAs(second);
    }

    /// <summary>Ensures a missing registration source reports the public argument name.</summary>
    [Test]
    public void Constructor_RejectsNullPluginSequence()
    {
        Action create = () => new GeneratorRegistry(null!);

        create.Should().Throw<ArgumentNullException>().WithParameterName("plugins");
    }

    /// <summary>Supplies plugin identity metadata without constructing language-specific generation services.</summary>
    /// <param name="languageId">The plugin's primary language identifier.</param>
    /// <param name="aliases">The aliases claimed by the plugin.</param>
    private sealed class TestPlugin(string languageId, params string[] aliases) : IGeneratorPlugin
    {
        /// <inheritdoc />
        public string LanguageId { get; } = languageId;

        /// <inheritdoc />
        public string DisplayName => LanguageId;

        /// <inheritdoc />
        public IReadOnlyList<string> LanguageAliases { get; } = aliases;

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Registry tests do not supply generation options.</exception>
        public IGeneratorOptionsDescriptor OptionsDescriptor => throw new NotSupportedException("Registry tests only use plugin identifiers.");

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Registry tests do not create generation pipelines.</exception>
        public ICodeGenerationPipeline CreatePipeline(IServiceProvider services) =>
            throw new NotSupportedException("Registry tests only use plugin identifiers.");

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Registry tests do not configure generation services.</exception>
        public void ConfigureServices(IServiceCollection services) =>
            throw new NotSupportedException("Registry tests only use plugin identifiers.");
    }
}
