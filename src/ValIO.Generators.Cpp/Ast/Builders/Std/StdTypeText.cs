using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;

namespace ValIO.Generators.Cpp.Ast.Builders.Std;

/// <summary>
/// Текстовое представление TypeNode для std-профиля (используется в сигнатурах
/// RawTypeNode и generic-аргументах вроде ValIO::MakeError&lt;T&gt;).
/// </summary>
public static class StdTypeText
{
    public static string Render(TypeNode type) => type switch
    {
        SimpleTypeNode simple => simple.Name.Name,
        GenericTypeNode generic => $"{generic.Name.Name}<{string.Join(", ", generic.TypeArguments.Select(Render))}>",
        RawTypeNode raw => raw.RawTypeString,
        _ => type.ToString() ?? "std::string",
    };
}
