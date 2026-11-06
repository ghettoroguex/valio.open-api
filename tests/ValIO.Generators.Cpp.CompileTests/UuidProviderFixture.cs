using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.Cpp.CompileTests;

/// <summary>Creates an explicit UUID contract shared by every native provider acceptance case.</summary>
internal static class UuidProviderFixture
{
    /// <summary>Creates required, nullable, optional, array, and dictionary UUID properties.</summary>
    /// <returns>The language-neutral model contract used by the complete generator pipeline.</returns>
    public static IrModel CreateModel()
    {
        var uuid = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Uuid, Format = "uuid" };
        return new IrModel
        {
            Name = "UuidProbe",
            Properties =
            [
                new IrProperty { Name = "Id", OriginalName = "id", Type = uuid, IsRequired = true },
                new IrProperty { Name = "Ids", OriginalName = "ids", Type = new IrArrayType { ElementType = uuid }, IsRequired = true },
                new IrProperty { Name = "ByName", OriginalName = "by_name", Type = new IrDictionaryType { KeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String }, ValueType = uuid }, IsRequired = true },
                new IrProperty { Name = "NullableId", OriginalName = "nullable_id", Type = new IrNullableType { InnerType = uuid }, IsRequired = true },
                new IrProperty { Name = "OptionalId", OriginalName = "optional_id", Type = uuid, IsRequired = false },
                new IrProperty { Name = "OptionalNullableId", OriginalName = "optional_nullable_id", Type = new IrNullableType { InnerType = uuid }, IsRequired = false },
            ],
        };
    }
}
