using System.Collections.Immutable;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Tests.Fixtures;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Integration;

/// <summary>Verifies request serialization and independently modeled TypeScript presence and nullability.</summary>
[TestFixture]
public sealed class TransportRequestSerializationRegressionTests
{
    /// <summary>Exercises the same multipart contract across every supported HTTP client transport.</summary>
    /// <param name="transport">The transport under test.</param>
    /// <returns>The asynchronous generation check.</returns>
    [TestCase("axios")]
    [TestCase("fetch")]
    [TestCase("solid")]
    [TestCase("nextjs")]
    [TestCase("ky")]
    [TestCase("got")]
    [TestCase("ofetch")]
    [TestCase("undici")]
    [TestCase("angular-rxjs")]
    [TestCase("angular-promise")]
    public async Task MultipartRequest_AllTransportsSerializeNativeFormData(string transport)
    {
        var content = await GenerateUpload(transport, required: true);

        content.Should().Contain("const formData = new FormData();");
        content.Should().MatchRegex("formData\\.append\\(\\s+\"file\",\\s+params\\.form\\.file\\s+\\)");
        content.Should().MatchRegex("formData\\.append\\(\\s+\"metadata\",\\s+JSON\\.stringify\\(params\\.form\\.metadata\\)\\s+\\)");
        content.Should().Contain("for (const item of params.form.tags)");
        content.Should().MatchRegex("formData\\.append\\(\\s+\"tags\",\\s+String\\(item\\)\\s+\\)");
        content.Should().Contain("item !== null");
        content.Should().NotContain("application/json");
        content.Should().NotContain("data: params.form");
        content.Should().NotContain("body: params.form");
    }

    /// <summary>Ensures an omitted optional multipart body never dereferences the absent form model.</summary>
    /// <param name="transport">The transport under test.</param>
    /// <returns>The asynchronous generation check.</returns>
    [TestCase("axios")]
    [TestCase("fetch")]
    [TestCase("solid")]
    [TestCase("nextjs")]
    [TestCase("ky")]
    [TestCase("got")]
    [TestCase("ofetch")]
    [TestCase("undici")]
    [TestCase("angular-rxjs")]
    [TestCase("angular-promise")]
    public async Task OptionalMultipartRequest_GuardsModelAccess(string transport)
    {
        var content = await GenerateUpload(transport, required: false);

        content.Should().MatchRegex("if \\(\\(\\(params\\.form !== undefined\\) && \\(params\\.form !== null\\)\\)\\)\\s*\\{\\s*if \\(\\(\\(params\\.form\\.file");
        content.Should().Contain("? formData : undefined");
    }

    /// <summary>Checks that optional request validation occurs only when the caller provides a body.</summary>
    /// <param name="transport">The transport under test.</param>
    /// <returns>The asynchronous generation check.</returns>
    [TestCase("axios")]
    [TestCase("fetch")]
    [TestCase("solid")]
    [TestCase("nextjs")]
    [TestCase("ky")]
    [TestCase("got")]
    [TestCase("ofetch")]
    [TestCase("undici")]
    [TestCase("angular-promise")]
    public async Task OptionalMultipartRequest_ValidationDoesNotRequireAbsentBody(string transport)
    {
        var content = await GenerateUpload(transport, required: false, validationLibrary: "zod4");

        content.Should().Contain("const parsedBody = ((params.form !== undefined) ?");
        content.Should().Contain(": undefined);");
        content.Should().Contain("parsedBody !== undefined");
        content.Should().Contain("parsedBody !== null");
    }

