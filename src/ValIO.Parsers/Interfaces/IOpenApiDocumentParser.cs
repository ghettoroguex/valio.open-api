using Microsoft.OpenApi;

namespace ValIO.Parsers.Interfaces;

/// <summary>Loads OpenAPI descriptions and rejects reader errors before generation can proceed.</summary>
public interface IOpenApiDocumentParser
{
    /// <summary>Reads a local JSON or YAML OpenAPI description.</summary>
    /// <param name="filePath">The path of the source description.</param>
    /// <returns>A document without reader errors.</returns>
    /// <exception cref="ArgumentException">The source path is empty.</exception>
    /// <exception cref="FileNotFoundException">The source file does not exist.</exception>
    /// <exception cref="InvalidOperationException">The source has OpenAPI reader errors.</exception>
    Task<OpenApiDocument> ParseFromFileAsync(string filePath);

    /// <summary>Reads a file URI or downloads an HTTP or HTTPS OpenAPI description.</summary>
    /// <param name="url">The absolute source URL.</param>
    /// <returns>A document without reader errors.</returns>
    /// <exception cref="ArgumentException">The URL is invalid or uses an unsupported scheme.</exception>
    /// <exception cref="HttpRequestException">The source cannot be downloaded successfully.</exception>
    /// <exception cref="InvalidOperationException">The source has OpenAPI reader errors.</exception>
    Task<OpenApiDocument> ParseFromUrlAsync(string url);

    /// <summary>Reads the remaining bytes of a borrowed stream without closing the caller's stream.</summary>
    /// <param name="stream">The source, positioned at the description's first byte.</param>
    /// <param name="format">JSON or YAML; null or whitespace enables content detection.</param>
    /// <returns>A document without reader errors.</returns>
    /// <exception cref="ArgumentNullException">The source stream is null.</exception>
    /// <exception cref="NotSupportedException">The requested serialization format is unsupported.</exception>
    /// <exception cref="InvalidOperationException">The source has OpenAPI reader errors.</exception>
    Task<OpenApiDocument> ParseFromStreamAsync(Stream stream, string? format);
}
