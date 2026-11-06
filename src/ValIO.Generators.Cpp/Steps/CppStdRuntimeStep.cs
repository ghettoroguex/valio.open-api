using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Runtime;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Emits only runtime layers actually required by the selected model, serializer, and client outputs.
/// </summary>
public sealed class CppStdRuntimeStep(ICppAstRenderer renderer) : CppStdStepBase(renderer)
{
    /// <inheritdoc />
    public override string Name => "cpp-std-runtime";
    /// <inheritdoc />
    public override int Order => 70;

    /// <inheritdoc />
    public override bool ShouldExecute(GenerationContext context)
        => base.ShouldExecute(context)
           && (EffectiveOptions(context).TemporalBackend != CppTemporalBackend.String
               || EffectiveOptions(context).UuidBackend != CppUuidBackend.String
               || (EffectiveOptions(context).EmitSerializers
                && ((context.Configuration.GenerateModels && context.IrDocument.Models.Count > 0)
                    || (context.Configuration.GenerateEnums && context.IrDocument.Enums.Count > 0)))
               || (context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0));

    /// <inheritdoc />
    public override Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var options = EffectiveOptions(context);
        ct.ThrowIfCancellationRequested();
        if (context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0 && !options.EmitSerializers)
            throw new InvalidOperationException("Standalone C++ clients require emitSerializers=true; disable client generation for pure DTO output.");
        if (options.TemporalBackend != CppTemporalBackend.String)
            AddRuntimeFile(context, TemporalRuntimeTemplates.TypesHeaderFileName, TemporalRuntimeTemplates.TypesHeader(options.TemporalBackend));
        if (options.UuidBackend != CppUuidBackend.String)
            AddRuntimeFile(context, UuidRuntimeTemplates.TypesHeaderFileName, UuidRuntimeTemplates.TypesHeader(options.UuidBackend));
        var hasCodecs = (options.EmitSerializers && ((context.Configuration.GenerateModels && context.IrDocument.Models.Count > 0)
                || (context.Configuration.GenerateEnums && context.IrDocument.Enums.Count > 0)))
            || (context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0);
        if (!hasCodecs) return Task.CompletedTask;
        if (options.TemporalBackend != CppTemporalBackend.String)
            AddRuntimeFile(context, TemporalRuntimeTemplates.JsonHeaderFileName, TemporalRuntimeTemplates.JsonHeader(options.TemporalBackend));
        if (options.UuidBackend != CppUuidBackend.String)
            AddRuntimeFile(context, UuidRuntimeTemplates.JsonHeaderFileName, UuidRuntimeTemplates.JsonHeader(options.UuidBackend));
        AddRuntimeFile(context, StdRuntimeTemplates.JsonTypesHeaderFileName,
            StdRuntimeTemplates.JsonTypesHeader(options.JsonInclude, options.TemporalBackend, options.UuidBackend));
        AddRuntimeFile(context, JsonValidationTemplates.HeaderFileName, JsonValidationTemplates.Header(options.JsonInclude));
        if (context.Configuration.GenerateClients && context.IrDocument.Clients.Count > 0)
        {
            AddRuntimeFile(context, StdRuntimeTemplates.CancellationHeaderFileName, StdRuntimeTemplates.CancellationHeader());
            AddRuntimeFile(context, StdRuntimeTemplates.ExpectedHeaderFileName, StdRuntimeTemplates.ExpectedHeader());
            AddRuntimeFile(context, StdRuntimeTemplates.ErrorHeaderFileName, StdRuntimeTemplates.ErrorHeader());
            AddRuntimeFile(context, StdRuntimeTemplates.HttpHeaderFileName, StdRuntimeTemplates.HttpHeader());
            AddRuntimeFile(context, RequestParameterRuntimeTemplates.HeaderFileName, RequestParameterRuntimeTemplates.Header());
            AddRuntimeFile(context, StdRuntimeTemplates.HttpSourceFileName, StdRuntimeTemplates.HttpSource(options.HttpInclude));
            AddRuntimeFile(context, StdRuntimeTemplates.JsonHeaderFileName, StdRuntimeTemplates.JsonHeader(options.JsonInclude));
        }
        return Task.CompletedTask;
    }

    /// <summary>Adds a canonical runtime file once even if a pipeline is resumed or executed repeatedly.</summary>
    /// <param name="context">The current generation context.</param>
    /// <param name="path">The canonical output path.</param>
    /// <param name="content">The generated runtime content.</param>
    private static void AddRuntimeFile(GenerationContext context, string path, string content)
    {
        if (!context.GeneratedFiles.Any(file => file.RelativePath == path))
            context.GeneratedFiles.Add(new GeneratedFile(path, content));
    }
}
