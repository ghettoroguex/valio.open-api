using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;

namespace ValIO.Core.Ast.Visitors;

/// <summary>
/// Базовый абстрактный класс для посетителя AST с default implementations
/// </summary>
public abstract class BaseAstVisitor<TResult> : IBaseAstVisitor<TResult>
{
    /// <summary>
    /// Посещает узел (диспетчеризация через Accept)
    /// </summary>
    public virtual TResult Visit(AstNode node)
    {
        throw new NotSupportedException("BaseAstVisitor.Visit must be overridden in classes that implement IAstVisitor");
    }

    // Базовые узлы
    public abstract TResult VisitIdentifier(IdentifierNode node);
    public abstract TResult VisitComment(CommentNode node);
    public abstract TResult VisitAttribute(AttributeNode node);
    public abstract TResult VisitBlock(BlockNode node);
    public abstract TResult VisitImport(ImportNode node);
    public abstract TResult VisitExport(ExportNode node);
    
    // Типы
    public abstract TResult VisitRawType(RawTypeNode node);
    public abstract TResult VisitSimpleType(SimpleTypeNode node);
    public abstract TResult VisitGenericType(GenericTypeNode node);
    public abstract TResult VisitNullableType(NullableTypeNode node);
    public abstract TResult VisitOptionalType(OptionalTypeNode node);
    public abstract TResult VisitArrayType(ArrayTypeNode node);
    public abstract TResult VisitUnionType(UnionTypeNode node);
    public abstract TResult VisitIntersectionType(IntersectionTypeNode node);
    public abstract TResult VisitDictionaryType(DictionaryTypeNode node);

    /// <summary>
    /// Visits an inline object type literal (<c>{ a?: T; b: U }</c>). Defaults to <see cref="DefaultVisit"/>
    /// because inline type literals are currently produced only by the TypeScript generator; a language
    /// visitor that needs them overrides this method (so existing visitors are unaffected by its addition).
    /// </summary>
    /// <param name="node">The type-literal node to visit.</param>
    /// <returns>The visitor result for <paramref name="node"/>.</returns>
    public virtual TResult VisitTypeLiteral(TypeLiteralNode node) => DefaultVisit(node);

    /// <summary>
    /// Индексация obj[index]. Virtual-default (прецедент VisitTypeLiteral): генераторы,
    /// которые этот узел не строят, ничего не замечают; C++ переопределяет.
    /// </summary>
    public virtual TResult VisitIndexAccessExpression(IndexAccessExpressionNode node) => DefaultVisit(node);

    // Универсальные декларации (промоучены из C#; см. IAstVisitor). Virtual-default по
    // прецеденту VisitTypeLiteral: генераторы, которые эти узлы не строят (TypeScript и др.),
    // ничего не замечают; C# и C++ переопределяют.
    public virtual TResult VisitNamespaceDeclaration(NamespaceDeclarationNode node) => DefaultVisit(node);
    public virtual TResult VisitClassDeclaration(ClassDeclarationNode node) => DefaultVisit(node);
    public virtual TResult VisitStructDeclaration(StructDeclarationNode node) => DefaultVisit(node);
    public virtual TResult VisitFieldDeclaration(FieldDeclarationNode node) => DefaultVisit(node);

    
    // Выражения
    public abstract TResult VisitLiteralExpression(LiteralExpressionNode node);
    public abstract TResult VisitIdentifierExpression(IdentifierExpressionNode node);
    public abstract TResult VisitCallExpression(CallExpressionNode node);
    public abstract TResult VisitMemberAccessExpression(MemberAccessExpressionNode node);
    public abstract TResult VisitUnaryExpression(UnaryExpressionNode node);
    public abstract TResult VisitBinaryExpression(BinaryExpressionNode node);
    public abstract TResult VisitConditionalExpression(ConditionalExpressionNode node);
    public abstract TResult VisitAwaitExpression(AwaitExpressionNode node);
    public abstract TResult VisitCastExpression(CastExpressionNode node);
    
    // Statements
    public abstract TResult VisitBlankLineStatement(BlankLineStatementNode node);
    public abstract TResult VisitReturnStatement(ReturnStatementNode node);
    public abstract TResult VisitIfStatement(IfStatementNode node);
    public abstract TResult VisitForEachStatement(ForEachStatementNode node);
    public abstract TResult VisitExpressionStatement(ExpressionStatementNode node);
    public abstract TResult VisitVariableDeclarationStatement(VariableDeclarationStatementNode node);
    public abstract TResult VisitThrowStatement(ThrowStatementNode node);
    public abstract TResult VisitUsingStatement(UsingStatementNode node);
    public abstract TResult VisitTryCatchStatement(TryCatchStatementNode node);
    public abstract TResult VisitCatchClause(CatchClauseNode node);
    
    // Объявления
    public abstract TResult VisitTypeDeclaration(TypeDeclarationNode node);
    public abstract TResult VisitPropertyDeclaration(PropertyDeclarationNode node);
    public abstract TResult VisitMethodDeclaration(MethodDeclarationNode node);
    public abstract TResult VisitParameterDeclaration(ParameterDeclarationNode node);
    public abstract TResult VisitEnumMemberDeclaration(EnumMemberDeclarationNode node);
    
    /// <summary>
    /// Метод по умолчанию для неизвестных узлов
    /// </summary>
    public virtual TResult VisitDefault(AstNode node)
    {
        throw new NotSupportedException($"Visit for node type {node.GetType().Name} is not supported");
    }

    /// <summary>
    /// Обработка по умолчанию для неизвестных или неподдерживаемых узлов (реализация IBaseAstVisitor)
    /// </summary>
    public virtual TResult DefaultVisit(AstNode node) => VisitDefault(node);
}
