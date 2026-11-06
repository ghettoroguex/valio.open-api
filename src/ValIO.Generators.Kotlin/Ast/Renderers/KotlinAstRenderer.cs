using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.Kotlin.Ast.Nodes;
using ValIO.Generators.Kotlin.Ast.Visitors;

namespace ValIO.Generators.Kotlin.Ast.Renderers;

/// <summary>
/// Renders Kotlin AST nodes to source text. This is the only place in the
/// Kotlin plugin allowed to use a <see cref="StringBuilder"/> (AST-first rule).
/// </summary>
public sealed class KotlinAstRenderer : KotlinAstVisitorBase<string>, IKotlinAstRenderer
{
    private const int IndentSize = 4;

    public string Render(KotlinFileNode file) => VisitKotlinFile(file);

    public override string VisitKotlinFile(KotlinFileNode node)
    {
        var sb = new StringBuilder();

        if (node.FileDoc != null && !string.IsNullOrWhiteSpace(node.FileDoc.Text))
        {
            AppendKDoc(sb, indent: 0, node.FileDoc.Text);
            sb.AppendLine();
        }

        sb.AppendLine($"package {node.Package}");
        sb.AppendLine();

        if (node.Imports.Count > 0)
        {
            // Stable ordering for deterministic output.
            var imports = node.Imports
                .OrderBy(i => i.FqName, StringComparer.Ordinal)
                .ToList();
            foreach (var imp in imports)
                sb.AppendLine(VisitKotlinImport(imp));
            sb.AppendLine();
        }

        for (var i = 0; i < node.Declarations.Count; i++)
        {
            var rendered = RenderDeclaration(node.Declarations[i], indent: 0);
            sb.Append(rendered);
            if (!rendered.EndsWith("\n", StringComparison.Ordinal))
                sb.AppendLine();
            if (i < node.Declarations.Count - 1)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    public override string VisitKotlinImport(KotlinImportNode node) =>
        node.Alias != null
            ? $"import {node.FqName} as {node.Alias}"
            : $"import {node.FqName}";

    public override string VisitKotlinAnnotation(KotlinAnnotationNode node) =>
        node.Arguments.Count == 0
            ? $"@{node.Name}"
            : $"@{node.Name}({string.Join(", ", node.Arguments)})";

    public override string VisitKotlinDataClass(KotlinDataClassNode node) =>
        RenderDataClass(node, indent: 0);

    private string RenderDataClass(KotlinDataClassNode node, int indent)
    {
        var sb = new StringBuilder();
        var prefix = Indent(indent);

        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
            AppendKDoc(sb, indent, node.Documentation.Text);

        foreach (var ann in node.KotlinAnnotations)
            sb.AppendLine($"{prefix}{VisitKotlinAnnotation(ann)}");

        if (node.Properties.Count == 0)
        {
            sb.AppendLine($"{prefix}data class {node.Name.Name}");
            return sb.ToString();
        }

        sb.AppendLine($"{prefix}data class {node.Name.Name}(");

        var propIndent = indent + 1;
        for (var i = 0; i < node.Properties.Count; i++)
        {
            var prop = node.Properties[i];
            // Trailing comma is allowed (and idiomatic) for data classes since Kotlin 1.4.
            sb.Append(RenderConstructorProperty(prop, propIndent));
            sb.AppendLine(",");
        }

        sb.AppendLine($"{prefix})");
        return sb.ToString();
    }

    public override string VisitKotlinClassDeclaration(KotlinClassDeclarationNode node) =>
        RenderClass(node, indent: 0);

    private string RenderClass(KotlinClassDeclarationNode node, int indent)
    {
        var sb = new StringBuilder();
        var prefix = Indent(indent);

        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
            AppendKDoc(sb, indent, node.Documentation.Text);

        foreach (var ann in node.KotlinAnnotations)
            sb.AppendLine($"{prefix}{VisitKotlinAnnotation(ann)}");

        var visibilityModifier = string.IsNullOrEmpty(node.Visibility) ? "" : node.Visibility + " ";

        if (node.ConstructorProperties.Count == 0)
        {
            sb.Append($"{prefix}{visibilityModifier}class {node.Name.Name}");
        }
        else
        {
            sb.AppendLine($"{prefix}{visibilityModifier}class {node.Name.Name}(");
            var propIndent = indent + 1;
            for (var i = 0; i < node.ConstructorProperties.Count; i++)
            {
                var prop = node.ConstructorProperties[i];
                sb.Append(RenderConstructorProperty(prop, propIndent));
                sb.AppendLine(",");
            }
            sb.Append($"{prefix})");
        }

        if (node.Members.Count == 0)
        {
            sb.AppendLine();
            return sb.ToString();
        }

        sb.AppendLine(" {");
        var bodyIndent = indent + 1;
        for (var i = 0; i < node.Members.Count; i++)
        {
            var rendered = RenderDeclaration(node.Members[i], bodyIndent);
            sb.Append(rendered);
            if (!rendered.EndsWith("\n", StringComparison.Ordinal))
                sb.AppendLine();
            if (i < node.Members.Count - 1)
                sb.AppendLine();
        }
        sb.AppendLine($"{prefix}}}");
        return sb.ToString();
    }

    public override string VisitKotlinProperty(KotlinPropertyNode node) =>
        node.IsConstructorParameter
            ? RenderConstructorProperty(node, indent: 0)
            : RenderRegularProperty(node, indent: 0);

    private string RenderConstructorProperty(KotlinPropertyNode node, int indent)
    {
        var sb = new StringBuilder();
        var prefix = Indent(indent);

        foreach (var ann in node.KotlinAnnotations)
            sb.AppendLine($"{prefix}{VisitKotlinAnnotation(ann)}");

        var visibility = string.IsNullOrEmpty(node.Visibility) ? "" : node.Visibility + " ";
        var keyword = node.IsReadOnly ? "val" : "var";
        var typeText = RenderType(node.Type);
        var defaultClause = node.DefaultValueLiteral != null ? $" = {node.DefaultValueLiteral}" : "";

        sb.Append($"{prefix}{visibility}{keyword} {node.Name.Name}: {typeText}{defaultClause}");
        return sb.ToString();
    }

    private string RenderRegularProperty(KotlinPropertyNode node, int indent)
    {
        var sb = new StringBuilder();
        var prefix = Indent(indent);

        foreach (var ann in node.KotlinAnnotations)
            sb.AppendLine($"{prefix}{VisitKotlinAnnotation(ann)}");

        var visibility = string.IsNullOrEmpty(node.Visibility) ? "" : node.Visibility + " ";
        var keyword = node.IsReadOnly ? "val" : "var";
        var typeText = RenderType(node.Type);
        var defaultClause = node.DefaultValueLiteral != null ? $" = {node.DefaultValueLiteral}" : "";

        sb.AppendLine($"{prefix}{visibility}{keyword} {node.Name.Name}: {typeText}{defaultClause}");
        return sb.ToString();
    }

    public override string VisitKotlinEnumDeclaration(KotlinEnumDeclarationNode node) =>
        RenderEnum(node, indent: 0);

    private string RenderEnum(KotlinEnumDeclarationNode node, int indent)
    {
        var sb = new StringBuilder();
        var prefix = Indent(indent);

        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
            AppendKDoc(sb, indent, node.Documentation.Text);

        foreach (var ann in node.KotlinAnnotations)
            sb.AppendLine($"{prefix}{VisitKotlinAnnotation(ann)}");

        sb.AppendLine($"{prefix}enum class {node.Name.Name} {{");

        var memberIndent = indent + 1;
        for (var i = 0; i < node.Members.Count; i++)
        {
            var member = node.Members[i];
            var isLast = i == node.Members.Count - 1;
            sb.Append(RenderEnumMember(member, memberIndent, isLast));
        }

        sb.AppendLine($"{prefix}}}");
        return sb.ToString();
    }

    public override string VisitKotlinEnumMember(KotlinEnumMemberNode node) =>
        RenderEnumMember(node, indent: 0, isLast: true);

    private string RenderEnumMember(KotlinEnumMemberNode node, int indent, bool isLast)
    {
        var sb = new StringBuilder();
        var prefix = Indent(indent);
        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
            AppendKDoc(sb, indent, node.Documentation.Text);
        var anns = node.KotlinAnnotations.Count == 0
            ? string.Empty
            : string.Join(" ", node.KotlinAnnotations.Select(VisitKotlinAnnotation)) + " ";
        // Always emit a trailing comma — Kotlin allows it for enum entries and it simplifies appending later.
        sb.AppendLine($"{prefix}{anns}{node.Name},");
        // `isLast` reserved for future use (e.g. closing semicolon for enums with bodies).
        _ = isLast;
        return sb.ToString();
    }

    public override string VisitKotlinFunctionDeclaration(KotlinFunctionDeclarationNode node) =>
        RenderFunction(node, indent: 0);

    /// <summary>Renders a function using its selected structured or text-backed body representation.</summary>
    /// <param name="node">The function declaration.</param>
    /// <param name="indent">The surrounding declaration indentation level.</param>
    /// <returns>The complete function declaration.</returns>
    private string RenderFunction(KotlinFunctionDeclarationNode node, int indent)
    {
        var sb = new StringBuilder();
        var prefix = Indent(indent);

        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
            AppendKDoc(sb, indent, node.Documentation.Text);

        foreach (var ann in node.KotlinAnnotations)
            sb.AppendLine($"{prefix}{VisitKotlinAnnotation(ann)}");

        var modifiers = new List<string>();
        if (!string.IsNullOrEmpty(node.Visibility)) modifiers.Add(node.Visibility);
        if (node.IsOverride) modifiers.Add("override");
        if (node.IsSuspend) modifiers.Add("suspend");
        modifiers.Add("fun");
        var header = string.Join(" ", modifiers);

        var paramText = string.Join(", ", node.Parameters.Select(RenderParameter));
        var returnAnnotation = node.ReturnType != null
            ? $": {RenderType(node.ReturnType)}"
            : "";

        if (node.StructuredBody != null)
        {
            sb.AppendLine($"{prefix}{header} {node.Name.Name}({paramText}){returnAnnotation} {{");
            foreach (var line in VisitBlock(node.StructuredBody).Split('\n'))
                sb.AppendLine(Indent(indent + 1) + line.TrimEnd('\r'));
            sb.AppendLine(prefix + "}");
            return sb.ToString();
        }
        if (node.Body.Count == 0)
        {
            sb.AppendLine($"{prefix}{header} {node.Name.Name}({paramText}){returnAnnotation}");
            return sb.ToString();
        }

        if (node.IsExpressionBody)
        {
            // Expression body: `fun name(...): Type = expression`.
            // The first body line is appended right after `= `. Continuation
            // lines align with the function's own indent — builders are
            // expected to manage any further nesting (lambdas, etc.).
            sb.Append($"{prefix}{header} {node.Name.Name}({paramText}){returnAnnotation} = ");
            for (var i = 0; i < node.Body.Count; i++)
            {
                var line = node.Body[i];
                if (i == 0)
                    sb.AppendLine(line);
                else
                    sb.AppendLine($"{prefix}{line}");
            }
            return sb.ToString();
        }

        sb.AppendLine($"{prefix}{header} {node.Name.Name}({paramText}){returnAnnotation} {{");
        var blockIndent = indent + 1;
        foreach (var line in node.Body)
        {
            if (string.IsNullOrEmpty(line))
            {
                sb.AppendLine();
                continue;
            }
            sb.AppendLine($"{Indent(blockIndent)}{line}");
        }
        sb.AppendLine($"{prefix}}}");
        return sb.ToString();
    }

    public override string VisitKotlinParameter(KotlinParameterNode node) => RenderParameter(node);

    /// <inheritdoc/>
    public override string VisitKotlinObjectExpression(KotlinObjectExpressionNode node)
    {
        var result = new StringBuilder();
        result.AppendLine($"object : {RenderType(node.InterfaceType)} {{");
        foreach (var member in node.Members)
            result.Append(RenderDeclaration(member, 1));
        result.Append('}');
        return result.ToString();
    }

    private string RenderParameter(KotlinParameterNode node)
    {
        var typeText = RenderType(node.Type);
        var defaultClause = node.DefaultValueLiteral != null ? $" = {node.DefaultValueLiteral}" : "";
        return $"{node.Name.Name}: {typeText}{defaultClause}";
    }

    private string RenderDeclaration(DeclarationNode declaration, int indent) =>
        declaration switch
        {
            KotlinDataClassNode dc       => RenderDataClass(dc, indent),
            KotlinClassDeclarationNode c => RenderClass(c, indent),
            KotlinEnumDeclarationNode e  => RenderEnum(e, indent),
            KotlinFunctionDeclarationNode f => RenderFunction(f, indent),
            KotlinPropertyNode p         => RenderRegularProperty(p, indent),
            _ => throw new NotSupportedException(
                $"Cannot render declaration of type {declaration.GetType().Name} in Kotlin file")
        };

    // ------------- Type rendering -------------

    public override string VisitSimpleType(SimpleTypeNode node)
    {
        if (node.LiteralValue != null)
            return RenderLiteralValue(node.LiteralValue, LiteralKind.String);
        return node.Name.Name + (node.IsNullable ? "?" : string.Empty);
    }

    public override string VisitGenericType(GenericTypeNode node)
    {
        var args = string.Join(", ", node.TypeArguments.Select(RenderType));
        var suffix = node.IsNullable ? "?" : string.Empty;
        return $"{node.Name.Name}<{args}>{suffix}";
    }

    public override string VisitNullableType(NullableTypeNode node)
    {
        var inner = RenderType(node.InnerType);
        return inner.EndsWith("?", StringComparison.Ordinal) ? inner : inner + "?";
    }

    public override string VisitOptionalType(OptionalTypeNode node) =>
        RenderType(node.InnerType);

    public override string VisitArrayType(ArrayTypeNode node) =>
        $"List<{RenderType(node.ElementType)}>";

    public override string VisitUnionType(UnionTypeNode node) =>
        // Kotlin has no native unions — fall back to Any (the type mapper already
        // does this; this branch covers any path that bypasses the mapper).
        "Any";

    public override string VisitIntersectionType(IntersectionTypeNode node) =>
        "Any";

    public override string VisitDictionaryType(DictionaryTypeNode node) =>
        $"Map<{RenderType(node.KeyType)}, {RenderType(node.ValueType)}>";

    public override string VisitRawType(RawTypeNode node) => node.RawTypeString;

    private string RenderType(TypeNode type) => type.Accept(this);

    // ------------- Misc visitors -------------

    public override string VisitIdentifier(IdentifierNode node) => node.Name;

    public override string VisitComment(CommentNode node) => node.Text;

    public override string VisitAttribute(AttributeNode node) =>
        node.Arguments.Count > 0
            ? $"@{node.Name.Name}({string.Join(", ", node.Arguments.Select(Visit))})"
            : $"@{node.Name.Name}";

    /// <inheritdoc/>
    public override string VisitBlock(BlockNode node) => string.Join("\n", node.Statements.Select(statement =>
        Visit(statement) ?? throw new NotSupportedException($"Cannot render structured Kotlin statement '{statement.GetType().Name}'.")));

    public override string VisitImport(ImportNode node) => string.Empty;
    public override string VisitExport(ExportNode node) => string.Empty;

    public override string VisitLiteralExpression(LiteralExpressionNode node) =>
        RenderLiteralValue(node.Value, node.LiteralKind);

    public override string VisitIdentifierExpression(IdentifierExpressionNode node) =>
        node.Identifier.Name;

    /// <inheritdoc/>
    public override string VisitCallExpression(CallExpressionNode node)
    {
        var target = Visit(node.Target);
        var typeArgument = node.GenericTypeArgument == null ? string.Empty : "<" + RenderType(node.GenericTypeArgument) + ">";
        var args = string.Join(", ", node.Arguments.Select(Visit));
        return $"{target}{typeArgument}({args})";
    }

    public override string VisitMemberAccessExpression(MemberAccessExpressionNode node) =>
        $"{Visit(node.Object)}.{node.Member.Name}";

    public override string VisitUnaryExpression(UnaryExpressionNode node)
    {
        var op = node.Operator switch
        {
            UnaryOperator.LogicalNot => "!",
            UnaryOperator.Negate => "-",
            _ => ""
        };
        return $"{op}{Visit(node.Operand)}";
    }

    public override string VisitBinaryExpression(BinaryExpressionNode node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        if (node.Operator == BinaryOperator.Assign || node.IsAssignment)
            return $"{left} = {right}";
        var op = node.Operator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessThanOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterThanOrEqual => ">=",
            BinaryOperator.LogicalAnd => "&&",
            BinaryOperator.LogicalOr => "||",
            _ => throw new NotSupportedException($"Operator {node.Operator} not supported in Kotlin renderer")
        };
        return $"{left} {op} {right}";
    }

    public override string VisitConditionalExpression(ConditionalExpressionNode node) =>
        $"if ({Visit(node.Condition)}) {Visit(node.TrueExpression)} else {Visit(node.FalseExpression)}";

    public override string VisitAwaitExpression(AwaitExpressionNode node) =>
        // Kotlin has no `await` — coroutines are implicit. Pass-through the inner expression.
        Visit(node.Expression);

    public override string VisitCastExpression(CastExpressionNode node) =>
        $"{Visit(node.Expression)} as {RenderType(node.TargetType)}";

    public override string VisitReturnStatement(ReturnStatementNode node) =>
        node.Value == null ? "return" : $"return {Visit(node.Value)}";

    public override string VisitIfStatement(IfStatementNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"if ({Visit(node.Condition)}) {{");
        sb.AppendLine(Visit(node.ThenBlock));
        sb.Append("}");
        if (node.ElseBlock != null)
        {
            sb.AppendLine(" else {");
            sb.AppendLine(Visit(node.ElseBlock));
            sb.Append("}");
        }
        return sb.ToString();
    }

