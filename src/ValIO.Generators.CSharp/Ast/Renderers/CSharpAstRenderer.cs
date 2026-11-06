using System.Globalization;
using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Http;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Ast.Visitors;
using ValIO.Core.Utilities;
using ValIO.Generators.CSharp.Ast.Nodes;
using ValIO.Generators.CSharp.Ast.Visitors;
using ValIO.Generators.CSharp.Services;

namespace ValIO.Generators.CSharp.Ast.Renderers;

/// <summary>
/// Рендерер AST для генерации C# кода
/// Реализует ICSharpAstVisitor и IHttpAstVisitor для поддержки HTTP клиентов
/// </summary>
public class CSharpAstRenderer : CSharpAstVisitorBase<string>, ICSharpAstRenderer, IHttpAstVisitor<string>
{
    private readonly StringBuilder _sb = new();
    private int _indentLevel = 0;

    /// <summary>
    /// Рендерит CompilationUnit
    /// </summary>
    public string Render(CompilationUnitNode node)
    {
        _sb.Clear();
        _indentLevel = 0;
        
        // Рендерим using директивы
        if (node.Usings.Count > 0)
        {
            foreach (var usingDirective in node.Usings)
            {
                var rendered =  Render(usingDirective);
                _sb.Append(rendered);
            }
            CodeFormatter.AppendEmptyLine(_sb);
        }
        
        // Рендерим namespaces (без очистки _sb)
        if (node.Namespaces.Count > 0)
        {
            foreach (var namespaceNode in node.Namespaces)
            {
                RenderNamespaceContent(namespaceNode);
            }
        }
        
        // Рендерим declarations верхнего уровня
        if (node.Declarations.Count > 0)
        {
            foreach (var declaration in node.Declarations)
            {
                var rendered = RenderDeclaration(declaration);
                _sb.Append(rendered);
                CodeFormatter.AppendEmptyLine(_sb);
            }
        }
        
        return NormalizeEndOfFile(_sb);
    }

    /// <summary>
    /// Рендерит Namespace (отдельно, с собственным StringBuilder)
    /// </summary>
    public string Render(NamespaceDeclarationNode node)
    {
        _sb.Clear();
        _indentLevel = 0;
        
        CodeFormatter.AppendLine(_sb, _indentLevel, $"namespace {node.Name.Name};");
        CodeFormatter.AppendEmptyLine(_sb);
        
        foreach (var declaration in node.Declarations)
        {
            var rendered = RenderDeclaration(declaration);
            _sb.Append(rendered);
            CodeFormatter.AppendEmptyLine(_sb);
        }
        
        return NormalizeEndOfFile(_sb);
    }

    private static string NormalizeEndOfFile(StringBuilder builder) =>
        builder.ToString().TrimEnd('\r', '\n') + Environment.NewLine;
    
    /// <summary>
    /// Рендерит содержимое Namespace без очистки StringBuilder (для использования в CompilationUnit)
    /// </summary>
    private void RenderNamespaceContent(NamespaceDeclarationNode node)
    {
        CodeFormatter.AppendLine(_sb, _indentLevel, $"namespace {node.Name.Name};");
        CodeFormatter.AppendEmptyLine(_sb);
        
        foreach (var declaration in node.Declarations)
        {
            var rendered = RenderDeclaration(declaration);
            _sb.Append(rendered);
            CodeFormatter.AppendEmptyLine(_sb);
        }
    }

