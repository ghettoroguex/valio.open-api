using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Ky.Ast.Builders;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits a <c>ky</c> client (<c>{op}.ky.ts</c>) per IR operation via AST.
/// Activated when <c>options.Transports</c> contains "ky".
/// </summary>
public sealed class KyClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-ky-client";
    public override int Order => 42;
    protected override string TransportId => TypeScriptOptionIds.Transport.Ky;
    protected override string FileSuffix => ".ky.ts";

    public KyClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public KyClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new KyClientAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildOperationClientSourceFile(operation, fileOrg);
    }
}
