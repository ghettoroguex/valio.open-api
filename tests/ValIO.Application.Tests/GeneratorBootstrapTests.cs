using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Application.DependencyInjection;
using ValIO.Core.Plugins;
using ValIO.Generators.Cpp;
using ValIO.Plugins.Bootstrap;

namespace ValIO.Application.Tests;

/// <summary>Checks the real front-end composition root for duplicate registrations and unusable plugin identities.</summary>
[TestFixture]
public sealed class GeneratorBootstrapTests
{
    /// <summary>Ensures the CLI bootstrap resolves exactly the supported language plugins and every alias.</summary>
    /// <param name="registrationCount">How often the idempotent composition extensions are invoked.</param>
    [TestCase(1)]
    [TestCase(2)]
    public void AddAllGeneratorPlugins_ResolvesTwelveUniquePlugins(int registrationCount)
    {
        var services = new ServiceCollection();
        for (var index = 0; index < registrationCount; index++)
            services.AddValIOCore().AddValIOApplication().AddAllGeneratorPlugins();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IGeneratorRegistry>();
        var plugins = provider.GetServices<IGeneratorPlugin>().ToArray();

        plugins.Should().HaveCount(12);
        plugins.Select(plugin => plugin.LanguageId).Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(12);
        registry.AllPlugins.Should().Equal(plugins);
        foreach (var plugin in plugins)
        {
            registry.FindPlugin(plugin.LanguageId.ToUpperInvariant()).Should().BeSameAs(plugin);
            foreach (var alias in plugin.LanguageAliases)
                registry.FindPlugin(alias.ToUpperInvariant()).Should().BeSameAs(plugin);
        }

        registry.FindPlugin("cpp-std").Should().BeSameAs(provider.GetRequiredService<CppGeneratorPlugin>());
    }

    /// <summary>Ensures repeated composition does not repeat internal plugin services or singleton factories.</summary>
    [Test]
    public void CompositionExtensions_LeaveServiceDescriptorsUnchangedWhenRepeated()
    {
        var services = new ServiceCollection();
        services.AddValIOCore().AddValIOApplication().AddAllGeneratorPlugins();
        var descriptors = services.ToArray();

        services.AddValIOCore().AddValIOApplication().AddAllGeneratorPlugins();

        services.Should().Equal(descriptors);
    }
}
