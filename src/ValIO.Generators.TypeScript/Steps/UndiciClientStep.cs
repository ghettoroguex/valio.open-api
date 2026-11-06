using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Undici.Ast.Builders;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits an <c>undici</c> client (<c>{op}.undici.ts</c>) per IR operation via AST.
/// Activated when <c>options.Transports</c> contains "undici".
/// </summary>
public sealed class UndiciClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-undici-client";
    public override int Order => 43;
    protected override string TransportId => TypeScriptOptionIds.Transport.Undici;
    protected override string FileSuffix => ".undici.ts";

    public UndiciClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public UndiciClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new UndiciClientAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildOperationClientSourceFile(operation, fileOrg);
    }
}
