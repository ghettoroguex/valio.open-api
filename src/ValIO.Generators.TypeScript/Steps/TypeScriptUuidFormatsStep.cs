using System.Text;
using ValIO.Core.Utilities;
using ValIO.Core.Plugins;
using ValIO.Generators.TypeScript.Options;

namespace ValIO.Generators.TypeScript.Steps;

/// <summary>
/// Step: emits the shared <c>{modelsPath}/formats.ts</c> constant file (UUID pattern + regex) used by
/// generated validation schemas when <c>uuidFormat == "regex"</c>. Only runs when validation is
/// enabled and the document actually contains a uuid field.
/// </summary>
/// <remarks>
/// The file lives under <c>modelsPath</c> alongside the co-located model schemas (which import it as
/// <c>../formats</c>); params/results schemas reference it as <c>../../{modelsPath}/formats</c>.
/// </remarks>
public sealed class TypeScriptUuidFormatsStep : ICodeGenerationStep
{
    public string Name => "typescript-uuid-formats";
    public int Order => 29;

    public bool ShouldExecute(GenerationContext context)
    {
        if (context.PluginOptions is not TypeScriptGenerationOptions opts) return false;
        if (string.IsNullOrEmpty(opts.ValidationLibrary)) return false;
        if (TypeScriptOptionIds.UuidFormat.Resolve(opts.UuidFormat) != TypeScriptOptionIds.UuidFormat.Regex) return false;
        return UuidFormatSupport.DocumentUsesUuid(context.IrDocument);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(GenerationContext context, CancellationToken ct = default)
    {
        var fileOrg = context.Configuration.FileOrganization;
        var modelsPath = fileOrg.ModelsPath ?? "models";

        var sb = new StringBuilder();
        sb.AppendLine("// Shared format patterns for generated validation schemas.");
        sb.AppendLine($"export const {TypeScriptOptionIds.UuidFormat.PatternConstName} = \"{CodeEscapeHelper.EscapeTypeScriptString(TypeScriptOptionIds.UuidFormat.Pattern)}\";");
        sb.AppendLine($"export const {TypeScriptOptionIds.UuidFormat.RegexConstName} = new RegExp({TypeScriptOptionIds.UuidFormat.PatternConstName});");

        var relativePath = $"{modelsPath}/{TypeScriptOptionIds.UuidFormat.ConstFileName}.ts";
        context.GeneratedFiles.Add(new GeneratedFile(relativePath, sb.ToString()));
        return Task.CompletedTask;
    }
}
