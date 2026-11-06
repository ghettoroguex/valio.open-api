using System.Text;
using System.Text.RegularExpressions;

namespace ValIO.Generators.Cpp.Services;

/// <summary>
/// Сервис именования для Unreal Engine C++.
/// Применяет UE-конвенции: F-префикс для структур, U-префикс для классов,
/// E-префикс для enum, PascalCase для свойств и методов.
/// </summary>
public sealed class UENamingConvention
{
    /// <summary>
    /// True if <paramref name="pascal"/> already carries the Hungarian-notation prefix
    /// <paramref name="prefix"/> (prefix followed by an uppercase letter, e.g. "FFoo", not just
    /// any identifier that happens to start with the same letter, e.g. "Furniture").
    /// </summary>
    private static bool HasUePrefix(string pascal, char prefix)
    {
        return pascal.Length > 1 && pascal[0] == prefix && char.IsUpper(pascal[1]);
    }

    /// <summary>
    /// Имя структуры (F-префикс): FMyModelDto
    /// </summary>
    public string StructName(string modelName)
    {
        var pascal = ToPascalCase(modelName);
        return HasUePrefix(pascal, 'F') ? pascal : $"F{pascal}";
    }

    /// <summary>
    /// Имя класса (U-префикс): UMyAsyncAction
    /// </summary>
    public string ClassName(string name)
    {
        var pascal = ToPascalCase(name);
        return HasUePrefix(pascal, 'U') ? pascal : $"U{pascal}";
    }

    /// <summary>
    /// Имя enum (E-префикс): EMyStatus
    /// </summary>
    public string EnumName(string name)
    {
        var pascal = ToPascalCase(name);
        return HasUePrefix(pascal, 'E') ? pascal : $"E{pascal}";
    }

    /// <summary>
    /// Имя свойства/поля (PascalCase): MyProperty
    /// </summary>
    public string PropertyName(string name)
    {
        return ToPascalCase(name);
    }

    /// <summary>
    /// Имя типа делегата: FOn{OperationName}Completed
    /// </summary>
    public string DelegateName(string operationName)
    {
        return $"FOn{ToPascalCase(operationName)}Completed";
    }

    /// <summary>
    /// Имя результата: F{OperationName}Result
    /// </summary>
    public string ResultStructName(string operationName)
    {
        return $"F{ToPascalCase(operationName)}Result";
    }

    /// <summary>
    /// Имя Params-структуры: F{OperationName}Params
    /// </summary>
    public string ParamsStructName(string operationName)
    {
        return $"F{ToPascalCase(operationName)}Params";
    }

    /// <summary>
    /// Имя под-структуры Params для route: F{OperationName}RouteParams
    /// </summary>
    public string RouteParamsStructName(string operationName)
    {
        return $"F{ToPascalCase(operationName)}RouteParams";
    }

    /// <summary>
    /// Имя под-структуры Params для query: F{OperationName}QueryParams
    /// </summary>
    public string QueryParamsStructName(string operationName)
    {
        return $"F{ToPascalCase(operationName)}QueryParams";
    }

    /// <summary>
    /// Имя нативной free-функции: {ClientName}_{MethodName}
    /// </summary>
    public string NativeFunctionName(string clientName, string methodName)
    {
        return $"{ToPascalCase(clientName)}_{ToPascalCase(methodName)}";
    }

    /// <summary>
    /// Имя функции Build (создание FValIORequest): {ClientName}_Build{MethodName}Request
    /// </summary>
    public string BuildRequestFunctionName(string clientName, string methodName)
    {
        return $"{ToPascalCase(clientName)}_Build{ToPascalCase(methodName)}Request";
    }

    /// <summary>
    /// Имя функции Send (отправка запроса): {ClientName}_Send{MethodName}
    /// </summary>
    public string SendFunctionName(string clientName, string methodName)
    {
        return $"{ToPascalCase(clientName)}_Send{ToPascalCase(methodName)}";
    }

    /// <summary>
    /// Имя поля тела ответа по status code: Body400, Body404, BodyDefault
    /// </summary>
    public string ResultBodyFieldName(string statusCode)
    {
        return statusCode == "default" ? "BodyDefault" : $"Body{statusCode}";
    }

    /// <summary>
    /// Имя Parse-функции: Parse{MethodName}Result
    /// </summary>
    public string ParseFunctionName(string methodName)
    {
        return $"Parse{ToPascalCase(methodName)}Result";
    }

    /// <summary>
    /// Имя async action класса: U{ClientName}{MethodName}Async
    /// </summary>
    public string AsyncActionClassName(string clientName, string methodName)
    {
        return $"U{ToPascalCase(clientName)}{ToPascalCase(methodName)}Async";
    }

