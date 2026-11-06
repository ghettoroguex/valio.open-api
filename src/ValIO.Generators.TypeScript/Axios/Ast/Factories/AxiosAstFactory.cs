using ValIO.Core.Interfaces;
using ValIO.Core.Ast.Builders;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Axios.Ast.Builders;
using ValIO.Generators.TypeScript.Axios.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Axios.Ast.Factories;

/// <summary>
/// Фабрика для создания Axios AST Builder'ов и Renderer'ов
/// </summary>
public class AxiosAstFactory : AstBuilderBase
{
    private readonly INamingConverter _namingConverter;
    private readonly Func<ValIO.Core.IR.Types.IrType, ValIO.Core.Ast.Nodes.TypeNode> _typeBuilder;
    private AxiosAstBuilder? _builder;
    private IAxiosAstRenderer? _renderer;
    
    /// <summary>Creates an Axios AST factory bound to one finalized IR document.</summary>
    /// <param name="documentContext">The shared model and enum indexes.</param>
    /// <param name="namingConverter">The generated naming policy.</param>
    /// <param name="typeBuilder">The TypeScript type mapping function.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public AxiosAstFactory(IrDocumentContext documentContext, INamingConverter namingConverter, Func<ValIO.Core.IR.Types.IrType, ValIO.Core.Ast.Nodes.TypeNode> typeBuilder)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
    }
    
    /// <summary>
    /// Создает или возвращает существующий AxiosAstBuilder
    /// </summary>
    public AxiosAstBuilder GetBuilder()
    {
        return _builder ??= new AxiosAstBuilder(DocumentContext, _namingConverter, _typeBuilder);
    }
    
    /// <summary>
    /// Создает или возвращает существующий AxiosAstRenderer
    /// </summary>
    public IAxiosAstRenderer GetRenderer()
    {
        return _renderer ??= new AxiosAstRenderer();
    }
}
