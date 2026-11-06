using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;

namespace ValIO.Core.Tests.Ast;

/// <summary>Protects immutable metadata, complete traversal, and shared declaration clone contracts.</summary>
[TestFixture]
public class AstContractTests
{
    /// <summary>Ensures caller mutations cannot change an already-created node or its clone.</summary>
    [Test]
    public void Metadata_SnapshotsCallerDictionary()
    {
        var metadata = new Dictionary<string, object> { ["source"] = "schema" };
        var original = new IdentifierNode("Model", metadata);
        var clone = original.Clone();

        metadata["source"] = "changed";
        metadata["additional"] = true;

        original.Metadata.Should().HaveCount(1).And.ContainKey("source").WhoseValue.Should().Be("schema");
        clone.Metadata.Should().BeEquivalentTo(original.Metadata);
    }

    /// <summary>Ensures metadata stays optional for every node through the common base contract.</summary>
    [Test]
    public void Metadata_NullBecomesEmptyDictionary()
    {
        var node = new IdentifierNode("Model", metadata: null);

        node.Metadata.Should().BeEmpty();
        node.Clone().Metadata.Should().BeEmpty();
    }

    /// <summary>Ensures unary expressions preserve their annotations and independently clone their syntax.</summary>
    [Test]
    public void UnaryExpression_ClonePreservesTypeOperandAndMetadata()
    {
        var node = new UnaryExpressionNode(
            UnaryOperator.LogicalNot,
            Identifier("ready"),
            Type("bool"),
            new Dictionary<string, object> { ["origin"] = "condition" });

        var clone = (UnaryExpressionNode)node.Clone();

        clone.Should().BeEquivalentTo(node, options => options.RespectingRuntimeTypes().AllowingInfiniteRecursion());
        AssertIndependentChildren(node, clone);
        clone.Metadata.Should().ContainKey("origin").WhoseValue.Should().Be("condition");
    }

    /// <summary>Ensures dependency walkers can reach an expression's inferred result type.</summary>
    /// <param name="node">A shared expression with an explicitly supplied type.</param>
    [TestCaseSource(nameof(TypedExpressions))]
    public void TypedExpression_ChildrenContainResultType(ExpressionNode node)
    {
        node.Type.Should().NotBeNull();
        node.Children.Should().ContainSingle(child => ReferenceEquals(child, node.Type));

        var clone = (ExpressionNode)node.Clone();
        clone.Children.Should().ContainSingle(child => ReferenceEquals(child, clone.Type));
        AssertIndependentChildren(node, clone);
    }

    /// <summary>Ensures a generic method's argument participates in traversal even without an inferred type.</summary>
    [Test]
    public void CallExpression_ChildrenContainGenericTypeArgument()
    {
        var genericType = new ArrayTypeNode(Type("Payload"));
        var call = new CallExpressionNode(Identifier("deserialize"), genericTypeArgument: genericType);

        call.Children.Should().Equal(call.Target, genericType);
        var clone = (CallExpressionNode)call.Clone();
        clone.GenericTypeArgument.Should().NotBeSameAs(genericType);
        clone.Children.Should().Contain(clone.GenericTypeArgument!);
        AssertIndependentChildren(call, clone);
    }

    /// <summary>Ensures the promoted declarations retain all fields and concrete node types when cloned.</summary>
    /// <param name="node">A shared declaration containing nested syntax and semantic flags.</param>
    [TestCaseSource(nameof(PromotedDeclarations))]
    public void PromotedDeclaration_ClonePreservesContract(DeclarationNode node)
    {
        var clone = node.Clone();

        clone.GetType().Should().Be(node.GetType());
        clone.Should().BeEquivalentTo(node, options => options.RespectingRuntimeTypes().AllowingInfiniteRecursion());
        AssertIndependentChildren(node, clone);
    }

