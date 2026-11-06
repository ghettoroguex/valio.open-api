using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;

namespace ValIO.Core.Ast.Visitors;

/// <summary>
/// Базовый интерфейс посетителя для language-agnostic AST узлов
/// </summary>
public interface IBaseAstVisitor<TResult>
{
    /// <summary>
    /// Посещает узел (диспетчеризация по типу)
    /// </summary>
    TResult Visit(AstNode node);

    /// <summary>
    /// Обработка по умолчанию для неизвестных или неподдерживаемых узлов
    /// </summary>
    TResult DefaultVisit(AstNode node);
    
    // Базовые узлы (общие для всех языков)
    TResult VisitIdentifier(IdentifierNode node);
    TResult VisitComment(CommentNode node);
    TResult VisitAttribute(AttributeNode node);
    TResult VisitBlock(BlockNode node);
    TResult VisitImport(ImportNode node);
    TResult VisitExport(ExportNode node);
    
    // Типы (общие)
    TResult VisitRawType(RawTypeNode node);
    TResult VisitSimpleType(SimpleTypeNode node);
    TResult VisitGenericType(GenericTypeNode node);
    TResult VisitNullableType(NullableTypeNode node);
    TResult VisitOptionalType(OptionalTypeNode node);
    TResult VisitArrayType(ArrayTypeNode node);
    TResult VisitUnionType(UnionTypeNode node);
    TResult VisitIntersectionType(IntersectionTypeNode node);
    TResult VisitDictionaryType(DictionaryTypeNode node);
    TResult VisitTypeLiteral(TypeLiteralNode node);
    
    // Выражения (общие)
    TResult VisitLiteralExpression(LiteralExpressionNode node);
    TResult VisitIdentifierExpression(IdentifierExpressionNode node);
    TResult VisitCallExpression(CallExpressionNode node);
    TResult VisitMemberAccessExpression(MemberAccessExpressionNode node);
    TResult VisitIndexAccessExpression(IndexAccessExpressionNode node);
    TResult VisitUnaryExpression(UnaryExpressionNode node);
    TResult VisitBinaryExpression(BinaryExpressionNode node);
    TResult VisitConditionalExpression(ConditionalExpressionNode node);
    TResult VisitAwaitExpression(AwaitExpressionNode node);
    TResult VisitCastExpression(CastExpressionNode node);
    
    // Statements (общие)
    TResult VisitBlankLineStatement(BlankLineStatementNode node);
    TResult VisitReturnStatement(ReturnStatementNode node);
    TResult VisitIfStatement(IfStatementNode node);
    TResult VisitForEachStatement(ForEachStatementNode node);
    TResult VisitExpressionStatement(ExpressionStatementNode node);
    TResult VisitVariableDeclarationStatement(VariableDeclarationStatementNode node);
    TResult VisitThrowStatement(ThrowStatementNode node);
    TResult VisitUsingStatement(UsingStatementNode node);
    TResult VisitTryCatchStatement(TryCatchStatementNode node);
    TResult VisitCatchClause(CatchClauseNode node);
    
    // Объявления (общие)
    TResult VisitTypeDeclaration(TypeDeclarationNode node);
    TResult VisitPropertyDeclaration(PropertyDeclarationNode node);
    TResult VisitMethodDeclaration(MethodDeclarationNode node);
    TResult VisitParameterDeclaration(ParameterDeclarationNode node);
    TResult VisitEnumMemberDeclaration(EnumMemberDeclarationNode node);
}
