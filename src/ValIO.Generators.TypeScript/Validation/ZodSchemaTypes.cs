using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;

namespace ValIO.Generators.TypeScript.Validation;

/// <summary>Builds version-specific Zod output annotations while accepting untrusted input.</summary>
internal static class ZodSchemaTypes
{
    /// <summary>Creates a Zod schema type with an explicit unknown input contract.</summary>
    /// <param name="output">The validated output type.</param>
    /// <param name="importPath">The selected Zod major's module path.</param>
    /// <returns>A schema annotation preserving output checking without restricting external input.</returns>
    public static TypeNode Build(TypeNode output, string importPath)
    {
        var unknown = new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true);
        return new GenericTypeNode(new IdentifierNode("ZodSchema"), importPath == "zod/v4"
            ? [output, unknown]
            : [output, new SimpleTypeNode(new IdentifierNode("z.ZodTypeDef")), unknown]);
    }
}
