using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Tests.Builders;

/// <summary>Verifies that native callable types remain structured, traversable TypeScript AST values.</summary>
[TestFixture]
public sealed class TypeQueryTypeNodeTests
{
    /// <summary>Renders qualified native values and deep-clones their expression children and metadata.</summary>
    [Test]
    public void NativeTypeQuery_PreservesTraversalCloningAndRendering()
    {
        var expression = new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("globalThis")), new IdentifierNode("fetch"));
        var node = new TypeQueryTypeNode(expression, new Dictionary<string, object> { ["source"] = "native transport" });

        var clone = (TypeQueryTypeNode)node.Clone();

        node.Children.Should().ContainSingle().Which.Should().BeSameAs(expression);
        clone.Expression.Should().NotBeSameAs(node.Expression);
        clone.Metadata["source"].Should().Be("native transport");
        clone.Accept(new TypeScriptAstRenderer()).Should().Be("typeof globalThis.fetch");
    }
}
