using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Generators.Cpp.Services;

/// <summary>Shares typed JSON wire-format selection between model codecs and HTTP request/response codecs.</summary>
public static class StdJsonWireFormat
{
    /// <summary>Builds a scoped runtime format argument from the schema's leaf representation.</summary>
    /// <param name="type">The schema type, including nullable and container wrappers.</param>
    /// <returns>The structured <c>ValIO::JsonStringFormat</c> enum member expression.</returns>
    /// <remarks>Model references validate their own fields and therefore do not inherit a container's leaf format.</remarks>
    public static ExpressionNode BuildArgument(IrType type)
    {
        return new MemberAccessExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("ValIO::JsonStringFormat")),
            new IdentifierNode(Name(type)), useScopeOperator: true);
    }

    /// <summary>Resolves the leaf string representation without inspecting rendered language types.</summary>
    /// <param name="type">The schema type to inspect.</param>
    /// <returns>The runtime enum member name.</returns>
    public static string Name(IrType type) => type switch
    {
        IrNullableType nullable => Name(nullable.InnerType),
        IrOptionalType optional => Name(optional.InnerType),
        IrArrayType array => Name(array.ElementType),
        IrDictionaryType dictionary => Name(dictionary.ValueType),
        IrPrimitiveType primitive => primitive.PrimitiveKind switch
        {
            IrPrimitiveKind.String when primitive.Format == "duration" => "Duration",
            IrPrimitiveKind.String when primitive.Format is "time-span" or "timespan" => "TimeSpan",
            IrPrimitiveKind.Date => "Date",
            IrPrimitiveKind.Time => "Time",
            IrPrimitiveKind.DateTime => "DateTime",
            IrPrimitiveKind.Uuid => "Uuid",
            IrPrimitiveKind.ByteArray => "Base64",
            IrPrimitiveKind.Binary or IrPrimitiveKind.File => "Binary",
            _ => "Plain",
        },
        _ => "Plain",
    };
}
