using FluentAssertions;
using NUnit.Framework;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Tests.Services;

[TestFixture]
public class StdNamingConventionTests
{
    private readonly StdNamingConvention _naming = new();

    [Test]
    public void Names_ArePascalCase_WithoutUePrefixes()
    {
        _naming.StructName("buildingModel").Should().Be("BuildingModel");
        _naming.EnumName("orderStatus").Should().Be("OrderStatus");
        _naming.PropertyName("displayName").Should().Be("DisplayName");
        _naming.MethodName("getOrderById").Should().Be("GetOrderById");
    }

    [TestCase("class", "Class")]     // ключевые слова lowercase — PascalCase сам снимает коллизию
    [TestCase("template", "Template")]
    [TestCase("union", "Union")]
    [TestCase("default", "Default")]
    public void CppKeywords_AreLowercase_PascalCaseDefuses(string keyword, string expected)
        => _naming.PropertyName(keyword).Should().Be(expected);

    [TestCase("NULL", "NULL_")]      // макро-токены выживают в исходном регистре — суффикс обязателен
    [TestCase("TRUE", "TRUE_")]
    [TestCase("FALSE", "FALSE_")]
    public void MacroTokens_GetUnderscoreSuffix(string macro, string expected)
        => _naming.PropertyName(macro).Should().Be(expected);

    [Test]
    public void ClientAndInterfaceNames()
    {
        _naming.ClientClassName("store").Should().Be("StoreClient");
        _naming.InterfaceName("store").Should().Be("IStore");
    }

    [Test]
    public void Paths_AreOriginalNameBased_AndAffixStable()
    {
        _naming.ModelHeaderPath("buildingModel").Should().Be("Models/BuildingModel.h");
        _naming.EnumHeaderPath("orderStatus").Should().Be("Enums/OrderStatus.h");
        _naming.SerializerHeaderPath("buildingModel").Should().Be("Serializers/BuildingModelJson.h");
        _naming.SerializerSourcePath("buildingModel").Should().Be("Serializers/BuildingModelJson.cpp");
        _naming.ClientHeaderPath("store").Should().Be("Clients/StoreClient.h");
        _naming.ClientSourcePath("store").Should().Be("Clients/Impl/StoreClient.cpp");
        _naming.InterfaceHeaderPath("store").Should().Be("Interfaces/IStore.h");
    }

    [TestCase("std", "Std")]        // namespace-имена — PascalCase снимает коллизию
    [TestCase("windows", "Windows")]
    [TestCase("interface", "Interface")]
    public void Namespace_PascalCases_Names(string raw, string expected)
        => _naming.Namespace(raw).Should().Be(expected);

    [Test]
    public void Namespace_MacroToken_GetsSuffix()
        => _naming.Namespace("NULL").Should().Be("NULL_");
}
