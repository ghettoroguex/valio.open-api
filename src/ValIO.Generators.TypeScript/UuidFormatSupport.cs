using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.TypeScript;

/// <summary>
/// Helpers for the <c>uuidFormat = regex</c> mode: detect whether a model/operation/document uses
/// any <see cref="IrPrimitiveKind.Uuid"/> field, so the shared <c>schemas/formats.ts</c> constant is
/// only generated/imported where it is actually needed.
/// </summary>
public static class UuidFormatSupport
{
    /// <summary>True if the IR type tree contains a uuid primitive (recurses containers, not model refs).</summary>
    public static bool ContainsUuid(IrType? type)
    {
        switch (type)
        {
            case IrPrimitiveType primitive:
                return primitive.PrimitiveKind == IrPrimitiveKind.Uuid;
            case IrArrayType array:
                return ContainsUuid(array.ElementType);
            case IrDictionaryType dict:
                return ContainsUuid(dict.KeyType) || ContainsUuid(dict.ValueType);
            case IrUnionType union:
                return union.Types.Any(ContainsUuid);
            case IrIntersectionType inter:
                return inter.Types.Any(ContainsUuid);
            case IrNullableType nullable:
                return ContainsUuid(nullable.InnerType);
            case IrOptionalType optional:
                return ContainsUuid(optional.InnerType);
            default:
                return false;
        }
    }

    /// <summary>True if any property (or composition member) of the model is a uuid.</summary>
    public static bool ModelUsesUuid(IrModel model)
    {
        if (model.Properties.Any(p => ContainsUuid(p.Type)))
            return true;
        return model.Composition != null && model.Composition.Types.Any(ContainsUuid);
    }

    /// <summary>Checks path, query and header parameters for inline UUID types.</summary>
    /// <param name="operation">The operation to inspect.</param>
    /// <returns>Whether any parameter contains a UUID type.</returns>
    public static bool OperationParamsUseUuid(IrOperation operation)
        => operation.PathParameters.Any(p => ContainsUuid(p.Type))
        || operation.QueryParameters.Any(p => ContainsUuid(p.Type))
        || operation.HeaderParameters.Any(p => ContainsUuid(p.Type));

    /// <summary>Checks models, parameters, request bodies and responses for inline UUID types.</summary>
    /// <param name="document">The document to inspect.</param>
    /// <returns>Whether the generated validation may need the shared UUID format.</returns>
    public static bool DocumentUsesUuid(IrDocument document)
        => document.Models.Any(ModelUsesUuid)
        || document.Clients.Any(c => c.Operations.Any(operation => OperationParamsUseUuid(operation)
            || ContainsUuid(operation.RequestBody?.Type)
            || operation.Responses.Any(response => ContainsUuid(response.Type))));
}
