using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Общий каркас std-шагов (cpp-httplib + nlohmann_json): активны только при
/// Runtime == Std (без опций работает UE-профиль). Формирует mapper/нейминг
/// с прокинутыми именами enum'ов и дренажирует warnings маппера в context.Warnings.
/// </summary>
public abstract class CppStdStepBase : ICodeGenerationStep
{
    /// <summary>Creates a generation step using the renderer owned by its pipeline.</summary>
    /// <param name="renderer">The pipeline's independent AST renderer.</param>
    /// <exception cref="ArgumentNullException">The renderer is null.</exception>
    protected CppStdStepBase(ICppAstRenderer renderer)
    {
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>Gets the renderer shared only by this request's sequential pipeline steps.</summary>
    /// <value>The renderer supplied at construction.</value>
    protected ICppAstRenderer Renderer { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract int Order { get; }

    /// <inheritdoc />
    public virtual bool ShouldExecute(GenerationContext context)
    {
        var opts = context.PluginOptions as CppGenerationOptions;
        return opts?.Runtime == CppRuntimeProfile.Std;
    }

    /// <inheritdoc />
    public abstract Task ExecuteAsync(GenerationContext context, CancellationToken ct = default);

    /// <summary>Resolves standalone options when plugin-specific settings are absent.</summary>
    /// <param name="context">The current request context.</param>
    /// <returns>The effective standalone generation options.</returns>
    protected static CppGenerationOptions EffectiveOptions(GenerationContext context)
        => context.PluginOptions as CppGenerationOptions ?? new CppGenerationOptions { Runtime = CppRuntimeProfile.Std };

    /// <summary>
    /// Корневой namespace: опция «namespace» (валидный C++ идентификатор) либо «Api».
    /// Configuration.Namespace не используется — это C#-формат (точечные сегменты).
    /// </summary>
    protected static string ResolveRootNamespace(GenerationContext context)
    {
        var opts = EffectiveOptions(context);
        return string.IsNullOrWhiteSpace(opts.Namespace) ? "Api" : new StdNamingConvention().Namespace(opts.Namespace);
    }

    /// <summary>Creates step-owned mapping services with the request's shared document indexes.</summary>
    /// <param name="context">The current generation request.</param>
    /// <returns>The naming rules and document-aware mapper.</returns>
    protected static (StdNamingConvention Naming, StdCppTypeMapper Mapper) CreateServices(GenerationContext context)
    {
        var naming = new StdNamingConvention();
        var mapper = new StdCppTypeMapper(naming, context.DocumentContext, EffectiveOptions(context));
        return (naming, mapper);
    }

    /// <summary>Переносит warnings маппера (union/any/…) в диагностику прогона (с дедупом).</summary>
    protected static void DrainWarnings(GenerationContext context, StdCppTypeMapper mapper)
    {
        // Шаги создают собственные мапперы с одинаковыми warnings — дедуп на уровне прогона.
        foreach (var warning in mapper.Warnings.Distinct())
        {
            if (!context.Warnings.Contains(warning))
                context.Warnings.Add(warning);
        }
    }
}
