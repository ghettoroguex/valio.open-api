using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Http;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.CSharp.Ast.Nodes;

namespace ValIO.Generators.CSharp.Ast.Visitors;

/// <summary>
/// Базовый класс для посетителя C# AST узлов
/// </summary>
public abstract class CSharpAstVisitorBase<TResult> : BaseAstVisitor<TResult>, ICSharpAstVisitor<TResult>
{
    public override TResult VisitBlankLineStatement(BlankLineStatementNode node) => VisitDefault(node);

    /// <summary>
    /// Переопределяем Visit для поддержки IAstVisitor
    /// </summary>
    public override TResult Visit(AstNode node)
    {
        return node.Accept(this);
    }
    
    // Абстрактные методы только для C# узлов
    public abstract TResult VisitCompilationUnit(CompilationUnitNode node);
    public abstract TResult VisitUsingDirective(UsingDirectiveNode node);
    public abstract TResult VisitRecordDeclaration(RecordDeclarationNode node);
    public abstract TResult VisitConstructorDeclaration(ConstructorDeclarationNode node);

    /// <inheritdoc/>
    public abstract TResult VisitDefaultExpression(DefaultExpressionNode node);

    /// <inheritdoc />
    public abstract TResult VisitTypeOfExpression(TypeOfExpressionNode node);

    // Namespace/Class/Struct/Field — универсальные узлы Core; Visit-методы
    // наследуются из IAstVisitor (virtual-default в BaseAstVisitor),
    // рендерер переопределяет их напрямую.
    
    // Enum и Interface (общие для C# и TypeScript, определены в IAstVisitor)
    // Реализуем их здесь, так как они используются в C#
    public abstract TResult VisitEnumDeclaration(EnumDeclarationNode node);
    public abstract TResult VisitInterfaceDeclaration(InterfaceDeclarationNode node);
    
    // HTTP специфичные узлы (из IAstVisitor)
    public abstract TResult VisitHttpMethod(HttpMethodNode node);
    public abstract TResult VisitRoute(RouteNode node);
    public abstract TResult VisitPathParameter(PathParameterNode node);
    public abstract TResult VisitQueryParameter(QueryParameterNode node);
    public abstract TResult VisitHeaderParameter(HeaderParameterNode node);
    public abstract TResult VisitRequestBody(RequestBodyNode node);
    public abstract TResult VisitResponseType(ResponseTypeNode node);
}
