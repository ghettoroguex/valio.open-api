using System.Collections.Immutable;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.IR;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>Builds shared AST serialization for multipart and URL-encoded request model bodies.</summary>
public static class TypeScriptFormBody
{
    /// <summary>
    /// Materializes a form model into native multipart data or a URL-encoded text body.
    /// </summary>
    /// <param name="requestBody">The form request body whose model properties define the wire fields.</param>
    /// <param name="bodySource">The validated or raw TypeScript request model expression.</param>
    /// <param name="modelsByName">The request model lookup from the active IR document.</param>
    /// <param name="statements">The endpoint statements that receive the native form construction.</param>
    /// <returns>The native multipart data or encoded text, or <paramref name="bodySource"/> when the model is unavailable.</returns>
    /// <exception cref="ArgumentNullException">One of the required inputs is null.</exception>
    /// <exception cref="NotSupportedException">A URL-encoded form field requires an unspecified binary-to-text encoding.</exception>
    public static ExpressionNode Build(
        IrRequestBody requestBody,
        ExpressionNode bodySource,
        IReadOnlyDictionary<string, IrModel> modelsByName,
        List<StatementNode> statements)
    {
        ArgumentNullException.ThrowIfNull(requestBody);
        ArgumentNullException.ThrowIfNull(bodySource);
        ArgumentNullException.ThrowIfNull(modelsByName);
        ArgumentNullException.ThrowIfNull(statements);

        if (Unwrap(requestBody.Type) is not IrModelReference modelReference
            || !modelsByName.TryGetValue(modelReference.ModelName, out var model))
        {
            return bodySource;
        }

        var formData = Id("formData");
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("formData"),
            type: null,
            initializer: new CallExpressionNode(
                Id(requestBody.IsMultipartFormData ? "FormData" : "URLSearchParams"),
                ImmutableList<ExpressionNode>.Empty,
                isNewExpression: true)));

        var appendStatements = new List<StatementNode>();
        foreach (var property in IrInheritancePlan.FlattenProperties(model, modelsByName))
        {
            var value = Member(bodySource, property.Name);
            var append = BuildMultipartAppendStatement(
                formData,
                property.OriginalName,
                value,
                property.Type,
                requestBody.IsMultipartFormData);
            appendStatements.Add(new IfStatementNode(
                IsPresent(value),
                new BlockNode(ImmutableList.Create<StatementNode>(append))));
        }

        ExpressionNode encodedBody = requestBody.IsMultipartFormData
            ? formData
            : new CallExpressionNode(Member(formData, "toString"), ImmutableList<ExpressionNode>.Empty);
        if (requestBody.IsRequired)
        {
            statements.AddRange(appendStatements);
            return encodedBody;
        }

        statements.Add(new IfStatementNode(
            IsPresent(bodySource),
            new BlockNode(appendStatements.ToImmutableList())));
        return new ConditionalExpressionNode(IsPresent(bodySource), encodedBody, Id("undefined"));
    }

    /// <summary>
    /// Builds one multipart append statement, preserving repeated wire fields for array-valued properties.
    /// </summary>
    /// <param name="formData">The <c>FormData</c> local receiving the value.</param>
    /// <param name="wireName">The original OpenAPI form field name.</param>
    /// <param name="value">The property value expression.</param>
    /// <param name="type">The property type that controls scalar/binary conversion.</param>
    /// <param name="multipart">Whether native binary values are supported by the form encoding.</param>
    /// <returns>A scalar append or a <c>for...of</c> statement for arrays.</returns>
    private static StatementNode BuildMultipartAppendStatement(
        IdentifierExpressionNode formData,
        string wireName,
        MemberAccessExpressionNode value,
        IrType type,
        bool multipart)
    {
        if (TryGetMultipartArrayElementType(type, out var elementType))
        {
            var item = Id("item");
            var appendItem = BuildMultipartAppendCall(formData, wireName, item, elementType, multipart);
            return new ForEachStatementNode(
                new IdentifierNode("item"),
                value,
                new BlockNode(ImmutableList.Create<StatementNode>(new IfStatementNode(
                    IsPresent(item), new BlockNode(ImmutableList.Create<StatementNode>(appendItem))))));
        }

        return BuildMultipartAppendCall(formData, wireName, value, type, multipart);
    }

    /// <summary>
    /// Builds the <c>FormData.append</c> call for one non-null scalar or binary multipart value.
    /// </summary>
    /// <param name="formData">The destination <c>FormData</c> local.</param>
    /// <param name="wireName">The original OpenAPI form field name.</param>
    /// <param name="value">The value expression to append.</param>
    /// <param name="type">The value type that determines binary versus text encoding.</param>
    /// <param name="multipart">Whether native binary values are supported by the form encoding.</param>
    /// <returns>The append expression statement.</returns>
    /// <exception cref="NotSupportedException">A URL-encoded form field uses a native binary type without a textual encoding.</exception>
    private static ExpressionStatementNode BuildMultipartAppendCall(
        IdentifierExpressionNode formData,
        string wireName,
        ExpressionNode value,
        IrType type,
        bool multipart)
    {
        if (!multipart && IsMultipartBinary(type))
            throw new NotSupportedException("Binary URL-encoded fields require an explicit textual encoding; use multipart/form-data for native binary values.");
        ExpressionNode appendValue = multipart && Unwrap(type) is IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.ByteArray }
            ? new CallExpressionNode(Id("Blob"), ImmutableList.Create<ExpressionNode>(
                new ArrayLiteralExpressionNode(ImmutableList.Create<ExpressionNode>(
                    new CallExpressionNode(Id("Uint8Array"), ImmutableList.Create(value), isNewExpression: true)))), isNewExpression: true)
            : multipart && IsMultipartBinary(type)
                ? value
            : IsMultipartStructured(type)
                ? ExpressionBuilders.BuildJsonStringify(value)
                : new CallExpressionNode(Id("String"), ImmutableList.Create(value));
        return new ExpressionStatementNode(new CallExpressionNode(
            Member(formData, "append"),
            ImmutableList.Create<ExpressionNode>(Str(wireName), appendValue)));
    }

    /// <summary>
    /// Determines whether the supplied multipart property must be appended as a binary value rather than converted
    /// to text.
    /// </summary>
    /// <param name="type">The property type from the operation model.</param>
    /// <returns><see langword="true"/> for binary/file values; otherwise <see langword="false"/>.</returns>
    private static bool IsMultipartBinary(IrType type)
    {
        type = Unwrap(type);

        return type is IrPrimitiveType
        {
            PrimitiveKind: IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray or IrPrimitiveKind.File,
        };
    }

    /// <summary>
    /// Determines whether a multipart field has structured JSON semantics rather than scalar text semantics.
    /// </summary>
    /// <param name="type">The property or array-element type to inspect.</param>
    /// <returns><see langword="true"/> when the field must be serialized with <c>JSON.stringify</c>.</returns>
    private static bool IsMultipartStructured(IrType type)
    {
        type = Unwrap(type);

        return type is IrModelReference or IrDictionaryType or IrUnionType or IrIntersectionType;
    }

    /// <summary>
    /// Resolves the element type of a nullable/optional multipart array.
    /// </summary>
    /// <param name="type">The property type to inspect.</param>
    /// <param name="elementType">The array element type when the property is an array.</param>
    /// <returns><see langword="true"/> when <paramref name="type"/> is an array; otherwise <see langword="false"/>.</returns>
    private static bool TryGetMultipartArrayElementType(IrType type, out IrType elementType)
    {
        type = Unwrap(type);

        if (type is IrArrayType array)
        {
            elementType = array.ElementType;
            return true;
        }

        elementType = type;
        return false;
    }

    /// <summary>Unwraps any ordering of optional and nullable wrappers.</summary>
    /// <param name="type">The wrapped type.</param>
    /// <returns>The innermost type.</returns>
    private static IrType Unwrap(IrType type) => type switch
    {
        IrNullableType nullable => Unwrap(nullable.InnerType),
        IrOptionalType optional => Unwrap(optional.InnerType),
        _ => type,
    };

    /// <summary>Builds a presence check that excludes null and undefined values.</summary>
    /// <param name="value">The value to check.</param>
    /// <returns>The presence condition.</returns>
    private static ExpressionNode IsPresent(ExpressionNode value) =>
        new BinaryExpressionNode(
            new BinaryExpressionNode(value, BinaryOperator.NotEqual, Id("undefined")),
            BinaryOperator.LogicalAnd,
            new BinaryExpressionNode(value, BinaryOperator.NotEqual, Id("null")));

    /// <summary>Creates an identifier expression for a generated local or runtime global.</summary>
    /// <param name="name">The identifier name.</param>
    /// <returns>The identifier expression.</returns>
    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    /// <summary>Creates a member access expression.</summary>
    /// <param name="source">The object to access.</param>
    /// <param name="member">The sanitized member name.</param>
    /// <returns>The member expression.</returns>
    private static MemberAccessExpressionNode Member(ExpressionNode source, string member) =>
        new(source, new IdentifierNode(member));

    /// <summary>Creates a string literal expression.</summary>
    /// <param name="value">The unescaped string.</param>
    /// <returns>The string literal.</returns>
    private static LiteralExpressionNode Str(string value) => new(value, LiteralKind.String);
}
