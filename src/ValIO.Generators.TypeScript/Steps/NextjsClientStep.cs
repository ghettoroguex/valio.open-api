using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.Models;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Nextjs.Ast.Builders;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits a Next.js (App Router) <c>fetch</c> client (<c>{op}.nextjs.ts</c>) per IR operation
/// via AST. Activated when <c>options.Transports</c> contains "nextjs". The client is server-safe
/// (no <c>"use client"</c>) and threads Next's fetch extensions (cache tags + revalidation).
/// </summary>
public sealed class NextjsClientStep : TypeScriptClientStepBase
{
    public override string Name => "typescript-nextjs-client";
    public override int Order => 49;
    protected override string TransportId => TypeScriptOptionIds.Transport.Nextjs;
    protected override string FileSuffix => ".nextjs.ts";

    public NextjsClientStep(INamingConverter namingConverter) : base(namingConverter) { }
    public NextjsClientStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
        : base(namingConverter, nodeRenderers) { }

    /// <inheritdoc />
    /// <inheritdoc />
    protected override SourceFileNode BuildOperationSourceFile(
        IrDocumentContext documentContext, IrOperation operation, FileOrganization fileOrg, TypeScriptGenerationOptions? options)
    {
        var builder = new NextjsClientAstBuilder(documentContext, NamingConverter, options);
        return builder.BuildOperationClientSourceFile(operation, fileOrg);
    }
}
