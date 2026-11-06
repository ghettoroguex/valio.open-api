using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Generators.Python.Ast.Builders;
using ValIO.Generators.Python.Ast.Renderers;
using ValIO.Generators.Python.Options;
using ValIO.Generators.Python.Tests.TestHelpers;

namespace ValIO.Generators.Python.Tests.Integration;

/// <summary>Checks that model and client builders share indexes while isolating same-named document declarations.</summary>
[TestFixture]
public sealed class PythonBuilderContextTests
{
    /// <summary>Resolves the same generated reference as a model or enum using only its own document context.</summary>
    /// <returns>A task completing after declaration classification, module paths, and index sharing are checked.</returns>
    [Test]
    public async Task Builders_IsolateSameNamedModelAndEnumAcrossDocuments()
    {
        using var harness = new PythonPipelineTestHarness();
        var source = await PythonPipelineTestHarness.LoadDocumentAsync("simple-schema.json");
        var baseline = await harness.RunAsync(source);
        var stateReference = new IrModelReference { ModelName = "State" };
        var stateModel = baseline.IrDocument.Models[0] with
        {
            Name = "State", OriginalName = "FirstPayload", Properties = [],
        };
        var owner = stateModel with
        {
            Name = "Owner", OriginalName = "Owner",
            Properties = [new IrProperty { Name = "State", OriginalName = "state", Type = stateReference, IsRequired = true }],
        };
        var stateEnum = baseline.IrDocument.Enums[0] with { Name = "State", OriginalName = "StatusWire" };
        var modelContext = new IrDocumentContext(baseline.IrDocument with { Models = [stateModel, owner], Enums = [] });
        var enumContext = new IrDocumentContext(baseline.IrDocument with { Models = [owner], Enums = [stateEnum] });
        var naming = new NamingConverter();
        var mapper = new PythonIrTypeMapper();
        var modelBuilder = new PydanticModelAstBuilder(modelContext, naming, mapper);
        var modelClientBuilder = new HttpxClientAstBuilder(modelContext, naming, mapper);
        var enumBuilder = new PydanticModelAstBuilder(enumContext, naming, mapper);
        var enumClientBuilder = new HttpxClientAstBuilder(enumContext, naming, mapper);
        modelBuilder.ModelsByName.Should().BeSameAs(modelContext.ModelsByName);
        modelClientBuilder.ModelsByName.Should().BeSameAs(modelBuilder.ModelsByName);
        enumBuilder.EnumsByName.Should().BeSameAs(enumContext.EnumsByName);
        enumClientBuilder.EnumsByName.Should().BeSameAs(enumBuilder.EnumsByName);
        enumClientBuilder.EnumNames.Should().BeSameAs(enumContext.EnumNames);

        var operation = baseline.IrDocument.Clients[0].Operations[0] with
        {
            MethodName = "GetState", Path = "/state", PathParameters = [], QueryParameters = [], HeaderParameters = [],
            RequestBody = null, Responses = [new IrResponse { StatusCode = "200", Type = stateReference }],
        };
        var client = baseline.IrDocument.Clients[0] with { Operations = [operation] };
        var renderer = new PythonAstRenderer();
        renderer.Render(modelBuilder.BuildModule(owner)).Should().Contain("from .first_payload import State");
        renderer.Render(enumBuilder.BuildModule(owner)).Should().Contain("from .status_wire import State");
        var modelClient = renderer.Render(modelClientBuilder.BuildModule(client, new PythonGenerationOptions()));
        var enumClient = renderer.Render(enumClientBuilder.BuildModule(client, new PythonGenerationOptions()));
        modelClient.Should().Contain("from ..models.first_payload import State");
        modelClient.Should().Contain("State.model_validate(response.json())");
        enumClient.Should().Contain("from ..models.status_wire import State");
        enumClient.Should().NotContain("State.model_validate");
        enumClient.Should().Contain("TypeAdapter(get_type_hints(self.get_state)");
    }
}
