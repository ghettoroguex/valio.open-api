using FluentAssertions;
using NUnit.Framework;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.Cpp.Ast.Renderers;

namespace ValIO.Generators.Cpp.Tests.Renderers;

/// <summary>
/// Golden-тесты рендера универсальных core-деклараций (std-профиль C++):
/// namespace, struct, class, interface, field, method, enum.
/// UE-профиль эти узлы не строит — рендер полностью принадлежит std-профилю.
/// </summary>
[TestFixture]
public class CppAstRendererCoreDeclarationTests
{
    private CppAstRenderer _renderer = null!;

    [SetUp]
    public void Setup() => _renderer = new CppAstRenderer();

    /// <summary>Рендер с нормализацией переводов строк под golden-ожидания.</summary>
    private string Render(AstNode node) => _renderer.Render(node).Replace("\r\n", "\n");

    private static SimpleTypeNode Simple(string name, bool isPrimitive = false)
        => new(new IdentifierNode(name), isPrimitive: isPrimitive);

    [Test]
    public void Struct_WithBasesAndFields_RendersPublicInheritanceAndInitializers()
    {
        var node = new StructDeclarationNode(
            new IdentifierNode("Order"),
            baseTypes: [Simple("BaseOrder")],
            members:
            [
                new FieldDeclarationNode(
                    new IdentifierNode("Id"),
                    Simple("int64_t", isPrimitive: true),
                    AccessModifier.Public,
                    defaultValue: new LiteralExpressionNode("0", LiteralKind.Number)),
                new FieldDeclarationNode(
                    new IdentifierNode("Note"),
                    Simple("std::optional<std::string>"),
                    AccessModifier.Public),
            ]);

        var expected =
            "struct Order : public BaseOrder\n" +
            "{\n" +
            "\tint64_t Id = 0;\n" +
            "\tstd::optional<std::string> Note;\n" +
            "};\n\n";

        Render(node).Should().Be(expected);
    }

    [Test]
    public void Struct_FieldDocumentation_RendersDoxygenAbove()
    {
        var node = new StructDeclarationNode(
            new IdentifierNode("Order"),
            members:
            [
                new FieldDeclarationNode(
                    new IdentifierNode("Id"),
                    Simple("int64_t", isPrimitive: true),
                    AccessModifier.Public,
                    documentation: new CommentNode("Идентификатор заказа.", CommentKind.MultiLine)),
            ]);

        Render(node).Should().Be(
            "struct Order\n" +
            "{\n" +
            "\t/** Идентификатор заказа. */\n" +
            "\tint64_t Id;\n" +
            "};\n\n");
    }

    [Test]
    public void Class_SealedAndAccessSections_RendersFinalBeforeBases()
    {
        var node = new ClassDeclarationNode(
            new IdentifierNode("StoreClient"),
            isSealed: true,
            baseTypes: [Simple("IStoreClient")],
            members:
            [
                new MethodDeclarationNode(new IdentifierNode("PlaceOrder")),
                new FieldDeclarationNode(
                    new IdentifierNode("_http"),
                    Simple("ValIO::HttpClient"),
                    AccessModifier.Private),
            ]);

        var expected =
            "class StoreClient final : public IStoreClient\n" +
            "{\n" +
            "\tpublic:\n" +
            "\tvoid PlaceOrder();\n" +
            "\n" +
            "\tprivate:\n" +
            "\tValIO::HttpClient _http;\n" +
            "};\n\n";

        Render(node).Should().Be(expected);
    }

    [Test]
    public void Interface_RendersVirtualDestructorAndPureVirtualMethods()
    {
        var method = new MethodDeclarationNode(
            new IdentifierNode("PlaceOrder"),
            Simple("ValIO::Expected<Order, ValIO::Error>"),
            parameters:
            [
                new ParameterDeclarationNode(
                    new IdentifierNode("request"),
                    new RawTypeNode("const Order&")),
            ],
            isInterfaceMethod: true,
            isConst: true);

        var node = new InterfaceDeclarationNode(
            new IdentifierNode("IStoreClient"),
            members: [method]);

        var expected =
            "class IStoreClient\n" +
            "{\n" +
            "\tpublic:\n" +
            "\tvirtual ~IStoreClient() = default;\n" +
            "\n" +
            "\tvirtual ValIO::Expected<Order, ValIO::Error> PlaceOrder(const Order& request) const = 0;\n" +
            "};\n\n";

        Render(node).Should().Be(expected);
    }

    [Test]
    public void Method_Modifiers_RenderInCanonicalOrder()
    {
        var node = new MethodDeclarationNode(
            new IdentifierNode("Get"),
            Simple("int32_t", isPrimitive: true),
            isVirtual: true,
            isConst: true,
            noexcept: true,
            isOverride: true);

        Render(node).Should().Be("virtual int32_t Get() const noexcept override;\n");
    }

    [Test]
    public void Method_StaticAbstract_RenderPrefixesAndPureSpecifier()
    {
        var node = new MethodDeclarationNode(
            new IdentifierNode("Create"),
            isStatic: true,
            isAbstract: true);

        Render(node).Should().Be("static virtual void Create() = 0;\n");
    }

    [Test]
    public void Enum_UnderlyingTypeAndValues_RenderEnumClass()
    {
        var node = new EnumDeclarationNode(
            new IdentifierNode("PetStatus"),
            underlyingType: Simple("std::int32_t", isPrimitive: true),
            members:
            [
                new EnumMemberDeclarationNode(new IdentifierNode("Placed"),
                    new LiteralExpressionNode("0", LiteralKind.Number)),
                new EnumMemberDeclarationNode(new IdentifierNode("Approved"),
                    new LiteralExpressionNode("1", LiteralKind.Number)),
            ]);

        var expected =
            "enum class PetStatus : std::int32_t\n" +
            "{\n" +
            "\tPlaced = 0,\n" +
            "\tApproved = 1\n" +
            "};\n\n";

        Render(node).Should().Be(expected);
    }

    [Test]
    public void Namespace_IndentsNestedDeclarationsAsAWhole()
    {
        var node = new NamespaceDeclarationNode(
            new IdentifierNode("Petstore"),
            declarations:
            [
                new StructDeclarationNode(
                    new IdentifierNode("Order"),
                    members:
                    [
                        new FieldDeclarationNode(
                            new IdentifierNode("Id"),
                            Simple("int64_t", isPrimitive: true),
                            AccessModifier.Public),
                    ]),
            ]);

        var expected =
            "namespace Petstore\n" +
            "{\n" +
            "\tstruct Order\n" +
            "\t{\n" +
            "\t\tint64_t Id;\n" +
            "\t};\n" +
            "\n" +
            "} // namespace Petstore\n";

        Render(node).Should().Be(expected);
    }

    [Test]
    public void Method_ParameterDefaultValue_RendersInline()
    {
        // Сырой C++-токен дефолта — Identifier-выражение (литерал String теперь
        // всегда кавыченная строка, «std::nullopt» строкой быть не может).
        var node = new MethodDeclarationNode(
            new IdentifierNode("List"),
            parameters:
            [
                new ParameterDeclarationNode(
                    new IdentifierNode("include"),
                    Simple("std::optional<std::string>"),
                    isRequired: false,
                    defaultValue: new IdentifierExpressionNode(new IdentifierNode("std::nullopt"))),
            ]);

        Render(node).Should()
            .Be("void List(std::optional<std::string> include = std::nullopt);\n");
    }
}
