using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Http;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Visitors;
using ValIO.Generators.Kotlin.Ast.Nodes;

namespace ValIO.Generators.Kotlin.Ast.Visitors;

/// <summary>
/// Base for Kotlin-specific visitors: dispatches via Accept and leaves the
/// concrete visit methods abstract.
/// </summary>
public abstract class KotlinAstVisitorBase<TResult> : BaseAstVisitor<TResult>, IKotlinAstVisitor<TResult>
{
    public override TResult Visit(AstNode node) => node.Accept(this);

    public override TResult VisitBlankLineStatement(BlankLineStatementNode node) => VisitDefault(node);

    // Kotlin-specific nodes.
    public abstract TResult VisitKotlinFile(KotlinFileNode node);
    public abstract TResult VisitKotlinImport(KotlinImportNode node);
    public abstract TResult VisitKotlinAnnotation(KotlinAnnotationNode node);
    public abstract TResult VisitKotlinDataClass(KotlinDataClassNode node);
    public abstract TResult VisitKotlinClassDeclaration(KotlinClassDeclarationNode node);
    public abstract TResult VisitKotlinProperty(KotlinPropertyNode node);
    public abstract TResult VisitKotlinEnumDeclaration(KotlinEnumDeclarationNode node);
    public abstract TResult VisitKotlinEnumMember(KotlinEnumMemberNode node);
    public abstract TResult VisitKotlinFunctionDeclaration(KotlinFunctionDeclarationNode node);
    public abstract TResult VisitKotlinParameter(KotlinParameterNode node);
    /// <inheritdoc/>
    public abstract TResult VisitKotlinObjectExpression(KotlinObjectExpressionNode node);

    // IAstVisitor — shared.
    public abstract TResult VisitEnumDeclaration(EnumDeclarationNode node);
    public abstract TResult VisitInterfaceDeclaration(InterfaceDeclarationNode node);

    // HTTP nodes — Kotlin plugin does not consume them directly; default to VisitDefault.
    public virtual TResult VisitHttpMethod(HttpMethodNode node) => VisitDefault(node);
    public virtual TResult VisitRoute(RouteNode node) => VisitDefault(node);
    public virtual TResult VisitPathParameter(PathParameterNode node) => VisitDefault(node);
    public virtual TResult VisitQueryParameter(QueryParameterNode node) => VisitDefault(node);
    public virtual TResult VisitHeaderParameter(HeaderParameterNode node) => VisitDefault(node);
    public virtual TResult VisitRequestBody(RequestBodyNode node) => VisitDefault(node);
    public virtual TResult VisitResponseType(ResponseTypeNode node) => VisitDefault(node);
}
