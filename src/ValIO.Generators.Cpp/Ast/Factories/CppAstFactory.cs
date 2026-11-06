using ValIO.Core.IR;
using ValIO.Generators.Cpp.Ast.Builders;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.Ast.Factories;

/// <summary>
/// Creates Unreal builders with mandatory document indexes and independent renderers.
/// </summary>
public sealed class CppAstFactory
{
    /// <summary>Holds the options shared by builders created through this factory.</summary>
    private readonly CppGenerationOptions _options;
    /// <summary>Holds the finalized document indexes required by every created builder.</summary>
    private readonly IrDocumentContext _documentContext;

    /// <summary>Requires generation options and a finalized document before creating builders.</summary>
    /// <param name="options">The Unreal generation options.</param>
    /// <param name="documentContext">The document indexes supplied to every builder.</param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    public CppAstFactory(CppGenerationOptions options, IrDocumentContext documentContext)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _documentContext = documentContext ?? throw new ArgumentNullException(nameof(documentContext));
    }

    /// <summary>
    /// Creates a facade bound to this factory's document context.
    /// </summary>
    /// <returns>A new document-aware builder.</returns>
    public CppAstBuilder CreateBuilder()
    {
        return new CppAstBuilder(_options, _documentContext);
    }

    /// <summary>
    /// Creates an independent renderer without sharing mutable rendering state.
    /// </summary>
    /// <returns>A new renderer instance.</returns>
    public CppAstRenderer CreateRenderer()
    {
        return new CppAstRenderer();
    }
}
