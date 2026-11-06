using System.Diagnostics;
using System.Text;
using Microsoft.OpenApi;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Builders;
using ValIO.Core.Services;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Generators.CSharp.Ast.Renderers;
using ValIO.Generators.CSharp.Models;
using ValIO.Generators.CSharp.Options;
using ValIO.Generators.CSharp.Services;
using ValIO.Generators.CSharp.Steps;
using ValIO.Tests.Fixtures;

namespace ValIO.Generators.CSharp.Tests;

/// <summary>Compiles and executes generated C# in an isolated SDK project without repository references.</summary>
[TestFixture]
[NonParallelizable]
public sealed class CSharpGeneratedCompilationTests
{
    /// <summary>Compiles nullable references from real OpenAPI and exercises their System.Text.Json codecs.</summary>
    /// <returns>The asynchronous source generation, compiler and runtime verification.</returns>
    [Test]
    public async Task NullableReferenceUnionsCompileAndRoundTrip()
    {
        await using var input = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("CoreIR/nullable-reference-unions-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(input, "json");
        var ir = new IrBuilder(new SchemaResolver(parsed.Document!), new NamingConverter()).BuildFromOpenApi(parsed.Document!);
        var context = new GenerationContext
        {
            IrDocument = ir,
            Configuration = new GenerationConfiguration { Namespace = "Generated.Contracts" },
            PluginOptions = new CSharpGenerationOptions()
        };
        var naming = new CSharpNamingConverter();
        var renderer = new CSharpAstRenderer();
        await new CSharpModelsStep(naming, renderer).ExecuteAsync(context);
        await new CSharpEnumsStep(naming, renderer).ExecuteAsync(context);
        await new CSharpClientStep(naming, renderer).ExecuteAsync(context);
        var model = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith("NullableReferences.cs", StringComparison.Ordinal)).Content;
        model.Should().Contain("NullableCountry?").And.Contain("NullableState?");
        var directory = Path.Combine(Path.GetTempPath(), "ValIO.CSharp.Nullable", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "Smoke.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        foreach (var file in context.GeneratedFiles)
        {
            var destination = Path.Combine(directory, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, file.Content);
        }
        await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), """"
            using System.Text.Json;
            using Generated.Contracts;
            var empty = JsonSerializer.Deserialize<NullableReferences>("""{"country":null,"state":null,"countries":[null],"states":{"one":null}}""")!;
            var emptyJson = JsonSerializer.SerializeToElement(empty);
            if (emptyJson.GetProperty("country").ValueKind != JsonValueKind.Null || emptyJson.GetProperty("state").ValueKind != JsonValueKind.Null) throw new Exception("Null references were lost.");
            var populated = JsonSerializer.Deserialize<NullableReferences>("""{"country":{"id":7,"name":"DE"},"state":"granted","countries":[null,{"id":9,"name":"US"}],"states":{"one":"pending","two":null}}""")!;
            var json = JsonSerializer.SerializeToElement(populated);
            if (json.GetProperty("country").GetProperty("id").GetInt32() != 7 || json.GetProperty("state").GetString() != "granted" || json.GetProperty("countries")[1].GetProperty("id").GetInt32() != 9) throw new Exception("Structured references did not round-trip.");
            try { JsonSerializer.Deserialize<NullableReferences>("""{"country":"DE","state":null,"countries":[],"states":{}}"""); throw new Exception("A model was decoded as string."); } catch (JsonException) { }
            try { JsonSerializer.Deserialize<NullableReferences>("""{"country":null,"state":"unknown","countries":[],"states":{}}"""); throw new Exception("An invalid enum was accepted."); } catch (JsonException) { }
            Console.WriteLine("CSharp nullable references passed");
            """");
        var result = await RunGeneratedProject(directory);
        result.ExitCode.Should().Be(0, "generated nullable references must compile and round-trip. Sources: {0}\n{1}", directory, result.Output);
        result.Output.Should().Contain("CSharp nullable references passed");
    }

