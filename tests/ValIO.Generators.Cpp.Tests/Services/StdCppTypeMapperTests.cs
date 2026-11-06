using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Services;
using ValIO.Generators.Cpp.Tests.TestHelpers;

namespace ValIO.Generators.Cpp.Tests.Services;

/// <summary>
/// Таблица маппинга IrType → std-C++ для std-профиля (вся таблица из плана W3).
/// </summary>
[TestFixture]
public class StdCppTypeMapperTests
{
    /// <summary>Maps types against an explicitly declared enum document.</summary>
    private readonly StdCppTypeMapper _mapper = new(new StdNamingConvention(), CppDocumentContextFactory.Create(enums: [new IrEnum { Name = "OrderStatus", BaseType = IrEnumBaseType.String, Values = [] }]));

    private static IrPrimitiveType Prim(IrPrimitiveKind kind) => new() { PrimitiveKind = kind };

    private string SimpleName(IrType type) => ((SimpleTypeNode)_mapper.MapType(type)).Name.Name;

    /// <summary>Verifies the standalone scalar type mapping, including explicit JSON null.</summary>
    /// <param name="kind">The primitive IR kind.</param>
    /// <param name="expected">The expected standalone C++ declaration type.</param>
    [TestCase(IrPrimitiveKind.String, "std::string")]
    [TestCase(IrPrimitiveKind.Number, "double")]
    [TestCase(IrPrimitiveKind.Integer, "std::int32_t")]
    [TestCase(IrPrimitiveKind.Long, "std::int64_t")]
    [TestCase(IrPrimitiveKind.Boolean, "bool")]
    [TestCase(IrPrimitiveKind.DateTime, "std::string")]
    [TestCase(IrPrimitiveKind.Date, "std::string")]
    [TestCase(IrPrimitiveKind.Time, "std::string")]
    [TestCase(IrPrimitiveKind.Uuid, "std::string")]
    [TestCase(IrPrimitiveKind.Void, "void")]
    [TestCase(IrPrimitiveKind.Null, "std::nullptr_t")]
    public void Primitives_MapToStdTypes(IrPrimitiveKind kind, string expected)
        => SimpleName(Prim(kind)).Should().Be(expected);

    [TestCase(IrPrimitiveKind.Binary)]
    [TestCase(IrPrimitiveKind.ByteArray)]
    [TestCase(IrPrimitiveKind.File)]
    public void BinaryTypes_MapToByteVector(IrPrimitiveKind kind)
    {
        var t = (GenericTypeNode)_mapper.MapType(Prim(kind));
        t.Name.Name.Should().Be("std::vector");
        ((SimpleTypeNode)t.TypeArguments[0]).Name.Name.Should().Be("std::uint8_t");
    }

    [Test]
    public void Any_MapsToString_WithWarning()
    {
        SimpleName(Prim(IrPrimitiveKind.Any)).Should().Be("std::string");
        _mapper.Warnings.Should().Contain(w => w.Contains("Any"));
    }

    [Test]
    public void Array_MapsToVector()
    {
        var t = (GenericTypeNode)_mapper.MapType(new IrArrayType { ElementType = Prim(IrPrimitiveKind.String) });
        t.Name.Name.Should().Be("std::vector");
        ((SimpleTypeNode)t.TypeArguments[0]).Name.Name.Should().Be("std::string");
    }

    [Test]
    public void Dictionary_MapsToUnorderedMap()
    {
        var t = (GenericTypeNode)_mapper.MapType(new IrDictionaryType
        {
            KeyType = Prim(IrPrimitiveKind.String),
            ValueType = Prim(IrPrimitiveKind.Integer),
        });
        t.Name.Name.Should().Be("std::unordered_map");
        t.TypeArguments.Should().HaveCount(2);
    }

    [Test]
    public void Nullable_MapsToOptional_InnerPreserved()
    {
        var t = (GenericTypeNode)_mapper.MapType(new IrNullableType { InnerType = Prim(IrPrimitiveKind.String) });
        t.Name.Name.Should().Be("std::optional");
        ((SimpleTypeNode)t.TypeArguments[0]).Name.Name.Should().Be("std::string");
    }

