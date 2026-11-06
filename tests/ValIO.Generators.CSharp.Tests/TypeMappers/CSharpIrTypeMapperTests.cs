using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.CSharp.Tests.TypeMappers;

/// <summary>
/// Exhaustive C# IR type-mapper coverage:
/// - all 14 primitives,
/// - composites (arrays, dictionaries, nullable and optional wrappers),
/// - unions and intersections require a faithful representation,
/// - import-target is always null (C# uses namespaces).
/// </summary>
[TestFixture]
public class CSharpIrTypeMapperTests
{
    /// <summary>Maps normalized types using the canonical C# representation.</summary>
    private readonly CSharpIrTypeMapper _mapper = new();

    /// <summary>Creates a primitive IR type for one mapping case.</summary>
    /// <param name="k">The semantic primitive kind.</param>
    /// <returns>The normalized type.</returns>
    private static IrPrimitiveType Prim(IrPrimitiveKind k) => new() { PrimitiveKind = k };

    /// <summary>Maps each semantic primitive to the same type emitted by C# models and clients.</summary>
    /// <param name="kind">The semantic primitive kind.</param>
    /// <param name="expected">The C# type name expected in generated source.</param>
    [TestCase(IrPrimitiveKind.String,    "string")]
    [TestCase(IrPrimitiveKind.Integer,   "int")]
    [TestCase(IrPrimitiveKind.Long,      "long")]
    [TestCase(IrPrimitiveKind.Number,    "double")]
    [TestCase(IrPrimitiveKind.Boolean,   "bool")]
    [TestCase(IrPrimitiveKind.Date,      "DateOnly")]
    [TestCase(IrPrimitiveKind.DateTime,  "DateTime")]
    [TestCase(IrPrimitiveKind.Time,      "TimeOnly")]
    [TestCase(IrPrimitiveKind.Uuid,      "Guid")]
    [TestCase(IrPrimitiveKind.File,      "Stream")]
    [TestCase(IrPrimitiveKind.Any,       "object")]
    [TestCase(IrPrimitiveKind.Void,      "void")]
    public void Primitives_MapToExpectedCSharpType(IrPrimitiveKind kind, string expected)
    {
        var t = (SimpleTypeNode)_mapper.MapType(Prim(kind));
        t.Name.Name.Should().Be(expected);
    }

    /// <summary>Represents byte arrays structurally so visitors can traverse their element type.</summary>
    /// <param name="kind">The binary schema kind.</param>
    [TestCase(IrPrimitiveKind.Binary)]
    [TestCase(IrPrimitiveKind.ByteArray)]
    public void Binary_UsesArraySyntax(IrPrimitiveKind kind)
    {
        var mapped = _mapper.MapType(Prim(kind)).Should().BeOfType<ArrayTypeNode>().Subject;

        mapped.ElementType.Should().BeOfType<SimpleTypeNode>().Which.Name.Name.Should().Be("byte");
    }

    /// <summary>Preserves the array representation emitted by generated C# models.</summary>
    [Test]
    public void Array_UsesTheGeneratedArrayRepresentation()
    {
        var t = new IrArrayType { ElementType = Prim(IrPrimitiveKind.String) };
        var array = (ArrayTypeNode)_mapper.MapType(t);
        ((SimpleTypeNode)array.ElementType).Name.Name.Should().Be("string");
    }

    /// <summary>Uses the shared dictionary syntax node with both mapped argument types.</summary>
    [Test]
    public void Dictionary_UsesTheSharedDictionaryNode()
    {
        var t = new IrDictionaryType
        {
            KeyType = Prim(IrPrimitiveKind.String),
            ValueType = new IrModelReference { ModelName = "Pet" },
        };
        var dictionary = (DictionaryTypeNode)_mapper.MapType(t);
        ((SimpleTypeNode)dictionary.KeyType).Name.Name.Should().Be("string");
        ((SimpleTypeNode)dictionary.ValueType).Name.Name.Should().Be("Pet");
    }

    /// <summary>Preserves an explicitly nullable value type.</summary>
    [Test]
    public void Nullable_WrapsAsNullableType()
    {
        var t = new IrNullableType { InnerType = Prim(IrPrimitiveKind.Integer) };
        _mapper.MapType(t).Should().BeOfType<NullableTypeNode>();
    }

    /// <summary>Keeps optionality separate from the C# value type.</summary>
    [Test]
    public void Optional_StripsWrapper()
    {
        var t = new IrOptionalType { InnerType = Prim(IrPrimitiveKind.Boolean) };
        ((SimpleTypeNode)_mapper.MapType(t)).Name.Name.Should().Be("bool");
    }

    /// <summary>Preserves the normalized model identifier.</summary>
    [Test]
    public void ModelReference_KeepsClassName()
    {
        var t = new IrModelReference { ModelName = "Pet" };
        ((SimpleTypeNode)_mapper.MapType(t)).Name.Name.Should().Be("Pet");
    }

    /// <summary>Rejects an unsupported native union without discarding its matching semantics.</summary>
    [Test]
    public void Union_RequiresAJsonValueRepresentation()
    {
        var t = new IrUnionType { Types = [Prim(IrPrimitiveKind.String), Prim(IrPrimitiveKind.Integer)] };
        var map = () => _mapper.MapType(t);
        map.Should().Throw<NotSupportedException>().WithMessage("*JSON converter*");
    }

    /// <summary>Rejects an intersection instead of silently dropping its second member.</summary>
    [Test]
    public void Intersection_RequiresAComposedModel()
    {
        var t = new IrIntersectionType
        {
            Types = [new IrModelReference { ModelName = "A" }, new IrModelReference { ModelName = "B" }]
        };
        var map = () => _mapper.MapType(t);
        map.Should().Throw<NotSupportedException>().WithMessage("*composed model*");
    }

    /// <summary>Avoids file imports for C# namespace-based references.</summary>
    [Test]
    public void GetImportTarget_AlwaysNull_CSharpUsesNamespaces()
    {
        _mapper.GetImportTarget(new IrModelReference { ModelName = "Pet" }).Should().BeNull();
        _mapper.GetImportTarget(Prim(IrPrimitiveKind.Uuid)).Should().BeNull();
        _mapper.GetImportTarget(new IrArrayType { ElementType = Prim(IrPrimitiveKind.String) }).Should().BeNull();
    }

    /// <summary>Recognizes explicitly optional schema properties.</summary>
    [Test]
    public void IsOptionalInLanguage_OptionalType_True()
    {
        var p = new IrProperty
        {
            Name = "x", OriginalName = "x",
            Type = new IrOptionalType { InnerType = Prim(IrPrimitiveKind.String) },
            IsRequired = true,
        };
        _mapper.IsOptionalInLanguage(p).Should().BeTrue();
    }

    /// <summary>Recognizes properties omitted from the schema required set.</summary>
    [Test]
    public void IsOptionalInLanguage_NotRequired_True()
    {
        var p = new IrProperty
        {
            Name = "x", OriginalName = "x",
            Type = Prim(IrPrimitiveKind.String), IsRequired = false,
        };
        _mapper.IsOptionalInLanguage(p).Should().BeTrue();
    }

    /// <summary>Retains required properties without optional wrappers.</summary>
    [Test]
    public void IsOptionalInLanguage_RequiredAndPlain_False()
    {
        var p = new IrProperty
        {
            Name = "x", OriginalName = "x",
            Type = Prim(IrPrimitiveKind.String), IsRequired = true,
        };
        _mapper.IsOptionalInLanguage(p).Should().BeFalse();
    }
}
