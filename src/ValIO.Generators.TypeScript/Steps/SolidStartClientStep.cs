using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Solid.Ast.Builders;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits a SolidStart server-function client (<c>{op}.solid.ts</c>) per IR operation.
/// Activated when <c>options.Transports</c> contains "solid" / "solidstart" / "solid-start"
/// (descriptor normalises to <see cref="TypeScriptOptionIds.Transport.Solid"/>).
///
/// Each file starts with a <c>"use server";</c> directive emitted by
/// <c>TypeScriptAstRenderer</c> (via <c>SourceFileNode.Metadata["directive"]</c>); its body is a
/// fetch-based async function — Solid's bundler turns it into an RPC handler on the
/// server and a typed RPC client on the browser at build time.
/// </summary>
public sealed class SolidStartClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-solidstart-client";
    public override int Order => 48;
    protected override string TransportId => TypeScriptOptionIds.Transport.Solid;
    protected override string FileSuffix => ".solid.ts";

    public SolidStartClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public SolidStartClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new SolidStartClientAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildOperationClientSourceFile(operation, fileOrg);
    }
}
