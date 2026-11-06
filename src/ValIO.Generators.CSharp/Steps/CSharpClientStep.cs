using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Builders;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Options;
using ValIO.Generators.CSharp.Models;

namespace ValIO.Generators.CSharp.Steps;

/// <summary>
/// Generates typed C# HTTP clients and their shared runtime support files from the normalized IR.
/// </summary>
public sealed class CSharpClientStep : ICodeGenerationStep
{
    /// <summary>Converts wire and schema names into valid C# identifiers.</summary>
    private readonly INamingConverter _namingConverter;

    /// <summary>Renders the generated C# abstract syntax tree.</summary>
    private readonly ICSharpAstRenderer _renderer;

    /// <inheritdoc/>
    public string Name => "csharp-client";
    /// <inheritdoc/>
    public int Order => 40;

    /// <summary>Initializes the client generation step.</summary>
    /// <param name="namingConverter">The converter used for generated identifiers.</param>
    /// <param name="renderer">The renderer used for C# source output.</param>
    public CSharpClientStep(INamingConverter namingConverter, ICSharpAstRenderer renderer)
    {
        _namingConverter = namingConverter;
        _renderer = renderer;
    }

    /// <inheritdoc/>
    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0;

    /// <inheritdoc/>
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as CSharpGenerationOptions;
        var ns = opts?.Namespace ?? context.Configuration.Namespace ?? "Generated";
        var builder = new CSharpAstBuilder(_namingConverter, context.DocumentContext, opts);

        if (opts?.GenerateMetrics == true && context.IrDocument.Clients.Count > 0)
        {
            context.GeneratedFiles.Add(new GeneratedFile("Api/IHttpClientMetrics.cs",
                _renderer.Render(builder.BuildCompilationUnitForIHttpClientMetrics(ns))));
            context.GeneratedFiles.Add(new GeneratedFile("Api/HttpClientMetrics.cs",
                _renderer.Render(builder.BuildCompilationUnitForHttpClientMetrics(ns))));
        }

