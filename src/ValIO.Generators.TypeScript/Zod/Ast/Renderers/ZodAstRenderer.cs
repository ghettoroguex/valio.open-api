using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Validation;
using ValIO.Generators.TypeScript.Zod.Ast.Enums;
using ValIO.Generators.TypeScript.Zod.Ast.Nodes;
using ValIO.Generators.TypeScript.Zod.Ast.Visitors;

namespace ValIO.Generators.TypeScript.Zod.Ast.Renderers;

/// <summary>
/// Рендерер AST для генерации Zod схем
/// </summary>
public class ZodAstRenderer : ZodAstVisitorBase<string>, IZodAstVisitor<string>, IZodAstRenderer
{
    /// <summary>Renders shared expression nodes through the canonical TypeScript renderer.</summary>
    private readonly TypeScriptAstRenderer _expressionRenderer = new([new ZodNodeRenderer()]);

    /// <summary>
    /// Рендерит ZodObjectNode
    /// </summary>
    public string Render(ZodObjectNode node)
    {
        return VisitZodObject(node);
    }

    /// <summary>
    /// Рендерит ZodSchemaNode (object, union, intersection)
    /// </summary>
    public string RenderSchema(ZodSchemaNode node)
    {
        return VisitZodSchema(node);
    }
    
    public override string VisitZodSchema(ZodSchemaNode node)
    {
        return node switch
        {
            ZodPrimitiveSchemaNode primitive => VisitZodPrimitiveSchema(primitive),
            ZodExpressionSchemaNode exprNode => VisitZodExpressionSchema(exprNode),
            ZodObjectNode obj => VisitZodObject(obj),
            ZodArrayNode array => VisitZodArray(array),
            ZodUnionNode union => VisitZodUnion(union),
            ZodIntersectionNode intersection => VisitZodIntersection(intersection),
            ZodEnumNode enumNode => VisitZodEnum(enumNode),
            ZodEffectsNode effects => VisitZodEffects(effects),
            _ => throw new NotSupportedException($"Unsupported ZodSchemaNode type: {node.GetType().Name}")
        };
    }

    /// <summary>
    /// Renders shared TypeScript expressions without duplicating syntax or literal escaping.
    /// </summary>
    /// <param name="node">The expression or nested Zod schema.</param>
    /// <returns>The rendered TypeScript expression.</returns>
    private string VisitExpression(AstNode node)
    {
        return _expressionRenderer.Visit(node);
    }
    
    /// <summary>
    /// Renders strict model primitives and lazy or eager schema references.
    /// </summary>
    /// <param name="node">The primitive or reference schema.</param>
    /// <returns>The Zod schema expression.</returns>
    public override string VisitZodPrimitiveSchema(ZodPrimitiveSchemaNode node)
    {
        if (!string.IsNullOrEmpty(node.SchemaReference))
        {
            if (node.IsLazy)
                return $"z.lazy(() => {node.SchemaReference})";
            return node.SchemaReference;
        }

        return node.SchemaKind switch
        {
            ZodSchemaKind.String => "z.string()",
            ZodSchemaKind.Number => "z.number()",
            ZodSchemaKind.Integer => "z.number().int()",
            ZodSchemaKind.BigInt => "z.bigint()",
            ZodSchemaKind.Boolean => "z.boolean()",
            ZodSchemaKind.Null => "z.null()",
            ZodSchemaKind.Any => "z.any()",
            _ => throw new NotSupportedException($"Unsupported primitive schema kind: {node.SchemaKind}")
        };
    }
    
    /// <summary>
    /// Рендерит Zod expression схему (цепочка вызовов)
    /// </summary>
    public override string VisitZodExpressionSchema(ZodExpressionSchemaNode node)
    {
        return VisitExpression(node.Expression);
    }

