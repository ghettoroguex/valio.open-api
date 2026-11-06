using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders.Std;

/// <summary>Separates property presence from nullability for standalone C++ models and codecs.</summary>
internal static class StdPropertyTypes
{
    /// <summary>Determines whether a JSON object may omit a property's key.</summary>
    /// <param name="property">The property contract.</param>
    /// <returns>Whether the field needs an outer presence wrapper.</returns>
    public static bool IsOptional(IrProperty property)
        => !property.IsRequired || property.Type is IrOptionalType;

    /// <summary>Removes only the property's presence wrapper, retaining nullable value semantics.</summary>
    /// <param name="property">The property contract.</param>
    /// <returns>The type stored when the JSON key is present.</returns>
    public static IrType ValueType(IrProperty property)
        => property.Type is IrOptionalType optional ? optional.InnerType : property.Type;

    /// <summary>Maps a field while preserving distinct absent, null, and concrete values.</summary>
    /// <param name="property">The property contract.</param>
    /// <param name="mapper">The runtime-specific type mapper.</param>
    /// <returns>A field type with separate presence and nullability wrappers when necessary.</returns>
    public static TypeNode MapField(IrProperty property, StdCppTypeMapper mapper)
    {
        var valueType = mapper.MapType(ValueType(property));
        return IsOptional(property)
            ? new GenericTypeNode(new IdentifierNode("std::optional"), [valueType])
            : valueType;
    }
}