        foreach (var client in context.IrDocument.Clients)
        {
            var compilationUnit = builder.BuildCompilationUnit(client, ns);
            var content = _renderer.Render(compilationUnit);
            var clientName = string.IsNullOrWhiteSpace(client.Name) ? "ApiClient" : client.Name;
            var relativePath = $"Api/{clientName}Client.cs";
            context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));

            if (opts?.GenerateInterfaces == true && opts.InterfaceFileStrategy == InterfaceFileStrategy.SeparateFile)
                context.GeneratedFiles.Add(new GeneratedFile($"Api/I{clientName}.cs",
                    _renderer.Render(builder.BuildCompilationUnitForInterface(client, ns))));
            if (opts?.GenerateTraces == true || opts?.GenerateMetrics == true)
                context.GeneratedFiles.Add(new GeneratedFile($"Api/{clientName}OpenTelemetryDecorator.cs",
                    _renderer.Render(builder.BuildCompilationUnitForDecorator(client, ns))));
        }

        if (context.IrDocument.Clients.SelectMany(client => client.Operations).Any(operation =>
            operation.PathParameters.Count > 0 || operation.QueryParameters.Count > 0 ||
            operation.HeaderParameters.Count > 0 || operation.RequestBody?.IsMultipartFormData == true))
        {
            context.GeneratedFiles.Add(new GeneratedFile("Api/HttpWireValueFormatter.cs", BuildHttpWireValueFormatter(ns)));
        }

        // Generated clients share one ApiException declaration per output project.
        if (context.IrDocument.Clients.Count > 0)
        {
            var apiExceptionUnit = builder.BuildCompilationUnitApiException(ns);
            var apiExceptionContent = _renderer.Render(apiExceptionUnit);
            context.GeneratedFiles.Add(new GeneratedFile("Api/ApiException.cs", apiExceptionContent));
        }

        if (context.IrDocument.Clients
            .SelectMany(client => client.Operations)
            .Any(operation => operation.RequestBody?.IsMultipartFormData == true))
        {
            context.GeneratedFiles.Add(new GeneratedFile(
                "Api/MultipartFormDataContentFactory.cs",
                BuildMultipartFormDataContentFactory(ns)));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Builds the source for the runtime helper that maps a typed request model to multipart content.
    /// </summary>
    /// <param name="ns">The generated project namespace.</param>
    /// <returns>The complete C# source file.</returns>
    private static string BuildMultipartFormDataContentFactory(string ns) => $$"""
        using System;
        using System.Collections;
        using System.Globalization;
        using System.IO;
        using System.Net.Http;
        using System.Net.Http.Headers;
        using System.Reflection;
        using System.Text;
        using System.Text.Json.Serialization;

        namespace {{ns}};

        /// <summary>Builds multipart HTTP content from a generated typed request model.</summary>
        internal static class MultipartFormDataContentFactory
        {
            /// <summary>Creates form-data parts using generated JSON property names as wire names.</summary>
            /// <typeparam name="TRequest">The generated request model type.</typeparam>
            /// <param name="request">The populated typed request model.</param>
            /// <returns>Multipart content owned by the generated HTTP request.</returns>
            /// <exception cref="ArgumentNullException">Thrown when <paramref name="request"/> is null.</exception>
            internal static MultipartFormDataContent Create<TRequest>(TRequest request)
            {
                ArgumentNullException.ThrowIfNull(request);
                var content = new MultipartFormDataContent();
                foreach (var property in typeof(TRequest).GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    var value = property.GetValue(request);
                    if (value is null)
                    {
                        continue;
                    }

                    var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
                    AddValue(content, name, value);
                }

                return content;
            }

            /// <summary>Adds one scalar, file, or repeated value to the multipart payload.</summary>
            /// <param name="content">The multipart payload being assembled.</param>
            /// <param name="name">The wire-level form field name.</param>
            /// <param name="value">The non-null field value.</param>
            private static void AddValue(MultipartFormDataContent content, string name, object value)
            {
                if (value is IEnumerable values and not string and not byte[] and not Stream)
                {
                    foreach (var item in values)
                    {
                        if (item is not null)
                        {
                            AddValue(content, name, item);
                        }
                    }

                    return;
                }

                HttpContent part;
                var isFile = false;
                switch (value)
                {
                    case Stream stream:
                        part = new StreamContent(stream);
                        isFile = true;
                        break;
                    case byte[] bytes:
                        part = new ByteArrayContent(bytes);
                        isFile = true;
                        break;
                    default:
                        var text = HttpWireValueFormatter.Format(value);
                        part = new StringContent(text, Encoding.UTF8);
                        break;
                }

                if (isFile)
                {
                    part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    content.Add(part, name, name);
                }
                else
                {
                    content.Add(part, name);
                }
            }

        }
        """;

    /// <summary>Builds the shared formatter used by URL, header, and multipart scalar values.</summary>
    /// <param name="ns">The generated project namespace.</param>
    /// <returns>A standalone runtime helper honoring enum wire names and invariant formats.</returns>
    private static string BuildHttpWireValueFormatter(string ns) => $$"""
        using System;
        using System.Globalization;
        using System.Reflection;
        using System.Text.Json.Serialization;

        namespace {{ns}};

        /// <summary>Formats scalar HTTP values consistently across URL, header, and form transports.</summary>
        internal static class HttpWireValueFormatter
        {
            /// <summary>Formats one scalar using its invariant wire representation.</summary>
            /// <param name="value">The scalar value, or null.</param>
            /// <returns>The invariant text before transport-specific escaping.</returns>
            /// <exception cref="NotSupportedException">The value is not a supported HTTP scalar.</exception>
            /// <exception cref="ArgumentOutOfRangeException">An enum value is outside its declared members.</exception>
            internal static string Format(object? value) => value switch
            {
                null => string.Empty,
                string text => text,
                bool boolean => boolean ? "true" : "false",
                DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeOnly time => time.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture),
                DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
                DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
                Enum enumValue => FormatEnum(enumValue),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => throw new NotSupportedException($"The HTTP scalar type {value.GetType().Name} is not supported."),
            };

            /// <summary>Uses declared string wire names and underlying numeric values for enum fields.</summary>
            /// <param name="value">The declared enum member.</param>
            /// <returns>The original schema value.</returns>
            /// <exception cref="ArgumentOutOfRangeException">The enum value has no declared member.</exception>
            private static string FormatEnum(Enum value)
            {
                var type = value.GetType();
                var memberName = Enum.GetName(type, value)
                    ?? throw new ArgumentOutOfRangeException(nameof(value), value, "The enum value is not declared by the schema.");
                var wireName = type.GetField(memberName)?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;
                return wireName ?? Convert.ToString(
                    Convert.ChangeType(value, Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }
        """;
}
