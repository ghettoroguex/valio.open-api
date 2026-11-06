using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;

namespace ValIO.Generators.Cpp.Ast.Builders.Std;

/// <summary>
/// Общие фабрики выражений std-билдеров (единая точка вместо копий Ident в каждом билдере).
/// </summary>
internal static class StdExpressions
{
    /// <summary>Идентификатор; также — официально задокументированный способ встроить сырой C++-токен/выражение.</summary>
    internal static IdentifierExpressionNode Ident(string name) => new(new IdentifierNode(name));

    /// <summary>Цепочка scope-доступа A::B::C.</summary>
    internal static ExpressionNode Scope(params string[] parts)
    {
        ExpressionNode current = Ident(parts[0]);
        for (var i = 1; i < parts.Length; i++)
            current = new MemberAccessExpressionNode(current, new IdentifierNode(parts[i]), useScopeOperator: true);
        return current;
    }
}
