using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;

namespace ValIO.Core.IR;

/// <summary>
/// IR представление HTTP операции
/// </summary>
public sealed record IrOperation
{
    /// <summary>
    /// Имя метода (sanitized, с учётом affix-преобразования, если оно применялось)
    /// </summary>
    public required string MethodName { get; init; }

    private readonly string? _originalMethodName;

    /// <summary>
    /// Исходное (до affix) имя метода. Используется для имён файлов, путей импорта и
    /// производных типов (result/params), которые не должны менять базу при добавлении
    /// method-prefix/suffix. По умолчанию равно <see cref="MethodName"/>, если affix не применялся.
    /// </summary>
    public string OriginalMethodName
    {
        get => _originalMethodName ?? MethodName;
        init => _originalMethodName = value;
    }

    /// <summary>
    /// HTTP метод (GET, POST, PUT, DELETE и т.д.)
    /// </summary>
    public required string HttpMethod { get; init; }
    
    /// <summary>
    /// Путь операции (например, "/api/users/{id}")
    /// </summary>
    public required string Path { get; init; }
    
    /// <summary>
    /// Краткое описание операции
    /// </summary>
    public string? Summary { get; init; }
    
    /// <summary>
    /// Подробное описание операции
    /// </summary>
    public string? Description { get; init; }
    
    /// <summary>
    /// Path параметры
    /// </summary>
    public required IReadOnlyList<IrParameter> PathParameters { get; init; }
    
    /// <summary>
    /// Query параметры
    /// </summary>
    public required IReadOnlyList<IrParameter> QueryParameters { get; init; }
    
    /// <summary>
    /// Header параметры
    /// </summary>
    public required IReadOnlyList<IrParameter> HeaderParameters { get; init; }
    
    /// <summary>
    /// Request body
    /// </summary>
    public IrRequestBody? RequestBody { get; init; }
    
    /// <summary>
    /// Ответы операции (включая ошибки)
    /// </summary>
    public required IReadOnlyList<IrResponse> Responses { get; init; }
    
    /// <summary>
    /// Теги операции (для группировки)
    /// </summary>
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// Represents an operation parameter, preserving source identity, validation, and HTTP serialization metadata.
/// </summary>
public sealed record IrParameter
{
    /// <summary>
    /// Gets the sanitized identifier used by generated source declarations.
    /// </summary>
    /// <value>The language-neutral parameter identifier.</value>
    public required string Name { get; init; }

    /// <summary>Stores the original wire name when identifier sanitization changes it.</summary>
    private readonly string? _originalName;

    /// <summary>
    /// Gets the original HTTP query, path, or header name before source identifier sanitization.
    /// </summary>
    /// <value>The original wire name, falling back to <see cref="Name"/> for manually constructed IR.</value>
    public string OriginalName
    {
        get => _originalName ?? Name;
        init => _originalName = value;
    }

    /// <summary>
    /// Gets the language-independent type of the parameter value.
    /// </summary>
    /// <value>The parameter type, including its collection and nullability semantics.</value>
    public required IrType Type { get; init; }
    
    /// <summary>
    /// Gets whether the HTTP operation requires the parameter to be present.
    /// </summary>
    /// <value>Whether omission violates the source contract.</value>
    public required bool IsRequired { get; init; }

    /// <summary>
    /// Gets the parameter description, falling back to its schema description when necessary.
    /// </summary>
    /// <value>The source documentation, or <see langword="null"/> when none is provided.</value>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the source schema default used by generated validators or optional parameter handling.
    /// </summary>
    /// <value>The decoded default value, or <see langword="null"/> when absent.</value>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// Validation constraints from the parameter's schema (string lengths, numeric ranges, regex pattern), or
    /// <see langword="null"/> when the schema declares none. Emitted as an exported constants object in the
    /// generated params interface file and referenced by the params schema — mirroring how model-property
    /// constraints (<see cref="IrProperty.Constraints"/>) are handled.
    /// </summary>
    /// <value>The extracted constraints, or <see langword="null"/> when the parameter has none.</value>
    /// <seealso cref="IrPropertyConstraints"/>
    public IrPropertyConstraints? Constraints { get; init; }

    /// <summary>Gets the normalized HTTP parameter serialization style supplied by the source reader.</summary>
    /// <remarks>
    /// The OpenAPI builder always sets this value. For manually constructed legacy IR, generators resolve
    /// an absent value from the parameter location: form for query parameters and simple for path or header parameters.
    /// </remarks>
    /// <value>The serialization style, or <see langword="null"/> when no metadata was supplied.</value>
    public IrParameterStyle? Style { get; init; }

    /// <summary>Gets whether array elements or object properties expand into separate parameter entries.</summary>
    /// <remarks>
    /// The OpenAPI builder preserves the reader's effective value, including source defaults.
    /// For manually constructed legacy IR, the default is true for form or cookie style and false for other styles.
    /// </remarks>
    /// <value>The normalized explode flag, or <see langword="null"/> when no metadata was supplied.</value>
    public bool? Explode { get; init; }
}

/// <summary>
/// IR представление request body
/// </summary>
public sealed record IrRequestBody
{
    /// <summary>
    /// Тип request body
    /// </summary>
    public required IrType Type { get; init; }
    
    /// <summary>
    /// Является ли request body обязательным
    /// </summary>
    public required bool IsRequired { get; init; }
    
    /// <summary>
    /// Content type (application/json, application/x-www-form-urlencoded, multipart/form-data)
    /// </summary>
    public string? ContentType { get; init; }
    
    /// <summary>
    /// Описание
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// True когда content type — application/json или null.
    /// </summary>
    public bool IsJson => ContentType is null or "application/json";

    /// <summary>
    /// True когда content type — multipart/form-data или application/x-www-form-urlencoded.
    /// </summary>
    public bool IsForm => ContentType is "multipart/form-data" or "application/x-www-form-urlencoded";

    /// <summary>
    /// True когда content type — multipart/form-data.
    /// </summary>
    public bool IsMultipartFormData => ContentType == "multipart/form-data";

    /// <summary>
    /// True когда content type — application/x-www-form-urlencoded.
    /// </summary>
    public bool IsFormUrlEncoded => ContentType == "application/x-www-form-urlencoded";
}

/// <summary>
/// IR представление ответа операции
/// </summary>
public sealed record IrResponse
{
    /// <summary>
    /// HTTP статус код (200, 400, 404, "default" и т.д.)
    /// </summary>
    public required string StatusCode { get; init; }
    
    /// <summary>
    /// Тип ответа (если есть)
    /// </summary>
    public IrType? Type { get; init; }
    
    /// <summary>
    /// Описание
    /// </summary>
    public string? Description { get; init; }
}