    /// <summary>Verifies generated models, enums, HTTP requests, interfaces, and telemetry compile together.</summary>
    /// <param name="traces">Whether the fixture generates tracing.</param>
    /// <param name="metrics">Whether the fixture generates metrics.</param>
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task GeneratedProject_CompilesAndPreservesWireContracts(bool traces, bool metrics)
    {
        var context = new GenerationContext
        {
            IrDocument = CreateDocument(),
            Configuration = new GenerationConfiguration { Namespace = "Generated.Contracts" },
            PluginOptions = new CSharpGenerationOptions
            {
                GenerateInterfaces = traces || metrics,
                InterfaceFileStrategy = InterfaceFileStrategy.SeparateFile,
                GenerateTraces = traces,
                GenerateMetrics = metrics,
                GenerateValidationAttributes = true,
                GenerateValidationConstants = true,
                UseSealed = true
            }
        };
        var naming = new CSharpNamingConverter();
        var renderer = new CSharpAstRenderer();
        await new CSharpModelsStep(naming, renderer).ExecuteAsync(context);
        await new CSharpEnumsStep(naming, renderer).ExecuteAsync(context);
        await new CSharpClientStep(naming, renderer).ExecuteAsync(context);

        var directory = Path.Combine(Path.GetTempPath(), "ValIO.CSharp.Generated", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "Smoke.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                  </PropertyGroup>
                </Project>
                """);
            await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.Config"),
                "<configuration><packageSources><clear /></packageSources></configuration>");
            foreach (var file in context.GeneratedFiles)
            {
                var destination = Path.Combine(directory, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllTextAsync(destination, file.Content);
            }
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "CSharpHttpPipeline", "BearerReplayHandler.cs"),
                Path.Combine(directory, "BearerReplayHandler.cs"));
            File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "HttpPipelineChecks.cs.txt"),
                Path.Combine(directory, "HttpPipelineChecks.cs"));
            var metricsSetup = metrics ? """
                long successfulRequests = 0;
                long failedRequests = 0;
                var recordedDurations = 0;
                using var metricsListener = new System.Diagnostics.Metrics.MeterListener();
                metricsListener.InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == "test") listener.EnableMeasurementEvents(instrument);
                };
                metricsListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
                {
                    if (instrument.Name == "requests.success") successfulRequests += measurement;
                    if (instrument.Name == "requests.error") failedRequests += measurement;
                });
                metricsListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) => recordedDurations++);
                metricsListener.Start();
                """ + Environment.NewLine : string.Empty;
            var telemetryChecks = traces || metrics
                ? "using var source = new System.Diagnostics.ActivitySource(\"test\");\n" +
                    metricsSetup +
                    (metrics ? "using var meter = new System.Diagnostics.Metrics.Meter(\"test\");\n" : string.Empty) +
                    "var decorated = new TransportApiTelemetryDecorator(client, source" +
                    (metrics ? ", new HttpClientMetrics(meter)" : string.Empty) + ");\n" + """
                    if (await decorated.Send("x", null, null, true, null, null, null, null, null, null, null, null, null) != 7)
                        throw new InvalidOperationException("Decorated typed response failed.");
                    await decorated.Ping();
                    handler.ReturnError = true;
                    try
                    {
                        await decorated.Ping();
                        throw new InvalidOperationException("Decorated HTTP error was not surfaced.");
                    }
                    catch (ApiException)
                    {
                    }
                    handler.ReturnError = false;
                    """ + Environment.NewLine + (metrics ? """
                    if (successfulRequests != 2 || failedRequests != 1 || recordedDurations != 3)
                        throw new InvalidOperationException("Telemetry did not record typed, bodyless, and failed calls exactly once.");
                    """ : string.Empty)
                : string.Empty;
            await File.WriteAllTextAsync(Path.Combine(directory, "Program.cs"), RuntimeProgram.Replace("// TELEMETRY_CHECKS", telemetryChecks, StringComparison.Ordinal));

            var result = await RunGeneratedProject(directory);

            result.ExitCode.Should().Be(0, "generated C# must compile and pass runtime checks:{0}{1}", Environment.NewLine, result.Output);
            result.Output.Should().Contain("CSharp generated runtime passed");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Executes the isolated generated program and captures compiler or runtime diagnostics.</summary>
    /// <param name="directory">The temporary project directory owned by this test.</param>
    /// <returns>The process exit code and combined diagnostic output.</returns>
    private static async Task<(int ExitCode, string Output)> RunGeneratedProject(string directory)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--project");
        start.ArgumentList.Add("Smoke.csproj");
        start.ArgumentList.Add("--verbosity");
        start.ArgumentList.Add("quiet");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException("Generated C# compilation exceeded two minutes.");
        }
        return (process.ExitCode, await output + await error);
    }

    /// <summary>Creates a cross-feature document with multiple bases, enum defaults, file uploads, and colliding names.</summary>
    /// <returns>The normalized IR used by every generated source step.</returns>
    private static IrDocument CreateDocument()
    {
        var text = Primitive(IrPrimitiveKind.String);
        var nullableText = new IrNullableType { InnerType = text };
        var state = new IrModelReference { ModelName = "State" };
        var models = new IrModel[]
        {
            new() { Name = "Named", Properties = [Property("name", "Name", nullableText)] },
            new() { Name = "Tagged", Properties = [Property("tag", "Tag", nullableText)] },
            new()
            {
                Name = "Combined",
                Composition = new IrComposition
                {
                    Kind = IrCompositionKind.AllOf,
                    Types = [new IrModelReference { ModelName = "Named" }, new IrModelReference { ModelName = "Tagged" }]
                },
                Properties =
                [
                    Property("message", "Message", text) with { Constraints = new IrPropertyConstraints { DefaultValue = "default!" } },
                    Property("state", "State", state) with { IsRequired = false, Constraints = new IrPropertyConstraints { DefaultValue = "in_progress" } },
                    Property("date", "Date", Primitive(IrPrimitiveKind.Date)),
                    Property("time", "Time", Primitive(IrPrimitiveKind.Time)),
                    Property("when", "When", Primitive(IrPrimitiveKind.DateTime)),
                    Property("quantity", "Quantity", Primitive(IrPrimitiveKind.Integer)) with
                    {
                        Constraints = new IrPropertyConstraints { Min = 1.5m, Max = 5.5m }
                    },
                    Property("longValue", "LongValue", Primitive(IrPrimitiveKind.Long)) with
                    {
                        Constraints = new IrPropertyConstraints { Min = 9007199254740993m, Max = long.MaxValue }
                    },
                    Property("exclusive", "Exclusive", Primitive(IrPrimitiveKind.Number)) with
                    {
                        Constraints = new IrPropertyConstraints { ExclusiveMin = 1m, ExclusiveMax = 2m }
                    },
                    Property("negative", "Negative", Primitive(IrPrimitiveKind.Integer)) with
                    {
                        Constraints = new IrPropertyConstraints { Max = 5m }
                    },
                    Property("codes", "Codes", new IrNullableType
                    {
                        InnerType = new IrArrayType { ElementType = new IrNullableType { InnerType = Primitive(IrPrimitiveKind.Integer) } }
                    })
                ]
            },
            new()
            {
                Name = "UploadForm",
                Properties =
                [
                    Property("content", "Content", Primitive(IrPrimitiveKind.File)),
                    Property("state", "State", state),
                    Property("amount", "Amount", Primitive(IrPrimitiveKind.Number)),
                    Property("enabled", "Enabled", Primitive(IrPrimitiveKind.Boolean))
                ]
            }
        };
        var operation = new IrOperation
        {
            MethodName = "Send",
            HttpMethod = "POST",
            Path = "/files/{path-id}",
            PathParameters = [new IrParameter { Name = "path_id2", OriginalName = "path-id", Type = text, IsRequired = true }],
            QueryParameters =
            [
                new() { Name = "ratio", Type = new IrNullableType { InnerType = Primitive(IrPrimitiveKind.Number) }, IsRequired = false },
                new() { Name = "state", Type = state, IsRequired = false },
                new() { Name = "enabled", Type = Primitive(IrPrimitiveKind.Boolean), IsRequired = true },
                new() { Name = "tags", Type = new IrNullableType { InnerType = new IrArrayType { ElementType = nullableText } }, IsRequired = false },
                new() { Name = "url", Type = text, IsRequired = false },
                new() { Name = "url_", Type = text, IsRequired = false },
                new() { Name = "responseContent", Type = text, IsRequired = false },
                new() { Name = "queryParts", Type = text, IsRequired = false },
                new() { Name = "item", Type = text, IsRequired = false },
                new() { Name = "cancellationToken", Type = text, IsRequired = false }
            ],
            HeaderParameters = [new IrParameter { Name = "X_Trace", OriginalName = "X.Trace", Type = text, IsRequired = false }],
            RequestBody = new IrRequestBody { Type = new IrModelReference { ModelName = "UploadForm" }, IsRequired = false, ContentType = "multipart/form-data" },
            Responses = [new IrResponse { StatusCode = "200", Type = Primitive(IrPrimitiveKind.Integer) }]
        };
        return new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models = models,
            Enums =
            [
                new IrEnum { Name = "State", BaseType = IrEnumBaseType.String, Values = [new IrEnumValue { Name = "InProgress", Value = "in_progress" }] },
                new IrEnum { Name = "Priority", BaseType = IrEnumBaseType.Integer, Values = [new IrEnumValue { Name = "High", Value = 2 }] },
                new IrEnum { Name = "LargeCode", BaseType = IrEnumBaseType.Integer, Values = [new IrEnumValue { Name = "Large", Value = 9007199254740993L }] }
            ],
            Clients = [new IrClient { Name = "TransportApi", Operations = [operation, new IrOperation
            {
                MethodName = "Ping", HttpMethod = "GET", Path = "/ping",
                PathParameters = [], QueryParameters = [], HeaderParameters = [],
                Responses = [new IrResponse { StatusCode = "204" }]
            }, new IrOperation
            {
                MethodName = "SendJson", HttpMethod = "POST", Path = "/json",
                PathParameters = [], QueryParameters = [], HeaderParameters = [],
                RequestBody = new IrRequestBody
                {
                    Type = new IrModelReference { ModelName = "Combined" }, IsRequired = true, ContentType = "application/json"
                },
                Responses = [new IrResponse { StatusCode = "200", Type = Primitive(IrPrimitiveKind.Integer) }]
            }] }],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
                TopologicalOrder = models.Select(model => model.Name).ToArray()
            }
        };
    }

    /// <summary>Creates one primitive type used by the cross-feature fixture.</summary>
    /// <param name="kind">The normalized primitive kind.</param>
    /// <returns>The primitive IR value.</returns>
    private static IrPrimitiveType Primitive(IrPrimitiveKind kind) => new() { PrimitiveKind = kind };

    /// <summary>Creates a model property while preserving the original wire name.</summary>
    /// <param name="wire">The schema property name.</param>
    /// <param name="name">The C# source identifier.</param>
    /// <param name="type">The normalized property type.</param>
    /// <returns>The property metadata.</returns>
    private static IrProperty Property(string wire, string name, IrType type) => new()
    {
        OriginalName = wire, Name = name, Type = type, IsRequired = true
    };

    /// <summary>Exercises generated source under a culture with comma decimal separators.</summary>
    private const string RuntimeProgram = """"
        using System.Globalization;
        using System.ComponentModel.DataAnnotations;
        using System.Net;
        using System.Text.Json;
        using Generated.Contracts;

        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var model = JsonSerializer.Deserialize<Combined>("""
        {"name":null,"tag":null,"codes":[1,null],"date":"2026-10-11","time":"12:34:56","when":"2026-10-11T12:34:56Z"}
        """
        )!;
        if (model.Message != "default!" || model.State != State.InProgress || model.Codes![1] != null)
            throw new InvalidOperationException("Model default, enum, or nullable-array roundtrip failed.");
        var clone = JsonSerializer.Deserialize<Combined>(JsonSerializer.Serialize(model))!;
        if (clone.Date != model.Date || clone.Time != model.Time || clone.When != model.When)
            throw new InvalidOperationException("Date/time JSON roundtrip failed.");
        if (!new Combined().Equals(new Combined()))
            throw new InvalidOperationException("Nullable property equality failed.");
        if (new Combined().Equals((object)new object()))
            throw new InvalidOperationException("Unrelated objects must compare as unequal.");
        if (HttpWireValueFormatter.Format(Priority.High) != "2")
            throw new InvalidOperationException("Numeric enum wire value failed.");
        if (HttpWireValueFormatter.Format(LargeCode.Large) != "9007199254740993" ||
            JsonSerializer.Deserialize<LargeCode>("9007199254740993") != LargeCode.Large)
            throw new InvalidOperationException("Large numeric enum JSON and wire value failed.");
        if (Validator.TryValidateProperty(1, new ValidationContext(model) { MemberName = "Quantity" }, null) ||
            !Validator.TryValidateProperty(2, new ValidationContext(model) { MemberName = "Quantity" }, null) ||
            Validator.TryValidateProperty(9007199254740992L, new ValidationContext(model) { MemberName = "LongValue" }, null) ||
            !Validator.TryValidateProperty(9007199254740993L, new ValidationContext(model) { MemberName = "LongValue" }, null) ||
            Validator.TryValidateProperty(1d, new ValidationContext(model) { MemberName = "Exclusive" }, null) ||
            !Validator.TryValidateProperty(1.5d, new ValidationContext(model) { MemberName = "Exclusive" }, null) ||
            !Validator.TryValidateProperty(-1, new ValidationContext(model) { MemberName = "Negative" }, null))
            throw new InvalidOperationException("Integer, long, exclusive, or one-sided range validation failed.");
        using var handler = new CaptureHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid") };
        var client = new TransportApi(http);
        var count = await client.Send("seg/one", 1.5, State.InProgress, true, ["x y", null],
            "url", "url2", "response", "parts", "item", "cancel", "trace", null);
        if (count != 7 || handler.HadContent || handler.Header != "trace")
            throw new InvalidOperationException("Numeric response, optional body, or header transmission failed.");
        var uri = handler.Uri!.OriginalString;
        if (!uri.Contains("/files/seg%2Fone") || !uri.Contains("ratio=1.5") ||
            !uri.Contains("state=in_progress") || !uri.Contains("enabled=true") || !uri.Contains("tags=x%20y"))
            throw new InvalidOperationException("Invariant and escaped URL serialization failed: " + uri);
        var form = new UploadForm { Content = new MemoryStream([1, 2, 3]), State = State.InProgress, Amount = 1.5, Enabled = true };
        await client.Send("x", null, null, true, null, null, null, null, null, null, null, null, form);
        if (!handler.Body!.Contains("in_progress") || !handler.Body.Contains("1.5") || !handler.Body.Contains("true"))
            throw new InvalidOperationException("Multipart invariant and enum wire values failed.");
        handler.ReturnError = true;
        try
        {
            await client.Send("x", null, null, true, null, null, null, null, null, null, null, null, null);
            throw new InvalidOperationException("The HTTP error was not surfaced.");
        }
        catch (ApiException exception) when (exception.Content == "<html>gateway failure</html>")
        {
        }
        handler.ReturnError = false;
        await client.Ping();
        // TELEMETRY_CHECKS
        await HttpPipelineChecks.Run();
        Console.WriteLine("CSharp generated runtime passed");

        /// <summary>Captures generated HTTP requests without making a network connection.</summary>
        internal sealed class CaptureHandler : HttpMessageHandler
        {
            /// <summary>Gets the last generated request URI.</summary>
            internal Uri? Uri { get; private set; }
            /// <summary>Gets the last custom header value.</summary>
            internal string? Header { get; private set; }
            /// <summary>Gets whether the last request contained a body.</summary>
            internal bool HadContent { get; private set; }
            /// <summary>Gets the last serialized request body.</summary>
            internal string? Body { get; private set; }
            /// <summary>Gets or sets whether the next response simulates a non-JSON gateway error.</summary>
            internal bool ReturnError { get; set; }
            /// <inheritdoc/>
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Uri = request.RequestUri;
                Header = request.Headers.TryGetValues("X.Trace", out var values) ? values.Single() : null;
                HadContent = request.Content != null;
                Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(ReturnError ? HttpStatusCode.BadGateway : HttpStatusCode.OK)
                {
                    Content = new StringContent(ReturnError ? "<html>gateway failure</html>" : "7")
                };
            }
        }
        """";
}
