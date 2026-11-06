using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using ValIO.Parsers.Interfaces;

namespace ValIO.Parsers.Services;

/// <summary>Loads validated OpenAPI documents from JSON or YAML files, URLs, and streams.</summary>
public class OpenApiDocumentParser : IOpenApiDocumentParser
{
    /// <summary>Creates the HTTP clients used to retrieve remote descriptions.</summary>
    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>Reports source locations and reader diagnostics.</summary>
    private readonly ILogger<OpenApiDocumentParser> _logger;

    /// <summary>Initializes the parser with shared HTTP and diagnostic services.</summary>
    /// <param name="httpClientFactory">The factory used for HTTP and HTTPS sources.</param>
    /// <param name="logger">The destination for parse errors and warnings.</param>
    /// <exception cref="ArgumentNullException">A required service is null.</exception>
    public OpenApiDocumentParser(IHttpClientFactory httpClientFactory, ILogger<OpenApiDocumentParser> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<OpenApiDocument> ParseFromFileAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"OpenAPI file not found: {filePath}", filePath);

        var format = DetectFormat(filePath);
        _logger.LogInformation("Parsing OpenAPI file: {FilePath}, Format: {Format}", filePath, format);
        await using var stream = File.OpenRead(filePath);
        return await ParseFromStreamAsync(stream, format);
    }

    /// <inheritdoc/>
    public async Task<OpenApiDocument> ParseFromUrlAsync(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL: {url}", nameof(url));

        if (uri.IsFile)
            return await ParseFromFileAsync(uri.LocalPath);

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"Unsupported OpenAPI source scheme: {uri.Scheme}", nameof(url));

        _logger.LogInformation("Downloading OpenAPI document from: {Url}", url);
        var httpClient = _httpClientFactory.CreateClient();
        using var response = await httpClient.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        var format = DetectFormatFromContentType(response.Content.Headers.ContentType?.MediaType)
            ?? DetectFormat(uri.AbsolutePath);
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await ParseFromStreamAsync(stream, format);
    }

    /// <inheritdoc/>
    public async Task<OpenApiDocument> ParseFromStreamAsync(Stream stream, string? format)
    {
        ArgumentNullException.ThrowIfNull(stream);

        // Buffer only when content sniffing needs to replay a forward-only source.
        if (string.IsNullOrWhiteSpace(format) && !stream.CanSeek)
        {
            await using var buffered = new MemoryStream();
            await stream.CopyToAsync(buffered);
            buffered.Position = 0;
            return await ParseFromStreamAsync(buffered, null);
        }

        format = string.IsNullOrWhiteSpace(format)
            ? DetectFormatFromStream(stream)
            : format.Trim().ToLowerInvariant();
        if (format == "yml")
            format = "yaml";
        if (format is not ("json" or "yaml"))
            throw new NotSupportedException($"Unsupported OpenAPI document format: {format}");

        var settings = new OpenApiReaderSettings { LeaveStreamOpen = true };
        settings.AddYamlReader();
        var readResult = await OpenApiDocument.LoadAsync(stream, format, settings);

        if (readResult.Diagnostic?.Warnings.Count > 0)
        {
            var warnings = string.Join(", ", readResult.Diagnostic.Warnings.Select(w => w.Message));
            _logger.LogWarning("OpenAPI parsing warnings: {Warnings}", warnings);
        }

        var unresolved = new UnresolvedReferenceVisitor();
        if (readResult.Document is not null)
            new OpenApiWalker(unresolved).Walk(readResult.Document);

        // A non-null document may be incomplete after recoverable reader errors.
        if (readResult.Document is null || readResult.Diagnostic?.Errors.Count > 0 || unresolved.Errors.Count > 0)
        {
            var errors = string.Join(", ", (readResult.Diagnostic?.Errors.Select(e => $"{e.Pointer}: {e.Message}")
                ?? Enumerable.Empty<string>()).Concat(unresolved.Errors));
            if (errors.Length == 0)
                errors = "Unknown error";
            _logger.LogError("OpenAPI parsing errors: {Errors}", errors);
            throw new InvalidOperationException($"Failed to parse OpenAPI document: {errors}");
        }

        return readResult.Document;
    }

    /// <summary>Maps known file suffixes to readers and leaves other names to content detection.</summary>
    /// <param name="filePath">The local path or URL path whose suffix is inspected.</param>
    /// <returns>The reader format, or null when the suffix is inconclusive.</returns>
    private static string? DetectFormat(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".json" => "json",
        ".yaml" or ".yml" => "yaml",
        _ => null
    };

    /// <summary>Recognizes JSON and YAML HTTP media types without guessing unknown types.</summary>
    /// <param name="contentType">The response media type, if supplied.</param>
    /// <returns>The reader format, or null when content detection is required.</returns>
    private static string? DetectFormatFromContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return null;
        if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            return "json";
        return contentType.Contains("yaml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("yml", StringComparison.OrdinalIgnoreCase) ? "yaml" : null;
    }

    /// <summary>Inspects the unread stream prefix while preserving its current position.</summary>
    /// <param name="stream">The seekable input positioned at the description's first byte.</param>
    /// <returns>The JSON or YAML reader format.</returns>
    /// <exception cref="IOException">The source cannot be read or repositioned.</exception>
    private static string DetectFormatFromStream(Stream stream)
    {
        var position = stream.Position;
        try
        {
            var buffer = new byte[1024];
            var bytesRead = stream.Read(buffer, 0, buffer.Length);
            var content = Encoding.UTF8.GetString(buffer, 0, bytesRead).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
            return content.StartsWith('{') || content.StartsWith('[') ? "json" : "yaml";
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>Finds unresolved references using typed reader state instead of diagnostic message wording.</summary>
    private sealed class UnresolvedReferenceVisitor : OpenApiVisitorBase
    {
        /// <summary>Gets unresolved reference locations that would weaken a generated contract.</summary>
        /// <value>The collected reference errors.</value>
        public List<string> Errors { get; } = new();

        /// <inheritdoc/>
        public override void Visit(IOpenApiReferenceHolder referenceHolder)
        {
            // Operation tags are names, not $ref values; their root metadata declarations are optional.
            if (referenceHolder is OpenApiTagReference)
                return;
            if (!referenceHolder.UnresolvedReference)
                return;
            var reference = referenceHolder is OpenApiSchemaReference schema
                ? schema.Reference.ReferenceV3 : PathString;
            Errors.Add($"{PathString}: Unresolved OpenAPI reference '{reference}'.");
        }
    }
}
