using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Tests.TestHelpers;
using ValIO.Generators.TypeScript.TypeBox.Ast.Builders;
using ValIO.Generators.TypeScript.Valibot.Ast.Builders;
using ValIO.Generators.TypeScript.Zod;
using ValIO.Generators.TypeScript.Zod.Ast.Factories;
using ValIO.Generators.TypeScript.Zod.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>Verifies shared document indexes and declaration-order-independent schema imports.</summary>
[TestFixture]
public sealed class ValidatorDocumentContextTests
{
    /// <summary>Resolves forward model and enum references from the same complete document index.</summary>
    /// <param name="validator">The validator whose source builder is tested.</param>
    [TestCase("zod")]
    [TestCase("valibot")]
    [TestCase("typebox")]
    public void ForwardReferences_UseOriginalFileNamesAndSharedIndexes(string validator)
    {
        var order = new IrModel
        {
            Name = "DtoOrder",
            OriginalName = "Order",
            Properties =
            [
                Property("line", new IrModelReference { ModelName = "DtoLine" }),
                Property("state", new IrModelReference { ModelName = "DtoState" })
            ]
        };
        var line = new IrModel { Name = "DtoLine", OriginalName = "Line", Properties = [] };
        var state = new IrEnum
        {
            Name = "DtoState", OriginalName = "State", BaseType = IrEnumBaseType.String,
            Values = [new IrEnumValue { Name = "Ready", Value = "ready" }]
        };
        var context = IrDocumentContexts.Create(models: [order, line], enums: [state]);
        var naming = new NamingConverter();
        AstBuilderBase builder = validator switch
        {
            "zod" => new ZodSchemaRenderer(context, naming, _ => new RawTypeNode("unknown")),
            "valibot" => new ValibotSchemaSourceFileBuilder(context, naming),
            _ => new TypeBoxSchemaSourceFileBuilder(context, naming)
        };

        // Build the dependent schema first: no prior render call can populate a name map.
        var output = builder switch
        {
            ZodSchemaRenderer zod => zod.RenderFromIr(order, currentFileName: "order", schemasPath: "models"),
            ValibotSchemaSourceFileBuilder valibot => new TypeScriptAstRenderer()
                .Render(valibot.BuildModelSchemaSourceFile(order, separateModels: true)),
            TypeBoxSchemaSourceFileBuilder typebox => new TypeScriptAstRenderer()
                .Render(typebox.BuildModelSchemaSourceFile(order, currentFileName: "order", schemasPath: "models")),
            _ => throw new InvalidOperationException("Unsupported validator source builder.")
        };

        builder.ModelsByName.Should().BeSameAs(context.ModelsByName);
        builder.EnumsByName.Should().BeSameAs(context.EnumsByName);
        builder.EnumNames.Should().BeSameAs(context.EnumNames);
        output.Should().Contain("../line/line.schema").And.Contain("../state/state.schema")
            .And.Contain("DtoLineSchema").And.Contain("DtoStateSchema")
            .And.NotContain("dtoLine/").And.NotContain("dtoState/");
    }

    /// <summary>Preserves inheritance resolution when the factory creates its cached schema builder.</summary>
    [Test]
    public void ZodFactory_ReusesDocumentContextForInheritedProperties()
    {
        var inherited = Property("id", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String });
        var baseModel = new IrModel { Name = "Base", Properties = [inherited] };
        var derived = new IrModel
        {
            Name = "Child",
            Properties = [inherited, Property("name", new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String })],
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = [new IrModelReference { ModelName = "Base" }]
            }
        };
        var context = IrDocumentContexts.Create(models: [derived, baseModel]);
        var factory = new ZodAstFactory(context, new NamingConverter(), _ => new RawTypeNode("unknown"));

        var builder = factory.GetBuilder();
        var output = new ZodAstRenderer().RenderSchema(builder.BuildZodSchemaFromIrModel(derived));

        builder.Should().BeSameAs(factory.GetBuilder());
        builder.ModelsByName.Should().BeSameAs(context.ModelsByName);
        output.Should().Contain("BaseSchema").And.Contain("name: z.string()")
            .And.NotContain("id: z.string()");
    }

    /// <summary>Creates a required property for a focused validator source scenario.</summary>
    /// <param name="name">The generated and original property name.</param>
    /// <param name="type">The property's validation type.</param>
    /// <returns>The required IR property.</returns>
    private static IrProperty Property(string name, IrType type) => new()
    {
        Name = name, OriginalName = name, Type = type, IsRequired = true
    };
}
