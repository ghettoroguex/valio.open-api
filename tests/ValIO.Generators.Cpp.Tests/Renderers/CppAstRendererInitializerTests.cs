using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.Cpp.Ast.Renderers;

namespace ValIO.Generators.Cpp.Tests.Renderers;

/// <summary>Checks declaration initializers use typed expression rendering instead of diagnostic node text.</summary>
[TestFixture]
public sealed class CppAstRendererInitializerTests
{
    /// <summary>Preserves a structured constant expression in enum, field, and parameter defaults.</summary>
    [Test]
    public void StructuredInitializers_RenderOperatorsForEveryDeclarationKind()
    {
        var value = new BinaryExpressionNode(
            new LiteralExpressionNode("7", LiteralKind.Number),
            BinaryOperator.Subtract,
            new LiteralExpressionNode("3", LiteralKind.Number));
        var type = new SimpleTypeNode(new IdentifierNode("int"), isPrimitive: true);
        var renderer = new CppAstRenderer();

        renderer.Render(new EnumMemberDeclarationNode(new IdentifierNode("Count"), value))
            .Should().Be("Count = 7 - 3");
        renderer.Render(new FieldDeclarationNode(new IdentifierNode("count"), type,
                AccessModifier.Public, defaultValue: value))
            .Should().Contain("int count = 7 - 3;");
        renderer.Render(new ParameterDeclarationNode(new IdentifierNode("count"), type, defaultValue: value))
            .Should().Be("int count = 7 - 3");
    }
}