    /// <summary>
    /// Рендерит Class
    /// </summary>
    public string Render(ClassDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderClass(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Record
    /// </summary>
    public string Render(RecordDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderRecord(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Struct
    /// </summary>
    public string Render(StructDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderStruct(sb, node, indentLevel);
        
        return sb.ToString();
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

    /// <summary>
    /// Рендерит Method
    /// </summary>
    public string Render(MethodDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderMethod(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Field
    /// </summary>
    public string Render(FieldDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderField(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит Constructor
    /// </summary>
    public string Render(ConstructorDeclarationNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = 0;
        
        RenderConstructor(sb, node, indentLevel);
        
        return sb.ToString();
    }

    /// <summary>
    /// Рендерит UsingDirective
    /// </summary>
    public string Render(UsingDirectiveNode node)
    {
        var sb = new StringBuilder();
        CodeFormatter.AppendLine(sb, 0, $"using {node.Namespace.Name};");
        return sb.ToString();
    }

    #region Visitor Pattern Implementation

    public override string VisitCompilationUnit(CompilationUnitNode node)
    {
        return Render(node);
    }

    public override string VisitNamespaceDeclaration(NamespaceDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitUsingDirective(UsingDirectiveNode node)
    {
        return Render(node);
    }

    public override string VisitClassDeclaration(ClassDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitRecordDeclaration(RecordDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitStructDeclaration(StructDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitInterfaceDeclaration(InterfaceDeclarationNode node)
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

    public override string VisitMethodDeclaration(MethodDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitFieldDeclaration(FieldDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitConstructorDeclaration(ConstructorDeclarationNode node)
    {
        return Render(node);
    }

    public override string VisitParameterDeclaration(ParameterDeclarationNode node)
    {
        var sb = new StringBuilder();
        RenderParameter(sb, node);
        return sb.ToString();
    }

    public override string VisitEnumMemberDeclaration(EnumMemberDeclarationNode node)
    {
        var sb = new StringBuilder();
        RenderEnumMember(sb, node, 1);
        return sb.ToString();
    }

    public override string VisitRawType(RawTypeNode node) => node.RawTypeString;

    public override string VisitSimpleType(SimpleTypeNode node)
    {
        return node.Name.Name;
    }

    public override string VisitNullableType(NullableTypeNode node)
    {
        return $"{RenderType(node.InnerType)}?";
    }

    public override string VisitArrayType(ArrayTypeNode node)
    {
        return $"{RenderType(node.ElementType)}[]";
    }

    public override string VisitGenericType(GenericTypeNode node)
    {
        var typeArgs = string.Join(", ", node.TypeArguments.Select(RenderType));
        return $"{node.Name.Name}<{typeArgs}>";
    }

    public override string VisitDictionaryType(DictionaryTypeNode node)
    {
        var keyType = RenderType(node.KeyType);
        var valueType = RenderType(node.ValueType);
        return $"Dictionary<{keyType}, {valueType}>";
    }

    public override string VisitLiteralExpression(LiteralExpressionNode node)
    {
        return RenderLiteral(node);
    }

    /// <inheritdoc/>
    public override string VisitDefaultExpression(DefaultExpressionNode node)
    {
        var value = node.Type is null ? "default" : $"default({RenderType(node.Type)})";
        return node.SuppressNullableWarning ? value + "!" : value;
    }

    /// <inheritdoc />
    public override string VisitTypeOfExpression(TypeOfExpressionNode node) => $"typeof({RenderType(node.TargetType)})";

    public override string VisitIdentifierExpression(IdentifierExpressionNode node)
    {
        return node.Identifier.Name;
    }

    /// <inheritdoc />
    public override string VisitCallExpression(CallExpressionNode node)
    {
        var target = Visit(node.Target);
        
        // Проверяем, есть ли generic type argument
        var genericTypeArgStr = "";
        if (node.GenericTypeArgument != null)
        {
            genericTypeArgStr = $"<{RenderType(node.GenericTypeArgument)}>";
        }
        else if (!string.IsNullOrEmpty(node.GenericTypeName))
        {
            genericTypeArgStr = $"<{node.GenericTypeName}>";
        }
        
        var args = string.Join(", ", node.Arguments.Select(Visit));
        
        // Проверяем, является ли это вызовом base() конструктора
        if (node.IsConstructorBaseCall)
        {
            return $"base({args})";
        }
        
        // Проверяем, является ли это new expression
        if (node.IsNewExpression)
        {
            // Если есть GenericTypeName, используем его вместо target
            var genericTypeName = !string.IsNullOrEmpty(node.GenericTypeName)
                ? node.GenericTypeName
                : $"{target}{genericTypeArgStr}";
            return $"new {genericTypeName}({args})";
        }
        
        // Проверяем, является ли это лямбда-выражением
        if (node.IsLambda && node.LambdaBody != null)
        {
            var lambdaBodyStr = Visit(node.LambdaBody);
            return $"async () => {lambdaBodyStr}";
        }
        
        return $"{target}{genericTypeArgStr}({args})";
    }

    public override string VisitUnaryExpression(UnaryExpressionNode node)
    {
        var op = node.Operator switch
        {
            UnaryOperator.Dereference => "*",
            UnaryOperator.AddressOf => "&",
            UnaryOperator.LogicalNot => "!",
            UnaryOperator.Negate => "-",
            _ => ""
        };
        return $"{op}{Visit(node.Operand)}";
    }

    public override string VisitMemberAccessExpression(MemberAccessExpressionNode node)
    {
        var target = Visit(node.Object);
        return $"{target}.{CSharpIdentifier.Escape(node.Member.Name)}";
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

    public override string VisitVariableDeclarationStatement(VariableDeclarationStatementNode node)
    {
        var type = node.Type != null ? $"{RenderType(node.Type)} " : "var ";
        var initializer = node.Initializer != null ? $" = {Visit(node.Initializer)}" : "";
        return $"{type}{CSharpIdentifier.Escape(node.Name.Name)}{initializer};";
    }
    
    public override string VisitThrowStatement(ThrowStatementNode node)
    {
        if (node.Expression == null)
        {
            return "throw;";
        }
        var expr = Visit(node.Expression);
        return $"throw {expr};";
    }
    
    public override string VisitTryCatchStatement(TryCatchStatementNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = _indentLevel;
        
        // try {
        CodeFormatter.AppendLine(sb, indentLevel, "try");
        CodeFormatter.AppendLine(sb, indentLevel, "{");
        
        // Тело try блока
        var savedIndent = _indentLevel;
        _indentLevel = indentLevel + 1;
        foreach (var stmt in node.TryBlock.Statements)
        {
            var rendered = Visit(stmt);
            if (rendered.Contains('\n'))
            {
                sb.Append(rendered);
                if (!rendered.EndsWith("\n") && !rendered.EndsWith("\r\n"))
                {
                    sb.AppendLine();
                }
            }
            else
            {
                CodeFormatter.AppendLine(sb, _indentLevel, rendered);
            }
        }
        _indentLevel = savedIndent;
        CodeFormatter.AppendLine(sb, indentLevel, "}");
        
        // catch блоки
        foreach (var catchClause in node.CatchClauses)
        {
            sb.Append(Visit(catchClause));
        }
        
        // finally блок
        if (node.FinallyBlock != null)
        {
            CodeFormatter.AppendLine(sb, indentLevel, "finally");
            CodeFormatter.AppendLine(sb, indentLevel, "{");
            _indentLevel = indentLevel + 1;
            foreach (var stmt in node.FinallyBlock.Statements)
            {
                var rendered = Visit(stmt);
                if (rendered.Contains('\n'))
                {
                    sb.Append(rendered);
                    if (!rendered.EndsWith("\n") && !rendered.EndsWith("\r\n"))
                    {
                        sb.AppendLine();
                    }
                }
                else
                {
                    CodeFormatter.AppendLine(sb, _indentLevel, rendered);
                }
            }
            _indentLevel = savedIndent;
            CodeFormatter.AppendLine(sb, indentLevel, "}");
        }
        
        return sb.ToString();
    }
    
    public override string VisitCatchClause(CatchClauseNode node)
    {
        var sb = new StringBuilder();
        var indentLevel = _indentLevel;
        
        // catch (ExceptionType ex) {
        if (node.ExceptionType != null && node.ExceptionVariable != null)
        {
            var exceptionType = RenderType(node.ExceptionType);
            CodeFormatter.AppendLine(sb, indentLevel, $"catch ({exceptionType} {node.ExceptionVariable.Name})");
        }
        else if (node.ExceptionType != null)
        {
            var exceptionType = RenderType(node.ExceptionType);
            CodeFormatter.AppendLine(sb, indentLevel, $"catch ({exceptionType})");
        }
        else
        {
            CodeFormatter.AppendLine(sb, indentLevel, "catch");
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "{");
        
        // Тело catch блока
        var savedIndent = _indentLevel;
        _indentLevel = indentLevel + 1;
        foreach (var stmt in node.Body.Statements)
        {
            var rendered = Visit(stmt);
            if (rendered.Contains('\n'))
            {
                sb.Append(rendered);
                if (!rendered.EndsWith("\n") && !rendered.EndsWith("\r\n"))
                {
                    sb.AppendLine();
                }
            }
            else
            {
                CodeFormatter.AppendLine(sb, _indentLevel, rendered);
            }
        }
        _indentLevel = savedIndent;
        CodeFormatter.AppendLine(sb, indentLevel, "}");
        
        return sb.ToString();
    }
    
    public override string VisitUsingStatement(UsingStatementNode node)
    {
        var sb = new StringBuilder();
        
        // Проверяем, является ли это using с переменной
        if (node.Resource is CallExpressionNode callExpr && 
            callExpr.IsUsingVariable &&
            callExpr.VariableDeclaration != null)
        {
            var varDecl = callExpr.VariableDeclaration;
            // Using с переменной: using (var x = ...) { ... }
            if (varDecl.Type != null)
            {
                var type = RenderType(varDecl.Type);
                var initializer = varDecl.Initializer != null ? Visit(varDecl.Initializer) : "";
                CodeFormatter.AppendLine(sb, _indentLevel, $"using ({type} {varDecl.Name.Name} = {initializer})");
            }
            else
            {
                var initializer = varDecl.Initializer != null ? Visit(varDecl.Initializer) : "";
                CodeFormatter.AppendLine(sb, _indentLevel, $"using (var {varDecl.Name.Name} = {initializer})");
            }
            
            // Рендерим тело using с правильными отступами
            CodeFormatter.AppendLine(sb, _indentLevel, "{");
            _indentLevel++;
            foreach (var statement in ((BlockNode)node.Body).Statements)
            {
                var stmt = Visit(statement);
                // Если строка многострочная, она уже имеет правильные отступы внутри
                if (stmt.Contains('\n'))
                {
                    sb.Append(stmt);
                    if (!stmt.EndsWith("\n") && !stmt.EndsWith("\r\n"))
                    {
                        sb.AppendLine();
                    }
                }
                else
                {
                    CodeFormatter.AppendLine(sb, _indentLevel, stmt);
                }
            }
            _indentLevel--;
            CodeFormatter.AppendLine(sb, _indentLevel, "}");
        }
        else
        {
            // Using с выражением: using (expression) { ... }
            var resource = Visit(node.Resource);
            CodeFormatter.AppendLine(sb, _indentLevel, $"using ({resource})");
            // Рендерим тело using с правильными отступами
            CodeFormatter.AppendLine(sb, _indentLevel, "{");
            _indentLevel++;
            foreach (var statement in ((BlockNode)node.Body).Statements)
            {
                var stmt = Visit(statement);
                // Если строка многострочная, она уже имеет правильные отступы внутри
                if (stmt.Contains('\n'))
                {
                    sb.Append(stmt);
                    if (!stmt.EndsWith("\n") && !stmt.EndsWith("\r\n"))
                    {
                        sb.AppendLine();
                    }
                }
                else
                {
                    CodeFormatter.AppendLine(sb, _indentLevel, stmt);
                }
            }
            _indentLevel--;
            CodeFormatter.AppendLine(sb, _indentLevel, "}");
        }
        
        return sb.ToString();
    }
    
    /// <inheritdoc />
    public override string VisitCastExpression(CastExpressionNode node)
    {
        var targetType = RenderType(node.TargetType);
        var expression = Visit(node.Expression);
        
        if (node.UseAsOperator)
        {
            return $"{expression} as {targetType}";
        }
        
        return $"({targetType}){expression}";
    }

    public override string VisitAwaitExpression(AwaitExpressionNode node)
    {
        var expr = Visit(node.Expression);
        return $"await {expr}";
    }

    public override string VisitIfStatement(IfStatementNode node)
    {
        var sb = new StringBuilder();
        var condition = Visit(node.Condition);
        CodeFormatter.AppendLine(sb, _indentLevel, $"if ({condition})");
        var thenBlock = Visit(node.ThenBlock);
        // VisitBlock возвращает строку с правильными отступами, добавляем её как есть
        sb.Append(thenBlock);
        if (node.ElseBlock != null)
        {
            CodeFormatter.AppendLine(sb, _indentLevel, "else");
            var elseBlock = Visit(node.ElseBlock);
            // VisitBlock возвращает строку с правильными отступами, добавляем её как есть
            sb.Append(elseBlock);
        }
        return sb.ToString();
    }

    public override string VisitForEachStatement(ForEachStatementNode node)
    {
        var sb = new StringBuilder();
        var variable = node.Variable.Name;
        var collection = Visit(node.Collection);
        CodeFormatter.AppendLine(sb, _indentLevel, $"foreach (var {variable} in {collection})");
        var body = Visit(node.Body);
        sb.Append(body);
        return sb.ToString();
    }

    #endregion

    #region Private Helper Methods

    private string RenderDeclaration(DeclarationNode declaration)
    {
        return declaration.Accept(this);
    }

    private void RenderClass(StringBuilder sb, ClassDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы и объявление класса
        var classModifiers = GetClassModifiers(node);
        var modifiers = RenderModifiers(node.AccessModifier, classModifiers);
        var baseTypes = RenderBaseTypes(node.BaseTypes);
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}class {node.Name.Name}{baseTypes}");
        CodeFormatter.AppendLine(sb, indentLevel, "{");
        
        // Члены класса
        foreach (var member in node.Members)
        {
            RenderMember(sb, member, indentLevel + 1);
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "}");
    }

    private void RenderRecord(StringBuilder sb, RecordDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы и объявление record
        var recordKeyword = node.IsRecordStruct ? "record struct" : "record";
        var modifiers = RenderModifiers(node.AccessModifier);
        var baseTypes = RenderBaseTypes(node.BaseTypes);
        var primaryParams = node.PrimaryConstructorParameters.Count > 0 
            ? $"({string.Join(", ", node.PrimaryConstructorParameters.Select(p => VisitParameterDeclaration(p)))})" 
            : "";
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}{recordKeyword} {node.Name.Name}{primaryParams}{baseTypes}");
        CodeFormatter.AppendLine(sb, indentLevel, "{");
        
        // Члены record
        foreach (var member in node.Members)
        {
            RenderMember(sb, member, indentLevel + 1);
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "}");
    }

    private void RenderStruct(StringBuilder sb, StructDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы и объявление struct
        var modifiers = RenderModifiers(node.AccessModifier);
        var baseTypes = RenderBaseTypes(node.BaseTypes);
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}struct {node.Name.Name}{baseTypes}");
        CodeFormatter.AppendLine(sb, indentLevel, "{");
        
        // Члены struct
        foreach (var member in node.Members)
        {
            RenderMember(sb, member, indentLevel + 1);
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "}");
    }

    private void RenderInterface(StringBuilder sb, InterfaceDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы и объявление interface
        var modifiers = RenderModifiers(node.AccessModifier);
        var baseTypes = RenderBaseTypes(node.BaseTypes);
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}interface {node.Name.Name}{baseTypes}");
        CodeFormatter.AppendLine(sb, indentLevel, "{");
        
        // Члены interface
        foreach (var member in node.Members)
        {
            RenderMember(sb, member, indentLevel + 1);
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "}");
    }

    private void RenderEnum(StringBuilder sb, EnumDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
        {
            RenderComment(sb, node.Documentation, indentLevel);
            CodeFormatter.AppendEmptyLine(sb);
        }
        
        // Атрибуты типа (например, [JsonConverter(typeof(JsonStringEnumConverter))])
        RenderAttributes(sb, node.Attributes, indentLevel);

        // Модификаторы и объявление enum
        var modifiers = RenderModifiers(node.AccessModifier);
        var underlyingType = node.UnderlyingType != null ? $" : {RenderType(node.UnderlyingType)}" : "";
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}enum {node.Name.Name}{underlyingType}");
        CodeFormatter.AppendLine(sb, indentLevel, "{");
        
        // Члены enum
        for (int i = 0; i < node.Members.Count; i++)
        {
            var member = (EnumMemberDeclarationNode)node.Members[i];
            var comma = i < node.Members.Count - 1 ? "," : "";
            RenderEnumMember(sb, member, indentLevel + 1, comma);
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, "}");
    }

    private void RenderProperty(StringBuilder sb, PropertyDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы (включая required) и объявление свойства
        var extraModifiers = node.IsRequired ? new[] { "required" } : null;
        var modifiers = RenderModifiers(AccessModifier.Public, extraModifiers); // PropertyDeclarationNode не имеет AccessModifier, используем public по умолчанию
        var type = RenderType(node.Type);
        var accessor = node.Accessor switch
        {
            PropertyAccessorKind.Get => " { get; }",
            PropertyAccessorKind.Set => " { set; }",
            PropertyAccessorKind.Init => " { get; init; }",
            PropertyAccessorKind.GetSet => " { get; set; }",
            PropertyAccessorKind.GetInit => " { get; init; }",
            _ => " { get; set; }"
        };
        // Инициализатор (например, "= default!;" или значение из constraints.DefaultValue) —
        // без него DefaultValue ранее молча терялся при рендеринге свойств.
        var initializer = node.DefaultValue != null ? $" = {Visit(node.DefaultValue)}" : "";
        var terminator = node.DefaultValue != null ? ";" : "";
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}{type} {CSharpIdentifier.Escape(node.Name.Name)}{accessor}{initializer}{terminator}");
    }

    /// <summary>Renders a method definition or signature with valid declaration modifiers.</summary>
    /// <param name="sb">The generated source buffer.</param>
    /// <param name="node">The method declaration.</param>
    /// <param name="indentLevel">The enclosing indentation depth.</param>
    private void RenderMethod(StringBuilder sb, MethodDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.UseInheritDoc)
        {
            // Используем inheritdoc для методов класса
            CodeFormatter.AppendLine(sb, indentLevel, "/// <inheritdoc />");
        }
        else if (node.Documentation != null)
        {
            var paramDocs = node.Parameters.Select(p => (p.Name.Name, p.Documentation?.Text)).ToList();
            CommentGenerator.GenerateXmlCommentWithParams(sb, indentLevel, node.Documentation.Text, paramDocs, null);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы и объявление метода
        var modifiers = RenderModifiers(node.AccessModifier);
        var returnType = node.ReturnType != null ? $"{RenderType(node.ReturnType)} " : "";
        var parameters = string.Join(", ", node.Parameters.Select(p => VisitParameterDeclaration(p)));
        var asyncKeyword = node.IsAsync && node.Body is not null && !node.IsInterfaceMethod && !node.IsAbstract ? "async " : "";
        
        // Проверяем, нужно ли добавить override
        var overrideKeyword = node.IsOverride ? "override " : "";
        
        // Проверяем, есть ли generic параметры
        var genericParams = "";
        if (node.GenericTypeParameters != null && node.GenericTypeParameters.Count > 0)
        {
            genericParams = $"<{string.Join(", ", node.GenericTypeParameters)}>";
        }
        
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}{overrideKeyword}{asyncKeyword}{returnType}{node.Name.Name}{genericParams}({parameters})");
        
        // Для методов интерфейса не добавляем тело, только точку с запятой
        if (node.IsInterfaceMethod)
        {
            CodeFormatter.AppendLine(sb, indentLevel, ";");
            return;
        }
        
        // Тело метода
        if (node.Body != null && node.Body.Statements.Count > 0)
        {
            CodeFormatter.AppendLine(sb, indentLevel, "{");
            var savedIndent = _indentLevel;
            _indentLevel = indentLevel + 1;
            foreach (var stmt in node.Body.Statements)
            {
                var rendered = Visit(stmt);
                // Если строка многострочная, она уже имеет правильные отступы внутри
                // Добавляем её как есть, без дополнительных отступов
                if (rendered.Contains('\n'))
                {
                    sb.Append(rendered);
                    if (!rendered.EndsWith("\n") && !rendered.EndsWith("\r\n"))
                    {
                        sb.AppendLine();
                    }
                }
                else
                {
                    CodeFormatter.AppendLine(sb, _indentLevel, rendered);
                }
            }
            _indentLevel = savedIndent;
            CodeFormatter.AppendLine(sb, indentLevel, "}");
        }
        else
        {
            CodeFormatter.AppendLine(sb, indentLevel, "{");
            CodeFormatter.AppendLine(sb, indentLevel, "}");
        }
    }

    private void RenderField(StringBuilder sb, FieldDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null)
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы и объявление поля
        var modifiers = RenderModifiers(node.AccessModifier);
        
        // Для static полей с начальным значением в static классах используем const, иначе static readonly
        var constKeyword = "";
        var readonlyKeyword = "";
        if (node.IsStatic && node.DefaultValue != null)
        {
            // Проверяем, является ли тип примитивным или string для const
            var isConstCompatible = node.Type is SimpleTypeNode simpleType && 
                (simpleType.IsPrimitive || simpleType.Name.Name == "string");
            
            if (isConstCompatible)
            {
                constKeyword = "const ";
            }
            else
            {
                readonlyKeyword = "readonly ";
            }
        }
        else if (node.IsReadOnly)
        {
            readonlyKeyword = "readonly ";
        }
        
        if (node.IsStatic && string.IsNullOrEmpty(constKeyword))
        {
            modifiers += "static ";
        }
        
        var type = RenderType(node.Type);
        var initializer = node.DefaultValue != null ? $" = {Visit(node.DefaultValue)}" : "";
        CodeFormatter.AppendLine(sb, indentLevel, $"{modifiers}{constKeyword}{readonlyKeyword}{type} {CSharpIdentifier.Escape(node.Name.Name)}{initializer};");
    }

    private void RenderConstructor(StringBuilder sb, ConstructorDeclarationNode node, int indentLevel)
    {
        // XML комментарий
        if (node.Documentation != null)
        {
            var paramDocs = node.Parameters.Select(p => (p.Name.Name, p.Documentation?.Text)).ToList();
            CommentGenerator.GenerateXmlCommentWithParams(sb, indentLevel, node.Documentation.Text, paramDocs, null);
        }
        
        // Атрибуты
        RenderAttributes(sb, node.Attributes, indentLevel);
        
        // Модификаторы и объявление конструктора
        var modifiers = RenderModifiers(node.AccessModifier);
        var parameters = string.Join(", ", node.Parameters.Select(p => VisitParameterDeclaration(p)));
        
        // Ищем вызов base() в теле конструктора
        string? baseCall = null;
        var bodyStatements = new List<StatementNode>();
        
        if (node.Body != null && node.Body.Statements.Count > 0)
        {
            foreach (var stmt in node.Body.Statements)
            {
                // Проверяем, является ли это вызовом base()
                if (stmt is ExpressionStatementNode exprStmt &&
                    exprStmt.Expression is CallExpressionNode callExpr &&
                    callExpr.IsConstructorBaseCall)
                {
                    // Извлекаем аргументы для base()
                    var baseArgsStr = string.Join(", ", callExpr.Arguments.Select(Visit));
                    baseCall = $"base({baseArgsStr})";
                }
                else
                {
                    // Это обычный statement, добавляем в тело
                    bodyStatements.Add(stmt);
                }
            }
        }
        
        // Формируем заголовок конструктора с base() вызовом, если он есть
        var constructorHeader = $"{modifiers}{node.Name.Name}({parameters})";
        if (baseCall != null)
        {
            constructorHeader += $" : {baseCall}";
        }
        CodeFormatter.AppendLine(sb, indentLevel, constructorHeader);
        
        // Тело конструктора (без вызова base(), так как он уже в заголовке)
        if (bodyStatements.Count > 0)
        {
            CodeFormatter.AppendLine(sb, indentLevel, "{");
            var savedIndent = _indentLevel;
            _indentLevel = indentLevel + 1;
            foreach (var stmt in bodyStatements)
            {
                var rendered = Visit(stmt);
                CodeFormatter.AppendLine(sb, _indentLevel, rendered);
            }
            _indentLevel = savedIndent;
            CodeFormatter.AppendLine(sb, indentLevel, "}");
        }
        else
        {
            CodeFormatter.AppendLine(sb, indentLevel, "{");
            CodeFormatter.AppendLine(sb, indentLevel, "}");
        }
    }

    private void RenderParameter(StringBuilder sb, ParameterDeclarationNode node)
    {
        var modifiers = node.ParameterKind switch
        {
            ParameterKind.Ref => "ref ",
            ParameterKind.Out => "out ",
            ParameterKind.In => "in ",
            _ => ""
        };
        var type = RenderType(node.Type);
        var defaultValue = node.DefaultValue != null ? $" = {Visit(node.DefaultValue)}" : "";
        sb.Append($"{modifiers}{type} {CSharpIdentifier.Escape(node.Name.Name)}{defaultValue}");
    }

    private void RenderEnumMember(StringBuilder sb, EnumMemberDeclarationNode node, int indentLevel, string? comma = null)
    {
        // XML комментарий
        if (node.Documentation != null && !string.IsNullOrWhiteSpace(node.Documentation.Text))
        {
            RenderComment(sb, node.Documentation, indentLevel);
        }
        
        // Атрибуты
        if (node.Attributes.Count > 0)
        {
            foreach (var attr in node.Attributes)
            {
                var rendered = Visit(attr);
                CodeFormatter.AppendLine(sb, indentLevel, rendered);
            }
        }
        
        // Значение enum
        var value = node.Value != null ? $" = {Visit(node.Value)}" : "";
        CodeFormatter.AppendLine(sb, indentLevel, $"{node.Name.Name}{value}{comma ?? ""}");
    }

    private void RenderMember(StringBuilder sb, DeclarationNode member, int indentLevel)
    {
        switch (member)
        {
            case PropertyDeclarationNode property:
                RenderProperty(sb, property, indentLevel);
                CodeFormatter.AppendEmptyLine(sb);
                break;
            case MethodDeclarationNode method:
                RenderMethod(sb, method, indentLevel);
                CodeFormatter.AppendEmptyLine(sb);
                break;
            case FieldDeclarationNode field:
                RenderField(sb, field, indentLevel);
                CodeFormatter.AppendEmptyLine(sb);
                break;
            case ConstructorDeclarationNode ctor:
                RenderConstructor(sb, ctor, indentLevel);
                CodeFormatter.AppendEmptyLine(sb);
                break;
            case ClassDeclarationNode cls:
                RenderClass(sb, cls, indentLevel);
                CodeFormatter.AppendEmptyLine(sb);
                break;
            default:
                throw new NotSupportedException($"Member type {member.GetType().Name} is not supported");
        }
    }

    private void RenderAttributes(StringBuilder sb, IReadOnlyList<AttributeNode> attributes, int indentLevel)
    {
        if (attributes.Count == 0)
            return;
        
        foreach (var attr in attributes)
        {
            var args = attr.Arguments.Count > 0 ? $"({string.Join(", ", attr.Arguments.Select(Visit))})" : "";
            CodeFormatter.AppendLine(sb, indentLevel, $"[{attr.Name.Name}{args}]");
        }
    }

    private string RenderModifiers(AccessModifier accessModifier, IReadOnlyList<string>? modifiers = null)
    {
        var parts = new List<string>();
        
        // Access modifier
        parts.Add(accessModifier switch
        {
            AccessModifier.Public => "public",
            AccessModifier.Private => "private",
            AccessModifier.Protected => "protected",
            AccessModifier.Internal => "internal",
            AccessModifier.ProtectedInternal => "protected internal",
            AccessModifier.PrivateProtected => "private protected",
            _ => ""
        });
        
        // Other modifiers (если переданы)
        if (modifiers != null)
        {
            parts.AddRange(modifiers.Where(m => !string.IsNullOrEmpty(m)));
        }
        
        var result = string.Join(" ", parts.Where(p => !string.IsNullOrEmpty(p)));
        return string.IsNullOrEmpty(result) ? "" : $"{result} ";
    }
    
    private IReadOnlyList<string> GetClassModifiers(ClassDeclarationNode node)
    {
        var modifiers = new List<string>();
        if (node.IsStatic) modifiers.Add("static");
        if (node.IsSealed) modifiers.Add("sealed");
        if (node.IsPartial) modifiers.Add("partial");
        return modifiers;
    }

    private string RenderBaseTypes(IReadOnlyList<TypeNode> baseTypes)
    {
        if (baseTypes.Count == 0)
            return "";
        
        return $" : {string.Join(", ", baseTypes.Select(RenderType))}";
    }

    private string RenderType(TypeNode type)
    {
        return type.Accept(this);
    }

    private void RenderComment(StringBuilder sb, CommentNode comment, int indentLevel)
    {
        if (comment.CommentKind == CommentKind.Xml)
        {
            CommentGenerator.GenerateXmlComment(sb, indentLevel, comment.Text);
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

    /// <summary>Renders a literal value without interpreting its content as C# syntax.</summary>
    /// <param name="node">The typed literal value.</param>
    /// <returns>Escaped C# source for the literal.</returns>
    private string RenderLiteral(LiteralExpressionNode node)
    {
        return node.LiteralKind switch
        {
            LiteralKind.String => $"\"{CodeEscapeHelper.EscapeString(node.Value?.ToString() ?? string.Empty)}\"",
            LiteralKind.Number => Convert.ToString(node.Value, CultureInfo.InvariantCulture) ?? "0",
            LiteralKind.Boolean => node.Value?.ToString()?.ToLowerInvariant() ?? "false",
            LiteralKind.Null => "null",
            LiteralKind.Character => $"'{CodeEscapeHelper.EscapeString(node.Value?.ToString() ?? string.Empty)}'",
            _ => node.Value?.ToString() ?? "null"
        };
    }

    // HTTP специфичные методы (для C# не используются напрямую, но требуются интерфейсом)
    public override string VisitHttpMethod(HttpMethodNode node)
    {
        return node.Method switch
        {
            HttpMethodKind.Get => "HttpMethod.Get",
            HttpMethodKind.Post => "HttpMethod.Post",
            HttpMethodKind.Put => "HttpMethod.Put",
            HttpMethodKind.Delete => "HttpMethod.Delete",
            HttpMethodKind.Patch => "HttpMethod.Patch",
            HttpMethodKind.Head => "HttpMethod.Head",
            HttpMethodKind.Options => "HttpMethod.Options",
            _ => throw new NotSupportedException($"HTTP method {node.Method} is not supported")
        };
    }

    public override string VisitRoute(RouteNode node)
    {
        return $"@\"{node.Path.Replace("\"", "\"\"")}\"";
    }

    public override string VisitPathParameter(PathParameterNode node)
    {
        return $"{RenderType(node.Type)} {CSharpIdentifier.Escape(node.Name.Name)}";
    }

    public override string VisitQueryParameter(QueryParameterNode node)
    {
        return $"{RenderType(node.Type)} {CSharpIdentifier.Escape(node.Name.Name)}";
    }

    public override string VisitHeaderParameter(HeaderParameterNode node)
    {
        return $"{RenderType(node.Type)} {CSharpIdentifier.Escape(node.Name.Name)}";
    }

    public override string VisitRequestBody(RequestBodyNode node)
    {
        return RenderType(node.Type);
    }

    public override string VisitResponseType(ResponseTypeNode node)
    {
        return RenderType(node.Type);
    }

    public override string VisitComment(CommentNode node)
    {
        if (node.CommentKind == CommentKind.Xml)
        {
            var sb = new StringBuilder();
            CommentGenerator.GenerateXmlComment(sb, 0, node.Text);
            return sb.ToString();
        }
        else if (node.CommentKind == CommentKind.SingleLine)
        {
            return $"// {node.Text}";
        }
        else if (node.CommentKind == CommentKind.MultiLine)
        {
            var lines = node.Text.Split('\n');
            var sb = new StringBuilder();
            sb.AppendLine("/*");
            foreach (var line in lines)
            {
                sb.AppendLine($" * {line.TrimEnd()}");
            }
            sb.AppendLine(" */");
            return sb.ToString();
        }
        return string.Empty;
    }
    
    public override string VisitTypeDeclaration(TypeDeclarationNode node)
    {
        // TypeDeclaration - это базовый класс, конкретные реализации обрабатываются отдельно
        throw new NotSupportedException("TypeDeclaration should be handled by specific visitor methods");
    }
    
    public override string VisitBlock(BlockNode node)
    {
        var sb = new StringBuilder();
        CodeFormatter.AppendLine(sb, _indentLevel, "{");
        _indentLevel++;
        foreach (var statement in node.Statements)
        {
            var stmt = Visit(statement);
            CodeFormatter.AppendLine(sb, _indentLevel, stmt);
        }
        _indentLevel--;
        CodeFormatter.AppendLine(sb, _indentLevel, "}");
        return sb.ToString();
    }
    
    public override string VisitBinaryExpression(BinaryExpressionNode node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        
        // Проверяем, является ли это присваиванием (по оператору или по флагу для обратной совместимости)
        if (node.Operator == BinaryOperator.Assign || node.IsAssignment)
        {
            return $"{left} = {right}";
        }
        
        var op = node.Operator switch
        {
            BinaryOperator.Add => "+",
            BinaryOperator.Subtract => "-",
            BinaryOperator.Multiply => "*",
            BinaryOperator.Divide => "/",
            BinaryOperator.Modulo => "%",
            BinaryOperator.Equal => "==",
            BinaryOperator.NotEqual => "!=",
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
            _ => throw new NotSupportedException($"Binary operator {node.Operator} is not supported")
        };
        return $"{left} {op} {right}";
    }
    
    public override string VisitExport(ExportNode node)
    {
        // Export не используется в C#
        return string.Empty;
    }
    
    public override string VisitIdentifier(IdentifierNode node)
    {
        return node.Name;
    }
    
    public override string VisitImport(ImportNode node)
    {
        // Import в C# - это using directive, обрабатывается отдельно
        var from = node.From ?? string.Empty;
        return $"using {from};";
    }
    
    public override string VisitUnionType(UnionTypeNode node)
    {
        // Union type в C# - это object или можно использовать OneOf библиотеку
        // Для простоты возвращаем object
        return "object";
    }
    
    public override string VisitOptionalType(OptionalTypeNode node)
    {
        // Optional type в C# - это nullable тип
        var innerType = RenderType(node.InnerType);
        return $"{innerType}?";
    }
    
    public override string VisitIntersectionType(IntersectionTypeNode node)
    {
        // Intersection type в C# не поддерживается напрямую
        // Возвращаем первый тип
        return RenderType(node.Types.FirstOrDefault() ?? new SimpleTypeNode(new IdentifierNode("object")));
    }
    
    public override string VisitConditionalExpression(ConditionalExpressionNode node)
    {
        var condition = Visit(node.Condition);
        var trueExpr = Visit(node.TrueExpression);
        var falseExpr = Visit(node.FalseExpression);
        return $"{condition} ? {trueExpr} : {falseExpr}";
    }
    
    public override string VisitAttribute(AttributeNode node)
    {
        if (node.Arguments != null && node.Arguments.Count > 0)
        {
            var args = new List<string>();
            foreach (var arg in node.Arguments)
            {
                // Проверяем, является ли это именованным аргументом
                if (arg is BinaryExpressionNode binaryExpr && 
                    binaryExpr.IsNamedArgument)
                {
                    // Рендерим как именованный аргумент: Name = Value
                    var name = Visit(binaryExpr.Left);
                    var value = Visit(binaryExpr.Right);
                    args.Add($"{name} = {value}");
                }
                else
                {
                    args.Add(Visit(arg));
                }
            }
            var argsStr = $"({string.Join(", ", args)})";
            return $"[{node.Name.Name}{argsStr}]";
        }
        return $"[{node.Name.Name}]";
    }

    #endregion
}
