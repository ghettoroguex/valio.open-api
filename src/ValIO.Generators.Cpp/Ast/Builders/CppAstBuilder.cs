using ValIO.Core.Ast.Builders;
using ValIO.Core.IR;
using ValIO.Generators.Cpp.Ast.Nodes;
using ValIO.Generators.Cpp.Models;
using ValIO.Generators.Cpp.Services;

namespace ValIO.Generators.Cpp.Ast.Builders;

/// <summary>Coordinates Unreal AST builders using one finalized document context.</summary>
public sealed class CppAstBuilder : AstBuilderBase
{
    /// <summary>Maps declaration references using the shared indexes.</summary>
    private readonly UETypeMapper _typeMapper;
    /// <summary>Builds model header contracts.</summary>
    private readonly UEModelAstBuilder _modelBuilder;
    /// <summary>Builds enum headers without document-specific state.</summary>
    private readonly UEEnumAstBuilder _enumBuilder;
    /// <summary>Builds operation result headers.</summary>
    private readonly UEResultAstBuilder _resultBuilder;
    /// <summary>Builds operation parameter headers.</summary>
    private readonly UEParamsAstBuilder _paramsBuilder;
    /// <summary>Builds endpoint declarations and implementations.</summary>
    private readonly UEApiAstBuilder _apiBuilder;

    /// <summary>Creates sub-builders with one mandatory finalized document context.</summary>
    /// <param name="options">The Unreal generation options.</param>
    /// <param name="documentContext">The shared declaration indexes.</param>
    /// <exception cref="ArgumentNullException">Either dependency is null.</exception>
    public CppAstBuilder(CppGenerationOptions options, IrDocumentContext documentContext) : base(documentContext)
    {
        ArgumentNullException.ThrowIfNull(options);
        var naming = new UENamingConvention();
        _typeMapper = new UETypeMapper(naming, documentContext);

        _modelBuilder = new UEModelAstBuilder(_typeMapper, naming, options, documentContext);
        _enumBuilder = new UEEnumAstBuilder(naming, options);
        _resultBuilder = new UEResultAstBuilder(_typeMapper, naming, options, documentContext);
        _paramsBuilder = new UEParamsAstBuilder(_typeMapper, naming, options, documentContext);
        _apiBuilder = new UEApiAstBuilder(_typeMapper, naming, options, _resultBuilder, documentContext);
    }

    /// <summary>
    /// Результат сборки: словарь файлового пути → AST-узла
    /// </summary>
    public sealed class BuildResult
    {
        /// <summary>
        /// Все сгенерированные файлы: относительный путь → AST-узел верхнего уровня
        /// </summary>
        public Dictionary<string, Core.Ast.Nodes.AstNode> Files { get; } = new();

        /// <summary>
        /// Предупреждения при сборке (например, пропуск рекурсивных свойств)
        /// </summary>
        public List<string> Warnings { get; } = new();
    }

    /// <summary>Builds all Unreal artifacts for the constructor context's original document.</summary>
    /// <param name="document">The exact document retained by the context.</param>
    /// <returns>The generated AST files and diagnostics.</returns>
    /// <exception cref="ArgumentException">The supplied document belongs to a different context.</exception>
    public BuildResult Build(IrDocument document)
    {
        if (!ReferenceEquals(document, DocumentContext.Document))
            throw new ArgumentException("The document must match the builder context.", nameof(document));
        var result = new BuildResult();

        var naming = new UENamingConvention();

        // 1. Модели → Public/Models/{StructName}.h
        foreach (var model in document.Models)
        {
            var headerNode = BuildModelHeader(model, result.Warnings);
            result.Files[naming.ModelFullPath(model.OriginalName)] = headerNode;
        }

        // 2. Enums → Public/Enums/{EnumName}.h
        foreach (var irEnum in document.Enums)
        {
            var headerNode = BuildEnumHeader(irEnum);
            result.Files[naming.EnumFullPath(irEnum.OriginalName)] = headerNode;
        }

        // 3. Per-client: Results, Params, endpoints
        foreach (var client in document.Clients)
        {
            foreach (var operation in client.Operations)
            {
                var opId = operation.OriginalMethodName;
                // Params, Result, endpoint header → Public/Endpoints/{OpId}/ (use ORIGINAL name so file paths don't change with affixes)
                result.Files[naming.ParamsFullPath(opId)] = BuildParamsHeader(operation, client.Name);
                result.Files[naming.ResultFullPath(opId)] = BuildResultHeader(operation, client.Name);

                var (header, source) = BuildEndpointFiles(client, operation);
                result.Files[naming.EndpointHeaderFullPath(opId)] = header;
                result.Files[naming.EndpointSourceFullPath(opId)] = source;
            }
        }

        return result;
    }

    // ===================================================================
    // Гранулярные методы: по одному файлу/сущности — используются
    // pipeline-шагами (CppUe*Step) и монолитным Build() выше,
    // поэтому оба пути дают идентичный вывод.
    // ===================================================================

    /// <summary>Builds a model header with shared inheritance and enum lookup.</summary>
    /// <param name="model">The model contract.</param>
    /// <param name="warnings">The optional diagnostic destination.</param>
    /// <returns>The generated model header.</returns>
    public CppHeaderFileNode BuildModelHeader(IrModel model, ICollection<string>? warnings = null)
        => _modelBuilder.Build(model, warnings);

    /// <summary>Хедер enum'а: Public/Enums/{Enum}.h (UENUM).</summary>
    public CppHeaderFileNode BuildEnumHeader(IrEnum irEnum) => _enumBuilder.Build(irEnum);

    /// <summary>Builds a parameter header using the document's enum classification.</summary>
    /// <param name="operation">The operation contract.</param>
    /// <param name="clientName">The owning client's generated name.</param>
    /// <returns>The generated parameter header.</returns>
    public CppHeaderFileNode BuildParamsHeader(IrOperation operation, string clientName)
        => _paramsBuilder.Build(operation, clientName);

    /// <summary>Builds a result header using the document's enum classification.</summary>
    /// <param name="operation">The operation contract.</param>
    /// <param name="clientName">The owning client's generated name.</param>
    /// <returns>The generated result header.</returns>
    public CppHeaderFileNode BuildResultHeader(IrOperation operation, string clientName)
        => _resultBuilder.Build(operation, clientName);

    /// <summary>Builds endpoint files using the shared model index.</summary>
    /// <param name="client">The owning client contract.</param>
    /// <param name="operation">The endpoint contract.</param>
    /// <returns>The endpoint header and implementation ASTs.</returns>
    public (CppHeaderFileNode Header, CppSourceFileNode Source) BuildEndpointFiles(IrClient client, IrOperation operation)
        => _apiBuilder.BuildEndpoint(client, operation);
}
