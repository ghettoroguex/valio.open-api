using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.Kotlin.Ast.Nodes;
using ValIO.Generators.Kotlin.Ast.Renderers;

namespace ValIO.Generators.Kotlin.Tests.Renderers;

/// <summary>Verifies structured anonymous callback declarations without embedding Kotlin body text.</summary>
[TestFixture]
public sealed class KotlinStructuredCallbackTests
{
    /// <summary>Checks member snapshots and complete deep cloning of callback body children.</summary>
    [Test]
    public void AnonymousObjectSnapshotsMembersAndDeepClonesStructuredChildren()
    {
        var body = new BlockNode([new ReturnStatementNode()]);
        var member = new KotlinFunctionDeclarationNode(new IdentifierNode("invoke"),
            [new KotlinParameterNode(new IdentifierNode("value"), new SimpleTypeNode(new IdentifierNode("String")))],
            structuredBody: body, isOverride: true);
        var members = new List<DeclarationNode> { member };
        var original = new KotlinObjectExpressionNode(new SimpleTypeNode(new IdentifierNode("Callback")), members);
        members.Clear();

        original.Members.Should().ContainSingle().Which.Should().BeSameAs(member);
        original.Children.Should().Contain(original.InterfaceType).And.Contain(member);
        member.Children.Should().Contain(body).And.Contain(member.Parameters[0]);
        var clone = (KotlinObjectExpressionNode)original.Clone();
        clone.InterfaceType.Should().NotBeSameAs(original.InterfaceType);
        var clonedMember = (KotlinFunctionDeclarationNode)clone.Members.Single();
        clonedMember.Should().NotBeSameAs(member);
        clonedMember.StructuredBody.Should().NotBeSameAs(body);
        clonedMember.StructuredBody!.Statements.Single().Should().NotBeSameAs(body.Statements.Single());
        clonedMember.Parameters.Single().Should().NotBeSameAs(member.Parameters.Single());
        clonedMember.IsOverride.Should().BeTrue();
    }

    /// <summary>Checks anonymous object dispatch, override rendering, and structured generic call arguments.</summary>
    [Test]
    public void AnonymousObjectRendersOverrideAndGenericCalls()
    {
        var call = new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode("finish")),
            genericTypeArgument: new SimpleTypeNode(new IdentifierNode("Response")));
        var member = new KotlinFunctionDeclarationNode(new IdentifierNode("invoke"),
            structuredBody: new BlockNode([new ExpressionStatementNode(call)]), isOverride: true);
        var node = new KotlinObjectExpressionNode(new SimpleTypeNode(new IdentifierNode("Callback")), [member]);

        new KotlinAstRenderer().Visit(node).Should().Contain("object : Callback {")
            .And.Contain("override fun invoke() {").And.Contain("finish<Response>()");
    }

    /// <summary>Checks that unsupported structured statements fail visibly during rendering.</summary>
    [Test]
    public void StructuredBodyRejectsUnsupportedStatements()
    {
        var member = new KotlinFunctionDeclarationNode(new IdentifierNode("invoke"),
            structuredBody: new BlockNode([new BlankLineStatementNode()]));

        var render = () => new KotlinAstRenderer().VisitKotlinFunctionDeclaration(member);
        render.Should().Throw<NotSupportedException>().WithMessage("*BlankLineStatementNode*");
    }

    /// <summary>Checks that one declaration cannot silently prefer one conflicting body representation.</summary>
    [Test]
    public void StructuredBodyRejectsTextAndExpressionBodyCombinations()
    {
        var withText = () => new KotlinFunctionDeclarationNode(new IdentifierNode("invoke"),
            body: ["return"], structuredBody: new BlockNode([]));
        var withExpression = () => new KotlinFunctionDeclarationNode(new IdentifierNode("invoke"),
            isExpressionBody: true, structuredBody: new BlockNode([]));

        withText.Should().Throw<ArgumentException>();
        withExpression.Should().Throw<ArgumentException>();
    }
}
