using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Got.Ast.Builders;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits a <c>got</c> client (<c>{op}.got.ts</c>) per IR operation via AST.
/// Activated when <c>options.Transports</c> contains "got".
/// </summary>
public sealed class GotClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-got-client";
    public override int Order => 45;
    protected override string TransportId => TypeScriptOptionIds.Transport.Got;
    protected override string FileSuffix => ".got.ts";

    public GotClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public GotClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new GotClientAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildOperationClientSourceFile(operation, fileOrg);
    }
}
