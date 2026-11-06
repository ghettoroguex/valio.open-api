namespace ValIO.Tests.Fixtures;

/// <summary>Reads explicit OpenAPI fixture documents copied beside each test assembly.</summary>
/// <remarks>The fixture location is independent of the working directory and source checkout.</remarks>
internal static class OpenApiFixture
{
    /// <summary>Loads a fixture document without changing its schemas or specification version.</summary>
    /// <param name="relativePath">The slash-separated path relative to the shared OpenAPI fixture directory.</param>
    /// <returns>The fixture contents exactly as stored on disk.</returns>
    /// <exception cref="ArgumentException">The path is empty, absolute, or contains a parent-directory segment.</exception>
    /// <exception cref="FileNotFoundException">The fixture was not copied to the test output directory.</exception>
    /// <exception cref="IOException">The fixture cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Reading the fixture is not permitted.</exception>
    public static string Read(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var segments = relativePath.Replace('\\', '/').Split('/');
        if (Path.IsPathRooted(relativePath) || segments.Contains("..", StringComparer.Ordinal))
        {
            throw new ArgumentException("A fixture path must stay within Fixtures/OpenApi.", nameof(relativePath));
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "OpenApi", Path.Combine(segments));
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"OpenAPI fixture '{relativePath}' is missing. Build the test project to copy its shared fixtures.", path);
        }

        return File.ReadAllText(path);
    }
}
