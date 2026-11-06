using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Tests.Builders.Std;

/// <summary>
/// Тесты std-билдеров W4: чистая модель (без следов JSON), enum + wire-маппинг,
/// JSON-кодек (единственное место с wire-именами), наследование allOf, рекурсия.
/// </summary>
[TestFixture]
public class StdBuildersTests
{
    private readonly CppAstRenderer _renderer = new();
    private readonly StdNamingConvention _naming = new();
    private readonly CppGenerationOptions _options = new();

    /// <summary>Creates finalized declaration indexes for one focused builder scenario.</summary>
    /// <param name="models">The model declarations used by the scenario.</param>
    /// <param name="enumNames">The enum names referenced by the scenario.</param>
    /// <returns>The document context shared by the mapper and builder.</returns>
    private static IrDocumentContext CreateContext(IEnumerable<IrModel> models, IEnumerable<string>? enumNames = null) => new(new IrDocument
    {
        Metadata = new IrDocumentMetadata(), Models = models.ToList(), Clients = [],
        Enums = (enumNames ?? []).Select(name => new IrEnum { Name = name, BaseType = IrEnumBaseType.String, Values = [] }).ToList(),
        TypeDependencies = new IrTypeDependencyGraph { Dependencies = new Dictionary<string, IReadOnlyList<string>>(), TopologicalOrder = [] },
    });

    private string Render(ValIO.Core.Ast.Nodes.AstNode node)
        => _renderer.Render(node).Replace("\r\n", "\n");

    private static IrProperty Prop(string originalName, string pascalName, IrPrimitiveKind kind, bool required = true) => new()
    {
        OriginalName = originalName,
        Name = pascalName,
        Type = new IrPrimitiveType { PrimitiveKind = kind },
        IsRequired = required,
    };

    // ===== Модель: чистый хедер =====

    [Test]
    public void Model_HeaderIsJsonFree_StdTypesAndOptional()
    {
        var model = new IrModel
        {
            Name = "Order",
            OriginalName = "orderDto",
            Properties =
            [
                Prop("buildingId", "BuildingId", IrPrimitiveKind.Long),
                Prop("displayName", "DisplayName", IrPrimitiveKind.String, required: false),
                Prop("count", "Count", IrPrimitiveKind.Integer),
            ],
        };

        var context = CreateContext([model]);
        var builder = new StdModelAstBuilder(new StdCppTypeMapper(_naming, context), _naming, context);
        var content = Render(builder.Build(model, "Petstore"));

        content.Should().Contain("struct Order");
        content.Should().Contain("std::int64_t BuildingId = 0;");
        content.Should().Contain("std::optional<std::string> DisplayName;");
        content.Should().Contain("std::int32_t Count = 0;");
        content.Should().Contain("namespace Petstore");

        // Чистота: ни JSON, ни third-party — только std-инклуды.
        content.Should().NotContainAny(["nlohmann", "json", "to_json", "from_json"]);
        content.Should().Contain("#include <cstdint>");
        content.Should().Contain("#include <optional>");
        content.Should().Contain("#include <string>");
        content.Should().NotContain("#include \"");
    }