    /// <summary>
    /// Имя PathsLibrary класса: U{ClientName}PathsLibrary (deprecated, use OperationLibraryClassName)
    /// </summary>
    public string PathsLibraryClassName(string clientName)
    {
        return $"U{ToPascalCase(clientName)}PathsLibrary";
    }

    /// <summary>
    /// Имя OperationLibrary класса (BlueprintFunctionLibrary для одного endpoint): U{ClientName}{MethodName}Library
    /// </summary>
    public string OperationLibraryClassName(string clientName, string methodName)
    {
        return $"U{ToPascalCase(clientName)}{ToPascalCase(methodName)}Library";
    }

    /// <summary>
    /// Имя метода построения пути: BuildPathUrl
    /// </summary>
    public string PathMethodName(string methodName)
    {
        return "BuildPathUrl";
    }

    /// <summary>
    /// Имя файла модели (без F-префикса): {ModelName}.h
    /// </summary>
    public string ModelFileName(string modelName)
    {
        return $"{ToPascalCase(modelName)}.h";
    }

    /// <summary>
    /// Полный путь к модели: Public/Models/{ModelName}.h
    /// </summary>
    public string ModelFullPath(string modelName)
    {
        return $"Public/Models/{ModelFileName(modelName)}";
    }

    /// <summary>
    /// Полный путь к enum: Public/Enums/{EnumName}.h
    /// </summary>
    public string EnumFullPath(string enumName)
    {
        return $"Public/Enums/{EnumFileName(enumName)}";
    }

    /// <summary>
    /// Путь include для модели (от папки Public): Models/{ModelName}.h
    /// </summary>
    public string ModelIncludePath(string modelName)
    {
        return $"Models/{ModelFileName(modelName)}";
    }

    /// <summary>
    /// Путь include для enum (от папки Public): Enums/{EnumName}.h
    /// </summary>
    public string EnumIncludePath(string enumName)
    {
        return $"Enums/{EnumFileName(enumName)}";
    }

    /// <summary>
    /// Путь include для модели из файла в Public/Endpoints/{OpId}/: ../../Models/{ModelName}.h
    /// </summary>
    public string ModelIncludePathFromEndpoint(string modelName)
    {
        return $"../../{ModelIncludePath(modelName)}";
    }

    /// <summary>
    /// Путь include для enum из файла в Public/Endpoints/{OpId}/: ../../Enums/{EnumName}.h
    /// </summary>
    public string EnumIncludePathFromEndpoint(string enumName)
    {
        return $"../../{EnumIncludePath(enumName)}";
    }

    /// <summary>
    /// Имя файла enum (без E-префикса): {EnumName}.h
    /// </summary>
    public string EnumFileName(string enumName)
    {
        return $"{ToPascalCase(enumName)}.h";
    }

    /// <summary>
    /// Имя файла Params (без F-префикса): {OperationName}Params.h
    /// </summary>
    public string ParamsFileName(string operationName)
    {
        return $"{ToPascalCase(operationName)}Params.h";
    }

    /// <summary>
    /// Имя файла Result (без F-префикса): {OperationName}Result.h
    /// </summary>
    public string ResultFileName(string operationName)
    {
        return $"{ToPascalCase(operationName)}Result.h";
    }

    /// <summary>
    /// Путь include для Result (в папке endpoint): {OperationName}Result.h (тот же каталог, что и OperationId.h)
    /// </summary>
    public string ResultIncludePath(string operationName)
    {
        return ResultFileName(operationName);
    }

    /// <summary>
    /// Путь include для Params (в папке endpoint): {OperationName}Params.h
    /// </summary>
    public string ParamsIncludePath(string operationName)
    {
        return ParamsFileName(operationName);
    }

    /// <summary>
    /// Полный путь к Result (от корня output): Public/Endpoints/{OperationId}/{OperationName}Result.h
    /// </summary>
    public string ResultFullPath(string operationName)
    {
        return $"Public/Endpoints/{ToPascalCase(operationName)}/{ResultFileName(operationName)}";
    }

    /// <summary>
    /// Полный путь к Params (от корня output): Public/Endpoints/{OperationId}/{OperationName}Params.h
    /// </summary>
    public string ParamsFullPath(string operationName)
    {
        return $"Public/Endpoints/{ToPascalCase(operationName)}/{ParamsFileName(operationName)}";
    }

    /// <summary>
    /// Полный путь к endpoint header: Public/Endpoints/{OperationId}/{OperationId}.h
    /// </summary>
    public string EndpointHeaderFullPath(string operationName)
    {
        return $"Public/Endpoints/{ToPascalCase(operationName)}/{EndpointHeaderFileName(operationName)}";
    }

