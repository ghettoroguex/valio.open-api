namespace ValIO.Core.Plugins;

/// <summary>Resolves language identifiers and aliases to a single generator plugin.</summary>
/// <remarks>Identifiers are case-insensitive, and distinct plugins cannot claim the same identifier.</remarks>
public sealed class GeneratorRegistry : IGeneratorRegistry
{
    /// <summary>Stores plugins in the caller's registration order.</summary>
    private readonly IReadOnlyList<IGeneratorPlugin> _plugins;

    /// <summary>Maps every validated language identifier and alias to its owning plugin.</summary>
    private readonly Dictionary<string, IGeneratorPlugin> _byId;

    /// <inheritdoc />
    public IReadOnlyList<IGeneratorPlugin> AllPlugins => _plugins;

    /// <summary>Creates a registry only after every plugin identifier and alias has been validated.</summary>
    /// <param name="plugins">Plugins to register, including any repeated registrations of the same instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="plugins"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Distinct plugins claim the same identifier or alias.</exception>
    /// <remarks>A plugin may repeat its own identifier or alias, including with different letter casing.</remarks>
    public GeneratorRegistry(IEnumerable<IGeneratorPlugin> plugins)
    {
        ArgumentNullException.ThrowIfNull(plugins);

        var registeredPlugins = plugins.ToArray();
        var byId = new Dictionary<string, IGeneratorPlugin>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in registeredPlugins)
        {
            var identifiers = plugin.LanguageAliases
                .Prepend(plugin.LanguageId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var identifier in identifiers)
            {
                if (byId.TryGetValue(identifier, out var existing) && !ReferenceEquals(existing, plugin))
                {
                    throw new InvalidOperationException(
                        $"Generator identifier '{identifier}' is claimed by both '{existing.LanguageId}' and '{plugin.LanguageId}'.");
                }
            }

            foreach (var identifier in identifiers)
                byId.TryAdd(identifier, plugin);
        }

        _plugins = registeredPlugins;
        _byId = byId;
    }

    /// <summary>Finds the plugin registered for a language identifier or alias.</summary>
    /// <param name="languageId">The case-insensitive identifier or alias to resolve.</param>
    /// <returns>The owning plugin, or <see langword="null"/> when the identifier is not registered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="languageId"/> is <see langword="null"/>.</exception>
    public IGeneratorPlugin? FindPlugin(string languageId) =>
        _byId.TryGetValue(languageId, out var p) ? p : null;
}
