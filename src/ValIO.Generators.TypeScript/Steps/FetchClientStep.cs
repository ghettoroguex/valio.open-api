using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits a <c>fetch</c>-based client (<c>{op}.fetch.ts</c>) per IR operation via AST.
/// Activated when <c>options.Transports</c> contains "fetch", or when options are missing
/// (fetch is the out-of-the-box default).
/// </summary>
public sealed class FetchClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-fetch-client";
    public override int Order => 40;
    protected override string TransportId => TypeScriptOptionIds.Transport.Fetch;
    protected override string FileSuffix => ".fetch.ts";
    protected override bool ActivateWhenOptionsMissing => true;

    public FetchClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public FetchClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new TypeScriptAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildSourceFileForOperation(operation, fileOrganization: fileOrg, transport: "fetch");
    }
}
