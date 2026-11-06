using System.Collections.Immutable;
using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Http;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Ast.Renderers;

/// <summary>
/// Рендерер AST для генерации TypeScript кода
/// Реализует ITypeScriptAstVisitor и IHttpAstVisitor для поддержки HTTP клиентов
///
/// Поддерживает Chain of Responsibility через <see cref="ITypeScriptNodeRenderer"/>:
/// плагин-узлы (Zod / Valibot / TypeBox / Axios / TanStack), не известные базовому
/// рендереру, попадают в <see cref="VisitDefault(AstNode)"/> и обрабатываются первым
/// подходящим звеном цепочки (отсортирована по <see cref="ITypeScriptNodeRenderer.Priority"/>).
/// </summary>
public class TypeScriptAstRenderer : TypeScriptAstVisitorBase<string>, ITypeScriptAstRenderer, IHttpAstVisitor<string>
{
    private readonly StringBuilder _sb = new();
    private int _indentLevel = 0;
    private readonly ImmutableArray<ITypeScriptNodeRenderer> _chain;

    /// <summary>
    /// Параметрless ctor — используется в тестах и в legacy-местах. Цепочка пуста,
    /// поведение совпадает с прежним: <see cref="VisitDefault(AstNode)"/> бросает
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    public TypeScriptAstRenderer()
    {
        _chain = ImmutableArray<ITypeScriptNodeRenderer>.Empty;
    }

    /// <summary>
    /// DI-ctor: получает цепочку плагин-рендереров (Zod / Valibot / TypeBox / Axios /
    /// TanStack). Сортируется по убыванию <see cref="ITypeScriptNodeRenderer.Priority"/>.
    /// </summary>
    public TypeScriptAstRenderer(IEnumerable<ITypeScriptNodeRenderer> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        _chain = chain.OrderByDescending(r => r.Priority).ToImmutableArray();
    }

    /// <summary>
    /// Рендерит SourceFile
    /// </summary>
    public string Render(SourceFileNode node)
    {
        _sb.Clear();
        _indentLevel = 0;

        // Top-of-file directive prologue (e.g. "use server"; for Solid Start /
        // "use client"; for React Server Components). Pass via
        // SourceFileNode.Metadata["directive"] = "use server" (no quotes).
        if (node.Metadata.TryGetValue("directive", out var directiveObj) && directiveObj is string directive && !string.IsNullOrEmpty(directive))
        {
            _sb.Append('"').Append(directive).Append("\";");
            CodeFormatter.AppendEmptyLine(_sb);
            CodeFormatter.AppendEmptyLine(_sb);
        }

        // File-level header comment (@file/@module/@generated), rendered above imports.
        if (node.FileDocumentation != null)
        {
            RenderComment(_sb, node.FileDocumentation, 0);
            CodeFormatter.AppendEmptyLine(_sb);
        }

        // Рендерим imports
        if (node.Imports.Count > 0)
        {
            foreach (var import in node.Imports)
            {
                RenderImport(import);
            }
            CodeFormatter.AppendEmptyLine(_sb);
        }
        
        // Рендерим declarations (export уже добавлен в RenderInterface, RenderEnum, RenderTypeAlias)
        foreach (var declaration in node.Declarations)
        {
            var rendered = RenderDeclaration(declaration);
            _sb.Append(rendered);
            CodeFormatter.AppendEmptyLine(_sb);
        }
        
        return _sb.ToString();
    }