    /// <summary>Supplies every shared expression shape whose result type must remain traversable.</summary>
    /// <returns>Typed expression nodes with distinct syntax children.</returns>
    private static IEnumerable<ExpressionNode> TypedExpressions()
    {
        yield return new UnaryExpressionNode(UnaryOperator.Negate, Identifier("value"), Type("Result"));
        yield return new BinaryExpressionNode(Identifier("left"), BinaryOperator.Add, Identifier("right"), Type("Result"));
        yield return new ConditionalExpressionNode(Identifier("condition"), Identifier("yes"), Identifier("no"), Type("Result"));
        yield return new MemberAccessExpressionNode(Identifier("model"), new IdentifierNode("value"), Type("Result"));
        yield return new IndexAccessExpressionNode(Identifier("items"), Identifier("index"), Type("Result"));
        yield return new CallExpressionNode(Identifier("create"), type: Type("Result"));
        yield return new AwaitExpressionNode(Identifier("pending"), Type("Result"));
        yield return new CastExpressionNode(Type("Target"), Identifier("value"), Type("Result"));
        yield return new IdentifierExpressionNode(new IdentifierNode("value"), Type("Result"));
        yield return new LiteralExpressionNode(42, LiteralKind.Number, Type("Result"));
    }

    /// <summary>Supplies namespace, class, struct, and field nodes shared by the C# and C++ generators.</summary>
    /// <returns>Declarations with non-default modifiers, documentation, attributes, and metadata.</returns>
    private static IEnumerable<DeclarationNode> PromotedDeclarations()
    {
        yield return Field();
        yield return new StructDeclarationNode(
            new IdentifierNode("Payload"),
            AccessModifier.Internal,
            isReadOnly: true,
            baseTypes: [Type("IModel")],
            members: [Field()],
            documentation: new CommentNode("Payload shape"),
            attributes: [new AttributeNode(new IdentifierNode("Serializable"))],
            metadata: new Dictionary<string, object> { ["origin"] = "schema" });
        yield return new ClassDeclarationNode(
            new IdentifierNode("Client"),
            AccessModifier.Internal,
            isSealed: true,
            isStatic: true,
            isPartial: true,
            baseTypes: [Type("IClient")],
            members: [Field()],
            documentation: new CommentNode("Client declaration"),
            attributes: [new AttributeNode(new IdentifierNode("Generated"))],
            metadata: new Dictionary<string, object> { ["origin"] = "operation" });
        yield return new NamespaceDeclarationNode(
            new IdentifierNode("Generated"),
            declarations: [new StructDeclarationNode(new IdentifierNode("Payload"), members: [Field()])],
            documentation: new CommentNode("Generated namespace"),
            attributes: [new AttributeNode(new IdentifierNode("Generated"))],
            metadata: new Dictionary<string, object> { ["origin"] = "document" });
    }

    /// <summary>Creates a field with a nested type and all optional syntax populated.</summary>
    /// <returns>A field suitable for recursively checking the clone contract.</returns>
    private static FieldDeclarationNode Field() => new(
        new IdentifierNode("Values"),
        new ArrayTypeNode(Type("string"), isReadOnly: true, isNullable: true, isOptional: true),
        AccessModifier.Protected,
        isReadOnly: true,
        isStatic: true,
        defaultValue: new LiteralExpressionNode(null, LiteralKind.Null),
        documentation: new CommentNode("Stored values"),
        attributes: [new AttributeNode(new IdentifierNode("Generated"))],
        metadata: new Dictionary<string, object> { ["origin"] = "property" });

    /// <summary>Creates an identifier expression without inferred type annotations.</summary>
    /// <param name="name">The identifier spelling.</param>
    /// <returns>An untyped identifier expression.</returns>
    private static IdentifierExpressionNode Identifier(string name) => new(new IdentifierNode(name));

    /// <summary>Creates a named type node for contract checks without language-specific type mapping.</summary>
    /// <param name="name">The type identifier.</param>
    /// <returns>A non-nullable named type.</returns>
    private static SimpleTypeNode Type(string name) => new(new IdentifierNode(name));

    /// <summary>Checks that cloning replaces every syntax child while preserving traversal order and shape.</summary>
    /// <param name="original">The original syntax subtree.</param>
    /// <param name="clone">The cloned syntax subtree.</param>
    private static void AssertIndependentChildren(AstNode original, AstNode clone)
    {
        clone.Should().NotBeSameAs(original);
        clone.GetType().Should().Be(original.GetType());
        clone.Children.Should().HaveCount(original.Children.Count);
        for (var index = 0; index < original.Children.Count; index++)
        {
            AssertIndependentChildren(original.Children[index], clone.Children[index]);
        }
    }
}
