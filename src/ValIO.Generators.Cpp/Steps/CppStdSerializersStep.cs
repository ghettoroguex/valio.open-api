using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Builders.Std;
using ValIO.Generators.Cpp.Ast.Renderers;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// JSON-кодеки std-профиля: Serializers/{N}Json.h/.cpp для моделей и enum'ов —
/// единственные файлы, которые включают nlohmann и знают wire-имена.
/// </summary>
public sealed class CppStdSerializersStep(ICppAstRenderer renderer) : CppStdStepBase(renderer)
{
    /// <inheritdoc />
    public override string Name => "cpp-std-serializers";
    /// <inheritdoc />
    public override int Order => 60;

    /// <inheritdoc />
    public override bool ShouldExecute(GenerationContext context)
        => base.ShouldExecute(context)
           && EffectiveOptions(context).EmitSerializers
           && (context.Configuration.GenerateModels || context.Configuration.GenerateEnums)
           && (context.IrDocument.Models.Count > 0 || context.IrDocument.Enums.Count > 0);

    /// <inheritdoc />
    public override Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var (naming, mapper) = CreateServices(context);
        var options = EffectiveOptions(context);
        var enumBuilder = new StdEnumAstBuilder(naming);
        var modelBuilder = new StdModelJsonAstBuilder(mapper, naming, context.DocumentContext);
        var rootNamespace = ResolveRootNamespace(context);

        foreach (var irEnum in context.IrDocument.Enums)
        {
            ct.ThrowIfCancellationRequested();
            if (!context.Configuration.GenerateEnums)
                continue;
            var (codecHeader, codecSource) = enumBuilder.BuildJson(irEnum, rootNamespace, options);
            context.GeneratedFiles.Add(new GeneratedFile(
                naming.SerializerHeaderPath(irEnum.OriginalName), Renderer.Render(codecHeader)));
            context.GeneratedFiles.Add(new GeneratedFile(
                naming.SerializerSourcePath(irEnum.OriginalName), Renderer.Render(codecSource)));
        }

        if (!context.Configuration.GenerateModels)
            return Task.CompletedTask;

        foreach (var model in context.IrDocument.Models)
        {
            ct.ThrowIfCancellationRequested();
            var (codecHeader, codecSource) = modelBuilder.Build(
                model, rootNamespace, options, context.Warnings);
            context.GeneratedFiles.Add(new GeneratedFile(
                naming.SerializerHeaderPath(model.OriginalName), Renderer.Render(codecHeader)));
            context.GeneratedFiles.Add(new GeneratedFile(
                naming.SerializerSourcePath(model.OriginalName), Renderer.Render(codecSource)));
        }

        DrainWarnings(context, mapper);
        return Task.CompletedTask;
    }
}
