using System.Text.Json;
using Microsoft.OpenApi;

namespace ValIO.Core.IR.Utilities;

/// <summary>Proves when JSON null is disjoint from a composition alternative before nullable normalization.</summary>
/// <remarks>Unknown, untyped and cyclic schemas remain unproven. Properties alone do not exclude null.</remarks>
internal static class OpenApiNullability
{
    /// <summary>Reads explicit JSON Schema and legacy OpenAPI null allowances.</summary>
    /// <param name="schema">The source schema.</param>
    /// <returns>Whether the type or legacy nullable extension explicitly permits null.</returns>
    public static bool HasExplicitNullAllowance(IOpenApiSchema schema)
        => schema.Type?.HasFlag(JsonSchemaType.Null) == true ||
           schema.Extensions?.TryGetValue(OpenApiConstants.NullableExtension, out var extension) == true &&
           extension is JsonNodeExtension { Node: { } node } && node.GetValueKind() == JsonValueKind.True;

    /// <summary>Recognizes a bare null alternative without predicates that could reject null.</summary>
    /// <param name="schema">The alternative to inspect.</param>
    /// <returns>Whether this alternative can safely become a nullable wrapper.</returns>
    public static bool IsNullOnly(IOpenApiSchema schema)
        => schema.Type == JsonSchemaType.Null &&
           schema is not IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> &&
           schema.AllOf is not { Count: > 0 } && schema.AnyOf is not { Count: > 0 } &&
           schema.OneOf is not { Count: > 0 } && schema.Enum is not { Count: > 0 } &&
           schema.Not == null && schema.Const == null && schema.Properties is not { Count: > 0 };

    /// <summary>Conservatively proves that an alternative cannot match null, following local references.</summary>
    /// <param name="schema">The alternative whose null match must be ruled out.</param>
    /// <param name="document">The document that owns component references.</param>
    /// <returns>True only when a structural predicate proves that null is rejected.</returns>
    public static bool RejectsNull(IOpenApiSchema schema, OpenApiDocument document)
        => RejectsNull(schema, document, new HashSet<IOpenApiSchema>(ReferenceEqualityComparer.Instance));

    /// <summary>Traverses composition predicates with path-local cycle detection.</summary>
    /// <param name="schema">The current predicate.</param>
    /// <param name="document">The owning document.</param>
    /// <param name="visiting">Schemas on the current reference/composition path.</param>
    /// <returns>Whether null rejection has been proved without assuming unknown predicates.</returns>
    private static bool RejectsNull(IOpenApiSchema schema, OpenApiDocument document, HashSet<IOpenApiSchema> visiting)
    {
        if (!visiting.Add(schema))
            return false;

        try
        {
            if (schema is IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> holder)
            {
                if (holder.Reference is { IsExternal: false, Id: { } id } &&
                    document.Components?.Schemas?.TryGetValue(id, out var target) == true)
                    return RejectsNull(target, document, visiting);
                return false;
            }

            if (HasExplicitNullAllowance(schema))
                return false;
            if (schema.Type is { } type && type != default)
                return true;
            if (schema.AllOf?.Any(member => RejectsNull(member, document, visiting)) == true)
                return true;
            if (schema.AnyOf is { Count: > 0 } anyOf && anyOf.All(member => RejectsNull(member, document, visiting)))
                return true;
            return schema.OneOf is { Count: > 0 } oneOf && oneOf.All(member => RejectsNull(member, document, visiting));
        }
        finally
        {
            visiting.Remove(schema);
        }
    }
}
