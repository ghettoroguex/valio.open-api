using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.OFetch.Ast.Builders;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits an <c>ofetch</c> client (<c>{op}.ofetch.ts</c>) per IR operation via AST.
/// Activated when <c>options.Transports</c> contains "ofetch".
/// </summary>
public sealed class OFetchClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-ofetch-client";
    public override int Order => 44;
    protected override string TransportId => TypeScriptOptionIds.Transport.OFetch;
    protected override string FileSuffix => ".ofetch.ts";

    public OFetchClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public OFetchClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new OFetchClientAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildOperationClientSourceFile(operation, fileOrg);
    }
}