    /// <summary>Verifies transports that formerly forwarded sanitized header fields unchanged.</summary>
    /// <param name="transport">The transport under test.</param>
    /// <returns>The asynchronous generation check.</returns>
    [TestCase("ky")]
    [TestCase("got")]
    [TestCase("ofetch")]
    [TestCase("undici")]
    public async Task HeaderParameters_KeepWireNames(string transport)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/wire-headers-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = [transport] });
        var endpoint = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith($".{transport}.ts", StringComparison.Ordinal));

        endpoint.Content.Should().Contain("'X-Request-ID': params.headers?.X_Request_ID");
        endpoint.Content.Should().NotContain("headers: params.headers");
    }

    /// <summary>Ensures fetch operations with no response body never invoke the JSON parser.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task FetchNoContentResponse_DoesNotReadJson()
    {
        var content = await GenerateUpload("fetch", required: true);

        content.Should().Contain("await httpClient(");
        content.Should().NotContain("response.json()");
        content.Should().NotContain("const response =");
    }

    /// <summary>Ensures Next.js uses the native Headers codec and emits a default JSON content type.</summary>
    /// <returns>The asynchronous generation check.</returns>
    [Test]
    public async Task NextjsJsonRequest_NormalizesCallerHeadersAndSetsContentType()
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read("Transport/json-request-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions { Transports = ["nextjs"] });
        var content = context.GeneratedFiles.Single(file => file.RelativePath.EndsWith(".nextjs.ts", StringComparison.Ordinal)).Content;

        content.Should().Contain("new Headers(options?.headers)");
        content.Should().Contain("requestHeaders.has(\"Content-Type\")");
        content.Should().MatchRegex("requestHeaders\\.set\\(\\s+\"Content-Type\",\\s+\"application/json\"\\s+\\)");
        content.Should().Contain("headers: requestHeaders");
        content.Should().NotContain("as Record<string, string>");
    }

    /// <summary>Verifies that required-nullable and optional-nonnullable properties remain different contracts.</summary>
    [Test]
    public void ModelProperties_PreservePresenceIndependentlyFromNullability()
    {
        var renderer = new TypeScriptAstRenderer();
        var stringType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
        var model = new IrModel
        {
            Name = "Presence",
            Properties =
            [
                new IrProperty { OriginalName = "requiredNullable", Name = "requiredNullable", Type = new IrNullableType { InnerType = stringType }, IsRequired = true },
                new IrProperty { OriginalName = "optionalString", Name = "optionalString", Type = stringType, IsRequired = false },
            ],
        };

        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create(models: [model]), new ValIO.Core.Services.NamingConverter());
        var content = renderer.Render(builder.BuildSourceFile(model));

        content.Should().Contain("requiredNullable: string | null;");
        content.Should().NotContain("requiredNullable?:");
        content.Should().Contain("optionalString?: string;");
        content.Should().NotContain("optionalString?: string | null");
    }

    /// <summary>Verifies optional value unions remain valid when composed as array element types.</summary>
    [Test]
    public void OptionalValueTypes_RenderUndefinedUnionsAndPreserveArrayGrouping()
    {
        var renderer = new TypeScriptAstRenderer();
        var optional = new OptionalTypeNode(new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true));
        var array = new ArrayTypeNode(optional, isNullable: true, isOptional: true);

        renderer.VisitOptionalType(optional).Should().Be("string | undefined");
        renderer.VisitArrayType(array).Should().Be("(string | undefined)[] | null | undefined");
    }

    /// <summary>Ensures immediately invoked arrow factories retain their callable expression boundary.</summary>
    [Test]
    public void ArrowFactories_AreParenthesizedWhenCalled()
    {
        var factory = new ValIO.Generators.TypeScript.Ast.Nodes.Expressions.ArrowFunctionExpressionNode(
            [], new LiteralExpressionNode(42, LiteralKind.Number));
        var call = new CallExpressionNode(factory, []);

        new TypeScriptAstRenderer().VisitCallExpression(call).Should().Be("(() => 42)()");
    }

    /// <summary>Preserves explicit callback return contracts for discriminated result mapping.</summary>
    [Test]
    public void ArrowCallbacks_PreserveDeclaredReturnType()
    {
        var callback = new ValIO.Generators.TypeScript.Ast.Nodes.Expressions.ArrowFunctionExpressionNode(
            [], new LiteralExpressionNode(42, LiteralKind.Number), returnType: new SimpleTypeNode(new IdentifierNode("number"), isPrimitive: true));

        new TypeScriptAstRenderer().VisitArrowFunctionExpression(callback).Should().Be("(): number => 42");
    }

    /// <summary>Preserves case-distinct path identity and replaces every occurrence regardless of IR ordering.</summary>
    [Test]
    public void RequestPath_ReplacesRepeatedCaseSensitiveWireTokens()
    {
        var stringType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
        var operation = new IrOperation
        {
            MethodName = "find", HttpMethod = "GET", Path = "/{ID}/{id}/{ID}/{missing}",
            PathParameters =
            [
                new IrParameter { Name = "lower", OriginalName = "id", Type = stringType, IsRequired = true },
                new IrParameter { Name = "upper", OriginalName = "ID", Type = stringType, IsRequired = true },
            ],
            QueryParameters = [], HeaderParameters = [], Responses = [],
        };
        var renderer = new TypeScriptAstRenderer();
        var source = new IdentifierExpressionNode(new IdentifierNode("route"));

        renderer.VisitTemplateLiteralExpression((ValIO.Generators.TypeScript.Ast.Nodes.Expressions.TemplateLiteralExpressionNode)
            TypeScriptRequestPath.BuildUrl(operation, source)).Should().Be("`/${route.upper}/${route.lower}/${route.upper}/{missing}`");
    }

    /// <summary>Allows a unique casing fallback while retaining exact and ambiguous wire-name identities.</summary>
    /// <param name="path">The path containing original wire tokens.</param>
    /// <param name="wireNames">The declared original parameter names.</param>
    /// <param name="expected">The expected rendered literal or template.</param>
    [TestCase("/{dictionaryId}/{dictionaryId}", new[] { "DictionaryId" }, "`/${route.value0}/${route.value0}`")]
    [TestCase("/{ID}/{id}/{iD}", new[] { "ID", "id" }, "`/${route.value0}/${route.value1}/{iD}`")]
    [TestCase("/{iD}", new[] { "ID", "id" }, "\"/{iD}\"")]
    public void RequestPath_UsesOnlyUnambiguousCasingFallback(string path, string[] wireNames, string expected)
    {
        var operation = new IrOperation
        {
            MethodName = "find", HttpMethod = "GET", Path = path,
            PathParameters = wireNames.Select((wireName, index) => new IrParameter
            {
                Name = $"value{index}", OriginalName = wireName, IsRequired = true,
                Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
            }).ToList(),
            QueryParameters = [], HeaderParameters = [], Responses = [],
        };
        var expression = TypeScriptRequestPath.BuildUrl(operation, new IdentifierExpressionNode(new IdentifierNode("route")));

        new TypeScriptAstRenderer().Visit(expression).Should().Be(expected);
    }

    /// <summary>Checks that model alternatives and sibling properties are represented simultaneously.</summary>
    /// <param name="kind">The union composition kind.</param>
    [TestCase(IrCompositionKind.OneOf)]
    [TestCase(IrCompositionKind.AnyOf)]
    public void UnionModel_PreservesAlternativesAndSiblingProperties(IrCompositionKind kind)
    {
        var model = new IrModel
        {
            Name = "Animal",
            Composition = new IrComposition
            {
                Kind = kind,
                Types = [new IrModelReference { ModelName = "Dog" }, new IrModelReference { ModelName = "Cat" }],
            },
            Properties =
            [
                new IrProperty
                {
                    OriginalName = "tag", Name = "tag", IsRequired = true,
                    Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
                },
            ],
        };
        var builder = new TypeScriptAstBuilder(IrDocumentContexts.Create(models: [model]), new ValIO.Core.Services.NamingConverter());

        var content = new TypeScriptAstRenderer().Render(builder.BuildSourceFile(model));

        content.Should().Contain("export type Animal = (Dog | Cat) & { tag: string };");
        content.Should().NotContain("interface Animal");
    }

    /// <summary>Checks shared inherited-property traversal and binary-array conversion for multipart bodies.</summary>
    [Test]
    public void MultipartBody_IncludesInheritedFieldsAndConvertsByteArraysToBlob()
    {
        var baseModel = new IrModel
        {
            Name = "Base",
            Properties =
            [
                new IrProperty
                {
                    OriginalName = "wire.bytes", Name = "bytes", IsRequired = true,
                    Type = new IrNullableType
                    {
                        InnerType = new IrOptionalType
                        {
                            InnerType = new IrNullableType
                            {
                                InnerType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.ByteArray },
                            },
                        },
                    },
                },
            ],
        };
        var derived = new IrModel
        {
            Name = "Derived", Properties = [],
            Composition = new IrComposition { Kind = IrCompositionKind.AllOf, Types = [new IrModelReference { ModelName = "Base" }] },
        };
        var statements = new List<StatementNode>();
        TypeScriptFormBody.Build(
            new IrRequestBody { IsRequired = true, ContentType = "multipart/form-data", Type = new IrModelReference { ModelName = "Derived" } },
            new IdentifierExpressionNode(new IdentifierNode("body")),
            new Dictionary<string, IrModel> { [baseModel.Name] = baseModel, [derived.Name] = derived },
            statements);

        var content = new TypeScriptAstRenderer().VisitBlock(new BlockNode(statements.ToImmutableList()));

        content.Should().Contain("\"wire.bytes\"");
        content.Should().Contain("new Blob([new Uint8Array(body.bytes)])");
        content.Should().NotContain("String(body.bytes)");
    }

    /// <summary>Checks that arbitrary wire keys cannot break object literal syntax or alter the prototype.</summary>
    /// <param name="key">The unescaped wire key.</param>
    /// <param name="expected">The expected rendered key.</param>
    [TestCase("name'with\\slash\n", "'name\\'with\\\\slash\\n': value")]
    [TestCase("\"quoted\"", "'\\\"quoted\\\"': value")]
    [TestCase("__proto__", "[\"__proto__\"]: value")]
    public void ObjectWireKeys_AreEscapedOnce(string key, string expected)
    {
        var node = new PropertyAssignmentNode(new IdentifierNode(key), new IdentifierExpressionNode(new IdentifierNode("value")));

        new TypeScriptAstRenderer().VisitPropertyAssignment(node).Should().Be(expected);
    }

    /// <summary>Generates a representative multipart operation and selects its transport implementation.</summary>
    /// <param name="transport">The transport to generate.</param>
    /// <param name="required">Whether the request body is required.</param>
    /// <param name="validationLibrary">The optional runtime validation library.</param>
    /// <returns>The generated operation or Angular service text.</returns>
    private static async Task<string> GenerateUpload(string transport, bool required, string? validationLibrary = null)
    {
        using var harness = new PipelineTestHarness();
        var document = await PipelineTestHarness.LoadDocumentFromJsonAsync(OpenApiFixture.Read(
            required ? "Transport/multipart-required-3.0.json" : "Transport/multipart-optional-3.0.json"));
        var context = await harness.RunAsync(document, new TypeScriptGenerationOptions
        {
            Transports = [transport],
            ValidationLibrary = validationLibrary,
        });
        var suffix = transport is "angular-rxjs" or "angular-promise" ? ".service.ts" : $".{transport}.ts";
        return context.GeneratedFiles.Single(file => file.RelativePath.EndsWith(suffix, StringComparison.Ordinal)).Content;
    }
}
