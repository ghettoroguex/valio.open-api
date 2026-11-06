using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Ast.Renderers;

namespace ValIO.Generators.Cpp.Tests.Renderers;

/// <summary>Protects constructor initialization and method-body preservation in C++ rendering.</summary>
[TestFixture]
public sealed class CppConstructorAndMethodTests
{
    /// <summary>Checks constructor initializers participate in traversal and are independently cloned.</summary>
    [Test]
    public void Constructor_InitializersAreRenderedTraversedAndCloned()
    {
        var initializer = new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode("_value")),
            [new LiteralExpressionNode(42, LiteralKind.Number)]);
        var original = new CppFreeFunctionNode(new IdentifierNode("Client::Client"), new RawTypeNode(string.Empty),
            body: new BlockNode([]), metadata: new Dictionary<string, object> { ["cpp.constructor"] = true },
            initializers: [initializer]);
        var clone = (CppFreeFunctionNode)original.Clone();
        original.Children.Should().Contain(initializer);
        clone.Initializers.Should().ContainSingle().Which.Should().NotBeSameAs(initializer);
        new CppAstRenderer().Render(clone).Should().Contain("Client::Client() : _value(42)");
    }

    /// <summary>Checks an implementation renderer emits its actual body and Doxygen instead of fabricating an empty body.</summary>
    [Test]
    public void MethodImplementation_PreservesBodyAndDocumentation()
    {
        var method = new CppMethodImplementationNode(new IdentifierNode("Client"), new IdentifierNode("Value"),
            new RawTypeNode("int"), [], new BlockNode([new ReturnStatementNode(new LiteralExpressionNode(42, LiteralKind.Number))]),
            isConst: true, documentation: new CommentNode("@brief Returns the configured value.", CommentKind.MultiLine));
        var source = new CppAstRenderer().Render(method);
        source.Should().Contain("@brief Returns the configured value.");
        source.Should().Contain("int Client::Value() const");
        source.Should().Contain("return 42;");
    }
}