    [Test]
    public void Optional_WrapsInner_InOptional()
    {
        var t = (GenericTypeNode)_mapper.MapType(new IrOptionalType { InnerType = Prim(IrPrimitiveKind.Long) });
        t.Name.Name.Should().Be("std::optional");
        ((SimpleTypeNode)t.TypeArguments[0]).Name.Name.Should().Be("std::int64_t");
    }

    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    [Test]
    public void ModelReference_MapsToPlainName_StructVsEnum()
    {
        SimpleName(new IrModelReference { ModelName = "BuildingModel" }).Should().Be("BuildingModel");
        SimpleName(new IrModelReference { ModelName = "OrderStatus" }).Should().Be("OrderStatus");
    }

    [Test]
    public void Union_MapsToRawJsonString_WithWarning()
    {
        SimpleName(new IrUnionType { Types = [Prim(IrPrimitiveKind.String), Prim(IrPrimitiveKind.Integer)] })
            .Should().Be("std::string");
        _mapper.Warnings.Should().Contain(w => w.Contains("union"));
    }

    [Test]
    public void Intersection_MapsToRawJsonString_WithWarning()
    {
        SimpleName(new IrIntersectionType { Types = [Prim(IrPrimitiveKind.String)] })
            .Should().Be("std::string");
        _mapper.Warnings.Should().Contain(w => w.Contains("intersection"));
    }

    /// <summary>Verifies explicit scalar initialization, including required JSON null fields.</summary>
    /// <param name="kind">The primitive IR kind.</param>
    /// <param name="expected">The expected initializer, or null for value initialization.</param>
    [TestCase(IrPrimitiveKind.Number, "0.0")]
    [TestCase(IrPrimitiveKind.Integer, "0")]
    [TestCase(IrPrimitiveKind.Long, "0")]
    [TestCase(IrPrimitiveKind.Boolean, "false")]
    [TestCase(IrPrimitiveKind.String, null)]
    [TestCase(IrPrimitiveKind.Null, "nullptr")]
    public void DefaultValue_ForPrimitives(IrPrimitiveKind kind, string? expected)
        => _mapper.GetDefaultValue(Prim(kind)).Should().Be(expected);

    /// <summary>Checks enum wire text dispatch works for both integer-backed and string-backed enums.</summary>
    [Test]
    public void FormatQueryLiteral_BoolIntegerEnum()
    {
        _mapper.FormatQueryLiteral(Prim(IrPrimitiveKind.Boolean), "IncludeDeleted")
            .Should().Be("(IncludeDeleted ? \"true\" : \"false\")");
        _mapper.FormatQueryLiteral(Prim(IrPrimitiveKind.Long), "Id")
            .Should().Be("std::to_string(Id)");
        _mapper.FormatQueryLiteral(new IrModelReference { ModelName = "OrderStatus" }, "Status")
            .Should().Be("ToWireText(Status)");
        _mapper.FormatQueryLiteral(Prim(IrPrimitiveKind.String), "Filter")
            .Should().Be("Filter");
    }

    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    [Test]
    public void FormatQueryLiteral_ModelReference_SerializesToJsonDump()
    {
        // Модель в query-параметре — компилируемое wire-представление: JSON-строка.
        _mapper.FormatQueryLiteral(new IrModelReference { ModelName = "BuildingModel" }, "Filter")
            .Should().Be("ValIO::Dump(Filter)");
    }

    [Test]
    public void StdIncludesFor_CollectsByUsage()
    {
        var type = new IrDictionaryType
        {
            KeyType = Prim(IrPrimitiveKind.String),
            ValueType = new IrOptionalType { InnerType = Prim(IrPrimitiveKind.Long) },
        };

        StdCppTypeMapper.StdIncludesFor(type)
            .Should().BeEquivalentTo(["<cstdint>", "<optional>", "<string>", "<unordered_map>"]);
    }

    /// <summary>Ensures pure-null types have their own C++ type and required declaration header.</summary>
    [Test]
    public void NullContract_RequiresCstddefAndPreservesOptionalWrapper()
    {
        var pureNull = Prim(IrPrimitiveKind.Null);
        StdCppTypeMapper.StdIncludesFor(pureNull).Should().Equal("<cstddef>");
        var optionalNull = (GenericTypeNode)_mapper.MapType(new IrOptionalType { InnerType = pureNull });
        optionalNull.Name.Name.Should().Be("std::optional");
        ((SimpleTypeNode)optionalNull.TypeArguments.Single()).Name.Name.Should().Be("std::nullptr_t");
    }
}
