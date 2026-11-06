using ValIO.Generators.TypeScript.Tests.TestHelpers;
using System.Collections.Immutable;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.Builders;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>
/// Component-level unit tests for <see cref="TypeScriptParamsAstBuilder"/>.
/// These tests construct IR directly without going through OpenAPI / pipeline.
/// </summary>
[TestFixture]
public class TypeScriptParamsAstBuilderTests
{
    private readonly NamingConverter _namingConverter = new();
    private TypeScriptParamsAstBuilder _sut = null!;

    [SetUp]
    public void Setup()
    {
        var mapper = new TypeScriptIrTypeMapper();
        _sut = new TypeScriptParamsAstBuilder(IrDocumentContexts.Create(), _namingConverter, mapper.MapType);
    }

    private static IrPrimitiveType Prim(IrPrimitiveKind k) => new() { PrimitiveKind = k };

    private static IrOperation Op(
        string methodName,
        IReadOnlyList<IrParameter>? pathParams = null,
        IReadOnlyList<IrParameter>? queryParams = null,
        IReadOnlyList<IrParameter>? headerParams = null,
        IrRequestBody? requestBody = null)
        => new()
        {
            MethodName = methodName,
            Path = "/x",
            HttpMethod = "GET",
            PathParameters = pathParams ?? [],
            QueryParameters = queryParams ?? [],
            HeaderParameters = headerParams ?? [],
            RequestBody = requestBody,
            Responses = [],
        };

    private static IrParameter PathStringParam(string name, bool required = true) => new()
    {
        Name = name, IsRequired = required,
        Type = Prim(IrPrimitiveKind.String),
    };

    private static IrParameter QueryStringParam(string name, bool required = false) => new()
    {
        Name = name, IsRequired = required,
        Type = Prim(IrPrimitiveKind.String),
    };

    [Test]
    public void HasParameters_TrueWhenAnyParamGroupPresent()
    {
        _sut.HasParameters(Op("getX", pathParams: [PathStringParam("id")])).Should().BeTrue();
        _sut.HasParameters(Op("getX", queryParams: [QueryStringParam("q")])).Should().BeTrue();
        _sut.HasParameters(Op("getX", requestBody: new IrRequestBody { Type = Prim(IrPrimitiveKind.String), IsRequired = true })).Should().BeTrue();
        _sut.HasParameters(Op("getX")).Should().BeFalse();
    }

    [Test]
    public void SingleGroup_BuildsFlatInterface()
    {
        var sourceFile = _sut.BuildParamsSourceFile(
            Op("getById", pathParams: [PathStringParam("id")]),
            importPath: null,
            fileOrganization: null,
            currentOperationName: "getById");

        var declarations = sourceFile.Declarations.OfType<InterfaceDeclarationNode>().ToList();
        declarations.Should().HaveCount(1, "single param group → flat layout, no sub-interfaces");
        declarations[0].Name.Name.Should().Be("GetByIdParams");
        // Property is `id`, not `route: {...}`.
        var memberNames = declarations[0].Members.OfType<ValIO.Generators.TypeScript.Ast.Nodes.PropertySignatureNode>().Select(p => p.Name.Name).ToList();
        memberNames.Should().Contain("id");
        memberNames.Should().NotContain("route");
    }

    [Test]
    public void MultipleGroups_BuildsAggregateInterfaceWithSubInterfaces()
    {
        var sourceFile = _sut.BuildParamsSourceFile(
            Op("update",
                pathParams: [PathStringParam("id")],
                queryParams: [QueryStringParam("filter")]),
            importPath: null,
            fileOrganization: null,
            currentOperationName: "update");

        var declarations = sourceFile.Declarations.OfType<InterfaceDeclarationNode>().ToList();
        declarations.Should().HaveCount(3, "Route, Query, and outer Params interfaces");
        declarations.Select(d => d.Name.Name).Should().Contain(new[]
        {
            "UpdateRouteParams", "UpdateQueryParams", "UpdateParams"
        });
    }

    [Test]
    public void BinaryRequestBody_AddsBodyField()
    {
        var sourceFile = _sut.BuildParamsSourceFile(
            Op("upload",
                pathParams: [PathStringParam("id")],
                requestBody: new IrRequestBody
                {
                    Type = Prim(IrPrimitiveKind.Binary),
                    IsRequired = false,
                    ContentType = "application/octet-stream",
                }),
            importPath: null,
            fileOrganization: null,
            currentOperationName: "upload");

        var outerParams = sourceFile.Declarations.OfType<InterfaceDeclarationNode>()
            .First(d => d.Name.Name == "UploadParams");
        var memberNames = outerParams.Members.OfType<ValIO.Generators.TypeScript.Ast.Nodes.PropertySignatureNode>().Select(p => p.Name.Name).ToList();
        memberNames.Should().Contain("body", "binary content-type is treated as body, not form");
    }

    [Test]
    public void FormRequestBody_AddsFormField_NotBody()
    {
        var sourceFile = _sut.BuildParamsSourceFile(
            Op("submit",
                pathParams: [PathStringParam("id")],
                requestBody: new IrRequestBody
                {
                    Type = Prim(IrPrimitiveKind.String),
                    IsRequired = true,
                    ContentType = "multipart/form-data",
                }),
            importPath: null,
            fileOrganization: null,
            currentOperationName: "submit");

        var outerParams = sourceFile.Declarations.OfType<InterfaceDeclarationNode>()
            .First(d => d.Name.Name == "SubmitParams");
        var memberNames = outerParams.Members.OfType<ValIO.Generators.TypeScript.Ast.Nodes.PropertySignatureNode>().Select(p => p.Name.Name).ToList();
        memberNames.Should().Contain("form");
        memberNames.Should().NotContain("body");
    }

    [Test]
    public void BodyWithNestedModelReference_ImportsModelType()
    {
        var sourceFile = _sut.BuildParamsSourceFile(
            Op("create",
                requestBody: new IrRequestBody
                {
                    Type = new IrArrayType { ElementType = new IrModelReference { ModelName = "User" } },
                    IsRequired = true,
                    ContentType = "application/json",
                }),
            importPath: null,
            fileOrganization: new FileOrganization { ModelsPath = "models" },
            currentOperationName: "create");

        var importedNames = sourceFile.Imports
            .SelectMany(i => i.Items.Select(item => item.Name))
            .ToList();
        importedNames.Should().Contain("User", "body's array element type must be imported");
    }

    [Test]
    public void AllOptionalQuery_QueryWrapperItselfBecomesOptional()
    {
        var sourceFile = _sut.BuildParamsSourceFile(
            Op("list",
                pathParams: [PathStringParam("orgId")],
                queryParams: [QueryStringParam("limit", required: false), QueryStringParam("after", required: false)]),
            importPath: null,
            fileOrganization: null,
            currentOperationName: "list");

        var outer = sourceFile.Declarations.OfType<InterfaceDeclarationNode>()
            .First(d => d.Name.Name == "ListParams");
        var queryMember = outer.Members.OfType<ValIO.Generators.TypeScript.Ast.Nodes.PropertySignatureNode>()
            .First(p => p.Name.Name == "query");
        queryMember.IsRequired.Should().BeFalse("when every query param is optional, `query` itself is optional");
    }
}
