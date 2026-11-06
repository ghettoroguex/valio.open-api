using ValIO.Core.Ast.Builders;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Zod.Ast.Builders;
using ValIO.Generators.TypeScript.Zod.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Zod.Ast.Factories;

/// <summary>
/// Creates and reuses Zod AST services for one shared document context.
/// </summary>
public class ZodAstFactory : AstBuilderBase
{
    /// <summary>The naming converter shared by generated schema builders.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>The language-specific mapping from IR types to TypeScript type nodes.</summary>
    private readonly Func<ValIO.Core.IR.Types.IrType, ValIO.Core.Ast.Nodes.TypeNode> _typeBuilder;
    /// <summary>The builder cached for this document context.</summary>
    private ZodAstBuilder? _builder;
    /// <summary>The syntax renderer reused for successive schema nodes.</summary>
    private IZodAstRenderer? _renderer;
    
    /// <summary>Creates a factory bound to the finalized input document.</summary>
    /// <param name="documentContext">The shared immutable declaration indexes.</param>
    /// <param name="namingConverter">The naming rules for generated declarations.</param>
    /// <param name="typeBuilder">The conversion from IR types to TypeScript type nodes.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public ZodAstFactory(IrDocumentContext documentContext, INamingConverter namingConverter,
        Func<ValIO.Core.IR.Types.IrType, ValIO.Core.Ast.Nodes.TypeNode> typeBuilder)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
    }
    
    /// <summary>
    /// Gets the document-bound schema builder, creating it once on demand.
    /// </summary>
    /// <returns>The builder sharing this factory's document indexes.</returns>
    public ZodAstBuilder GetBuilder()
    {
        return _builder ??= new ZodAstBuilder(DocumentContext, _namingConverter, _typeBuilder);
    }
    
    /// <summary>
    /// Gets the reusable schema syntax renderer, creating it once on demand.
    /// </summary>
    /// <returns>The cached Zod syntax renderer.</returns>
    public IZodAstRenderer GetRenderer()
    {
        return _renderer ??= new ZodAstRenderer();
    }
}