    public override string VisitForEachStatement(ForEachStatementNode node) =>
        $"for ({node.Variable.Name} in {Visit(node.Collection)}) {{\n{Visit(node.Body)}\n}}";

    public override string VisitExpressionStatement(ExpressionStatementNode node) =>
        Visit(node.Expression);

    public override string VisitVariableDeclarationStatement(VariableDeclarationStatementNode node)
    {
        var initializer = node.Initializer != null ? $" = {Visit(node.Initializer)}" : "";
        return $"val {node.Name.Name}{initializer}";
    }

    public override string VisitThrowStatement(ThrowStatementNode node) =>
        node.Expression == null ? "throw RuntimeException()" : $"throw {Visit(node.Expression)}";

    public override string VisitUsingStatement(UsingStatementNode node) =>
        // Kotlin uses .use{} extension; pass-through scope.
        $"{Visit(node.Resource)}.use {{\n{Visit(node.Body)}\n}}";

    public override string VisitTryCatchStatement(TryCatchStatementNode node)
    {
        var sb = new StringBuilder();
        sb.AppendLine("try {");
        sb.AppendLine(Visit(node.TryBlock));
        sb.Append("}");
        foreach (var c in node.CatchClauses)
        {
            sb.AppendLine(" " + Visit(c));
        }
        if (node.FinallyBlock != null)
        {
            sb.AppendLine(" finally {");
            sb.AppendLine(Visit(node.FinallyBlock));
            sb.Append("}");
        }
        return sb.ToString();
    }