    /// <summary>
    /// Рендерит Interface
    /// </summary>
    public string Render(InterfaceDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderInterface(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит TypeAlias
    /// </summary>
    public string Render(TypeAliasDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderTypeAlias(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Function
    /// </summary>
    public string Render(FunctionDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderFunction(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Variable
    /// </summary>
    public string Render(VariableDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderVariable(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Enum
    /// </summary>
    public string Render(EnumDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderEnum(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Property
    /// </summary>
    public string Render(PropertyDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderProperty(sb, node, indentLevel);
        
        return sb.ToString();
    }

    #region Visitor Pattern Implementation

    public override string VisitSourceFile(SourceFileNode node)
    {
        return Render(node);
    }

    public override string VisitInterfaceDeclaration(InterfaceDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitTypeAliasDeclaration(TypeAliasDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitFunctionDeclaration(FunctionDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitVariableDeclaration(VariableDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitEnumDeclaration(EnumDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitPropertyDeclaration(PropertyDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitParameterDeclaration(ParameterDeclarationNode node)
    {
        var sb = new StringBuilder();
        RenderParameter(sb, node);
        return sb.ToString();
    }

    /// <inheritdoc />
    public override string VisitRawType(RawTypeNode node) => ApplyTypeModifiers(node.RawTypeString, node);

    /// <inheritdoc />
    public override string VisitSimpleType(SimpleTypeNode node)
    {
        var value = node.LiteralValue switch
        {
            string text => $"\"{CodeEscapeHelper.EscapeTypeScriptString(text)}\"",
            bool boolean => boolean ? "true" : "false",
            int or long or short or byte or float or double or decimal =>
                Convert.ToString(node.LiteralValue, System.Globalization.CultureInfo.InvariantCulture)!,
            _ => node.Name.Name,
        };
        return ApplyTypeModifiers(value, node);
    }

    /// <inheritdoc />
    public override string VisitNullableType(NullableTypeNode node) =>
        ApplyTypeModifiers(RenderType(node.InnerType), node);

    /// <inheritdoc />
    public override string VisitOptionalType(OptionalTypeNode node) =>
        ApplyTypeModifiers(RenderType(node.InnerType), node);

    /// <inheritdoc />
    public override string VisitArrayType(ArrayTypeNode node)
    {
        var readonlyStr = node.IsReadOnly ? "readonly " : "";
        var element = RenderType(node.ElementType);
        if (node.ElementType is IntersectionTypeNode || HasUnionSemantics(node.ElementType))
            element = $"({element})";
        return ApplyTypeModifiers($"{readonlyStr}{element}[]", node);
    }

    /// <inheritdoc />
    public override string VisitGenericType(GenericTypeNode node)
    {
        var typeArgs = string.Join(", ", node.TypeArguments.Select(RenderType));
        return ApplyTypeModifiers($"{node.Name.Name}<{typeArgs}>", node);
    }

    /// <inheritdoc />
    public override string VisitUnionType(UnionTypeNode node) =>
        ApplyTypeModifiers(string.Join(" | ", node.Types.Select(RenderType)), node);

    /// <inheritdoc />
    public override string VisitIntersectionType(IntersectionTypeNode node) =>
        ApplyTypeModifiers(string.Join(" & ", node.Types.Select(type =>
            HasUnionSemantics(type) ? $"({RenderType(type)})" : RenderType(type))), node);

    /// <inheritdoc />
    public override string VisitDictionaryType(DictionaryTypeNode node) =>
        ApplyTypeModifiers($"Record<{RenderType(node.KeyType)}, {RenderType(node.ValueType)}>", node);

    /// <inheritdoc />
    public override string VisitTypeLiteral(TypeLiteralNode node)
    {
        if (node.Members.Count == 0)
            return ApplyTypeModifiers("{}", node);
        var members = string.Join("; ", node.Members.Select(RenderTypeLiteralMember));
        return ApplyTypeModifiers($"{{ {members} }}", node);
    }

    /// <summary>Renders value optionality as a TypeScript union rather than the property-presence marker.</summary>
    /// <param name="value">The rendered unmodified type.</param>
    /// <param name="node">The type whose value modifiers are applied.</param>
    /// <returns>The nullable and optional value type.</returns>
    private static string ApplyTypeModifiers(string value, TypeNode node) =>
        value + (node.IsNullable ? " | null" : "") + (node.IsOptional ? " | undefined" : "");

    /// <summary>Determines whether a type must be grouped before intersection or array composition.</summary>
    /// <param name="type">The composed type.</param>
    /// <returns>Whether the rendered type contains a union at its outer level.</returns>
    private static bool HasUnionSemantics(TypeNode type) =>
        type is UnionTypeNode or NullableTypeNode or OptionalTypeNode || type.IsNullable || type.IsOptional;

    /// <summary>Renders a single object type-literal member as <c>{readonly }name{?}: Type</c>.</summary>
    /// <param name="member">The member to render.</param>
    /// <returns>The TypeScript property-signature text for <paramref name="member"/>.</returns>
    private string RenderTypeLiteralMember(TypeLiteralMember member)
    {
        var readonlyStr = member.IsReadonly ? "readonly " : "";
        var optionalStr = member.IsOptional ? "?" : "";
        return $"{readonlyStr}{member.Name}{optionalStr}: {RenderType(member.Type)}";
    }

    public override string VisitLiteralExpression(LiteralExpressionNode node)
    {
        return RenderLiteral(node);
    }

    public override string VisitIdentifierExpression(IdentifierExpressionNode node)
    {
        return node.Identifier.Name;
    }

    /// <inheritdoc />
    public override string VisitCallExpression(CallExpressionNode node)
    {
        var target = Visit(node.Target);
        if (node.Target is ArrowFunctionExpressionNode)
            target = $"({target})";
        var generic = node.GenericTypeArgument != null
            ? $"<{RenderType(node.GenericTypeArgument)}>"
            : !string.IsNullOrWhiteSpace(node.GenericTypeName)
                ? $"<{node.GenericTypeName}>"
                : "";
        var prefix = node.IsNewExpression ? "new " : "";
        if (node.Arguments.Count >= 2)
        {
            var indent = new string(' ', (_indentLevel + 1) * 4);
            var closingIndent = new string(' ', _indentLevel * 4);
            var argLines = node.Arguments.Select(a => indent + Visit(a));
            return $"{prefix}{target}{generic}(\n{string.Join(",\n", argLines)}\n{closingIndent})";
        }
        var args = string.Join(", ", node.Arguments.Select(Visit));
        return $"{prefix}{target}{generic}({args})";
    }

    public override string VisitMemberAccessExpression(MemberAccessExpressionNode node)
    {
        var target = Visit(node.Object);
        var op = node.UseOptionalChaining ? "?." : ".";
        return $"{target}{op}{node.Member.Name}";
    }

    public override string VisitObjectLiteralExpression(ObjectLiteralExpressionNode node)
    {
        if (node.Properties.Count == 0)
            return node.AsConstAssertion ? "{} as const" : "{}";

        string result;
        if (node.PreferMultilineFormat && node.Properties.Count > 0)
        {
            var indent = new string(' ', (_indentLevel + 1) * 4);
            var closingIndent = new string(' ', _indentLevel * 4);
            var propertyLines = node.Properties.Select(p => indent + VisitPropertyAssignment(p));
            result = "{\n" + string.Join(",\n", propertyLines) + "\n" + closingIndent + "}";
        }
        else
        {
            var properties = string.Join(", ", node.Properties.Select(p => VisitPropertyAssignment(p)));
            result = $"{{ {properties} }}";
        }
        return node.AsConstAssertion ? $"{result} as const" : result;
    }
    
    public override string VisitArrayLiteralExpression(ArrayLiteralExpressionNode node)
    {
        if (node.PreferMultilineFormat && node.Elements.Count > 1)
        {
            var indent = new string(' ', (_indentLevel + 1) * 4);
            var closingIndent = new string(' ', _indentLevel * 4);
            var elementLines = node.Elements.Select(e => indent + Visit(e));
            return "[\n" + string.Join(",\n", elementLines) + "\n" + closingIndent + "]";
        }
        var elements = string.Join(", ", node.Elements.Select(Visit));
        return $"[{elements}]";
    }

    public override string VisitSpreadExpression(SpreadExpressionNode node)
    {
        return $"...{Visit(node.Expression)}";
    }

    /// <inheritdoc />
    public override string VisitArrowFunctionExpression(ArrowFunctionExpressionNode node)
    {
        var asyncKw = node.IsAsync ? "async " : "";
        var parameters = string.Join(", ", node.Parameters.Select(p => RenderParameterDeclaration(p)));
        var returnType = node.ReturnType == null ? "" : $": {RenderType(node.ReturnType)}";
        if (node.BodyExpression != null)
        {
            var bodyText = Visit(node.BodyExpression);
            // ObjectLiteral как тело arrow function должен быть в скобках,
            // иначе TS парсит `() => { ... }` как блок с labeled statements.
            if (node.BodyExpression is ObjectLiteralExpressionNode)
                bodyText = $"({bodyText})";
            return $"{asyncKw}({parameters}){returnType} => {bodyText}";
        }
        var blockContent = string.Join(" ", node.BodyBlock!.Statements.Select(Visit));
        return $"{asyncKw}({parameters}){returnType} => {{ {blockContent} }}";
    }

    public override string VisitTemplateLiteralExpression(TemplateLiteralExpressionNode node)
    {
        var parts = string.Join("", node.Parts.Select(p => p.IsExpression ? $"${{{Visit(p.Expression!)}}}" : p.Text));
        return $"`{parts}`";
    }

    public override string VisitReturnStatement(ReturnStatementNode node)
    {
        if (node.Value == null)
        {
            return "return;";
        }
        var expr = Visit(node.Value);
        return $"return {expr};";
    }

    public override string VisitExpressionStatement(ExpressionStatementNode node)
    {
        var expr = Visit(node.Expression);
        return $"{expr};";
    }

    public override string VisitIfStatement(IfStatementNode node)
    {
        var sb = new StringBuilder();
        var savedIndent = _indentLevel;
        var condition = Visit(node.Condition);
        CodeFormatter.AppendLine(sb, _indentLevel, $"if ({condition}) {{");
        _indentLevel++;
        foreach (var stmt in node.ThenBlock.Statements)
        {
            var rendered = Visit(stmt);
            CodeFormatter.AppendLine(sb, _indentLevel, rendered);
        }
        _indentLevel = savedIndent;
        CodeFormatter.AppendLine(sb, _indentLevel, "}");
        return sb.ToString();
    }

    public override string VisitForEachStatement(ForEachStatementNode node)
    {
        var sb = new StringBuilder();
        var item = node.Variable.Name;
        var collection = Visit(node.Collection);
        CodeFormatter.AppendLine(sb, _indentLevel, $"for (const {item} of {collection}) {{");
        _indentLevel++;
        foreach (var stmt in node.Body.Statements)
        {
            var rendered = Visit(stmt);
            CodeFormatter.AppendLine(sb, _indentLevel, rendered);
        }
        _indentLevel--;
        CodeFormatter.AppendLine(sb, _indentLevel, "}");
        return sb.ToString();
    }

    public override string VisitBinaryExpression(BinaryExpressionNode node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        var op = GetBinaryOperator(node.Operator);
        return $"({left} {op} {right})";
    }

    public override string VisitUnaryExpression(UnaryExpressionNode node)
    {
        var operand = Visit(node.Operand);
        return node.Operator switch
        {
            UnaryOperator.LogicalNot => $"!{operand}",
            UnaryOperator.Negate => $"-{operand}",
            _ => throw new NotSupportedException($"Unary operator {node.Operator} is not supported for TypeScript")
        };
    }
    
    /// <summary>
    /// Преобразует BinaryOperator в строку оператора TypeScript
    /// </summary>
    private static string GetBinaryOperator(BinaryOperator op)
    {
        return op switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Equal => "===",
            BinaryOperator.NotEqual => "!==",
            BinaryOperator.LessThan => "<",
            BinaryOperator.LessThanOrEqual => "<=",
            BinaryOperator.GreaterThan => ">",
            BinaryOperator.GreaterThanOrEqual => ">=",
            BinaryOperator.LogicalAnd => "&&",
            BinaryOperator.LogicalOr => "||",
            BinaryOperator.BitwiseAnd => "&",
            BinaryOperator.BitwiseOr => "|",
            BinaryOperator.BitwiseXor => "^",
            BinaryOperator.LeftShift => "<<",
            BinaryOperator.RightShift => ">>",
            BinaryOperator.Assign => "=",
            BinaryOperator.NullishCoalescing => "??",
            _ => throw new NotSupportedException($"Binary operator {op} is not supported")
        };
    }

    public override string VisitConditionalExpression(ConditionalExpressionNode node)
    {
        var condition = Visit(node.Condition);
        var trueExpr = Visit(node.TrueExpression);
        var falseExpr = Visit(node.FalseExpression);
        return $"({condition} ? {trueExpr} : {falseExpr})";
    }

    public override string VisitBlock(BlockNode node)
    {
        var sb = new StringBuilder();
        CodeFormatter.AppendLine(sb, _indentLevel, "{");
        _indentLevel++;
        foreach (var stmt in node.Statements)
        {
            var rendered = Visit(stmt);
            CodeFormatter.AppendLine(sb, _indentLevel, rendered);
        }
        _indentLevel--;
        CodeFormatter.AppendLine(sb, _indentLevel, "}");
        return sb.ToString();
    }

    public override string VisitImport(ImportNode node)
    {
        var sb = new StringBuilder();
        RenderImport(node);
        return sb.ToString();
    }

    public override string VisitExport(ExportNode node)
    {
        var declaration = Visit(node.Declaration);
        var exportKeyword = node.ExportKind switch
        {
            ExportKind.Default => "export default ",
            ExportKind.Named => "export ",
            _ => "export "
        };
        return $"{exportKeyword}{declaration}";
    }

    public override string VisitIdentifier(IdentifierNode node)
    {
        return node.Name;
    }

    public override string VisitComment(CommentNode node)
    {
        var sb = new StringBuilder();
        RenderComment(sb, node, 0);
        return sb.ToString();
    }

    public override string VisitAttribute(AttributeNode node)
    {
        // TypeScript не имеет атрибутов в том же смысле, что C#
        // Можно использовать декораторы, но для базовой реализации возвращаем пустую строку
        return "";
    }

    public override string VisitEnumMemberDeclaration(EnumMemberDeclarationNode node)
    {
        var sb = new StringBuilder();
        RenderEnumMember(sb, node, 0);
        return sb.ToString();
    }

    /// <inheritdoc />
    public override string VisitPropertyAssignment(PropertyAssignmentNode node)
    {
        var value = Visit(node.Value);
        if (node.IsSpread)
        {
            return $"...{value}";
        }
        var key = node.Name.Name;
        if (node.IsShorthand)
        {
            return key; // Shorthand: { name } вместо { name: name }
        }
        return $"{QuoteKeyIfNeeded(key)}: {value}";
    }

    /// <summary>
    /// Quotes and escapes wire keys that cannot be emitted as TypeScript identifiers.
    /// </summary>
    /// <param name="key">The unescaped property key.</param>
    /// <returns>The identifier or an escaped single-quoted string literal.</returns>
    private static string QuoteKeyIfNeeded(string key)
    {
        if (key == "__proto__")
            return "[\"__proto__\"]";
        return IsSafeObjectKey(key) ? key : $"'{CodeEscapeHelper.EscapeTypeScriptString(key)}'";
    }

    /// <summary>Determines whether a wire key can be emitted as an unquoted ASCII identifier.</summary>
    /// <param name="name">The unescaped key.</param>
    /// <returns>Whether quoting is unnecessary.</returns>
    private static bool IsSafeObjectKey(string name) =>
        System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z_$][A-Za-z0-9_$]*$");

    // C# специфичные методы (не используются для TypeScript, но требуются интерфейсом)
    public override string VisitTypeDeclaration(TypeDeclarationNode node)
    {
        throw new NotSupportedException("TypeDeclaration is not supported in TypeScript renderer. Use specific declaration types instead.");
    }

    public override string VisitGenericParameter(GenericParameterNode node)
    {
        var constraint = node.Constraint != null ? $" extends {RenderType(node.Constraint)}" : "";
        var defaultValue = node.Default != null ? $" = {RenderType(node.Default)}" : "";
        return $"{node.Name.Name}{constraint}{defaultValue}";
    }

    public override string VisitPropertySignature(PropertySignatureNode node)
    {
        var readonlyKeyword = node.IsReadonly ? "readonly " : "";
        var optional = node.IsRequired ? "" : "?";
        var type = RenderType(node.Type);
        return $"{readonlyKeyword}{node.Name.Name}{optional}: {type}";
    }

    /// <summary>
    /// Renders an interface property signature with independent presence and value constraints.
    /// </summary>
    /// <param name="sb">The output buffer.</param>
    /// <param name="node">The property signature.</param>
    /// <param name="indentLevel">The member indentation level.</param>
    private void RenderPropertySignature(StringBuilder sb, PropertySignatureNode node, int indentLevel)
    {
        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
        {
            CodeFormatter.AppendLine(sb, indentLevel, "/**");
            CodeFormatter.AppendLine(sb, indentLevel, $" * {node.Documentation.Text}");
            CodeFormatter.AppendLine(sb, indentLevel, " */");
        }
        // Property presence follows the IR required flag; nullable values do not imply an optional key.
        var optional = !node.IsRequired ? "?" : "";
        var type = RenderType(node.Type);
        CodeFormatter.AppendLine(sb, indentLevel, $"{node.Name.Name}{optional}: {type};");
    }

    // HTTP специфичные методы
    public override string VisitHttpMethod(HttpMethodNode node)
    {
        return node.Method switch
        {
            HttpMethodKind.Get => "GET",
            HttpMethodKind.Post => "POST",
            HttpMethodKind.Put => "PUT",
            HttpMethodKind.Delete => "DELETE",
            HttpMethodKind.Patch => "PATCH",
            HttpMethodKind.Head => "HEAD",
            HttpMethodKind.Options => "OPTIONS",
            _ => throw new NotSupportedException($"HTTP method {node.Method} is not supported")
        };
    }

    public override string VisitRoute(RouteNode node)
    {
        return $"\"{node.Path}\"";
    }

    public override string VisitPathParameter(PathParameterNode node)
    {
        return $"{node.Name.Name}: {RenderType(node.Type)}";
    }

    public override string VisitQueryParameter(QueryParameterNode node)
    {
        var optional = node.IsRequired ? "" : "?";
        return $"{node.Name.Name}{optional}: {RenderType(node.Type)}";
    }

    public override string VisitHeaderParameter(HeaderParameterNode node)
    {
        var optional = node.IsRequired ? "" : "?";
        return $"{node.Name.Name}{optional}: {RenderType(node.Type)}";
    }

    public override string VisitRequestBody(RequestBodyNode node)
    {
        return RenderType(node.Type);
    }

    public override string VisitResponseType(ResponseTypeNode node)
    {
        return RenderType(node.Type);
    }

    #endregion

    #region Private Helper Methods

    private void RenderImport(ImportNode import)
    {
        var from = !string.IsNullOrEmpty(import.From) ? $" from \"{import.From}\"" : "";
        var imports = import.Items.Count > 0 
            ? string.Join(", ", import.Items.Select(i => i.Name))
            : "*";
        
        if (import.ImportKind == ImportKind.Default && import.Items.Count > 0)
        {
            // Default import: import name from "module"
            CodeFormatter.AppendLine(_sb, 0, $"import {import.Items[0].Name}{from};");
        }
        else if (import.ImportKind == ImportKind.Namespace)
        {
            // Namespace import: import * as name from "module"
            var name = import.Items.Count > 0 ? import.Items[0].Name : "namespace";
            CodeFormatter.AppendLine(_sb, 0, $"import * as {name}{from};");
        }
        else if (import.IsTypeOnly && import.Items.Count > 1)
        {
            // Каждый type-only специфайр — на отдельной строке (`import type { X } from "m";`
            // на каждый тип). Так требуют линтеры; value/default/namespace-импорты не трогаем.
            foreach (var item in import.Items)
                CodeFormatter.AppendLine(_sb, 0, $"import type {{ {item.Name} }}{from};");
        }
        else
        {
            var namedImports = string.Join(", ", import.Items.Select(i => i.Name));
            var typeKeyword = import.IsTypeOnly ? "import type " : "import ";
            CodeFormatter.AppendLine(_sb, 0, $"{typeKeyword}{{ {namedImports} }}{from};");
        }
    }

    private string RenderDeclaration(DeclarationNode declaration)
    {
        return declaration.Accept(this);
    }

    private void RenderInterface(StringBuilder sb, InterfaceDeclarationNode node, int indentLevel)
    {
        // JSDoc комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Модификаторы и объявление interface
        // Для TypeScript interface всегда export
        var exportKeyword = "export "; // Всегда экспортируем интерфейсы
        var baseTypes = node.BaseTypes.Count > 0 ? $" extends {string.Join(", ", node.BaseTypes.Select(RenderType))}" : "";
        CodeFormatter.AppendLine(sb, indentLevel, $"{exportKeyword}interface {node.Name.Name}{baseTypes} {{");
        
        // Члены interface
        foreach (var member in node.Members)
        {
            if (member is PropertyDeclarationNode property)
            {
                RenderProperty(sb, property, indentLevel + 1);
            }
            else if (member is PropertySignatureNode propertySignature)
            {
                RenderPropertySignature(sb, propertySignature, indentLevel + 1);
            }
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "}");
    }

    private void RenderTypeAlias(StringBuilder sb, TypeAliasDeclarationNode node, int indentLevel)
    {
        // JSDoc комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Модификаторы и объявление type alias
        // Для TypeScript type alias всегда export
        var exportKeyword = "export "; // Всегда экспортируем type aliases
        var type = RenderType(node.Type);
        CodeFormatter.AppendLine(sb, indentLevel, $"{exportKeyword}type {node.Name.Name} = {type};");
    }

    private void RenderFunction(StringBuilder sb, FunctionDeclarationNode node, int indentLevel)
    {
        // Формируем расширенный JSDoc комментарий
        var jsDocLines = new List<string>();
        
        // Добавляем описание (@description)
        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
        {
            jsDocLines.Add($"@description {node.Documentation.Text}");
        }
        
        // Добавляем @function для явного указания, что это функция
        jsDocLines.Add("@function");
        
        // Добавляем @async для асинхронных функций
        if (node.IsAsync)
        {
            jsDocLines.Add("@async");
        }
        
        // Добавляем @deprecated если функция deprecated
        if (node.IsDeprecated)
        {
            jsDocLines.Add("@deprecated");
        }
        
        // Добавляем параметры (@param). Для optional — [name], не ?name (? это спецификатор типа в сигнатуре)
        foreach (var param in node.Parameters)
        {
            var paramType = param.Type != null ? RenderType(param.Type) : "unknown";
            var paramDescription = param.Documentation?.Text ?? "";
            var paramNameFormatted = param.IsRequired ? param.Name.Name : $"[{param.Name.Name}]";
            
            if (!string.IsNullOrWhiteSpace(paramDescription))
            {
                jsDocLines.Add($"@param {{{paramType}}} {paramNameFormatted} {paramDescription}");
            }
            else
            {
                jsDocLines.Add($"@param {{{paramType}}} {paramNameFormatted}");
            }
        }
        
        // Добавляем тип возвращаемого значения (@returns)
        // Проверяем, является ли это type predicate (value is Type)
        if (node.ReturnType != null)
        {
            if (node.IsTypePredicate && !string.IsNullOrEmpty(node.TypePredicateParameterName))
            {
                // Type predicate: value is Type
                var returnTypeStr = RenderType(node.ReturnType);
                jsDocLines.Add($"@returns {{{node.TypePredicateParameterName} is {returnTypeStr}}}");
            }
            else
            {
                var returnTypeStr = RenderType(node.ReturnType);
                jsDocLines.Add($"@returns {{{returnTypeStr}}}");
            }
        }
        
        if (!string.IsNullOrEmpty(node.Example))
            jsDocLines.Add($"@example {node.Example}");
        if (!string.IsNullOrEmpty(node.Todo))
            jsDocLines.Add($"@todo {node.Todo}");
        
        if (jsDocLines.Count > 0)
        {
            CodeFormatter.AppendLine(sb, indentLevel, "/**");
            foreach (var line in jsDocLines)
            {
                CodeFormatter.AppendLine(sb, indentLevel, $" * {line}");
            }
            CodeFormatter.AppendLine(sb, indentLevel, " */");
        }
        
        // Модификаторы и объявление функции
        var exportKeyword = node.IsExported ? "export " : "";
        var asyncKeyword = node.IsAsync ? "async " : "";
        var functionKeyword = node.IsArrowFunction ? "" : "function ";
        var genericParamsStr = node.GenericParameters.Count > 0
            ? "<" + string.Join(", ", node.GenericParameters.Select(VisitGenericParameter)) + ">"
            : "";
        var useMultilineParams = node.Parameters.Count >= 2;
        var paramsIndent = new string(' ', (indentLevel + 1) * 4);
        var parametersStr = useMultilineParams
            ? "\n" + string.Join(",\n", node.Parameters.Select(p => paramsIndent + RenderParameterDeclaration(p))) + "\n" + new string(' ', indentLevel * 4)
            : string.Join(", ", node.Parameters.Select(p => RenderParameterDeclaration(p)));

        // Проверяем, является ли это type predicate (value is Type)
        string returnType;
        if (node.ReturnType != null)
        {
            if (node.IsTypePredicate && !string.IsNullOrEmpty(node.TypePredicateParameterName))
            {
                // Type predicate: value is Type
                var returnTypeStr = RenderType(node.ReturnType);
                returnType = $": {node.TypePredicateParameterName} is {returnTypeStr}";
            }
            else
            {
                var renderedReturnType = RenderType(node.ReturnType);
                // Async function в TS обязательно возвращает Promise<T>.
                // Билдеры задают «внутренний» T; рендерер оборачивает, если уже не обёрнут.
                if (node.IsAsync && !renderedReturnType.StartsWith("Promise<", StringComparison.Ordinal))
                {
                    renderedReturnType = $"Promise<{renderedReturnType}>";
                }
                returnType = $": {renderedReturnType}";
            }
        }
        else
        {
            returnType = "";
        }
        
        if (node.IsArrowFunction)
        {
            // Arrow function: const fn = <T>(params) => body (generic после =, не после имени)
            if (node.Body != null && node.Body.Statements.Count == 1 && node.Body.Statements[0] is ReturnStatementNode returnStmt && returnStmt.Value != null)
            {
                var bodyExpression = Visit(returnStmt.Value);
                CodeFormatter.AppendLine(sb, indentLevel, $"{exportKeyword}const {node.Name.Name} = {genericParamsStr}{asyncKeyword}({parametersStr}){returnType} => {bodyExpression};");
            }
            else
            {
                CodeFormatter.AppendLine(sb, indentLevel, $"{exportKeyword}const {node.Name.Name} = {genericParamsStr}{asyncKeyword}({parametersStr}){returnType} => {{");
                if (node.Body != null && node.Body.Statements.Count > 0)
                {
                    var savedIndent = _indentLevel;
                    _indentLevel = indentLevel + 1;
                    foreach (var stmt in node.Body.Statements)
                    {
                        var rendered = Visit(stmt);
                        CodeFormatter.AppendLine(sb, _indentLevel, rendered);
                    }
                    _indentLevel = savedIndent;
                }
                CodeFormatter.AppendLine(sb, indentLevel, "};");
            }
        }
        else
        {
            // Обычная функция: function fn(params): returnType { body }
            CodeFormatter.AppendLine(sb, indentLevel, $"{exportKeyword}{asyncKeyword}{functionKeyword}{node.Name.Name}{genericParamsStr}({parametersStr}){returnType} {{");
            if (node.Body != null && node.Body.Statements.Count > 0)
            {
                var savedIndent = _indentLevel;
                _indentLevel = indentLevel + 1;
                foreach (var stmt in node.Body.Statements)
                {
                    var rendered = Visit(stmt);
                    CodeFormatter.AppendLine(sb, _indentLevel, rendered);
                }
                _indentLevel = savedIndent;
            }
            else
            {
                // Type-only функция (без тела)
                CodeFormatter.AppendLine(sb, indentLevel + 1, "// Empty function body");
            }
            CodeFormatter.AppendLine(sb, indentLevel, "}");
        }
    }

    private void RenderVariable(StringBuilder sb, VariableDeclarationNode node, int indentLevel)
    {
        // JSDoc комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Модификаторы и объявление переменной
        var exportKeyword = node.IsExported ? "export " : "";
        
        var constKeyword = node.DeclarationKind switch
        {
            VariableDeclarationKind.Const => "const",
            VariableDeclarationKind.Let => "let",
            VariableDeclarationKind.Var => "var",
            _ => "const"
        };
        var type = node.Type != null ? $": {RenderType(node.Type)}" : "";
        var initializer = node.Initializer != null ? $" = {Visit(node.Initializer)}" : "";
        
        // Проверяем, нужно ли добавить "as const satisfies" assertion для строгой типизации
        var asSatisfies = "";
        if (node.AsConstSatisfies && node.SatisfiesType != null)
        {
            // Используем "as const satisfies" для максимально строгой типизации
            var satisfiesTypeStr = RenderType(node.SatisfiesType);
            asSatisfies = $" as const satisfies {satisfiesTypeStr}";
        }
        // Если нет "as const satisfies", проверяем "as const" (для обратной совместимости)
        else if (node.AsConstAssertion)
        {
            asSatisfies = " as const";
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, $"{exportKeyword}{constKeyword} {node.Name.Name}{type}{initializer}{asSatisfies};");
    }

    private void RenderEnum(StringBuilder sb, EnumDeclarationNode node, int indentLevel)
    {
        // JSDoc комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Модификаторы и объявление enum
        // Для TypeScript enum всегда export
        var exportKeyword = "export "; // Всегда экспортируем enum'ы
        var constKeyword = ""; // TypeScript const enum можно добавить позже через metadata
        CodeFormatter.AppendLine(sb, indentLevel, $"{exportKeyword}{constKeyword}enum {node.Name.Name} {{");
        
        // Члены enum
        for (int i = 0; i < node.Members.Count; i++)
        {
            var member = node.Members[i];
            var comma = i < node.Members.Count - 1 ? "," : "";
            RenderEnumMember(sb, (EnumMemberDeclarationNode)member, indentLevel + 1, comma);
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "}");
    }

    /// <summary>Renders model property documentation and the independent presence marker.</summary>
    /// <param name="sb">The output buffer.</param>
    /// <param name="node">The model property.</param>
    /// <param name="indentLevel">The member indentation level.</param>
    private void RenderProperty(StringBuilder sb, PropertyDeclarationNode node, int indentLevel)
    {
        // Проверяем, является ли тип nullable или optional
        var isNullable = IsTypeNullable(node.Type);
        var isOptional = !node.IsRequired || IsTypeOptional(node.Type);
        var isNullishUnion = isNullable && isOptional;
        
        // Получаем constraints из metadata
        var constraints = node.Constraints;
        
        // Формируем JSDoc комментарий
        var jsDocLines = new List<string>();
        
        // Добавляем описание из документации (@description)
        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
        {
            jsDocLines.Add($"@description {node.Documentation.Text}");
        }
        
        // Добавляем @type для явного указания типа
        var typeString = RenderType(node.Type);
        jsDocLines.Add($"@type {{{typeString}}}");
        
        // Nullish: один тег вместо раздельных @optional / @nullable
        if (isNullishUnion)
            jsDocLines.Add("@nullish");
        else
        {
            if (isNullable)
                jsDocLines.Add("@nullable");
            if (isOptional && !node.IsRequired)
                jsDocLines.Add("@optional");
        }
        
        if (node.IsReadOnly)
            jsDocLines.Add("@readonly");
        if (node.IsDeprecated)
            jsDocLines.Add("@deprecated");
        
        // Добавляем constraints из IrPropertyConstraints
        if (constraints != null)
        {
            // Минимальная длина для строк
            if (constraints.MinLength.HasValue)
            {
                jsDocLines.Add($"@minLength {constraints.MinLength.Value}");
            }
            
            // Максимальная длина для строк
            if (constraints.MaxLength.HasValue)
            {
                jsDocLines.Add($"@maxLength {constraints.MaxLength.Value}");
            }
            
            // Минимальное значение для чисел
            if (constraints.Min.HasValue)
            {
                jsDocLines.Add($"@min {constraints.Min.Value}");
            }
            
            // Максимальное значение для чисел
            if (constraints.Max.HasValue)
            {
                jsDocLines.Add($"@max {constraints.Max.Value}");
            }
            
            // Исключающее минимальное значение
            if (constraints.ExclusiveMin.HasValue)
            {
                jsDocLines.Add($"@exclusiveMin {constraints.ExclusiveMin.Value}");
            }
            
            // Исключающее максимальное значение
            if (constraints.ExclusiveMax.HasValue)
            {
                jsDocLines.Add($"@exclusiveMax {constraints.ExclusiveMax.Value}");
            }
            
            // Регулярное выражение
            if (!string.IsNullOrWhiteSpace(constraints.Pattern))
            {
                jsDocLines.Add($"@pattern {constraints.Pattern}");
            }
            
            // Значение по умолчанию
            if (constraints.DefaultValue != null)
            {
                var defaultValueStr = RenderDefaultValue(constraints.DefaultValue);
                jsDocLines.Add($"@default {defaultValueStr}");
            }
        }
        
        if (!string.IsNullOrEmpty(node.Example))
            jsDocLines.Add($"@example {RenderDefaultValue(node.Example)}");
        if (node.IsEnum)
            jsDocLines.Add("@enum");
        if (node.IsConstant)
            jsDocLines.Add("@constant");
        
        // Генерируем JSDoc комментарий, если есть что добавить
        if (jsDocLines.Count > 0)
        {
            CodeFormatter.AppendLine(sb, indentLevel, "/**");
            foreach (var line in jsDocLines)
            {
                CodeFormatter.AppendLine(sb, indentLevel, $" * {line}");
            }
            CodeFormatter.AppendLine(sb, indentLevel, " */");
        }
        
        // A nullable or undefined-capable value does not make a required key optional.
        var optional = !node.IsRequired ? "?" : "";
        var isReadonly = node.IsReadOnly ? "readonly " : "";
        var type = RenderType(node.Type);
        CodeFormatter.AppendLine(sb, indentLevel, $"{isReadonly}{node.Name.Name}{optional}: {type};");
    }
    
    /// <summary>
    /// Рендерит значение по умолчанию для JSDoc
    /// </summary>
    private string RenderDefaultValue(object? value)
    {
        if (value == null)
            return "null";
        
        return value switch
        {
            string str => $"\"{str.Replace("\"", "\\\"")}\"",
            bool b => b ? "true" : "false",
            int or long or short or byte => value.ToString() ?? "0",
            float or double or decimal => value.ToString() ?? "0",
            _ => value.ToString() ?? "null"
        };
    }
    
    /// <summary>Determines whether the value type permits null independently from property presence.</summary>
    /// <param name="type">The property value type.</param>
    /// <returns>Whether null is a valid value.</returns>
    private static bool IsTypeNullable(TypeNode type) => type.IsNullable || type switch
    {
        UnionTypeNode union => union.Types.Any(IsTypeNullable),
        OptionalTypeNode optional => IsTypeNullable(optional.InnerType),
        SimpleTypeNode { Name.Name: "null" } => true,
        _ => false,
    };

    /// <summary>Determines whether the value type permits undefined.</summary>
    /// <param name="type">The property value type.</param>
    /// <returns>Whether undefined is a valid value.</returns>
    private static bool IsTypeOptional(TypeNode type) => type.IsOptional || type switch
    {
        UnionTypeNode union => union.Types.Any(IsTypeOptional),
        NullableTypeNode nullable => IsTypeOptional(nullable.InnerType),
        SimpleTypeNode { Name.Name: "undefined" } => true,
        _ => false,
    };

    private void RenderParameter(StringBuilder sb, ParameterDeclarationNode node)
    {
        var optional = node.IsRequired ? "" : "?";
        // Type=null означает «не выводить аннотацию» — используется для arrow‑callbacks
        // (например, `(This) => ...` для Type.Recursive).
        var type = node.Type != null ? $": {RenderType(node.Type)}" : "";
        var defaultValue = node.DefaultValue != null ? $" = {Visit(node.DefaultValue)}" : "";
        sb.Append($"{node.Name.Name}{optional}{type}{defaultValue}");
    }

    private string RenderParameterDeclaration(ParameterDeclarationNode node)
    {
        var sb = new StringBuilder();
        RenderParameter(sb, node);
        return sb.ToString();
    }

    private void RenderEnumMember(StringBuilder sb, EnumMemberDeclarationNode node, int indentLevel, string? comma = null)
    {
        // JSDoc комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Значение enum
        var value = node.Value != null ? $" = {Visit(node.Value)}" : "";
        CodeFormatter.AppendLine(sb, indentLevel, $"{node.Name.Name}{value}{comma ?? ""}");
    }


    private void RenderComment(StringBuilder sb, CommentNode comment, int indentLevel)
    {
        if (comment.CommentKind == CommentKind.JSDoc || comment.CommentKind == CommentKind.Xml)
        {
            CommentGenerator.GenerateJSDocComment(sb, indentLevel, comment.Text);
        }
        else if (comment.CommentKind == CommentKind.SingleLine)
        {
            CodeFormatter.AppendLine(sb, indentLevel, $"// {comment.Text}");
        }
        else if (comment.CommentKind == CommentKind.MultiLine)
        {
            var lines = comment.Text.Split('\n');
            CodeFormatter.AppendLine(sb, indentLevel, "/*");
            foreach (var line in lines)
            {
                CodeFormatter.AppendLine(sb, indentLevel, $" * {line.TrimEnd()}");
            }
            CodeFormatter.AppendLine(sb, indentLevel, " */");
        }
    }

    private string RenderType(TypeNode type)
    {
        return type.Accept(this);
    }

    private string RenderLiteral(LiteralExpressionNode node)
    {
        return node.LiteralKind switch
        {
            LiteralKind.String => $"\"{CodeEscapeHelper.EscapeTypeScriptString(node.Value?.ToString() ?? "")}\"",
            LiteralKind.Number => node.Value?.ToString() ?? "0",
            LiteralKind.Boolean => node.Value?.ToString()?.ToLowerInvariant() ?? "false",
            LiteralKind.Null => "null",
            LiteralKind.Character => $"'{node.Value}'",
            _ => node.Value?.ToString() ?? "null"
        };
    }

    // Реализация недостающих абстрактных методов BaseAstVisitor
    public override string VisitUsingStatement(UsingStatementNode node)
    {
        // TypeScript не имеет using statement в том же смысле, что C#
        // Можно использовать try-finally или просто игнорировать
        return VisitDefault(node);
    }
    
    public override string VisitMethodDeclaration(MethodDeclarationNode node)
    {
        // TypeScript использует FunctionDeclaration, а не MethodDeclaration
        // Преобразуем в функцию
        return VisitDefault(node);
    }
    
    public override string VisitThrowStatement(ThrowStatementNode node)
    {
        var exception = node.Expression != null ? Visit(node.Expression) : "";
        return $"throw {exception};";
    }
    
    public override string VisitBlankLineStatement(BlankLineStatementNode node) => "";

    public override string VisitVariableDeclarationStatement(VariableDeclarationStatementNode node)
    {
        var name = node.Name.Name;
        var type = node.Type != null ? $": {RenderType(node.Type)}" : "";
        var initializer = node.Initializer != null ? $" = {Visit(node.Initializer)}" : "";
        return $"const {name}{type}{initializer};";
    }
    
    public override string VisitTryCatchStatement(TryCatchStatementNode node)
    {
        var sb = new StringBuilder();
        var savedIndent = _indentLevel;
        CodeFormatter.AppendLine(sb, _indentLevel, "try {");
        _indentLevel++;
        foreach (var stmt in node.TryBlock.Statements)
        {
            var rendered = Visit(stmt);
            CodeFormatter.AppendLine(sb, _indentLevel, rendered);
        }
        _indentLevel = savedIndent;
        if (node.CatchClauses.Count > 0)
        {
            foreach (var catchClause in node.CatchClauses)
            {
                var catchCode = VisitCatchClause(catchClause);
                sb.Append(catchCode);
            }
        }
        if (node.FinallyBlock != null)
        {
            CodeFormatter.AppendLine(sb, _indentLevel, "finally {");
            _indentLevel++;
            foreach (var stmt in node.FinallyBlock.Statements)
            {
                var rendered = Visit(stmt);
                CodeFormatter.AppendLine(sb, _indentLevel, rendered);
            }
            _indentLevel = savedIndent;
            CodeFormatter.AppendLine(sb, _indentLevel, "}");
        }
        return sb.ToString();
    }
    
    public override string VisitAwaitExpression(AwaitExpressionNode node)
    {
        var expression = Visit(node.Expression);
        return $"await {expression}";
    }
    
    public override string VisitCatchClause(CatchClauseNode node)
    {
        var sb = new StringBuilder();
        var savedIndent = _indentLevel;
        var exceptionVar = node.ExceptionVariable != null ? node.ExceptionVariable.Name : "";
        var exceptionType = node.ExceptionType != null ? $": {RenderType(node.ExceptionType)}" : "";
        var exception = !string.IsNullOrEmpty(exceptionVar) ? $"({exceptionVar}{exceptionType})" : "";
        CodeFormatter.AppendLine(sb, _indentLevel, $"catch {exception} {{");
        _indentLevel++;
        foreach (var stmt in node.Body.Statements)
        {
            var rendered = Visit(stmt);
            CodeFormatter.AppendLine(sb, _indentLevel, rendered);
        }
        _indentLevel = savedIndent;
        CodeFormatter.AppendLine(sb, _indentLevel, "}");
        return sb.ToString();
    }
    
    public override string VisitCastExpression(CastExpressionNode node)
    {
        var expression = Visit(node.Expression);
        var targetType = RenderType(node.TargetType);
        // TypeScript использует as для type assertion
        return $"({expression} as {targetType})";
    }

    /// <summary>
    /// Catches AST nodes the base renderer can't dispatch directly (typically nodes
    /// from feature plugins — Zod / Valibot / TypeBox / Axios / TanStack — whose
    /// <c>Accept</c> falls back to <see cref="IBaseAstVisitor{TResult}.DefaultVisit(AstNode)"/>
    /// because this renderer is not their specialised visitor).
    ///
    /// Walks the registered <see cref="ITypeScriptNodeRenderer"/> chain (sorted by
    /// descending Priority) and returns the first matching renderer's output. If no
    /// renderer claims the node, falls back to the base behaviour
    /// (<see cref="NotSupportedException"/>).
    /// </summary>
    /// <param name="node">The TypeScript type query or plugin-owned node to render.</param>
    /// <returns>The structured node's rendered TypeScript syntax.</returns>
    public override string VisitDefault(AstNode node)
    {
        if (node is TypeQueryTypeNode typeQuery)
            return $"typeof {typeQuery.Expression.Accept(this)}";

        foreach (var renderer in _chain)
        {
            if (renderer.CanRender(node))
                return renderer.Render(node, this);
        }
        return base.VisitDefault(node);
    }

    #endregion
}
