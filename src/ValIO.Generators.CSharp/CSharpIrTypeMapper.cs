using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.Interfaces;

namespace ValIO.Generators.CSharp;

/// <summary>
/// Maps IR types to C#-specific AST type nodes.
/// </summary>
public sealed class CSharpIrTypeMapper : IIrTypeMapper
{
    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">The IR type is null.</exception>
    /// <exception cref="NotSupportedException">The IR type has no C# representation.</exception>
    /// <exception cref="InvalidOperationException">An intersection contains no types.</exception>
    public TypeNode MapType(IrType irType)
    {
        ArgumentNullException.ThrowIfNull(irType);
        return irType switch
        {
            IrPrimitiveType primitive => MapPrimitive(primitive),
            IrArrayType array => new ArrayTypeNode(MapType(array.ElementType)),
            IrDictionaryType dictionary => new DictionaryTypeNode(MapType(dictionary.KeyType), MapType(dictionary.ValueType)),
            IrNullableType nullable => MapNullable(nullable),
            IrOptionalType optional => MapType(optional.InnerType),
            IrModelReference reference => new SimpleTypeNode(new IdentifierNode(reference.ModelName)),
            IrUnionType { Types.Count: 1 } union => MapType(union.Types[0]),
            IrUnionType => throw new NotSupportedException("C# union values require an explicit result type and JSON converter, which are not implemented."),
            IrIntersectionType { Types.Count: 1 } intersection => MapType(intersection.Types[0]),
            IrIntersectionType { Types.Count: 0 } => throw new InvalidOperationException("Intersection type must have at least one type."),
            IrIntersectionType => throw new NotSupportedException("C# intersection values require a composed model, which cannot be represented by selecting one member."),
            _ => throw new NotSupportedException($"IR type {irType.GetType().Name} is not supported by C#.")
        };
    }

    /// <summary>Maps the primitive kind once for every C# generation entry point.</summary>
    /// <param name="p">The normalized primitive type.</param>
    /// <returns>The C# type used by model and client builders.</returns>
    private static TypeNode MapPrimitive(IrPrimitiveType p) => p.PrimitiveKind switch
    {
        IrPrimitiveKind.String   => new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
        IrPrimitiveKind.Integer  => new SimpleTypeNode(new IdentifierNode("int"), isPrimitive: true),
        IrPrimitiveKind.Long     => new SimpleTypeNode(new IdentifierNode("long"), isPrimitive: true),
        IrPrimitiveKind.Number   => new SimpleTypeNode(new IdentifierNode("double"), isPrimitive: true),
        IrPrimitiveKind.Boolean  => new SimpleTypeNode(new IdentifierNode("bool"), isPrimitive: true),
        IrPrimitiveKind.Date     => new SimpleTypeNode(new IdentifierNode("DateOnly"), isPrimitive: true),
        IrPrimitiveKind.DateTime => new SimpleTypeNode(new IdentifierNode("DateTime"), isPrimitive: true),
        IrPrimitiveKind.Time     => new SimpleTypeNode(new IdentifierNode("TimeOnly"), isPrimitive: true),
        IrPrimitiveKind.Uuid     => new SimpleTypeNode(new IdentifierNode("Guid"), isPrimitive: true),
        IrPrimitiveKind.Binary   => new ArrayTypeNode(new SimpleTypeNode(new IdentifierNode("byte"), isPrimitive: true)),
        IrPrimitiveKind.ByteArray => new ArrayTypeNode(new SimpleTypeNode(new IdentifierNode("byte"), isPrimitive: true)),
        IrPrimitiveKind.File     => new SimpleTypeNode(new IdentifierNode("Stream"), isPrimitive: true),
        IrPrimitiveKind.Any      => new SimpleTypeNode(new IdentifierNode("object")),
        IrPrimitiveKind.Void     => new SimpleTypeNode(new IdentifierNode("void"), isPrimitive: true),
        IrPrimitiveKind.Null     => new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("object"))),
        _                        => throw new NotSupportedException($"Primitive kind {p.PrimitiveKind} is not supported by C#.")
    };

    /// <summary>Builds exactly one nullable wrapper around a mapped type.</summary>
    /// <param name="nullable">The nullable IR type, including nested wrappers.</param>
    /// <returns>A nullable C# type without duplicate question marks.</returns>
    private TypeNode MapNullable(IrNullableType nullable)
    {
        var inner = MapType(nullable.InnerType);
        return inner is NullableTypeNode ? inner : new NullableTypeNode(inner);
    }

    /// <inheritdoc/>
    public bool IsOptionalInLanguage(IrProperty property) =>
        property.Type is IrOptionalType || !property.IsRequired;

    /// <inheritdoc/>
    public string? GetImportTarget(IrType irType) => null; // C# uses namespaces, not imports
}
