using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Models;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Tests.TestHelpers;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>Checks that declaration resolution uses one immutable document scope without generation-order state.</summary>
[TestFixture]
public sealed class TypeScriptDocumentContextTests
{
    /// <summary>Checks forward inheritance and original file names before the referenced model is built.</summary>
    [Test]
    public void ForwardDeclarations_ResolveFromSharedIndexes()
    {
        var inherited = new IrProperty
        {
            Name = "id", OriginalName = "id", IsRequired = true,
            Type = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String },
        };
        var baseModel = new IrModel { Name = "ApiBase", OriginalName = "BaseDto", Properties = [inherited] };
        var derived = new IrModel
        {
            Name = "ApiDerived", OriginalName = "DerivedDto", Properties = [inherited],
            Composition = new IrComposition { Kind = IrCompositionKind.AllOf, Types = [new IrModelReference { ModelName = baseModel.Name }] },
        };
        var context = IrDocumentContexts.Create(models: [derived, baseModel]);
        var first = new TypeScriptAstBuilder(context, new NamingConverter());
        var second = new TypeScriptAstBuilder(context, new NamingConverter());

        var content = new TypeScriptAstRenderer().Render(first.BuildSourceFile(derived, fileOrganization: new FileOrganization { SeparateModels = true }));

        first.ModelsByName.Should().BeSameAs(context.ModelsByName).And.BeSameAs(second.ModelsByName);
        content.Should().Contain("extends ApiBase").And.Contain("../baseDto/baseDto").And.NotContain("id: string");
    }

    /// <summary>Checks that operation names cannot overwrite declaration identity and alternating transports do not leak state.</summary>
    [Test]
    public void OperationAndTypeNames_HaveIndependentScopes()
    {
        var model = new IrModel { Name = "Shared", OriginalName = "Dto", Properties = [] };
        var operation = new IrOperation
        {
            MethodName = "Shared", OriginalMethodName = "readDto", HttpMethod = "GET", Path = "/value",
            PathParameters = [], QueryParameters = [], HeaderParameters = [],
            Responses = [new IrResponse { StatusCode = "200", Type = new IrModelReference { ModelName = model.Name } }],
        };
        var context = IrDocumentContexts.Create(models: [model]);
        var builder = new TypeScriptAstBuilder(context, new NamingConverter());
        var renderer = new TypeScriptAstRenderer();
        var firstFetch = renderer.Render(builder.BuildSourceFileForOperation(operation, transport: "fetch"));
        var axios = renderer.Render(builder.BuildSourceFileForOperation(operation, transport: "axios"));
        var secondFetch = renderer.Render(builder.BuildSourceFileForOperation(operation, transport: "fetch"));
        var modelSource = renderer.Render(builder.BuildSourceFile(model));

        firstFetch.Should().Be(secondFetch).And.Contain("./readDto.results").And.Contain("options?: RequestInit");
        axios.Should().Contain("options?: AxiosRequestConfig").And.Contain("./readDto.results");
        modelSource.Should().Contain("@file dto.ts");
        context.ModelsByName[model.Name].OriginalName.Should().Be("Dto");
    }
}
