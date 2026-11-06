using ValIO.Core.Plugins;
using ValIO.Generators.Cpp.Ast.Builders;
using ValIO.Generators.Cpp.Ast.Renderers;
using ValIO.Generators.Cpp.Models;

namespace ValIO.Generators.Cpp.Steps;

/// <summary>
/// Общий каркас UE-шагов: рендерер + гейтинг по runtime-профилю.
/// UE-профиль активен, когда опции отсутствуют (defaults) или Runtime == Ue;
/// std-профиль (cpp-httplib + nlohmann_json) отключает все UE-шаги.
/// </summary>
public abstract class CppUeStepBase : ICodeGenerationStep
{
    /// <summary>Creates a generation step using the renderer owned by its pipeline.</summary>
    /// <param name="renderer">The pipeline's independent AST renderer.</param>
    /// <exception cref="ArgumentNullException">The renderer is null.</exception>
    protected CppUeStepBase(ICppAstRenderer renderer)
    {
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>Рендерер AST → текст файла.</summary>
    protected ICppAstRenderer Renderer { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract int Order { get; }

    /// <inheritdoc />
    public virtual bool ShouldExecute(GenerationContext context)
    {
        var opts = context.PluginOptions as CppGenerationOptions;
        return opts?.Runtime != CppRuntimeProfile.Std;
    }

    /// <inheritdoc />
    public abstract Task ExecuteAsync(GenerationContext context, CancellationToken ct = default);

    /// <summary>
    /// Опции генерации (null-опции → значения по умолчанию = UE-профиль).
    /// </summary>
    protected static CppGenerationOptions EffectiveOptions(GenerationContext context)
        => context.PluginOptions as CppGenerationOptions ?? new CppGenerationOptions();

    /// <summary>Creates a facade using the request's mandatory shared document indexes.</summary>
    /// <param name="context">The current generation request.</param>
    /// <returns>A new facade bound to the request's finalized IR document.</returns>
    protected static CppAstBuilder CreateBuilder(GenerationContext context)
    {
        return new CppAstBuilder(EffectiveOptions(context), context.DocumentContext);
    }
}
