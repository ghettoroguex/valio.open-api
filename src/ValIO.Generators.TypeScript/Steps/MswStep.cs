using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Ast.Renderers;
using ValIO.Generators.TypeScript.Msw.Ast.Builders;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits a single <c>mocks/handlers.ts</c> file with an MSW handler array,
/// one <c>http.{verb}("/path", () =&gt; HttpResponse.json(...))</c> per IR operation.
/// Activated when <c>options.MockLibrary == "msw"</c>.
///
/// Output path is independent of <c>clientsPath</c> — MSW handlers are not a client,
/// and they sit beside (not inside) the API folder so they can be wired into both
/// dev and test setups without circular imports.
/// </summary>
public sealed class MswStep : ICodeGenerationStep
{
    private readonly INamingConverter _namingConverter;
    private readonly TypeScriptAstRenderer _renderer;

    public string Name => "typescript-msw-handlers";
    public int Order => 80;

    public MswStep(INamingConverter namingConverter)
        : this(namingConverter, Array.Empty<ITypeScriptNodeRenderer>())
    {
    }

    public MswStep(INamingConverter namingConverter, IEnumerable<ITypeScriptNodeRenderer> nodeRenderers)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _renderer = new TypeScriptAstRenderer(nodeRenderers);
    }

    public bool ShouldExecute(GenerationContext context)
    {
        var opts = context.PluginOptions as TypeScriptGenerationOptions;
        return string.Equals(opts?.MockLibrary, TypeScriptOptionIds.MockLibrary.Msw, StringComparison.OrdinalIgnoreCase);
    }

    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var builder = new MswHandlersAstBuilder(context.DocumentContext, _namingConverter);
        var fileOrg = context.Configuration.FileOrganization;

        var sourceFile = builder.BuildHandlersSourceFile(context.IrDocument, fileOrg);
        var content = _renderer.Render(sourceFile);

        context.GeneratedFiles.Add(new GeneratedFile("mocks/handlers.ts", content));
        return Task.CompletedTask;
    }
}