    public override string VisitCatchClause(CatchClauseNode node)
    {
        var exType = node.ExceptionType != null ? RenderType(node.ExceptionType) : "Throwable";
        var exVar = node.ExceptionVariable?.Name ?? "e";
        return $"catch ({exVar}: {exType}) {{\n{Visit(node.Body)}\n}}";
    }

    public override string VisitTypeDeclaration(TypeDeclarationNode node) =>
        throw new NotSupportedException("TypeDeclarationNode not supported by Kotlin renderer");

    public override string VisitPropertyDeclaration(PropertyDeclarationNode node) => VisitDefault(node);
    public override string VisitMethodDeclaration(MethodDeclarationNode node) => VisitDefault(node);
    public override string VisitParameterDeclaration(ParameterDeclarationNode node) => VisitDefault(node);
    public override string VisitEnumMemberDeclaration(EnumMemberDeclarationNode node) => VisitDefault(node);
    public override string VisitEnumDeclaration(EnumDeclarationNode node) => VisitDefault(node);
    public override string VisitInterfaceDeclaration(InterfaceDeclarationNode node) => VisitDefault(node);

    // ------------- Helpers -------------

    private static string Indent(int level) =>
        level <= 0 ? string.Empty : new string(' ', level * IndentSize);

    private static void AppendKDoc(StringBuilder sb, int indent, string text)
    {
        var prefix = Indent(indent);
        var trimmed = text.Trim('\r', '\n');
        if (!trimmed.Contains('\n'))
        {
            sb.AppendLine($"{prefix}/** {EscapeForKDoc(trimmed)} */");
            return;
        }

        sb.AppendLine($"{prefix}/**");
        foreach (var rawLine in trimmed.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                sb.AppendLine($"{prefix} *");
            else
                sb.AppendLine($"{prefix} * {line}");
        }
        sb.AppendLine($"{prefix} */");
    }

    private static string EscapeForKDoc(string text) =>
        text.Replace("*/", "*\\/", StringComparison.Ordinal);

    private static string RenderLiteralValue(object? value, LiteralKind? kind = null)
    {
        if (value == null) return "null";
        return value switch
        {
            string s when kind == LiteralKind.String => $"\"{EscapeStringLiteral(s)}\"",
            bool b => b ? "true" : "false",
            string s => $"\"{EscapeStringLiteral(s)}\"",
            _ => value.ToString() ?? "null"
        };
    }

    private static string EscapeStringLiteral(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal)
         .Replace("\n", "\\n", StringComparison.Ordinal)
         .Replace("\r", "\\r", StringComparison.Ordinal);
}