    [Test]
    public void Model_NestedReferences_AddQuotedIncludes()
    {
        var models = new Dictionary<string, IrModel>
        {
            ["Customer"] = new() { Name = "Customer", Properties = [Prop("name", "Name", IrPrimitiveKind.String)] },
        };
        var order = new IrModel
        {
            Name = "Order",
            Properties =
            [
                new IrProperty
                {
                    OriginalName = "buyer", Name = "Buyer",
                    Type = new IrModelReference { ModelName = "Customer" },
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
        var enumNames = new HashSet<string> { "OrderStatus" };

        var context = CreateContext(models.Values.Append(order), enumNames);
        var builder = new StdModelAstBuilder(new StdCppTypeMapper(_naming, context), _naming, context);
        var content = Render(builder.Build(order, "Petstore"));

        content.Should().Contain("#include \"Models/Customer.h\"");
        content.Should().Contain("#include \"Enums/OrderStatus.h\"");
        content.Should().Contain("std::optional<Customer> Buyer;");
        content.Should().Contain("OrderStatus Status = Petstore::OrderStatus();");
    }

    [Test]
    public void Model_AllOfSingleRefBase_RendersInheritance_OnlyOwnFields()
    {
        var baseModel = new IrModel
        {
            Name = "Animal",
            Properties = [Prop("name", "Name", IrPrimitiveKind.String)],
        };
        var derived = new IrModel
        {
            Name = "Dog",
            Properties =
            [
                Prop("name", "Name", IrPrimitiveKind.String), // приходит из flattening
                Prop("breed", "Breed", IrPrimitiveKind.String),
            ],
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = [new IrModelReference { ModelName = "Animal" }],
            },
        };
        var lookup = new Dictionary<string, IrModel> { ["Animal"] = baseModel };

        var context = CreateContext(lookup.Values.Append(derived));
        var builder = new StdModelAstBuilder(new StdCppTypeMapper(_naming, context), _naming, context);
        var content = Render(builder.Build(derived, "Petstore"));

        content.Should().Contain("struct Dog : public Animal");
        content.Should().Contain("std::string Breed;");
        content.Should().NotContain("std::string Name;"); // унаследовано — не переобъявляется
        content.Should().Contain("#include \"Models/Animal.h\"");
    }

    /// <summary>Rejects unsupported recursive value ownership instead of publishing a truncated model contract.</summary>
    [Test]
    public void Model_RecursiveProperty_ThrowsExplicitDiagnostic()
    {
        var model = new IrModel
        {
            Name = "Node",
            Properties =
            [
                Prop("id", "Id", IrPrimitiveKind.Long),
                new IrProperty
                {
                    OriginalName = "child", Name = "Child",
                    Type = new IrModelReference { ModelName = "Node" },
                    IsRequired = false,
                },
            ],
        };

        var context = CreateContext([model]);
        var builder = new StdModelAstBuilder(new StdCppTypeMapper(_naming, context), _naming, context);
        var warnings = new List<string>();
        var build = () => builder.Build(model, "Petstore", warnings);
        build.Should().Throw<NotSupportedException>().WithMessage("*Node*child*recursive ownership*");
    }

    // ===== Кодек модели =====

    /// <summary>Checks wire-name preservation and optional presence rather than nullable conflation.</summary>
    [Test]
    public void ModelJson_WireNamesFromOriginalName_SemanticsRequiredOptional()
    {
        var model = new IrModel
        {
            Name = "BuildingModel",
            OriginalName = "buildingModel",
            Properties =
            [
                Prop("buildingId", "BuildingId", IrPrimitiveKind.Long),
                Prop("displayName", "DisplayName", IrPrimitiveKind.String, required: false),
            ],
        };

        var context = CreateContext([model]);
        var builder = new StdModelJsonAstBuilder(new StdCppTypeMapper(_naming, context), _naming, context);
        var (header, source) = builder.Build(model, "Petstore", _options);

        var headerContent = Render(header);
        headerContent.Should().Contain("#include <nlohmann/json_fwd.hpp>");
        headerContent.Should().Contain("#include \"Models/BuildingModel.h\"");
        headerContent.Should().Contain("void to_json(nlohmann::json& j, const BuildingModel& value);");
        headerContent.Should().Contain("void from_json(const nlohmann::json& j, BuildingModel& value);");

        var sourceContent = Render(source);
        // wire-имена — OriginalName (не sanitized C++-имена!)
        sourceContent.Should().Contain("j[\"buildingId\"] = ValIO::WriteJson(value.BuildingId, ValIO::JsonStringFormat::Plain);");
        sourceContent.Should().Contain("value.BuildingId = ValIO::ReadJson<std::int64_t>(j.at(\"buildingId\"), ValIO::JsonStringFormat::Plain);");
        sourceContent.Should().Contain("if (value.DisplayName)");
        sourceContent.Should().Contain("j[\"displayName\"] = ValIO::WriteJson(*value.DisplayName, ValIO::JsonStringFormat::Plain);");
        sourceContent.Should().Contain("if (j.contains(\"displayName\"))");
        sourceContent.Should().Contain("value.DisplayName.emplace(ValIO::ReadJson<std::string>(j.at(\"displayName\"), ValIO::JsonStringFormat::Plain));");
        sourceContent.Should().Contain("value.DisplayName.reset();");
        // полный nlohmann — только в исходнике
        sourceContent.Should().Contain($"#include <{_options.JsonInclude}>");
    }

    [Test]
    public void ModelJson_Inheritance_CascadesThroughStaticCast()
    {
        var baseModel = new IrModel { Name = "Animal", Properties = [Prop("name", "Name", IrPrimitiveKind.String)] };
        var derived = new IrModel
        {
            Name = "Dog",
            Properties = [Prop("breed", "Breed", IrPrimitiveKind.String)],
            Composition = new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = [new IrModelReference { ModelName = "Animal" }],
            },
        };
        var lookup = new Dictionary<string, IrModel> { ["Animal"] = baseModel };

        var context = CreateContext(lookup.Values.Append(derived));
        var builder = new StdModelJsonAstBuilder(new StdCppTypeMapper(_naming, context), _naming, context);
        var (_, source) = builder.Build(derived, "Petstore", _options);
        var content = Render(source);

        content.Should().Contain("to_json(j, static_cast<const Animal&>(value));");
        content.Should().Contain("from_json(j, static_cast<Animal&>(value));");
        content.Should().Contain("#include \"Serializers/AnimalJson.h\"");
    }

    [Test]
    public void ModelJson_NestedModelAndEnum_IncludeTheirCodecs()
    {
        var order = new IrModel
        {
            Name = "Order",
            Properties =
            [
                new IrProperty
                {
                    OriginalName = "status", Name = "Status",
                    Type = new IrModelReference { ModelName = "OrderStatus" },
                    IsRequired = true,
                },
                new IrProperty
                {
                    OriginalName = "buyer", Name = "Buyer",
                    Type = new IrModelReference { ModelName = "Customer" },
                    IsRequired = true,
                },
            ],
        };
        var lookup = new Dictionary<string, IrModel> { ["Customer"] = new() { Name = "Customer", Properties = [] } };
        var enumNames = new HashSet<string> { "OrderStatus" };

        var context = CreateContext(lookup.Values.Append(order), enumNames);
        var builder = new StdModelJsonAstBuilder(new StdCppTypeMapper(_naming, context), _naming, context);
        var (_, source) = builder.Build(order, "Petstore", _options);
        var content = Render(source);

        content.Should().Contain("#include \"Serializers/CustomerJson.h\"");
        content.Should().Contain("#include \"Serializers/OrderStatusJson.h\"");
        content.Should().Contain("value.Status = ValIO::ReadJson<OrderStatus>(j.at(\"status\"), ValIO::JsonStringFormat::Plain);");
    }

    // ===== Enum =====

    /// <summary>Checks enum-qualified parsers so multiple string enums can coexist in one namespace.</summary>
    [Test]
    public void Enum_StringBase_WireMappingWithoutJson()
    {
        var irEnum = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values =
            [
                new IrEnumValue { Name = "placed", Value = "placed" },
                new IrEnumValue { Name = "approved", Value = "approved" },
            ],
        };

        var builder = new StdEnumAstBuilder(_naming);
        var content = Render(builder.Build(irEnum, "Petstore"));

        content.Should().Contain("enum class OrderStatus : std::int32_t");
        content.Should().Contain("Placed = 0,");
        content.Should().Contain("Approved = 1");
        content.Should().NotContainAny(["ToWire", "FromWire", "to_json", "from_json"]);
        // чистота: без JSON
        content.Should().NotContainAny(["nlohmann", "json"]);
        content.Should().NotContain("#include <string_view>");
    }

