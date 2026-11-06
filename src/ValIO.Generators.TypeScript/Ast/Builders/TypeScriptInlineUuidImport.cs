using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>Создаёт импорт общей константы UUID для встроенной проверки тела запроса.</summary>
internal static class TypeScriptInlineUuidImport
{
    /// <summary>Создаёт импорт, если встроенная схема тела ссылается на настроенную константу UUID.</summary>
    /// <param name="operations">Операции, генерируемые в исходном файле.</param>
    /// <param name="validation">Выбранная стратегия проверки клиента.</param>
    /// <param name="uuidFormat">Настроенный режим проверки UUID.</param>
    /// <param name="modelsPath">Настроенный каталог моделей.</param>
    /// <param name="relativePrefix">Относительный путь от сгенерированного файла до корневого каталога результатов.</param>
    /// <returns>Необходимый импорт времени выполнения или null, если встроенным схемам тела он не нужен.</returns>
    public static ImportNode? Create(
        IEnumerable<IrOperation> operations,
        IClientValidationStrategy validation,
        string? uuidFormat,
        string? modelsPath,
        string relativePrefix)
    {
        if (!validation.IsEnabled || validation.UuidConstName is not { } uuidConst
            || TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat) != TypeScriptOptionIds.UuidFormat.Regex
            || !operations.Any(operation => operation.RequestBody is { Type: var bodyType }
                && ValidationTypes.NeedsConstructedSchema(bodyType)
                && UuidFormatSupport.ContainsUuid(bodyType)))
            return null;

        return new ImportNode(
            items: [new IdentifierNode(uuidConst)],
            from: $"{relativePrefix}/{modelsPath ?? "models"}/{TypeScriptOptionIds.UuidFormat.ConstFileName}",
            kind: ImportKind.Named);
    }
}
