using ValIO.Core.Interfaces;
using ValIO.Core.Ast.Builders;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Ast.Factories;

/// <summary>
/// Фабрика для создания TypeScript AST Builder'ов и Renderer'ов
/// </summary>
public class TypeScriptAstFactory : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private ITypeScriptAstBuilder? _builder;
    private ITypeScriptAstRenderer? _renderer;

    /// <summary>Creates a factory bound to one finalized IR document.</summary>
    /// <param name="documentContext">The document indexes shared by the cached builder.</param>
    /// <param name="namingConverter">The generated naming policy.</param>
    /// <exception cref="ArgumentNullException">The context or naming converter is null.</exception>
    public TypeScriptAstFactory(IrDocumentContext documentContext, INamingConverter namingConverter)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
    }

    /// <summary>
    /// Создает или возвращает существующий TypeScriptAstBuilder
    /// </summary>
    public ITypeScriptAstBuilder GetBuilder()
    {
        return _builder ??= new TypeScriptAstBuilder(DocumentContext, _namingConverter);
    }

    /// <summary>
    /// Создает или возвращает существующий TypeScriptAstRenderer
    /// </summary>
    public ITypeScriptAstRenderer GetRenderer()
    {
        return _renderer ??= new TypeScriptAstRenderer();
    }
}