    /// <inheritdoc />
    public override string VisitZodObject(ZodObjectNode node)
    {
        return VisitExpression(new CallExpressionNode(
            new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("z")), new IdentifierNode("object")),
            [new ObjectLiteralExpressionNode(node.Properties
                .Select(property => new PropertyAssignmentNode(property.Name, BuildPropertyExpression(property))).ToArray(),
                preferMultilineFormat: true)]));
    }
    
    /// <inheritdoc />
    public override string VisitZodProperty(ZodPropertyNode node)
    {
        return VisitExpression(BuildPropertyExpression(node));
    }

    /// <summary>Builds independent nullability and optionality wrappers before shared expression rendering.</summary>
    /// <param name="node">The property schema and its presence metadata.</param>
    /// <returns>The complete property schema expression.</returns>
    private static ExpressionNode BuildPropertyExpression(ZodPropertyNode node)
    {
        ExpressionNode schema = node.Schema;
        if (node.IsNullable)
            schema = new CallExpressionNode(new MemberAccessExpressionNode(schema, new IdentifierNode("nullable")));
        if (!node.IsRequired || node.IsOptional)
            schema = new CallExpressionNode(new MemberAccessExpressionNode(schema, new IdentifierNode("optional")));
        return schema;
    }
    
    public override string VisitZodArray(ZodArrayNode node)
    {
        var elementSchema = Visit((AstNode)node.ElementSchema);
        return $"z.array({elementSchema})";
    }
    
    /// <inheritdoc />
    public override string VisitZodUnion(ZodUnionNode node)
    {
        if (node.IsExclusive)
            return VisitExpression(ExclusiveUnionAst.Zod(node.Variants.Cast<ExpressionNode>().ToArray()));
        var variants = node.Variants.Select(v => Visit((AstNode)v));
        return string.IsNullOrEmpty(node.Discriminator)
            ? $"z.union([{string.Join(", ", variants)}])"
            : $"z.discriminatedUnion(\"{node.Discriminator}\", [{string.Join(", ", variants)}])";
    }
    
    /// <inheritdoc />
    public override string VisitZodIntersection(ZodIntersectionNode node)
    {
        var result = $"z.intersection({Visit(node.Schemas[0])}, {Visit(node.Schemas[1])})";
        foreach (var schema in node.Schemas.Skip(2))
            result = $"{result}.and({Visit(schema)})";
        return result;
    }
    
    /// <inheritdoc />
    public override string VisitZodEnum(ZodEnumNode node)
    {
        var values = node.Values.Select(v => VisitLiteralExpression(v.Value)).ToArray();
        if (values.Length == 0)
            return "z.never()";
        if (node.Values.All(value => value.Value.LiteralKind == LiteralKind.String))
            return $"z.enum([{string.Join(", ", values)}])";
        var literals = values.Select(value => $"z.literal({value})").ToArray();
        return literals.Length == 1 ? literals[0] : $"z.union([{string.Join(", ", literals)}])";
    }
    
    public override string VisitZodEnumValue(ZodEnumValueNode node)
    {
        return VisitLiteralExpression(node.Value);
    }
    
    /// <inheritdoc />
    public override string VisitZodEffects(ZodEffectsNode node)
    {
        var sb = new StringBuilder();
        
        var innerSchema = Visit((AstNode)node.InnerSchema);
        sb.Append(innerSchema);
        
        switch (node.EffectKind)
        {
            case ZodEffectKind.Refine:
                if (node.EffectFunction != null)
                {
                    var effectFn = Visit(node.EffectFunction);
                    sb.Append($".refine({effectFn})");
                }
                break;
            case ZodEffectKind.Transform:
                if (node.EffectFunction != null)
                {
                    var effectFn = Visit(node.EffectFunction);
                    sb.Append($".transform({effectFn})");
                }
                break;
            case ZodEffectKind.SuperRefine:
                if (node.EffectFunction != null)
                {
                    var effectFn = Visit(node.EffectFunction);
                    sb.Append($".superRefine({effectFn})");
                }
                break;
            case ZodEffectKind.Optional:
                sb.Append(".optional()");
                break;
            case ZodEffectKind.Nullable:
                sb.Append(".nullable()");
                break;
        }
        
        return sb.ToString();
    }
    
    // Переопределяем базовые методы для корректной работы с выражениями
    
    /// <inheritdoc />
    public override string VisitLiteralExpression(LiteralExpressionNode node) => VisitExpression(node);
    
    public override string VisitIdentifierExpression(IdentifierExpressionNode node)
    {
        return node.Identifier.Name;
    }
    
    // Реализация всех абстрактных методов BaseAstVisitor через VisitDefault
    public override string VisitRawType(RawTypeNode node) => node.RawTypeString;
    public override string VisitSimpleType(SimpleTypeNode node) => VisitDefault(node);
    public override string VisitExpressionStatement(ExpressionStatementNode node) => VisitDefault(node);
    public override string VisitTypeDeclaration(TypeDeclarationNode node) => VisitDefault(node);
    public override string VisitNullableType(NullableTypeNode node) => VisitDefault(node);
    public override string VisitThrowStatement(ThrowStatementNode node) => VisitDefault(node);
    public override string VisitIfStatement(IfStatementNode node) => VisitDefault(node);
    public override string VisitDictionaryType(DictionaryTypeNode node) => VisitDefault(node);
    public override string VisitComment(CommentNode node) => VisitDefault(node);
    public override string VisitOptionalType(OptionalTypeNode node) => VisitDefault(node);
    public override string VisitEnumMemberDeclaration(EnumMemberDeclarationNode node) => VisitDefault(node);
    public override string VisitReturnStatement(ReturnStatementNode node) => VisitDefault(node);
    public override string VisitUnionType(UnionTypeNode node) => VisitDefault(node);
    public override string VisitGenericType(GenericTypeNode node) => VisitDefault(node);
    public override string VisitImport(ImportNode node) => VisitDefault(node);
    public override string VisitAttribute(AttributeNode node) => VisitDefault(node);
    /// <inheritdoc />
    public override string VisitCallExpression(CallExpressionNode node) => VisitExpression(node);
    public override string VisitIdentifier(IdentifierNode node) => VisitDefault(node);
    public override string VisitConditionalExpression(ConditionalExpressionNode node) => VisitDefault(node);
    public override string VisitExport(ExportNode node) => VisitDefault(node);
    public override string VisitIntersectionType(IntersectionTypeNode node) => VisitDefault(node);
    public override string VisitUsingStatement(UsingStatementNode node) => VisitDefault(node);
    public override string VisitCastExpression(CastExpressionNode node) => VisitDefault(node);
    public override string VisitForEachStatement(ForEachStatementNode node) => VisitDefault(node);
    public override string VisitAwaitExpression(AwaitExpressionNode node) => VisitDefault(node);
    /// <inheritdoc />
    public override string VisitMemberAccessExpression(MemberAccessExpressionNode node) => VisitExpression(node);
    public override string VisitPropertyDeclaration(PropertyDeclarationNode node) => VisitDefault(node);
    public override string VisitArrayType(ArrayTypeNode node) => VisitDefault(node);
    public override string VisitCatchClause(CatchClauseNode node) => VisitDefault(node);
    public override string VisitParameterDeclaration(ParameterDeclarationNode node) => VisitDefault(node);
    public override string VisitVariableDeclarationStatement(VariableDeclarationStatementNode node) => VisitDefault(node);
    public override string VisitMethodDeclaration(MethodDeclarationNode node) => VisitDefault(node);
    public override string VisitBlock(BlockNode node) => VisitDefault(node);
    public override string VisitBinaryExpression(BinaryExpressionNode node) => VisitDefault(node);
    public override string VisitTryCatchStatement(TryCatchStatementNode node) => VisitDefault(node);
}
