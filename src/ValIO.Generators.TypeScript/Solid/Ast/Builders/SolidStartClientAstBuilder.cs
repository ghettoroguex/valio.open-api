using ValIO.Core.Ast.Builders;
using System.Collections.Immutable;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Solid.Ast.Builders;

/// <summary>
/// Builds a SolidStart server-function client for a single IR operation.
/// The generated file has <c>"use server";</c> as its first directive (emitted via
/// <see cref="SourceFileNode"/>.Metadata["directive"]) and a fetch-based async function
/// body identical to what <see cref="FetchClientStep"/> emits — Solid's
/// <c>$server</c>-transform compiles the file into an RPC handler on the server
/// and a typed RPC client on the browser.
/// </summary>
public sealed class SolidStartClientAstBuilder : AstBuilderBase
{
    private readonly TypeScriptAstBuilder _innerBuilder;
    private readonly INamingConverter _namingConverter;

    /// <summary>Creates a SolidStartClientAstBuilder for one finalized IR document.</summary>
    /// <param name="documentContext">The shared immutable indexes of the finalized IR document.</param>
    /// <param name="namingConverter">The generated identifier naming policy.</param>
    /// <param name="options">The TypeScript generation settings.</param>
    /// <exception cref="ArgumentNullException">A required context or collaborator is null.</exception>
    public SolidStartClientAstBuilder(IrDocumentContext documentContext,
        INamingConverter namingConverter, TypeScriptGenerationOptions? options)
        : base(documentContext)
    {
        ArgumentNullException.ThrowIfNull(namingConverter);
        _namingConverter = namingConverter;
        _innerBuilder = new TypeScriptAstBuilder(documentContext, namingConverter, options);
    }


    /// <summary>
    /// Build a <see cref="SourceFileNode"/> whose body is a fetch-based async function
    /// (delegated to <see cref="TypeScriptAstBuilder.BuildSourceFileForOperation"/>) and
    /// whose metadata carries <c>directive = "use server"</c> so the renderer emits the
    /// SolidStart server-function prologue.
    /// </summary>
    public SourceFileNode BuildOperationClientSourceFile(IrOperation operation, FileOrganization fileOrganization)
    {
        ArgumentNullException.ThrowIfNull(operation);

        var fetchSource = _innerBuilder.BuildSourceFileForOperation(
            operation,
            importPath: null,
            fileOrganization: fileOrganization,
            transport: "fetch");

        // Wrap with the "use server" directive. SourceFileNode is immutable; rebuild
        // with the same imports/exports/declarations and a metadata dictionary.
        var metadata = new Dictionary<string, object>(fetchSource.Metadata)
        {
            ["directive"] = "use server"
        };

        var fileBase = _namingConverter.ToCamelCase(operation.OriginalMethodName);
        return new SourceFileNode(
            imports: fetchSource.Imports,
            exports: fetchSource.Exports,
            declarations: fetchSource.Declarations,
            metadata: metadata,
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{fileBase}.solid.ts",
                $"endpoints/{fileBase}",
                $"SolidStart server-function client for the {operation.MethodName} operation."));
    }
}
