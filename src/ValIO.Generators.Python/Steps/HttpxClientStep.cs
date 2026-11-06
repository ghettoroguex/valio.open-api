using ValIO.Core.Interfaces;
using ValIO.Core.Plugins;
using ValIO.Generators.Python.Ast.Builders;
using ValIO.Generators.Python.Ast.Renderers;
using ValIO.Generators.Python.Options;

namespace ValIO.Generators.Python.Steps;

/// <summary>
/// Generates one HTTPX facade module per client using shared document declaration indexes.
/// </summary>
public sealed class HttpxClientStep : ICodeGenerationStep
{
    /// <summary>Converts source client identifiers into Python module names.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Renders client AST modules into Python source text.</summary>
    private readonly IPythonAstRenderer _renderer;
    /// <summary>Maps shared request and response types into Python annotations.</summary>
    private readonly PythonIrTypeMapper _typeMapper;

    /// <summary>Gets the pipeline identifier selecting HTTPX client generation.</summary>
    /// <value>The Python HTTPX generation step identifier.</value>
    public string Name => "python-httpx-client";
    /// <summary>Gets the order placing clients after their model and enum declarations.</summary>
    /// <value>The client generation pipeline order.</value>
    public int Order => 40;

    /// <summary>Initializes HTTPX generation with the configured Python services.</summary>
    /// <param name="namingConverter">The converter for client module names.</param>
    /// <param name="renderer">The renderer for Python client modules.</param>
    /// <param name="typeMapper">The shared schema to Python annotation mapper.</param>
    public HttpxClientStep(
        INamingConverter namingConverter,
        IPythonAstRenderer renderer,
        PythonIrTypeMapper typeMapper)
    {
        _namingConverter = namingConverter;
        _renderer = renderer;
        _typeMapper = typeMapper;
    }

    /// <summary>Determines whether client generation is enabled for a document containing operations.</summary>
    /// <param name="context">The request configuration and finalized source document.</param>
    /// <returns>Whether the HTTPX client modules should be generated.</returns>
    public bool ShouldExecute(GenerationContext context)
        => context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0;

    /// <summary>Generates client modules with constructor-injected shared document indexes.</summary>
    /// <param name="context">The request containing shared indexes, Python options, and generated output.</param>
    /// <param name="ct">The cancellation token supplied by the generation pipeline.</param>
    /// <returns>A completed task after client modules have been added to the output.</returns>
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var opts = context.PluginOptions as PythonGenerationOptions ?? new PythonGenerationOptions();
        var builder = new HttpxClientAstBuilder(context.DocumentContext, _namingConverter, _typeMapper);

        foreach (var client in context.IrDocument.Clients)
        {
            var module = builder.BuildModule(client, opts);
            var content = _renderer.Render(module);
            var fileName = _namingConverter.ToSnakeCase(client.Name);
            var relativePath = $"api/{fileName}_api.py";
            context.GeneratedFiles.Add(new GeneratedFile(relativePath, content));
        }

        return Task.CompletedTask;
    }
}
