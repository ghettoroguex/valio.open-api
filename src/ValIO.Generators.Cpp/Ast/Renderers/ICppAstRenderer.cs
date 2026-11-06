using ValIO.Core.Ast.Nodes;

namespace ValIO.Generators.Cpp.Ast.Renderers;

/// <summary>
/// Интерфейс рендерера C++ AST в строковый код.
/// </summary>
public interface ICppAstRenderer
{
    /// <summary>
    /// Рендерит узел верхнего уровня (обычно CppHeaderFileNode или CppSourceFileNode)
    /// в полный текст файла.
    /// </summary>
    string Render(AstNode node);
}
