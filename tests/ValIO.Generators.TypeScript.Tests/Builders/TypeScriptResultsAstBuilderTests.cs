using ValIO.Generators.TypeScript.Tests.TestHelpers;
using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Nodes;

namespace ValIO.Generators.TypeScript.Tests.Builders;

[TestFixture]
public class TypeScriptResultsAstBuilderTests
{
    private readonly NamingConverter _namingConverter = new();
    private TypeScriptResultsAstBuilder _sut = null!;

    [SetUp]
    public void Setup()
    {
        var mapper = new TypeScriptIrTypeMapper();
        _sut = new TypeScriptResultsAstBuilder(IrDocumentContexts.Create(), _namingConverter, mapper.MapType);
    }

    private static IrOperation Op(string methodName, IReadOnlyList<IrResponse> responses) => new()
    {
        MethodName = methodName,
        Path = "/x",
        HttpMethod = "GET",
        PathParameters = [], QueryParameters = [], HeaderParameters = [],
        RequestBody = null,
        Responses = responses,
    };

    [Test]
    public void HasResponsesWithBody_FalseWhenNoTypedResponses()
    {
        var op = Op("ping", responses: [new IrResponse { StatusCode = "204", Type = null }]);
        _sut.HasResponsesWithBody(op).Should().BeFalse();
    }

    [Test]
    public void SingleResponse_GeneratesResultUnionWithOtherVariant()
    {
        var op = Op("getOne", responses:
        [
            new IrResponse { StatusCode = "200", Type = new IrModelReference { ModelName = "User" } },
        ]);

        var source = _sut.BuildResultsSourceFile(op, importPath: null, fileOrganization: null, currentOperationName: "getOne");
        var typeAliases = source.Declarations.OfType<InterfaceDeclarationNode>().Select(d => d.Name.Name).ToList();
        typeAliases.Should().Contain(new[] { "GetOneResult200", "GetOneResultOther" });
    }

    [Test]
    public void DefaultSuffix_UnionAliasIsResults_VariantsKeepResultBase()
    {
        var op = Op("getOne", responses:
        [
            new IrResponse { StatusCode = "200", Type = new IrModelReference { ModelName = "User" } },
        ]);

        var source = _sut.BuildResultsSourceFile(op, importPath: null, fileOrganization: null, currentOperationName: "getOne");

        _sut.ResultTypeName("getOne").Should().Be("GetOneResults");
        source.Declarations.OfType<TypeAliasDeclarationNode>().Select(d => d.Name.Name)
            .Should().ContainSingle().Which.Should().Be("GetOneResults");
        // Per-status variant interfaces keep the fixed "Result" base.
        source.Declarations.OfType<InterfaceDeclarationNode>().Select(d => d.Name.Name)
            .Should().Contain(new[] { "GetOneResult200", "GetOneResultOther" });
    }

    [Test]
    public void CustomSuffix_RenamesUnionAliasOnly()
    {
        var sut = new TypeScriptResultsAstBuilder(IrDocumentContexts.Create(), _namingConverter, new TypeScriptIrTypeMapper().MapType, resultSuffix: "Response");
        var op = Op("getOne", responses:
        [
            new IrResponse { StatusCode = "200", Type = new IrModelReference { ModelName = "User" } },
        ]);

        var source = sut.BuildResultsSourceFile(op, importPath: null, fileOrganization: null, currentOperationName: "getOne");

        sut.ResultTypeName("getOne").Should().Be("GetOneResponse");
        source.Declarations.OfType<TypeAliasDeclarationNode>().Select(d => d.Name.Name)
            .Should().ContainSingle().Which.Should().Be("GetOneResponse");
        // The union suffix never leaks into the variant interface names.
        source.Declarations.OfType<InterfaceDeclarationNode>().Select(d => d.Name.Name)
            .Should().Contain(new[] { "GetOneResult200", "GetOneResultOther" });
    }

    [Test]
    public void ResolveResultSuffix_FallsBackToResults_AndCapitalisesCustom()
    {
        TypeScriptResultsAstBuilder.ResolveResultSuffix(null).Should().Be("Results");
        TypeScriptResultsAstBuilder.ResolveResultSuffix("   ").Should().Be("Results");
        TypeScriptResultsAstBuilder.ResolveResultSuffix("response").Should().Be("Response");
        TypeScriptResultsAstBuilder.ResolveResultSuffix("  outcome ").Should().Be("Outcome");
    }

    [Test]
    public void NestedModelReferenceInArray_ImportedAtTopLevel()
    {
        var op = Op("list", responses:
        [
            new IrResponse
            {
                StatusCode = "200",
                Type = new IrArrayType { ElementType = new IrModelReference { ModelName = "Pet" } },
            },
        ]);

        var source = _sut.BuildResultsSourceFile(op, importPath: null,
            fileOrganization: new FileOrganization { ModelsPath = "models" },
            currentOperationName: "list");

        var imported = source.Imports.SelectMany(i => i.Items.Select(it => it.Name)).ToList();
        imported.Should().Contain("Pet");
    }
}
