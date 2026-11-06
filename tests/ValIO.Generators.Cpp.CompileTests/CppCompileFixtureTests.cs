using System.Diagnostics;
using System.Text;
using Microsoft.OpenApi;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Builders;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Plugins;
using ValIO.Core.Services;
using ValIO.Tests.Fixtures;
using ValIO.Generators.Cpp;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.CompileTests;

/// <summary>
/// Compile-фикстура std-профиля: генерируем клиента in-proc → собираем CMake-проектом
/// с точными пинами cpp-httplib v0.18.3 + nlohmann_json v3.12.0 → запускаем smoke
/// (локальный httplib::Server + сгенерированный клиент; wire-имена, optional-семантика,
/// enum-кодек, не-2xx → Error{Status}, void-ответы, async). Матрица стандартов 17/20/23
/// покрывает и polyfill Expected (17), и std::expected (23).
/// Требует cmake в PATH (локально без тулчейна — Assert.Ignore).
/// </summary>
[TestFixture]
[Category("CompileFixture")]
[Parallelizable(ParallelScope.None)]
public class CppCompileFixtureTests
{
    /// <summary>Creates models and operations that exercise enum overloads, nullable codecs, and multipart transport.</summary>
    /// <returns>The native smoke contract.</returns>
    private static IrDocument BuildDocument()
    {
        var order = new IrModel
        {
            Name = "Order",
            Properties =
            [
                new IrProperty
                {
                    OriginalName = "order_id", Name = "OrderId",
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Long },
                    IsRequired = true,
                },
                new IrProperty
                {
                    OriginalName = "note", Name = "Note",
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
                    IsRequired = false,
                },
                new IrProperty
                {
                    OriginalName = "status", Name = "Status",
                    Type = new IrModelReference { ModelName = "OrderStatus" },
                    IsRequired = true,
                },
            ],
        };
        var status = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values =
            [
                new IrEnumValue { Name = "placed", Value = "placed" },
                new IrEnumValue { Name = "approved", Value = "approved" },
            ],
        };

        var probe = new IrModel
        {
            Name = "CodecProbe",
            Properties =
            [
                new IrProperty
                {
                    Name = "RequiredNullable", OriginalName = "required_null", IsRequired = true,
                    Type = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } },
                },
                new IrProperty
                {
                    Name = "OptionalNullable", OriginalName = "optional_null", IsRequired = false,
                    Type = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } },
                },
                new IrProperty
                {
                    Name = "NullableItems", OriginalName = "nullable_items", IsRequired = true,
                    Type = new IrArrayType { ElementType = new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } } },
                },
                new IrProperty { Name = "Color", OriginalName = "color", IsRequired = false, Type = new IrModelReference { ModelName = "Color" } },
            ],
        };
        var upload = new IrModel
        {
            Name = "Upload",
            Properties =
            [
                new IrProperty { Name = "Title", OriginalName = "document.title", IsRequired = false, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } },
                new IrProperty
                {
                    Name = "Files", OriginalName = "documents", IsRequired = true,
                    Type = new IrArrayType { ElementType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Binary } },
                },
            ],
        };
        var nameBase = new IrModel
        {
            Name = "NameBase",
            Properties = [new IrProperty { Name = "Name", OriginalName = "name", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String } }],
        };
        var idBase = new IrModel
        {
            Name = "IdBase",
            Properties = [new IrProperty { Name = "Id", OriginalName = "id", IsRequired = true, Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Long } }],
        };
        var singleBase = new IrModel
        {
            Name = "SingleBase", Properties = [],
            Composition = new IrComposition { Kind = IrCompositionKind.AllOf, Types = [new IrModelReference { ModelName = "NameBase" }] },
        };
        var combined = new IrModel
        {
            Name = "Combined", Properties = [],
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = [new IrModelReference { ModelName = "NameBase" }, new IrModelReference { ModelName = "IdBase" }],
            },
        };
        var scalar = new IrOperation
        {
            MethodName = "GetActive", HttpMethod = "GET", Path = "/store/active",
            PathParameters = [], HeaderParameters = [],
            QueryParameters =
            [
                new IrParameter { Name = "Level", OriginalName = "filter[level]", IsRequired = true, Type = new IrModelReference { ModelName = "Level" } },
            ],
            Responses = [new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }],
        };

        IrParameter BrowseParameter(string name, string wireName, IrType type) => new()
        {
            Name = name, OriginalName = wireName, Type = type, IsRequired = false,
        };
        var integerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Integer };
        var booleanType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean };
        var stringType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
        var integerArray = new IrArrayType { ElementType = integerType };
        var statusArray = new IrArrayType { ElementType = new IrModelReference { ModelName = "OrderStatus" } };
        var browse = new IrOperation
        {
            MethodName = "Browse", HttpMethod = "GET", Path = "/browse",
            PathParameters = [],
            QueryParameters =
            [
                BrowseParameter("Mode", "mode", integerType) with { IsRequired = true },
                BrowseParameter("RequiredNullable", "required", new IrNullableType { InnerType = integerType }) with { IsRequired = true },
                BrowseParameter("Enabled", "enabled", booleanType),
                BrowseParameter("Count", "count", integerType),
                BrowseParameter("Ratio", "ratio", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Number }),
                BrowseParameter("Text", "text", stringType),
                BrowseParameter("Values", "values", integerArray),
                BrowseParameter("Switches", "switches", new IrArrayType { ElementType = booleanType }),
                BrowseParameter("States", "states", statusArray),
                BrowseParameter("Csv", "csv", integerArray) with { Style = IrParameterStyle.Form, Explode = false },
                BrowseParameter("Spaces", "spaces", integerArray) with { Style = IrParameterStyle.SpaceDelimited, Explode = false },
                BrowseParameter("Pipes", "pipes", integerArray) with { Style = IrParameterStyle.PipeDelimited, Explode = false },
                BrowseParameter("Filter", "filter", new IrModelReference { ModelName = "Order" }),
                BrowseParameter("MaybeText", "maybeText", new IrNullableType { InnerType = stringType }),
            ],
            HeaderParameters =
            [
                BrowseParameter("EnabledHeader", "X-Enabled", booleanType),
                BrowseParameter("CountHeader", "X-Count", integerType),
                BrowseParameter("TextHeader", "X-Text", stringType),
                BrowseParameter("ValuesHeader", "X-Values", integerArray) with { Style = IrParameterStyle.Simple, Explode = false },
                BrowseParameter("StatesHeader", "X-States", statusArray) with { Style = IrParameterStyle.Simple, Explode = false },
            ],
            Responses = [new IrResponse { StatusCode = "200", Type = booleanType }],
        };

        // Builds a multipart endpoint with a conventional PascalCase operation name.
        IrOperation UploadOperation(string verb) => new()
        {
            MethodName = $"Upload{verb[0]}{verb[1..].ToLowerInvariant()}", HttpMethod = verb, Path = "/store/upload",
            PathParameters = [], QueryParameters = [], HeaderParameters = [],
            RequestBody = new IrRequestBody { Type = new IrModelReference { ModelName = "Upload" }, IsRequired = true, ContentType = "multipart/form-data" },
            Responses = [new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }],
        };

        IrOperation Operation(string method, string path, string name, string? body = null, string successCode = "200", bool successBody = true) => new()
        {
            MethodName = name,
            HttpMethod = method,
            Path = path,
            PathParameters = path.Contains('{')
                ?
                [
                    new IrParameter
                    {
                        Name = "orderId",
                        Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Long },
                        IsRequired = true,
                    },
                ]
                : [],
            QueryParameters =
            [
                new IrParameter
                {
                    Name = "include",
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
                    IsRequired = false,
                },
            ],
            HeaderParameters = [],
            RequestBody = body == null ? null : new IrRequestBody
            {
                Type = new IrModelReference { ModelName = body },
                IsRequired = true,
                ContentType = "application/json",
            },
            Responses =
            [
                new IrResponse
                {
                    StatusCode = successCode,
                    Type = successBody ? new IrModelReference { ModelName = "Order" } : null,
                },
                new IrResponse { StatusCode = "404" },
            ],
        };

        return new IrDocument
        {
            Metadata = new IrDocumentMetadata(),
            Models = [order, probe, upload, nameBase, idBase, singleBase, combined, CheckedCodecFixture.CreateModel(), new IrModel { Name = "Empty", Properties = [] }],
            Enums =
            [
                status,
                new IrEnum { Name = "Color", BaseType = IrEnumBaseType.String, Values = [new IrEnumValue { Name = "red", Value = "red" }] },
                new IrEnum { Name = "Level", BaseType = IrEnumBaseType.Integer, Values = [new IrEnumValue { Name = "high", Value = 2 }] },
                new IrEnum
                {
                    Name = "Wide", BaseType = IrEnumBaseType.Integer,
                    Values = [new IrEnumValue { Name = "minimum", Value = long.MinValue }, new IrEnumValue { Name = "maximum", Value = long.MaxValue }],
                },
            ],
            Clients =
            [
                new IrClient
                {
                    Name = "Store",
                    Operations =
                    [
                        Operation("GET", "/store/order/{orderId}", "GetOrderById"),
                        Operation("POST", "/store/order", "PlaceOrder", body: "Order"),
                        Operation("POST", "/store/formats", "PlaceFormats", body: "CodecFormats"),
                        Operation("DELETE", "/store/order/{orderId}", "DeleteOrder", successCode: "204", successBody: false),
                        Operation("GET", "/store/maybe/{orderId}", "GetMaybeOrder") with
                        {
                            Responses = [new IrResponse { StatusCode = "200", Type = new IrModelReference { ModelName = "Order" } },
                                new IrResponse { StatusCode = "204" }, new IrResponse { StatusCode = "205" }],
                        },
                        Operation("GET", "/store/status/{orderId}", "GetStatus") with
                        {
                            Responses = [new IrResponse { StatusCode = "2XX", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }],
                        },
                        Operation("GET", "/store/status/{orderId}", "GetRangeOverride") with
                        {
                            Responses = [new IrResponse { StatusCode = "2XX" },
                                new IrResponse { StatusCode = "200", Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Boolean } }],
                        },
                        Operation("HEAD", "/store/order/{orderId}", "HeadOrder"),
                        scalar,
                        browse,
                        UploadOperation("POST"), UploadOperation("PUT"), UploadOperation("PATCH"),
                    ],
                },
            ],
            TypeDependencies = new IrTypeDependencyGraph
            {
                Dependencies = new Dictionary<string, IReadOnlyList<string>>(),
                TopologicalOrder = ["Order", "OrderStatus"],
            },
        };
    }

    /// <summary>Runs the real C++ pipeline and writes its output into an isolated fixture directory.</summary>
    /// <param name="outputDirectory">The generated output directory.</param>
    /// <param name="cxxStandard">The target C++ standard.</param>
    /// <param name="apiStyle">The client execution style.</param>
    /// <param name="clientShape">The client declaration shape.</param>
    /// <returns>The generated output directory.</returns>
    private static async Task<string> GenerateIntoAsync(string outputDirectory, int cxxStandard, string apiStyle, string clientShape)
    {
        var options = new CppGenerationOptions
        {
            Runtime = CppRuntimeProfile.Std,
            CxxStandard = cxxStandard,
            ApiStyle = apiStyle switch { "both" => CppApiStyle.Both, "async" => CppApiStyle.Async, _ => CppApiStyle.Sync },
            ClientShape = clientShape == "interface+impl" ? CppClientShape.InterfaceImpl : CppClientShape.Class,
            Namespace = "Petstore",
            EmitCmake = true,
        };

        var services = new ServiceCollection()
            .AddSingleton<ICppAstRenderer, CppAstRenderer>()
            .BuildServiceProvider();
        var pipeline = new CppGeneratorPlugin().CreatePipeline(services);
        var document = BuildDocument();
        var optionalBodies = await LoadOptionalBodiesAsync();
        var nullableReferences = await LoadNullableReferencesAsync();
        var models = document.Models.Concat(optionalBodies.Models).Concat(nullableReferences.Models).ToList();
        var enums = document.Enums.Concat(optionalBodies.Enums).Concat(nullableReferences.Enums).ToList();
        document = document with
        {
            Models = models,
            Enums = enums,
            Clients = document.Clients.Concat(optionalBodies.Clients).Concat(nullableReferences.Clients).ToList(),
            TypeDependencies = new IrTypeDependencyGraphBuilder().Build(models, enums),
        };
        var context = new GenerationContext
        {
            IrDocument = document,
            Configuration = new GenerationConfiguration { LanguageId = "cpp", PluginOptions = options },
            PluginOptions = options,
        };
        await pipeline.ExecuteAsync(context);

        foreach (var file in context.GeneratedFiles)
        {
            var target = Path.Combine(outputDirectory, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, file.Content);
        }
        return outputDirectory;
    }

    /// <summary>Loads the explicit optional-body contract through the real OpenAPI reader and IR pipeline.</summary>
    /// <returns>The JSON, nullable, form, multipart, and binary body contracts.</returns>
    /// <exception cref="InvalidOperationException">The on-disk OpenAPI fixture contains diagnostics.</exception>
    private static async Task<IrDocument> LoadOptionalBodiesAsync()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("Cpp/optional-request-bodies-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(stream, "json");
        if (parsed.Document is null || parsed.Diagnostic?.Errors.Count > 0)
            throw new InvalidOperationException("Cannot parse the optional C++ request-body fixture.");
        return new IrBuilder(new SchemaResolver(parsed.Document), new NamingConverter()).BuildFromOpenApi(parsed.Document);
    }

    /// <summary>Loads disjoint nullable model and enum references through the production reader and IR builder.</summary>
    /// <returns>The shared regression contract including nested containers and nullable HTTP bodies.</returns>
    /// <exception cref="InvalidOperationException">The source fixture cannot be parsed without errors.</exception>
    private static async Task<IrDocument> LoadNullableReferencesAsync()
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(OpenApiFixture.Read("CoreIR/nullable-reference-unions-3.1.json")));
        var parsed = await OpenApiDocument.LoadAsync(stream, "json");
        if (parsed.Document is null || parsed.Diagnostic?.Errors.Count > 0)
            throw new InvalidOperationException("Cannot parse the nullable reference fixture.");
        return new IrBuilder(new SchemaResolver(parsed.Document), new NamingConverter()).BuildFromOpenApi(parsed.Document);
    }

    private static string? FindCmake()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            var candidate = Path.Combine(directory.Trim('"'), "cmake" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    /// <summary>Runs a native tool with concurrent output capture and a bounded process-tree timeout.</summary>
    /// <param name="fileName">The native executable.</param>
    /// <param name="arguments">The tool arguments.</param>
    /// <param name="workingDirectory">The isolated fixture directory.</param>
    /// <param name="output">The captured standard output and error text.</param>
    /// <returns>The exit code, or minus one when the process exceeds its timeout.</returns>
    private static int RunTool(string fileName, string arguments, string workingDirectory, out string output)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(fileName, arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        var exited = process.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
        }
        output = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();
        return exited ? process.ExitCode : -1;
    }

    /// <summary>Uses an optional caller-provided checkout of a pinned dependency without downloading it again.</summary>
    /// <param name="environmentVariable">The environment variable containing the dependency source directory.</param>
    /// <param name="cmakeVariable">The corresponding FetchContent source override.</param>
    /// <param name="headerRelativePath">The required public header relative to the source root.</param>
    /// <returns>A quoted CMake argument, or an empty string when the override is absent.</returns>
    /// <exception cref="InvalidOperationException">The configured directory lacks its CMake project or public header.</exception>
    private static string DependencySourceArgument(string environmentVariable, string cmakeVariable, string headerRelativePath)
    {
        var configuredPath = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath))
            return string.Empty;

        var sourceRoot = Path.GetFullPath(configuredPath);
        if (!File.Exists(Path.Combine(sourceRoot, "CMakeLists.txt"))
            || !File.Exists(Path.Combine(sourceRoot, headerRelativePath)))
            throw new InvalidOperationException($"{environmentVariable} must identify a dependency source root containing CMakeLists.txt and {headerRelativePath}: {sourceRoot}");

        return $" -D{cmakeVariable}=\"{sourceRoot.Replace('\\', '/')}\"";
    }

    /// <summary>Enables exact-version installed packages when an offline CMake package prefix is configured.</summary>
    /// <returns>The installed dependency switch and normalized CMake search prefixes, or an empty string.</returns>
    /// <exception cref="InvalidOperationException">A configured package search directory does not exist.</exception>
    private static string InstalledDependencyArguments()
    {
        var configuredPath = Environment.GetEnvironmentVariable("VALIO_CPP_CMAKE_PREFIX_PATH");
        if (string.IsNullOrWhiteSpace(configuredPath))
            return string.Empty;

        var prefixes = configuredPath.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(Path.GetFullPath).ToArray();
        if (prefixes.Length == 0 || prefixes.Any(prefix => !Directory.Exists(prefix)))
            throw new InvalidOperationException($"VALIO_CPP_CMAKE_PREFIX_PATH must contain existing CMake package search directories: {configuredPath}");

        return $" -DCMAKE_PREFIX_PATH=\"{string.Join(';', prefixes).Replace('\\', '/')}\" -DVALIO_USE_INSTALLED_DEPENDENCIES=ON";
    }

    /// <summary>Compiles all generated translation units and runs HTTP and codec contract regressions.</summary>
    /// <param name="cxxStandard">The native language standard.</param>
    /// <param name="apiStyle">The client execution style.</param>
    /// <param name="clientShape">The client declaration shape.</param>
    /// <returns>The asynchronous test execution.</returns>
    [TestCase(17, "both", "interface+impl")]
    [TestCase(20, "sync", "class")]
    [TestCase(23, "both", "interface+impl")]
    [TestCase(17, "async", "class")]
    public async Task GeneratedClient_CompilesAndRoundTrips(int cxxStandard, string apiStyle, string clientShape)
    {
        var cmake = FindCmake();
        if (cmake == null)
            Assert.Ignore("cmake is not on PATH — compile fixture requires a C++ toolchain");

        var workRoot = Path.Combine(Path.GetTempPath(), $"valio-cpp-fixture-{cxxStandard}-{apiStyle}-{clientShape}-{Guid.NewGuid():N}");
        var generatedDir = Path.Combine(workRoot, "gen");
        var fixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var buildDir = Path.Combine(workRoot, "build");
        // The smoke executable selects the available client execution methods.
        var smokeAsync = apiStyle is "both" or "async" ? "1" : "0";
        var asyncOnly = apiStyle == "async" ? "1" : "0";
        var passed = false;
        try
        {
            await GenerateIntoAsync(generatedDir, cxxStandard, apiStyle, clientShape);

            // Forward slashes prevent CMake from interpreting Windows path characters as escapes.
            var configureArguments = $"-S \"{fixturesDir}\" -B \"{buildDir}\" -DGEN_DIR=\"{generatedDir.Replace('\\', '/')}\" -DCMAKE_CXX_STANDARD={cxxStandard} -DVALIO_SMOKE_ASYNC={smokeAsync} -DVALIO_ASYNC_ONLY={asyncOnly}";
            configureArguments += DependencySourceArgument("VALIO_CPP_HTTPLIB_SOURCE", "FETCHCONTENT_SOURCE_DIR_HTTPLIB", "httplib.h");
            configureArguments += DependencySourceArgument("VALIO_CPP_JSON_SOURCE", "FETCHCONTENT_SOURCE_DIR_JSON", Path.Combine("single_include", "nlohmann", "json.hpp"));
            configureArguments += InstalledDependencyArguments();
            var configureExit = RunTool(cmake, configureArguments, workRoot, out var configureOutput);
            await File.WriteAllTextAsync(Path.Combine(workRoot, "configure.log"), configureOutput);
            configureExit.Should().Be(0, $"cmake configure must succeed:\n{configureOutput}");

            var buildExit = RunTool(cmake, $"--build \"{buildDir}\" --config Debug", workRoot, out var buildOutput);
            await File.WriteAllTextAsync(Path.Combine(workRoot, "build.log"), buildOutput);
            buildExit.Should().Be(0, $"generated C++ must compile warning-clean against cpp-httplib 0.18.3 + nlohmann_json 3.12.0:\n{buildOutput}");

            var smokeExecutable = Directory.EnumerateFiles(buildDir, "smoke_main*", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetExtension(f) is ".exe" || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(f) == string.Empty);
            smokeExecutable.Should().NotBeNull($"smoke_main must be built:\n{buildOutput}");

            var smokeExit = RunTool(smokeExecutable!, string.Empty, workRoot, out var smokeOutput);
            await File.WriteAllTextAsync(Path.Combine(workRoot, "smoke.log"), smokeOutput);
            smokeExit.Should().Be(0, $"smoke round-trip must pass (wire names, optional semantics, enum codec, non-2xx, void, async):\n{smokeOutput}");
            passed = true;
        }
        finally
        {
            // Preserve the generated source, build tree, and tool logs when any assertion fails.
            if (!passed)
                TestContext.Progress.WriteLine($"Native fixture retained at {workRoot}");
            // FetchContent repositories contain read-only pack files on Windows.
            // Successful runs remove those attributes before best-effort cleanup.
            try
            {
                if (passed)
                {
                    foreach (var file in Directory.EnumerateFiles(workRoot, "*", SearchOption.AllDirectories))
                    {
                        var attributes = File.GetAttributes(file);
                        if (attributes.HasFlag(FileAttributes.ReadOnly))
                            File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                    }
                    Directory.Delete(workRoot, recursive: true);
                }
            }
            catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
            {
                // Leave directories still held open by the native toolchain for later cleanup.
            }
        }
    }
}
