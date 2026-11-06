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
/// Tests for <see cref="UETypeMapper"/> — IR → UE C++ type mapping
/// (FString / int32 / TArray / TMap / F-prefixed structs, etc.).
/// </summary>
[TestFixture]
public class UETypeMapperTests
{
    /// <summary>Holds the mapper created with an explicit context for each scenario.</summary>
    private UETypeMapper _sut = null!;

    /// <summary>Creates the mapper with a finalized document that declares the tested enum.</summary>
    [SetUp]
    public void Setup() => _sut = new UETypeMapper(new UENamingConvention(), CppDocumentContextFactory.Create(enums: [new IrEnum { Name = "Status", BaseType = IrEnumBaseType.String, Values = [] }]));

    private static IrPrimitiveType Prim(IrPrimitiveKind k) => new() { PrimitiveKind = k };

    [TestCase(IrPrimitiveKind.String,   "FString")]
    [TestCase(IrPrimitiveKind.Integer,  "int32")]
    [TestCase(IrPrimitiveKind.Long,     "int64")]
    [TestCase(IrPrimitiveKind.Number,   "double")]
    [TestCase(IrPrimitiveKind.Boolean,  "bool")]
    [TestCase(IrPrimitiveKind.Date,     "FString")]
    [TestCase(IrPrimitiveKind.DateTime, "FString")]
    [TestCase(IrPrimitiveKind.Time,     "FString")]
    [TestCase(IrPrimitiveKind.Uuid,     "FGuid")]
    [TestCase(IrPrimitiveKind.Any,      "FString")]
    [TestCase(IrPrimitiveKind.Void,     "void")]
    public void Primitives_MapToUeBuiltin(IrPrimitiveKind kind, string expected)
    {
        var t = (SimpleTypeNode)_sut.MapType(Prim(kind));
        t.Name.Name.Should().Be(expected);
    }

    [TestCase(IrPrimitiveKind.Binary)]
    [TestCase(IrPrimitiveKind.ByteArray)]
    [TestCase(IrPrimitiveKind.File)]
    public void Binary_MapsToTArrayOfUint8(IrPrimitiveKind kind)
    {
        var t = (GenericTypeNode)_sut.MapType(Prim(kind));
        t.Name.Name.Should().Be("TArray");
        ((SimpleTypeNode)t.TypeArguments[0]).Name.Name.Should().Be("uint8");
    }

    [Test]
    public void Array_BecomesTArray()
    {
        var t = new IrArrayType { ElementType = Prim(IrPrimitiveKind.Integer) };
        var generic = (GenericTypeNode)_sut.MapType(t);
        generic.Name.Name.Should().Be("TArray");
        ((SimpleTypeNode)generic.TypeArguments[0]).Name.Name.Should().Be("int32");
    }

    [Test]
    public void Dictionary_BecomesTMap()
    {
        var t = new IrDictionaryType
        {
            KeyType = Prim(IrPrimitiveKind.String),
            ValueType = Prim(IrPrimitiveKind.Integer),
        };
        var g = (GenericTypeNode)_sut.MapType(t);
        g.Name.Name.Should().Be("TMap");
        ((SimpleTypeNode)g.TypeArguments[0]).Name.Name.Should().Be("FString");
        ((SimpleTypeNode)g.TypeArguments[1]).Name.Name.Should().Be("int32");
    }

    [Test]
    public void ModelReference_WhenNotEnum_BecomesFStruct()
    {
        var t = new IrModelReference { ModelName = "Pet" };
        var simple = (SimpleTypeNode)_sut.MapType(t);
        simple.Name.Name.Should().Be("FPet");
    }

    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    [Test]
    public void ModelReference_WhenEnum_BecomesEEnum()
    {
        var t = new IrModelReference { ModelName = "Status" };
        var simple = (SimpleTypeNode)_sut.MapType(t);
        simple.Name.Name.Should().Be("EStatus");
    }

    [Test]
    public void Nullable_StripsWrapper_NoTOptional()
    {
        var t = new IrNullableType { InnerType = Prim(IrPrimitiveKind.String) };
        ((SimpleTypeNode)_sut.MapType(t)).Name.Name.Should().Be("FString");
    }

    [Test]
    public void Optional_StripsWrapper()
    {
        var t = new IrOptionalType { InnerType = Prim(IrPrimitiveKind.Boolean) };
        ((SimpleTypeNode)_sut.MapType(t)).Name.Name.Should().Be("bool");
    }

    [Test]
    public void Union_FallsBackToFirstType()
    {
        var t = new IrUnionType { Types = [Prim(IrPrimitiveKind.String), Prim(IrPrimitiveKind.Integer)] };
        ((SimpleTypeNode)_sut.MapType(t)).Name.Name.Should().Be("FString");
    }

    [Test]
    public void Union_Empty_FallsBackToFString()
    {
        var t = new IrUnionType { Types = Array.Empty<IrType>() };
        ((SimpleTypeNode)_sut.MapType(t)).Name.Name.Should().Be("FString");
    }

    [Test]
    public void Intersection_FallsBackToFirstType()
    {
        var t = new IrIntersectionType
        {
            Types = [new IrModelReference { ModelName = "Animal" }, new IrModelReference { ModelName = "Furry" }]
        };
        ((SimpleTypeNode)_sut.MapType(t)).Name.Name.Should().Be("FAnimal");
    }

    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    /// <summary>Verifies reference behavior against the constructor's finalized declaration indexes.</summary>
    [Test]
    public void IsEnumReference_TrueWhenEnumNameSet()
    {
        _sut.IsEnumReference("Status").Should().BeTrue();
        _sut.IsEnumReference("Pet").Should().BeFalse();
    }
}
