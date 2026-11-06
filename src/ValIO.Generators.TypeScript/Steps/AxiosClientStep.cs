using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits an <c>axios</c> client (<c>{op}.axios.ts</c>) per IR operation via AST.
/// Activated when <c>options.Transports</c> contains "axios".
/// </summary>
public sealed class AxiosClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-axios-client";
    public override int Order => 41;
    protected override string TransportId => TypeScriptOptionIds.Transport.Axios;
    protected override string FileSuffix => ".axios.ts";

    public AxiosClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public AxiosClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new TypeScriptAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildSourceFileForOperation(operation, fileOrganization: fileOrg, transport: "axios");
    }
}