    /// <summary>
    /// Полный путь к endpoint source: Private/Endpoints/{OperationId}.cpp
    /// </summary>
    public string EndpointSourceFullPath(string operationName)
    {
        return $"Private/Endpoints/{EndpointSourceFileName(operationName)}";
    }

    /// <summary>
    /// Путь include для endpoint header (из .cpp в Private/Endpoints): Endpoints/{OpId}/{OpId}.h
    /// Предполагается, что Public добавлен в include path.
    /// </summary>
    public string EndpointHeaderIncludePath(string operationName)
    {
        var opId = ToPascalCase(operationName);
        return $"Endpoints/{opId}/{EndpointHeaderFileName(operationName)}";
    }

    /// <summary>
    /// Имя файла endpoint header: {MethodName}.h
    /// </summary>
    public string EndpointHeaderFileName(string methodName)
    {
        return $"{ToPascalCase(methodName)}.h";
    }

    /// <summary>
    /// Имя файла реализации endpoint: {MethodName}.cpp
    /// </summary>
    public string EndpointSourceFileName(string methodName)
    {
        return $"{ToPascalCase(methodName)}.cpp";
    }

    /// <summary>
    /// Имя заголовочного файла Paths: {ClientName}Paths.h
    /// </summary>
    public string PathsHeaderFileName(string clientName)
    {
        return $"{ToPascalCase(clientName)}Paths.h";
    }

    /// <summary>
    /// Имя файла реализации Paths: {ClientName}Paths.cpp
    /// </summary>
    public string PathsSourceFileName(string clientName)
    {
        return $"{ToPascalCase(clientName)}Paths.cpp";
    }

    /// <summary>
    /// Имя заголовочного файла API: {ClientName}Api.h
    /// </summary>
    public string ApiHeaderFileName(string clientName)
    {
        return $"{ToPascalCase(clientName)}Api.h";
    }

    /// <summary>
    /// Имя файла реализации API: {ClientName}Api.cpp
    /// </summary>
    public string ApiSourceFileName(string clientName)
    {
        return $"{ToPascalCase(clientName)}Api.cpp";
    }

    /// <summary>
    /// Category для Models: BaseCategory|Models
    /// </summary>
    public static string ModelCategory(string baseCategory) =>
        $"{baseCategory}|Models";

    /// <summary>
    /// Category для Enums: BaseCategory|Enums
    /// </summary>
    public static string EnumCategory(string baseCategory) =>
        $"{baseCategory}|Enums";

    /// <summary>
    /// Category для Operation-специфичных типов (Result, Params, AsyncAction): BaseCategory|ClientName|OperationId
    /// </summary>
    public static string OperationCategory(string baseCategory, string clientName, string operationId) =>
        $"{baseCategory}|{ToPascalCase(clientName)}|{ToPascalCase(operationId)}";

    /// <summary>
    /// Category для Paths Library: BaseCategory|ClientName|Paths
    /// </summary>
    public static string PathsCategory(string baseCategory, string clientName) =>
        $"{baseCategory}|{ToPascalCase(clientName)}|Paths";

    /// <summary>
    /// Формирует Category для типа модели: BlueprintCategory|Models
    /// </summary>
    public string ModelTypeCategory(string blueprintCategory) =>
        $"{blueprintCategory}|Models";

    /// <summary>
    /// Формирует Category для enum: BlueprintCategory|Enums
    /// </summary>
    public string EnumTypeCategory(string blueprintCategory) =>
        $"{blueprintCategory}|Enums";

    /// <summary>
    /// Формирует Category для operation-специфичных типов (Result, Params, AsyncAction): BlueprintCategory|ClientName|OperationId
    /// </summary>
    public string OperationTypeCategory(string blueprintCategory, string clientName, string operationId) =>
        $"{blueprintCategory}|{ToPascalCase(clientName)}|{ToPascalCase(operationId)}";

    /// <summary>
    /// Формирует Category для Paths Library: BlueprintCategory|ClientName|Paths
    /// </summary>
    public string PathsTypeCategory(string blueprintCategory, string clientName) =>
        $"{blueprintCategory}|{ToPascalCase(clientName)}|Paths";

    /// <summary>
    /// Конвертирует строку в PascalCase.
    /// Обрабатывает camelCase, snake_case, kebab-case.
    /// </summary>
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Разбиваем по разделителям (_, -, пробел) и границам camelCase
        var words = Regex.Split(input, @"[-_\s]+|(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])")
            .Where(w => !string.IsNullOrEmpty(w));

        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                    sb.Append(word[1..]);
            }
        }

        return sb.ToString();
    }
}
