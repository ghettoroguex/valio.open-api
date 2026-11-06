using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Ast.Visitors;
using ValIO.Generators.TypeScript.Axios.Ast.Nodes;
using ValIO.Generators.TypeScript.Axios.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Axios.Ast.Renderers;

/// <summary>
/// Рендерер AST для генерации Axios кода
/// </summary>
public class AxiosAstRenderer : AxiosAstVisitorBase<string>, IAxiosAstVisitor<string>, IAxiosAstRenderer
{
    /// <summary>
    /// Рендерит AxiosCallExpressionNode
    /// </summary>
    public string Render(AxiosCallExpressionNode node)
    {
        return VisitAxiosCall(node);
    }
    
    public override string VisitAxiosCall(AxiosCallExpressionNode node)
    {
        var sb = new StringBuilder();

        // Рендерим instance
        var instance = Visit(node.Instance);
        sb.Append(instance);

        // Рендерим вызов request с конфигурацией
        sb.Append(".request(");
        var config = node.Config is AxiosConfigNode axiosConfigNode
            ? VisitAxiosConfig(axiosConfigNode)
            : Visit(node.Config);
        sb.Append(config);
        sb.Append(")");

        return sb.ToString();
    }
    
    public override string VisitAxiosConfig(AxiosConfigNode node)
    {
        var configSb = new StringBuilder();
        var configIndent = 0;
        configSb.Append("{");
        CodeFormatter.AppendLine(configSb, configIndent, "");
        configIndent++;
        
        // method
        CodeFormatter.Append(configSb, configIndent, "method: ");
        configSb.Append($"'{node.Method.Method.ToString().ToUpperInvariant()}'");
        configSb.Append(",");
        CodeFormatter.AppendLine(configSb, configIndent, "");
        
        // url
        CodeFormatter.Append(configSb, configIndent, "url: ");
        var url = Visit(node.Url);
        configSb.Append(url);
        configSb.Append(",");
        CodeFormatter.AppendLine(configSb, configIndent, "");
        
        // params, headers, data - из Properties если есть (params interface), иначе из QueryParameters/Headers
        var paramsProp = node.Properties.FirstOrDefault(p => p.Name.Name == "params");
        var headersProp = node.Properties.FirstOrDefault(p => p.Name.Name == "headers");
        var dataProp = node.Properties.FirstOrDefault(p => p.Name.Name == "data");

        if (paramsProp != null)
        {
            CodeFormatter.Append(configSb, configIndent, "params: ");
            configSb.Append(Visit(paramsProp.Value));
            configSb.Append(",");
            CodeFormatter.AppendLine(configSb, configIndent, "");
        }
        else if (node.QueryParameters.Count > 0)
        {
            CodeFormatter.Append(configSb, configIndent, "params: ");
            configSb.Append(BuildQueryParamsObject(node.QueryParameters));
            configSb.Append(",");
            CodeFormatter.AppendLine(configSb, configIndent, "");
        }

        if (headersProp != null)
        {
            CodeFormatter.Append(configSb, configIndent, "headers: ");
            configSb.Append(Visit(headersProp.Value));
            configSb.Append(",");
            CodeFormatter.AppendLine(configSb, configIndent, "");
        }
        else if (node.Headers.Count > 0)
        {
            CodeFormatter.Append(configSb, configIndent, "headers: ");
            configSb.Append(BuildHeadersObject(node.Headers));
            configSb.Append(",");
            CodeFormatter.AppendLine(configSb, configIndent, "");
        }

        if (dataProp != null)
        {
            CodeFormatter.Append(configSb, configIndent, "data: ");
            configSb.Append(Visit(dataProp.Value));
            configSb.Append(",");
            CodeFormatter.AppendLine(configSb, configIndent, "");
        }
        else if (node.Body != null)
        {
            CodeFormatter.Append(configSb, configIndent, "data: ");
            configSb.Append("data");
            configSb.Append(",");
            CodeFormatter.AppendLine(configSb, configIndent, "");
        }
        
        // Удаляем последнюю запятую
        if (configSb.Length > 0 && configSb[configSb.Length - 1] == ',')
        {
            configSb.Length--;
        }
        
        configIndent--;
        CodeFormatter.AppendLine(configSb, configIndent, "");
        CodeFormatter.Append(configSb, configIndent, "}");
        
        return configSb.ToString();
    }
    