    /// <summary>Checks integer enum values and their collision-free parser signature.</summary>
    [Test]
    public void Enum_IntegerBases_WireNumbers()
    {
        var irEnum = new IrEnum
        {
            Name = "Level",
            BaseType = IrEnumBaseType.Integer,
            Values =
            [
                new IrEnumValue { Name = "low", Value = 1 },
                new IrEnumValue { Name = "high", Value = 2 },
            ],
        };

        var builder = new StdEnumAstBuilder(_naming);
        var content = Render(builder.Build(irEnum, "Petstore"));

        content.Should().Contain("Low = 1,");
        content.Should().Contain("High = 2");
        content.Should().NotContainAny(["ToWire", "FromWire"]);
        var (_, source) = builder.BuildJson(irEnum, "Petstore", _options);
        Render(source).Should().Contain("std::int32_t ToWire(Level v)");
        Render(source).Should().Contain("std::optional<Level> LevelFromWire(std::int32_t w)");
        Render(source).Should().Contain("if (w == 2)");
    }

    /// <summary>Checks JSON codecs call the enum-specific parser and reject unknown wire values.</summary>
    [Test]
    public void EnumJson_CodecUsesToWireFromWireAndThrowsOnUnknown()
    {
        var irEnum = new IrEnum
        {
            Name = "OrderStatus",
            BaseType = IrEnumBaseType.String,
            Values = [new IrEnumValue { Name = "placed", Value = "placed" }],
        };

        var builder = new StdEnumAstBuilder(_naming);
        var (header, source) = builder.BuildJson(irEnum, "Petstore", _options);

        var headerContent = Render(header);
        headerContent.Should().Contain("#include <nlohmann/json_fwd.hpp>");
        headerContent.Should().Contain("#include \"Enums/OrderStatus.h\"");
        headerContent.Should().Contain("void to_json(nlohmann::json& j, const OrderStatus& value);");
        headerContent.Should().Contain("void from_json(const nlohmann::json& j, OrderStatus& value);");

        var sourceContent = Render(source);
        sourceContent.Should().Contain("j = ToWire(value);");
        sourceContent.Should().Contain("OrderStatusFromWire(ValIO::ReadJson<std::string>(j))");
        sourceContent.Should().Contain("if (parsed)");
        sourceContent.Should().Contain("value = *parsed;");
        sourceContent.Should().Contain("throw nlohmann::json::out_of_range::create(1701, \"unknown OrderStatus wire value\", nullptr);");
    }
}