    private string BuildQueryParamsObject(IReadOnlyList<ValIO.Core.Ast.Nodes.Http.QueryParameterNode> queryParameters)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        
        var first = true;
        foreach (var qp in queryParameters)
        {
            if (!first)
                sb.Append(", ");
            first = false;
            
            sb.Append(qp.Name.Name);
            sb.Append(": ");
            sb.Append(qp.Name.Name);
        }
        
        sb.Append("}");
        return sb.ToString();
    }
    
    private string BuildHeadersObject(IReadOnlyList<ValIO.Core.Ast.Nodes.Http.HeaderParameterNode> headers)
    {
        var sb = new StringBuilder();
        sb.Append("{");
        
        var first = true;
        foreach (var header in headers)
        {
            if (!first)
                sb.Append(", ");
            first = false;
            
            sb.Append($"'{header.Name.Name}': ");
            sb.Append(header.Name.Name);
        }
        
        sb.Append("}");
        return sb.ToString();
    }
    
    // Переопределяем базовые методы для корректной работы с выражениями
    
    public override string VisitTemplateLiteralExpression(TemplateLiteralExpressionNode node)
    {
        var result = new StringBuilder();
        result.Append("`");
        foreach (var part in node.Parts)
        {
            if (part.IsExpression)
            {
                result.Append("${");
                if (part.Expression != null)
                {
                    var expr = Visit(part.Expression);
                    result.Append(expr);
                }
                result.Append("}");
            }
            else
            {
                result.Append(part.Text ?? "");
            }
        }
        result.Append("`");
        return result.ToString();
    }
    
    public override string VisitIdentifierExpression(IdentifierExpressionNode node)
    {
        return node.Identifier.Name;
    }
    
    public override string VisitLiteralExpression(LiteralExpressionNode node)
    {
        return node.LiteralKind switch
        {
            LiteralKind.String => $"\"{node.Value}\"",
            LiteralKind.Number => node.Value?.ToString() ?? "0",
            LiteralKind.Boolean => node.Value?.ToString()?.ToLowerInvariant() ?? "false",
            LiteralKind.Null => "null",
            _ => node.Value?.ToString() ?? "null"
        };
    }
    
    /// <inheritdoc/>
    public override string VisitObjectLiteralExpression(ObjectLiteralExpressionNode node)
    {
        var result = new StringBuilder();
        var objIndent = 0;
        result.Append("{");
        CodeFormatter.AppendLine(result, objIndent, "");
        objIndent++;
        
        for (int i = 0; i < node.Properties.Count; i++)
        {
            var prop = node.Properties[i];
            CodeFormatter.Append(result, objIndent);
            var propValue = Visit(prop.Value);
            if (prop.IsSpread)
            {
                result.Append("...");
                result.Append(propValue);
            }
            else
            {
                var propName = Visit(prop.Name);
                result.Append(propName);
                result.Append(": ");
                result.Append(propValue);
            }
            
            if (i < node.Properties.Count - 1)
            {
                result.Append(",");
            }
            CodeFormatter.AppendLine(result, objIndent, "");
        }
        
        objIndent--;
        CodeFormatter.AppendLine(result, objIndent, "");
        CodeFormatter.Append(result, objIndent, "}");
        
        return result.ToString();
    }
    
    /// <inheritdoc/>
    public override string VisitPropertyAssignment(PropertyAssignmentNode node)
    {
        var value = Visit(node.Value);
        if (node.IsSpread)
            return $"...{value}";

        var name = Visit(node.Name);
        return $"{name}: {value}";
    }
    
    // Реализация всех абстрактных методов BaseAstVisitor через VisitDefault
    public override string VisitRawType(RawTypeNode node) => node.RawTypeString;
    public override string VisitSimpleType(SimpleTypeNode node) => VisitDefault(node);
    public override string VisitTypeAliasDeclaration(TypeAliasDeclarationNode node) => VisitDefault(node);
    public override string VisitExpressionStatement(ExpressionStatementNode node) => VisitDefault(node);
    public override string VisitTypeDeclaration(TypeDeclarationNode node) => VisitDefault(node);
    public override string VisitNullableType(NullableTypeNode node) => VisitDefault(node);
    public override string VisitThrowStatement(ThrowStatementNode node) => VisitDefault(node);
    public override string VisitVariableDeclaration(VariableDeclarationNode node) => VisitDefault(node);
    public override string VisitArrayLiteralExpression(ArrayLiteralExpressionNode node) => VisitDefault(node);
    public override string VisitIfStatement(IfStatementNode node) => VisitDefault(node);
    public override string VisitDictionaryType(DictionaryTypeNode node) => VisitDefault(node);
    public override string VisitComment(CommentNode node) => VisitDefault(node);
    public override string VisitOptionalType(OptionalTypeNode node) => VisitDefault(node);
    public override string VisitEnumMemberDeclaration(EnumMemberDeclarationNode node) => VisitDefault(node);
    public override string VisitReturnStatement(ReturnStatementNode node) => VisitDefault(node);
    public override string VisitUnionType(UnionTypeNode node) => VisitDefault(node);
    public override string VisitPropertySignature(PropertySignatureNode node) => VisitDefault(node);
    public override string VisitGenericType(GenericTypeNode node) => VisitDefault(node);
    public override string VisitImport(ImportNode node) => VisitDefault(node);
    public override string VisitFunctionDeclaration(FunctionDeclarationNode node) => VisitDefault(node);
    public override string VisitAttribute(AttributeNode node) => VisitDefault(node);
    public override string VisitCallExpression(CallExpressionNode node) => VisitDefault(node);
    public override string VisitIdentifier(IdentifierNode node) => VisitDefault(node);
    public override string VisitConditionalExpression(ConditionalExpressionNode node) => VisitDefault(node);
    public override string VisitExport(ExportNode node) => VisitDefault(node);
    public override string VisitIntersectionType(IntersectionTypeNode node) => VisitDefault(node);
    public override string VisitUsingStatement(UsingStatementNode node) => VisitDefault(node);
    public override string VisitCastExpression(CastExpressionNode node) => VisitDefault(node);
    public override string VisitForEachStatement(ForEachStatementNode node) => VisitDefault(node);
    public override string VisitAwaitExpression(AwaitExpressionNode node) => VisitDefault(node);
    public override string VisitMemberAccessExpression(MemberAccessExpressionNode node) => VisitDefault(node);
    public override string VisitPropertyDeclaration(PropertyDeclarationNode node) => VisitDefault(node);
    public override string VisitArrayType(ArrayTypeNode node) => VisitDefault(node);
    public override string VisitCatchClause(CatchClauseNode node) => VisitDefault(node);
    public override string VisitParameterDeclaration(ParameterDeclarationNode node) => VisitDefault(node);
    public override string VisitVariableDeclarationStatement(VariableDeclarationStatementNode node) => VisitDefault(node);
    public override string VisitMethodDeclaration(MethodDeclarationNode node) => VisitDefault(node);
    public override string VisitBlock(BlockNode node) => VisitDefault(node);
    public override string VisitBinaryExpression(BinaryExpressionNode node) => VisitDefault(node);
    public override string VisitTryCatchStatement(TryCatchStatementNode node) => VisitDefault(node);
    public override string VisitGenericParameter(GenericParameterNode node) => VisitDefault(node);
    public override string VisitSourceFile(SourceFileNode node) => VisitDefault(node);
    public override string VisitSpreadExpression(SpreadExpressionNode node) => $"...{Visit(node.Expression)}";
    public override string VisitArrowFunctionExpression(ArrowFunctionExpressionNode node)
    {
        var asyncKw = node.IsAsync ? "async " : "";
        var parameters = string.Join(", ", node.Parameters.Select(p => Visit(p)));
        if (node.BodyExpression != null)
            return $"{asyncKw}({parameters}) => {Visit(node.BodyExpression)}";
        var blockContent = string.Join(" ", node.BodyBlock!.Statements.Select(Visit));
        return $"{asyncKw}({parameters}) => {{ {blockContent} }}";
    }
}
