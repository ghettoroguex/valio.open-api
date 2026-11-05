using System.Collections.Immutable;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using ValIO.Core.Interfaces;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;

namespace ValIO.Core.IR.Builders;

/// <summary>
/// Построитель Language-agnostic IR из OpenAPI документа
/// </summary>
public class IrBuilder
{
    private readonly ISchemaResolver _schemaResolver;
    private readonly INamingConverter _namingConverter;
    
    // Словарь для хранения созданных inline моделей
    private readonly Dictionary<string, IrModel> _inlineModels = new();
    private int _inlineModelCounter = 0;

    // Диагностический контекст текущей точки построения типа (endpoint / роль / путь свойства).
    // Используется только для информативных сообщений об ошибках в BuildType.
    private SchemaBuildContext _ctx = new();
    
    public IrBuilder(ISchemaResolver schemaResolver, INamingConverter namingConverter)
    {
        _schemaResolver = schemaResolver ?? throw new ArgumentNullException(nameof(schemaResolver));
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
    }
    
    /// <summary>
    /// Строит IR из OpenAPI документа
    /// </summary>
    /// <param name="document">OpenAPI документ</param>
    /// <param name="warnings">Опциональная коллекция для записи предупреждений (дубликаты параметров и т.д.)</param>
    /// <param name="affixOptions">
    /// Опциональные prefix/suffix для имён моделей/методов/enum'ов. Применяются на уровне IR
    /// (см. <see cref="IrNameAffixer"/>) до построения графа зависимостей, поэтому распространяются
    /// на все определения, ссылки и импорты во всех языках. По умолчанию (null или <see cref="NameAffixOptions.IsNoOp"/>)
    /// преобразование пропускается и поведение идентично прежнему.
    /// </param>
    public IrDocument BuildFromOpenApi(
        OpenApiDocument document,
        ICollection<string>? warnings = null,
        NameAffixOptions? affixOptions = null)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        // Очищаем словарь inline моделей перед началом
        _inlineModels.Clear();
        _inlineModelCounter = 0;

        var models = ExtractModels(document);
        var enums = ExtractEnums(document);
        var clients = ExtractClients(document, warnings);

        // Добавляем inline модели к основным моделям
        IReadOnlyList<IrModel> allModels = models.Concat(_inlineModels.Values).ToList();
        IReadOnlyList<IrEnum> allEnums = enums;
        IReadOnlyList<IrClient> allClients = clients;

        // Применяем affix (prefix/suffix) к именам ДО построения графа зависимостей,
        // чтобы граф и все ссылки оперировали уже переименованными именами.
        if (affixOptions is { IsNoOp: false })
            (allModels, allEnums, allClients) =
                IrNameAffixer.Apply(allModels, allEnums, allClients, affixOptions, _namingConverter);

        var typeDependencies = BuildTypeDependencyGraph(allModels, allEnums, allClients);

        return new IrDocument
        {
            Metadata = ExtractMetadata(document),
            Models = allModels,
            Enums = allEnums,
            Clients = allClients,
            TypeDependencies = typeDependencies
        };
    }
    
    /// <summary>
    /// Извлекает метаданные из документа
    /// </summary>
    private IrDocumentMetadata ExtractMetadata(OpenApiDocument document)
    {
        return new IrDocumentMetadata
        {
            Title = document.Info?.Title,
            Version = document.Info?.Version,
            Description = document.Info?.Description,
            ServerUrl = document.Servers?.FirstOrDefault()?.Url
        };
    }
    
    /// <summary>
    /// Извлекает модели из компонентов
    /// </summary>
    private IReadOnlyList<IrModel> ExtractModels(OpenApiDocument document)
    {
        var models = new List<IrModel>();
        
        if (document.Components?.Schemas == null)
            return models;
        
        foreach (var (schemaName, schema) in document.Components.Schemas)
        {
            // Пропускаем enum схемы (они будут обработаны в ExtractEnums)
            // Enum должен иметь непустой список значений и тип string или integer, без Properties
            if (schema.Enum != null && schema.Enum.Count > 0)
            {
                if (schema.Type.HasValue)
                {
                    var type = schema.Type.Value;
                    var isStringEnum = type.HasFlag(JsonSchemaType.String) && !type.HasFlag(JsonSchemaType.Integer) && !type.HasFlag(JsonSchemaType.Number);
                    var isIntegerEnum = type.HasFlag(JsonSchemaType.Integer) && !type.HasFlag(JsonSchemaType.String) && !type.HasFlag(JsonSchemaType.Number);
                    
                    // Если это enum (string или integer) и нет Properties - пропускаем
                    if ((isStringEnum || isIntegerEnum) && (schema.Properties == null || schema.Properties.Count == 0))
                        continue;
                }
            }
            
            var sanitizedName = _namingConverter.SanitizeIdentifier(schemaName);
            var model = WithContext(
                new SchemaBuildContext { Role = SchemaRole.Model, RootModelName = sanitizedName },
                () => BuildModel(sanitizedName, schema, document));
            models.Add(model);
        }
        
        return models;
    }
    
    /// <summary>
    /// Строит модель из схемы
    /// </summary>
    private IrModel BuildModel(string name, IOpenApiSchema schema, OpenApiDocument document, string? parentModelName = null, PropertyContext? propertyContext = null)
    {
        var resolvedSchema = _schemaResolver.ResolveSchema(schema);
        
        // Для allOf берем только свойства из inline-схем (не из базового класса через $ref)
        // Свойства базового класса наследуются через базовый класс в C#
        var properties = new List<IrProperty>();
        if (resolvedSchema.AllOf != null && resolvedSchema.AllOf.Count > 0)
        {
            // Берем свойства только из inline-схем (без $ref), которые определяют собственные свойства дочернего класса
            foreach (var subSchema in resolvedSchema.AllOf)
            {
                var referencedSchemaName = GetSchemaNameRaw(subSchema);
                bool isBaseClass = !string.IsNullOrEmpty(referencedSchemaName);

                if (!isBaseClass)
                {
                    // Это inline-схема с собственными свойствами дочернего класса
                    var resolvedSubSchema = _schemaResolver.ResolveSchema(subSchema);
                    var subProperties = ExtractProperties(resolvedSubSchema, document, parentModelName);
                    properties.AddRange(subProperties);
                }
            }

            // Также добавляем свойства, определенные напрямую в схеме после allOf
            if (resolvedSchema.Properties != null && resolvedSchema.Properties.Count > 0)
            {
                var ownProperties = ExtractProperties(resolvedSchema, document, parentModelName);
                properties.AddRange(ownProperties);
            }
        }
        else
        {
            // Обычная схема без allOf
            properties = ExtractProperties(resolvedSchema, document, parentModelName).ToList();
        }
        
        var composition = ExtractComposition(schema, document, propertyContext);
        
        return new IrModel
        {
            Name = name,
            Description = schema.Description,
            Properties = properties,
            Composition = composition,
            OriginalSchema = schema
        };
    }
    
    /// <summary>
    /// Извлекает свойства из схемы
    /// </summary>
    private IReadOnlyList<IrProperty> ExtractProperties(IOpenApiSchema schema, OpenApiDocument document, string? parentModelName = null)
    {
        var properties = new List<IrProperty>();
        
        if (schema.Properties == null)
            return properties;
        
        foreach (var (propertyName, propertySchema) in schema.Properties)
        {
            var sanitizedName = _namingConverter.SanitizeIdentifier(propertyName);
            var safeName = _namingConverter.SanitizeReservedKeyword(sanitizedName);
            var propertyType = WithContext(
                _ctx with { PropertyPath = AppendPath(_ctx.PropertyPath, propertyName) },
                () => BuildType(propertySchema, document, propertyContext: new PropertyContext
                {
                    PropertyName = propertyName,
                    ParentModelName = parentModelName
                }));
            var isRequired = schema.Required?.Contains(propertyName) ?? false;
            var constraints = ExtractConstraints(propertySchema);
            
            properties.Add(new IrProperty
            {
                OriginalName = propertyName,
                Name = safeName,
                Type = propertyType,
                IsRequired = isRequired,
                Description = propertySchema.Description,
                Constraints = constraints
            });
        }
        
        return properties;
    }
    
    /// <summary>
    /// Контекст для генерации имен inline моделей
    /// </summary>
    private sealed class PropertyContext
    {
        public string? PropertyName { get; init; }
        public string? ParentModelName { get; init; }
        public bool IsForm { get; init; }
        public string? OperationId { get; init; }
    }

    /// <summary>Роль схемы в документе — для диагностических сообщений об ошибках построения IR.</summary>
    private enum SchemaRole { None, Model, PathParameter, QueryParameter, HeaderParameter, RequestBody, Response }

    /// <summary>
    /// Иммутабельный диагностический контекст: endpoint (метод + путь), операция, роль
    /// (path/query/header-параметр · тело запроса · ответ[код]), модель и путь свойства.
    /// Используется только для информативных сообщений об ошибках построения типа
    /// (см. <see cref="BuildType"/>). Сохраняется/восстанавливается через <see cref="WithContext{T}"/>.
    /// </summary>
    private sealed record SchemaBuildContext
    {
        public string? HttpMethod { get; init; }
        public string? Path { get; init; }
        public string? OperationId { get; init; }
        public SchemaRole Role { get; init; }
        public string? StatusCode { get; init; }
        public string? ContentType { get; init; }
        public string? ParameterName { get; init; }
        public string? RootModelName { get; init; }
        public IReadOnlyList<string> PropertyPath { get; init; } = Array.Empty<string>();

        /// <summary>Человекочитаемое описание места, где строится тип — например
        /// «response 200 (application/json) of operation 'getApartments' [GET /api/apartments], property 'result.data[].error'».</summary>
        public string Describe()
        {
            var parts = new List<string>(2);

            switch (Role)
            {
                case SchemaRole.Response:
                    var resp = "response";
                    if (!string.IsNullOrEmpty(StatusCode)) resp += $" {StatusCode}";
                    if (!string.IsNullOrEmpty(ContentType)) resp += $" ({ContentType})";
                    parts.Add(resp);
                    break;
                case SchemaRole.RequestBody:
                    parts.Add(string.IsNullOrEmpty(ContentType) ? "request body" : $"request body ({ContentType})");
                    break;
                case SchemaRole.PathParameter:
                    parts.Add($"path parameter '{ParameterName}'");
                    break;
                case SchemaRole.QueryParameter:
                    parts.Add($"query parameter '{ParameterName}'");
                    break;
                case SchemaRole.HeaderParameter:
                    parts.Add($"header parameter '{ParameterName}'");
                    break;
                case SchemaRole.Model:
                    if (!string.IsNullOrEmpty(RootModelName)) parts.Add($"model '{RootModelName}'");
                    break;
            }

            if (!string.IsNullOrEmpty(OperationId))
            {
                var op = $"operation '{OperationId}'";
                if (!string.IsNullOrEmpty(HttpMethod) && !string.IsNullOrEmpty(Path))
                    op += $" [{HttpMethod} {Path}]";
                parts.Add(op);
            }
            else if (!string.IsNullOrEmpty(HttpMethod) && !string.IsNullOrEmpty(Path))
            {
                parts.Add($"[{HttpMethod} {Path}]");
            }

            var location = string.Join(" of ", parts);

            if (PropertyPath.Count > 0)
            {
                var prop = $"property '{string.Join(".", PropertyPath)}'";
                location = string.IsNullOrEmpty(location) ? prop : $"{location}, {prop}";
            }

            return string.IsNullOrEmpty(location) ? "<unknown location>" : location;
        }
    }

    /// <summary>Выполняет <paramref name="body"/> с временно установленным диагностическим контекстом.</summary>
    private T WithContext<T>(SchemaBuildContext ctx, Func<T> body)
    {
        var previous = _ctx;
        _ctx = ctx;
        try { return body(); }
        finally { _ctx = previous; }
    }

    private static IReadOnlyList<string> AppendPath(IReadOnlyList<string> path, string segment)
    {
        var list = new List<string>(path.Count + 1);
        list.AddRange(path);
        list.Add(segment);
        return list;
    }

    private static IReadOnlyList<string> SuffixLastSegment(IReadOnlyList<string> path, string suffix)
    {
        if (path.Count == 0)
            return path;
        var list = new List<string>(path);
        list[^1] += suffix;
        return list;
    }
    
    /// <summary>Extracts property constraints using culture-independent OpenAPI number syntax.</summary>
    /// <param name="schema">The property schema containing the constraints.</param>
    /// <returns>The extracted constraints, or null when none are present.</returns>
    private IrPropertyConstraints? ExtractConstraints(IOpenApiSchema schema)
    {
        var hasConstraints = !string.IsNullOrEmpty(schema.Minimum) ||
                            !string.IsNullOrEmpty(schema.Maximum) ||
                            !string.IsNullOrEmpty(schema.ExclusiveMinimum) ||
                            !string.IsNullOrEmpty(schema.ExclusiveMaximum) ||
                            schema.MinLength.HasValue ||
                            schema.MaxLength.HasValue ||
                            !string.IsNullOrEmpty(schema.Pattern) ||
                            schema.Default != null;
        
        if (!hasConstraints)
            return null;
        
        decimal? min = null;
        decimal? max = null;
        if (decimal.TryParse(schema.Minimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMin))
            min = parsedMin;
        if (decimal.TryParse(schema.Maximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedMax))
            max = parsedMax;
        
        decimal? exclusiveMin = null;
        decimal? exclusiveMax = null;
        if (decimal.TryParse(schema.ExclusiveMinimum, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedExclusiveMin))
            exclusiveMin = parsedExclusiveMin;
        if (decimal.TryParse(schema.ExclusiveMaximum, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedExclusiveMax))
            exclusiveMax = parsedExclusiveMax;
        
        return new IrPropertyConstraints
        {
            Min = min,
            Max = max,
            ExclusiveMin = exclusiveMin,
            ExclusiveMax = exclusiveMax,
            MinLength = schema.MinLength,
            MaxLength = schema.MaxLength,
            Pattern = schema.Pattern,
            DefaultValue = ExtractDefaultValue(schema.Default)
        };
    }
    
    /// <summary>
    /// Извлекает значение по умолчанию
    /// </summary>
    private object? ExtractDefaultValue(JsonNode? defaultValue)
    {
        if (defaultValue == null)
            return null;
        
        // Извлекаем значение из JsonNode
        return defaultValue switch
        {
            JsonValue jsonValue => jsonValue.GetValue<object>(),
            JsonObject jsonObject => jsonObject,
            JsonArray jsonArray => jsonArray,
            _ => defaultValue.ToString()
        };
    }
    
    /// <summary>
    /// Извлекает композицию из схемы
    /// </summary>
    private IrComposition? ExtractComposition(IOpenApiSchema schema, OpenApiDocument document, PropertyContext? propertyContext = null)
    {
        if (schema.AllOf?.Count > 0)
        {
            // Для allOf композиция модели описывает НАСЛЕДОВАНИЕ: в Types кладём только
            // $ref-базовые классы. Свойства inline-членов уже извлечены в Properties
            // (см. BuildModel), поэтому inline-члены сюда не попадают — это убирает
            // дублирование свойств и исключает появление «осиротевших» inline-моделей.
            var baseRefs = schema.AllOf
                .Where(s => !string.IsNullOrEmpty(GetSchemaNameRaw(s)))
                .Select(s => BuildType(s, document, propertyContext))
                .OfType<IrModelReference>()
                .DistinctBy(r => r.ModelName)
                .ToList<IrType>();

            if (baseRefs.Count == 0)
                return null;

            return new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = baseRefs
            };
        }
        
        if (schema.OneOf?.Count > 0)
        {
            var types = schema.OneOf.Select(s => BuildType(s, document, propertyContext)).ToList();
            return new IrComposition
            {
                Kind = IrCompositionKind.OneOf,
                Types = types,
                DiscriminatorPropertyName = schema.Discriminator?.PropertyName,
                // Значение mapping — это $ref на схему-вариант. Берём id ссылки
                // (kvp.Value.Reference.Id, напр. "Dog"), а не Target?.Id (который пуст
                // для безымянных схем), и санитизируем под имена IR-моделей.
                DiscriminatorMapping = schema.Discriminator?.Mapping?.ToDictionary(
                    kvp => kvp.Key,
                    kvp => _namingConverter.SanitizeIdentifier(kvp.Value?.Reference?.Id ?? string.Empty)
                )
            };
        }
        
        if (schema.AnyOf?.Count > 0)
        {
            var types = schema.AnyOf.Select(s => BuildType(s, document, propertyContext)).ToList();
            return new IrComposition
            {
                Kind = IrCompositionKind.AnyOf,
                Types = types
            };
        }
        
        return null;
    }
    
    /// <summary>Recursively builds an IR type while preserving JSON nullability.</summary>
    /// <param name="schema">The OpenAPI schema to translate.</param>
    /// <param name="document">The document containing referenced schemas.</param>
    /// <param name="propertyContext">The containing property context, when present.</param>
    /// <returns>The translated IR type.</returns>
    /// <exception cref="ArgumentNullException">The schema or document is null.</exception>
    /// <exception cref="InvalidOperationException">The schema cannot be resolved or its type is unsupported.</exception>
    private IrType BuildType(IOpenApiSchema schema, OpenApiDocument document, PropertyContext? propertyContext = null)
    {
        if (schema == null)
            throw new ArgumentNullException(nameof(schema));
        if (document == null)
            throw new ArgumentNullException(nameof(document));
        
        // СНАЧАЛА проверяем ссылки ДО разрешения (после разрешения информация о ссылке теряется)
        if (schema is IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> referenceHolder)
        {
            var reference = referenceHolder.Reference;
            if (reference != null && !string.IsNullOrEmpty(reference.Id))
            {
                var modelName = _namingConverter.SanitizeIdentifier(reference.Id);
                return new IrModelReference { ModelName = modelName };
            }
        }
        
        // Разрешаем ссылки
        var resolvedSchema = _schemaResolver.ResolveSchema(schema);
        
        if (resolvedSchema == null)
            throw new InvalidOperationException($"Schema resolver returned null schema — at {_ctx.Describe()}");

        /// <summary>Applies only this schema's JSON-null allowance to a constructed type.</summary>
        /// <param name="core">The non-null constructed type.</param>
        /// <returns>The original type or an idempotent nullable wrapper.</returns>
        IrType PreserveNullability(IrType core) => SchemaAllowsJsonNull(resolvedSchema)
            ? IrCompositionNormalizer.WrapNullable(core) : core;
        
        // OneOf, AnyOf -> Union (проверяем до проверки Type, так как композиция может не иметь Type).
        // Нормализуем: чистые null-ветки ({type:null}) превращаем в nullable-обёртку, а не в член union;
        // одиночный/пустой union схлопывается (см. IrCompositionNormalizer).
        if (resolvedSchema.OneOf?.Count > 0 || resolvedSchema.AnyOf?.Count > 0)
        {
            var variants = (resolvedSchema.OneOf ?? resolvedSchema.AnyOf)!;
            var nullable = SchemaAllowsJsonNull(resolvedSchema);
            var members = new List<IrType>(variants.Count);
            foreach (var variant in variants)
            {
                if (IsNullOnlySchema(variant))
                {
                    nullable = true;
                    continue;
                }

                members.Add(BuildType(variant, document, propertyContext));
            }

            return IrCompositionNormalizer.NormalizeUnion(members, nullable);
        }

        // AllOf -> Intersection (проверяем до проверки Type). Нормализуем: single-element allOf
        // (паттерн Swashbuckle UseAllOfToExtendReferenceSchemas: {allOf:[{$ref}],nullable:true})
        // схлопывается в сам тип, sibling nullable восстанавливается как IrNullableType.
        if (resolvedSchema.AllOf?.Count > 0)
        {
            var members = resolvedSchema.AllOf.Select(s => BuildType(s, document, propertyContext)).ToList();
            var nullable = SchemaAllowsJsonNull(resolvedSchema);
            return IrCompositionNormalizer.NormalizeIntersection(members, nullable);
        }
        
        // Если Type не указан, но есть Properties - это объект
        if (!resolvedSchema.Type.HasValue)
        {
            // Если есть Properties, это объект с полями
            if (resolvedSchema.Properties != null && resolvedSchema.Properties.Count > 0)
            {
                // Проверяем, является ли это словарем (есть AdditionalProperties и это не false)
                // В OpenAPI AdditionalProperties может быть IOpenApiAny (boolean) или IOpenApiSchema
                if (resolvedSchema.AdditionalProperties != null)
                {
                    // Если это IOpenApiSchema, это словарь
                    if (resolvedSchema.AdditionalProperties is IOpenApiSchema schemaValue)
                    {
                        var keyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
                        var valueType = BuildType(schemaValue, document, propertyContext);
                        return PreserveNullability(new IrDictionaryType { KeyType = keyType, ValueType = valueType });
                    }
                    
                    // Если это не IOpenApiSchema, это может быть boolean false
                    // В этом случае это объект с фиксированными свойствами, не словарь
                    // Продолжаем обработку как inline объект ниже
                }
                
                // Это inline объект с фиксированными свойствами
                // Создаем inline модель с правильным именем на основе контекста
                var inlineModelName = GenerateInlineModelNameFromContext(propertyContext);
                var inlineModel = BuildModel(inlineModelName, resolvedSchema, document, propertyContext?.ParentModelName, propertyContext);
                _inlineModels[inlineModelName] = inlineModel;
                return PreserveNullability(new IrModelReference { ModelName = inlineModelName });
            }
            
            // Если есть AdditionalProperties, это словарь
            if (resolvedSchema.AdditionalProperties != null)
            {
                var keyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
                var valueType = BuildType(resolvedSchema.AdditionalProperties, document, propertyContext);
                return PreserveNullability(new IrDictionaryType { KeyType = keyType, ValueType = valueType });
            }
            
            // Если ничего нет, возвращаем any
            return new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Any };
        }
        
        // Составной JSON Schema type: null + несколько примитивов / null + один примитив
        var compositePrimitive = TryBuildCompositePrimitiveType(resolvedSchema);
        if (compositePrimitive != null)
            return compositePrimitive;
        
        // Один примитив без null в type (nullable обрабатывается в TryBuildCompositePrimitiveType)
        if (IsSingleNonNullPrimitive(resolvedSchema))
        {
            var core = resolvedSchema.Type!.Value & ~JsonSchemaType.Null;
            return new IrPrimitiveType
            {
                PrimitiveKind = MapPrimitive(core, resolvedSchema.Format)
            };
        }

        // OpenAPI 3.1 «только null» ({"type":"null"}) как самостоятельный тип — например конверт
        // success/error, где свойство `error` всегда null. Внутри oneOf/anyOf такая схема уже
        // схлопывается в nullable выше (IsNullOnlySchema); здесь обрабатывается standalone-случай.
        if (resolvedSchema.Type.Value == JsonSchemaType.Null)
            return new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Null };

        // Массив
        if (resolvedSchema.Type.Value.HasFlag(JsonSchemaType.Array))
        {
            if (resolvedSchema.Items == null)
                throw new InvalidOperationException(
                    $"Array schema must have Items property — at {_ctx.Describe()}");

            var items = resolvedSchema.Items!;
            var elementType = WithContext(
                _ctx with { PropertyPath = SuffixLastSegment(_ctx.PropertyPath, "[]") },
                () => BuildType(items, document, propertyContext));
            return PreserveNullability(new IrArrayType { ElementType = elementType });
        }
        
        // Object - проверяем, является ли это словарем или объектом с фиксированными свойствами
        if (resolvedSchema.Type.Value.HasFlag(JsonSchemaType.Object))
        {
            // Словарь: object с AdditionalProperties (не false) - это словарь
            // additionalProperties: false означает, что это НЕ словарь, а объект с фиксированными свойствами
            if (resolvedSchema.AdditionalProperties != null)
            {
                // Если это IOpenApiSchema, это словарь
                if (resolvedSchema.AdditionalProperties is IOpenApiSchema schemaValue)
                {
                    var keyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
                    var valueType = BuildType(schemaValue, document);
                    return PreserveNullability(new IrDictionaryType { KeyType = keyType, ValueType = valueType });
                }
                
                // Если это не IOpenApiSchema, это может быть boolean false
                // В этом случае это объект с фиксированными свойствами, не словарь
                // Продолжаем обработку ниже
            }
            
            // Если есть Properties, это объект с фиксированными полями
            // Для inline объектов (не в components/schemas) создаем отдельную модель
            if (resolvedSchema.Properties != null && resolvedSchema.Properties.Count > 0)
            {
                // Это inline объект - создаем для него модель с правильным именем на основе контекста
                var inlineModelName = GenerateInlineModelNameFromContext(propertyContext);
                var inlineModel = BuildModel(inlineModelName, resolvedSchema, document, propertyContext?.ParentModelName, propertyContext);
                _inlineModels[inlineModelName] = inlineModel;
                return PreserveNullability(new IrModelReference { ModelName = inlineModelName });
            }
            
            // Пустой объект без Properties и AdditionalProperties
            var emptyValueType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Any };
            var emptyKeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
            return PreserveNullability(new IrDictionaryType { KeyType = emptyKeyType, ValueType = emptyValueType });
        }
        
        var typeStr = resolvedSchema.Type?.ToString() ?? "null";
        throw new InvalidOperationException($"Unknown schema type: {typeStr} — at {_ctx.Describe()}");
    }
    
    /// <summary>
    /// Схема допускает JSON null: type содержит Null или legacy nullable: true.
    /// </summary>
    private static bool SchemaAllowsJsonNull(IOpenApiSchema schema)
    {
        if (schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null))
            return true;
        
        if (schema.Extensions != null &&
            schema.Extensions.TryGetValue(OpenApiConstants.NullableExtension, out var nullableExtension) &&
            nullableExtension is JsonNodeExtension { Node: { } jsonNode } &&
            jsonNode.GetValueKind() == JsonValueKind.True)
            return true;

        return false;
    }

    /// <summary>
    /// Схема представляет «только null» (OpenAPI 3.1 <c>{"type":"null"}</c>) — без других
    /// примитивов, объекта/массива, композиции, $ref или enum. Такая ветка в oneOf/anyOf
    /// означает nullable, а не отдельный член union.
    /// </summary>
    private static bool IsNullOnlySchema(IOpenApiSchema schema)
    {
        if (!schema.Type.HasValue || schema.Type.Value != JsonSchemaType.Null)
            return false;

        if (schema is IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> { Reference.Id.Length: > 0 })
            return false;

        if (schema.AllOf?.Count > 0 || schema.OneOf?.Count > 0 || schema.AnyOf?.Count > 0)
            return false;

        if (schema.Properties?.Count > 0 || (schema.Enum?.Count > 0))
            return false;

        return true;
    }
    
    /// <summary>
    /// Ровно один примитивный вид (без Null в флаге), без Object/Array.
    /// </summary>
    private static bool IsSingleNonNullPrimitive(IOpenApiSchema schema)
    {
        if (!schema.Type.HasValue)
            return false;
        
        var core = schema.Type.Value & ~JsonSchemaType.Null;
        if (core == default)
            return false;
        if (core.HasFlag(JsonSchemaType.Array) || core.HasFlag(JsonSchemaType.Object))
            return false;
        
        var primitiveMask = JsonSchemaType.String | JsonSchemaType.Number | JsonSchemaType.Integer | JsonSchemaType.Boolean;
        var prim = core & primitiveMask;
        if (prim == default)
            return false;
        
        return prim == JsonSchemaType.String ||
               prim == JsonSchemaType.Number ||
               prim == JsonSchemaType.Integer ||
               prim == JsonSchemaType.Boolean;
    }
    
    /// <summary>
    /// Несколько примитивных типов в одном type, и/или null + примитив(ы).
    /// </summary>
    private IrType? TryBuildCompositePrimitiveType(IOpenApiSchema schema)
    {
        if (!schema.Type.HasValue)
            return null;
        
        var full = schema.Type.Value;
        var core = full & ~JsonSchemaType.Null;
        
        if (core.HasFlag(JsonSchemaType.Array) || core.HasFlag(JsonSchemaType.Object))
            return null;
        
        var primitiveMask = JsonSchemaType.String | JsonSchemaType.Number | JsonSchemaType.Integer | JsonSchemaType.Boolean;
        var prim = core & primitiveMask;
        if (prim == default)
            return null;
        
        var hasNull = SchemaAllowsJsonNull(schema);
        
        var flags = new List<JsonSchemaType>(4);
        if (prim.HasFlag(JsonSchemaType.Integer))
            flags.Add(JsonSchemaType.Integer);
        if (prim.HasFlag(JsonSchemaType.Number))
            flags.Add(JsonSchemaType.Number);
        if (prim.HasFlag(JsonSchemaType.String))
            flags.Add(JsonSchemaType.String);
        if (prim.HasFlag(JsonSchemaType.Boolean))
            flags.Add(JsonSchemaType.Boolean);
        
        if (flags.Count == 0)
            return null;
        
        if (flags.Count == 1)
        {
            if (!hasNull)
                return null;
            
            var kind = MapPrimitive(flags[0], ResolveFormatForUnionMember(schema, flags[0]));
            return new IrNullableType { InnerType = new IrPrimitiveType { PrimitiveKind = kind } };
        }
        
        // string + другие примитивы: строка как сериализация (число, bool, дата…). Сначала format, затем нес-string типы.
        if (IsStringCoercionPrimitiveUnion(flags))
        {
            var kind = ResolveStringCoercionUnionPrimitiveKind(schema, flags);
            IrType inner = new IrPrimitiveType { PrimitiveKind = kind };
            return hasNull ? new IrNullableType { InnerType = inner } : inner;
        }
        
        var unionMembers = new List<IrType>(flags.Count);
        foreach (var flag in flags)
        {
            var kind = MapPrimitive(flag, ResolveFormatForUnionMember(schema, flag));
            unionMembers.Add(new IrPrimitiveType { PrimitiveKind = kind });
        }
        
        IrType result = new IrUnionType { Types = unionMembers };
        if (hasNull)
            result = new IrNullableType { InnerType = result };
        
        return result;
    }
    
    /// <summary>
    /// Union из string и одного или нескольких JSON-примитивов (integer, number, boolean) — string как fallback-представление.
    /// </summary>
    private static bool IsStringCoercionPrimitiveUnion(List<JsonSchemaType> flags)
    {
        if (flags.Count < 2)
            return false;
        if (!flags.Contains(JsonSchemaType.String))
            return false;
        foreach (var f in flags)
        {
            if (f != JsonSchemaType.String && f != JsonSchemaType.Integer && f != JsonSchemaType.Number &&
                f != JsonSchemaType.Boolean)
                return false;
        }
        return true;
    }
    
    /// <summary>
    /// Один целевой IrPrimitiveKind: сначала <see cref="schema.Format"/>, иначе приоритет boolean → integer → number, string только как носитель сериализации.
    /// </summary>
    private static IrPrimitiveKind ResolveStringCoercionUnionPrimitiveKind(IOpenApiSchema schema, List<JsonSchemaType> flags)
    {
        if (TryMapSchemaFormatToPrimitiveKind(schema.Format, out var fromFormat) &&
            IsPrimitiveKindCompatibleWithJsonTypeUnion(fromFormat, flags))
            return fromFormat;
        
        return MapStringCoercionUnionFromJsonTypes(schema.Format, flags);
    }
    
    /// <summary>
    /// Маппинг format в примитив IR (без учёта union — совместимость проверяется отдельно).
    /// </summary>
    private static bool TryMapSchemaFormatToPrimitiveKind(string? format, out IrPrimitiveKind kind)
    {
        kind = IrPrimitiveKind.String;
        if (string.IsNullOrWhiteSpace(format))
            return false;
        
        switch (format.Trim().ToLowerInvariant())
        {
            case "date":
                kind = IrPrimitiveKind.Date;
                return true;
            case "date-time":
                kind = IrPrimitiveKind.DateTime;
                return true;
            case "time":
                kind = IrPrimitiveKind.Time;
                return true;
            case "uuid":
            case "guid":
                kind = IrPrimitiveKind.Uuid;
                return true;
            case "int64":
            case "uint64":
                kind = IrPrimitiveKind.Long;
                return true;
            case "int32":
            case "uint32":
            case "int16":
            case "uint16":
            case "short":
            case "ushort":
            case "int8":
            case "uint8":
            case "sbyte":
                kind = IrPrimitiveKind.Integer;
                return true;
            case "byte":
                kind = IrPrimitiveKind.ByteArray;
                return true;
            case "float":
            case "double":
            case "decimal":
                kind = IrPrimitiveKind.Number;
                return true;
            case "binary":
                kind = IrPrimitiveKind.Binary;
                return true;
            case "byte-array":
            case "byte[]":
                kind = IrPrimitiveKind.ByteArray;
                return true;
            default:
                return false;
        }
    }
    
    /// <summary>
    /// Совместим ли выбранный по format примитив с набором JSON Schema type-флагов в union.
    /// </summary>
    private static bool IsPrimitiveKindCompatibleWithJsonTypeUnion(IrPrimitiveKind kind, List<JsonSchemaType> flags)
    {
        var hasString = flags.Contains(JsonSchemaType.String);
        var hasInt = flags.Contains(JsonSchemaType.Integer);
        var hasNum = flags.Contains(JsonSchemaType.Number);
        var hasBool = flags.Contains(JsonSchemaType.Boolean);
        
        return kind switch
        {
            IrPrimitiveKind.Date or IrPrimitiveKind.DateTime or IrPrimitiveKind.Time or IrPrimitiveKind.Uuid
                => hasString,
            IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray
                => hasString,
            IrPrimitiveKind.Long or IrPrimitiveKind.Integer or IrPrimitiveKind.Number
                => hasString || hasInt || hasNum,
            IrPrimitiveKind.Boolean
                => hasBool,
            _ => false
        };
    }
    
    /// <summary>
    /// Выбор примитива только по type-флажкам и дополнительно по format, если format не дал отдельного примитива на первом шаге.
    /// </summary>
    private static IrPrimitiveKind MapStringCoercionUnionFromJsonTypes(string? format, List<JsonSchemaType> flags)
    {
        if (flags.Contains(JsonSchemaType.Boolean))
            return IrPrimitiveKind.Boolean;
        
        var hasInt = flags.Contains(JsonSchemaType.Integer);
        var hasNum = flags.Contains(JsonSchemaType.Number);
        
        if (hasInt || hasNum)
        {
            if (TryMapSchemaFormatToPrimitiveKind(format, out var fmtKind) &&
                fmtKind is IrPrimitiveKind.Long or IrPrimitiveKind.Integer or IrPrimitiveKind.Number &&
                IsPrimitiveKindCompatibleWithJsonTypeUnion(fmtKind, flags))
                return fmtKind;
            
            if (hasInt && string.Equals(format, "int64", StringComparison.OrdinalIgnoreCase))
                return IrPrimitiveKind.Long;
            if (hasInt)
                return IrPrimitiveKind.Integer;
            return IrPrimitiveKind.Number;
        }
        
        return IrPrimitiveKind.String;
    }
    
    /// <summary>
    /// Format задаёт ширину/вид числа — для ветки <c>string</c> в union с integer/number не наследуем его (иначе string ведёт себя как число).
    /// </summary>
    private static bool IsNumericOrIntegerWidthFormat(string format)
    {
        return TryMapSchemaFormatToPrimitiveKind(format, out var k) &&
               k is IrPrimitiveKind.Long or IrPrimitiveKind.Integer or IrPrimitiveKind.Number;
    }
    
    /// <summary>
    /// format int64 на свойстве с type [integer, string] относится к числовой ветке, не к строке.
    /// </summary>
    private static string? ResolveFormatForUnionMember(IOpenApiSchema parent, JsonSchemaType memberFlag)
    {
        if (memberFlag != JsonSchemaType.String)
            return parent.Format;
        
        if (!parent.Type.HasValue)
            return parent.Format;
        
        var core = parent.Type.Value & ~JsonSchemaType.Null;
        var hasNumeric = core.HasFlag(JsonSchemaType.Integer) || core.HasFlag(JsonSchemaType.Number);
        if (hasNumeric && parent.Format != null && IsNumericOrIntegerWidthFormat(parent.Format))
            return null;
        
        return parent.Format;
    }
    
    /// <summary>
    /// Проверяет, есть ли в схеме хотя бы одно required свойство
    /// </summary>
    private bool HasRequiredPropertiesInSchema(IOpenApiSchema schema, OpenApiDocument document)
    {
        var resolvedSchema = _schemaResolver.ResolveSchema(schema);
        
        // Если в схеме есть Required и в нем есть хотя бы одно свойство
        if (resolvedSchema.Required != null && resolvedSchema.Required.Count > 0)
        {
            return true;
        }
        
        // Также проверяем allOf - если в какой-то из базовых схем есть required свойства
        if (resolvedSchema.AllOf != null && resolvedSchema.AllOf.Count > 0)
        {
            foreach (var allOfSchema in resolvedSchema.AllOf)
            {
                var resolvedAllOfSchema = _schemaResolver.ResolveSchema(allOfSchema);
                if (resolvedAllOfSchema.Required != null && resolvedAllOfSchema.Required.Count > 0)
                {
                    return true;
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Маппит примитивный тип OpenAPI в IrPrimitiveKind
    /// </summary>
    private IrPrimitiveKind MapPrimitive(JsonSchemaType? type, string? format)
    {
        if (!type.HasValue)
            return IrPrimitiveKind.String;
        
        var typeValue = type.Value;
        
        if (typeValue.HasFlag(JsonSchemaType.String))
        {
            if (TryMapSchemaFormatToPrimitiveKind(format, out var fromFmt) &&
                fromFmt is IrPrimitiveKind.Date or IrPrimitiveKind.DateTime or IrPrimitiveKind.Time
                    or IrPrimitiveKind.Uuid or IrPrimitiveKind.Binary or IrPrimitiveKind.ByteArray)
                return fromFmt;
            
            return format switch
            {
                "date" => IrPrimitiveKind.Date,
                "date-time" => IrPrimitiveKind.DateTime,
                "time" => IrPrimitiveKind.Time,
                "uuid" or "guid" => IrPrimitiveKind.Uuid,
                "binary" => IrPrimitiveKind.Binary,
                "byte" => IrPrimitiveKind.ByteArray,
                _ => IrPrimitiveKind.String
            };
        }
        
        if (typeValue.HasFlag(JsonSchemaType.Number))
            return IrPrimitiveKind.Number;
        
        if (typeValue.HasFlag(JsonSchemaType.Integer))
        {
            if (TryMapSchemaFormatToPrimitiveKind(format, out var intFmt) &&
                intFmt is IrPrimitiveKind.Long or IrPrimitiveKind.Integer)
                return intFmt;
            return string.Equals(format, "int64", StringComparison.OrdinalIgnoreCase)
                ? IrPrimitiveKind.Long
                : IrPrimitiveKind.Integer;
        }
        
        if (typeValue.HasFlag(JsonSchemaType.Boolean))
            return IrPrimitiveKind.Boolean;
        
        return IrPrimitiveKind.String;
    }
    
    /// <summary>
    /// Извлекает enum'ы из компонентов
    /// </summary>
    private IReadOnlyList<IrEnum> ExtractEnums(OpenApiDocument document)
    {
        var enums = new List<IrEnum>();
        
        if (document.Components?.Schemas == null)
            return enums;
        
        foreach (var (schemaName, schema) in document.Components.Schemas)
        {
            // Enum должен иметь непустой список значений и тип string или integer
            if (schema.Enum == null || schema.Enum.Count == 0)
                continue;
            
            // Enum должен быть либо string, либо integer (без других типов)
            if (!schema.Type.HasValue)
                continue;
            
            var type = schema.Type.Value;
            var isStringEnum = type.HasFlag(JsonSchemaType.String) && !type.HasFlag(JsonSchemaType.Integer) && !type.HasFlag(JsonSchemaType.Number);
            var isIntegerEnum = type.HasFlag(JsonSchemaType.Integer) && !type.HasFlag(JsonSchemaType.String) && !type.HasFlag(JsonSchemaType.Number);
            
            if (!isStringEnum && !isIntegerEnum)
                continue;
            
            // Если есть Properties, это не enum, а модель с enum полем
            if (schema.Properties != null && schema.Properties.Count > 0)
                continue;
            
            var sanitizedName = _namingConverter.SanitizeIdentifier(schemaName);
            var enumModel = BuildEnum(sanitizedName, schema);
            enums.Add(enumModel);
        }
        
        return enums;
    }
    
    /// <summary>
    /// Строит enum из схемы
    /// </summary>
    private IrEnum BuildEnum(string name, IOpenApiSchema schema)
    {
        var values = new List<IrEnumValue>();
        var baseType = schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Integer) 
            ? IrEnumBaseType.Integer 
            : IrEnumBaseType.String;
        
        if (schema.Enum != null)
        {
            foreach (var enumValue in schema.Enum)
            {
                var valueName = _namingConverter.SanitizeIdentifier(enumValue.ToString() ?? "Unknown");
                values.Add(new IrEnumValue
                {
                    Name = valueName,
                    Value = enumValue
                });
            }
        }
        
        return new IrEnum
        {
            Name = name,
            Description = schema.Description,
            Values = values,
            BaseType = baseType,
            OriginalSchema = schema
        };
    }
    
    /// <summary>
    /// Извлекает клиенты из путей
    /// </summary>
    private IReadOnlyList<IrClient> ExtractClients(OpenApiDocument document, ICollection<string>? warnings = null)
    {
        var operations = new List<IrOperation>();
        
        if (document.Paths == null)
            return Array.Empty<IrClient>();
        
        foreach (var pathEntry in document.Paths)
        {
            var path = pathEntry.Key;
            var pathItem = pathEntry.Value;
            
            if (pathItem.Operations == null)
                continue;
            
            foreach (var operationEntry in pathItem.Operations)
            {
                var httpMethod = operationEntry.Key;
                var openApiOperation = operationEntry.Value;
                
                if (openApiOperation == null)
                    continue;
                
                var operation = BuildOperation(path, httpMethod, pathItem, openApiOperation, document, warnings);
                operations.Add(operation);
            }
        }
        
        // Группируем операции по тегам (если есть) или создаем один клиент
        if (operations.Count == 0)
            return Array.Empty<IrClient>();
        
        var clients = new List<IrClient>();
        var operationsByTag = operations
            .Where(op => op.Tags != null && op.Tags.Count > 0)
            .GroupBy(op => op.Tags!.First())
            .ToList();
        
        // Клиенты по тегам
        foreach (var tagGroup in operationsByTag)
        {
            var clientName = _namingConverter.SanitizeIdentifier(tagGroup.Key);
            clients.Add(new IrClient
            {
                Name = clientName,
                Operations = tagGroup.ToList()
            });
        }
        
        // Операции без тегов или с пустыми тегами
        var operationsWithoutTags = operations
            .Where(op => op.Tags == null || op.Tags.Count == 0)
            .ToList();
        
        if (operationsWithoutTags.Count > 0)
        {
            clients.Add(new IrClient
            {
                Name = "ApiClient",
                Operations = operationsWithoutTags
            });
        }
        
        // Если все операции были сгруппированы по тегам, возвращаем клиенты по тегам
        // Иначе возвращаем один клиент со всеми операциями
        if (clients.Count == 0 && operations.Count > 0)
        {
            clients.Add(new IrClient
            {
                Name = "ApiClient",
                Operations = operations
            });
        }
        
        return clients;
    }
    
    /// <summary>
    /// Строит IrOperation из OpenAPI операции
    /// </summary>
    private IrOperation BuildOperation(
        string path,
        HttpMethod httpMethod,
        IOpenApiPathItem pathItem,
        OpenApiOperation openApiOperation,
        OpenApiDocument document,
        ICollection<string>? warnings = null)
    {
        var operationId = openApiOperation.OperationId ?? 
            $"{httpMethod.Method.ToLower()}{path.Replace("/", "").Replace("{", "").Replace("}", "")}";
        var methodName = _namingConverter.SanitizeIdentifier(operationId);

        // Базовый диагностический контекст операции (endpoint + operationId). Под-точки построения
        // (параметры / тело / ответы) уточняют роль через WithContext, восстанавливая этот контекст.
        _ctx = new SchemaBuildContext
        {
            HttpMethod = httpMethod.Method.ToUpperInvariant(),
            Path = path,
            OperationId = operationId
        };

        // Извлекаем параметры: объединяем path-level и operation-level (operation переопределяет path)
        var effectiveParameters = GetEffectiveParameters(pathItem, openApiOperation);
        var pathParameters = new List<IrParameter>();
        var queryParameters = new List<IrParameter>();
        var headerParameters = new List<IrParameter>();

        foreach (var parameter in effectiveParameters)
            {
                var role = parameter.In switch
                {
                    ParameterLocation.Path => SchemaRole.PathParameter,
                    ParameterLocation.Header => SchemaRole.HeaderParameter,
                    _ => SchemaRole.QueryParameter
                };
                var irParameter = WithContext(
                    _ctx with { Role = role, ParameterName = parameter.Name },
                    () => BuildParameter(parameter, document));

                switch (parameter.In)
                {
                    case ParameterLocation.Path:
                        pathParameters.Add(irParameter);
                        break;
                    case ParameterLocation.Query:
                        queryParameters.Add(irParameter);
                        break;
                    case ParameterLocation.Header:
                        headerParameters.Add(irParameter);
                        break;
                }
        }

        ValidateAndDeduplicateParameters(methodName, pathParameters, queryParameters, warnings);

        // Порядок path-параметров в OpenAPI может отличаться от порядка плейсхолдеров в path.
        // Для Printf и аналогов аргументы должны идти в порядке {paramName} в path.
        ReorderPathParametersToMatchPath(pathParameters, path);
        
        // Извлекаем request body (передаем operationId для генерации FormModel)
        IrRequestBody? requestBody = null;
        if (openApiOperation.RequestBody != null)
        {
            requestBody = BuildRequestBody(openApiOperation.RequestBody, document, operationId);
        }
        
        // Извлекаем responses
        var responses = new List<IrResponse>();
        if (openApiOperation.Responses != null)
        {
            foreach (var responseEntry in openApiOperation.Responses)
            {
                var statusCode = responseEntry.Key;
                var response = responseEntry.Value;
                
                var responseType = ExtractResponseType(response, statusCode, document);
                
                responses.Add(new IrResponse
                {
                    StatusCode = statusCode,
                    Type = responseType,
                    Description = response.Description
                });
            }
        }
        
        return new IrOperation
        {
            MethodName = methodName,
            HttpMethod = httpMethod.Method.ToUpperInvariant(),
            Path = path,
            Summary = openApiOperation.Summary,
            Description = openApiOperation.Description,
            PathParameters = pathParameters,
            QueryParameters = queryParameters,
            HeaderParameters = headerParameters,
            RequestBody = requestBody,
            Responses = responses,
            Tags = openApiOperation.Tags?.Select(t => t.Name).Where(n => !string.IsNullOrEmpty(n)).Select(n => n!).ToList() ?? new List<string>()
        };
    }
    
    /// <summary>
    /// Объединяет параметры Path Item и Operation. Параметры операции переопределяют path-level (по name+location).
    /// </summary>
    private static IReadOnlyList<IOpenApiParameter> GetEffectiveParameters(IOpenApiPathItem pathItem, OpenApiOperation openApiOperation)
    {
        var byKey = new Dictionary<string, IOpenApiParameter>(StringComparer.OrdinalIgnoreCase);
        string Key(IOpenApiParameter p) => $"{p.In}:{p.Name}";

        if (pathItem.Parameters != null)
            foreach (var p in pathItem.Parameters)
                if (p.Name != null && p.In.HasValue)
                    byKey[Key(p)] = p;

        if (openApiOperation.Parameters != null)
            foreach (var p in openApiOperation.Parameters)
                if (p.Name != null && p.In.HasValue)
                    byKey[Key(p)] = p;

        return byKey.Values.ToList();
    }

    /// <summary>
    /// Извлекает имена path-параметров из шаблона пути в порядке их появления.
    /// Например: "/api/projects/{projectId}/plans/{planId}" → ["projectId", "planId"].
    /// </summary>
    private static IReadOnlyList<string> ExtractPathParameterNamesInOrder(string path)
    {
        var matches = PathParamRegex.Matches(path);
        var result = new List<string>(matches.Count);
        foreach (Match m in matches)
        {
            if (m.Success && m.Groups.Count > 1)
                result.Add(m.Groups[1].Value);
        }
        return result;
    }

    private static readonly Regex PathParamRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Переупорядочивает path-параметры в соответствии с порядком плейсхолдеров в path.
    /// Это необходимо для корректной передачи аргументов в Printf и аналогичные функции.
    /// </summary>
    private static void ReorderPathParametersToMatchPath(List<IrParameter> pathParameters, string path)
    {
        if (pathParameters.Count == 0) return;

        var pathOrder = ExtractPathParameterNamesInOrder(path);
        if (pathOrder.Count == 0) return;

        var byName = pathParameters.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        var reordered = new List<IrParameter>(pathParameters.Count);
        foreach (var name in pathOrder)
        {
            if (byName.TryGetValue(name, out var param))
            {
                reordered.Add(param);
                byName.Remove(name);
            }
        }
        // Параметры из spec, которых нет в path (edge case) — добавляем в конец
        foreach (var param in byName.Values)
            reordered.Add(param);

        pathParameters.Clear();
        pathParameters.AddRange(reordered);
    }

    /// <summary>
    /// Проверяет уникальность параметров и выводит предупреждения о дубликатах.
    /// Удаляет дубликаты: при пересечении path/query по имени — оставляет path, удаляет query.
    /// </summary>
    private static void ValidateAndDeduplicateParameters(
        string methodName,
        List<IrParameter> pathParameters,
        List<IrParameter> queryParameters,
        ICollection<string>? warnings)
    {
        if (warnings == null) return;

        // Дубликаты внутри path — оставляем первое вхождение
        var pathNames = new Dictionary<string, (IrParameter Param, int Index)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < pathParameters.Count; i++)
        {
            var p = pathParameters[i];
            if (pathNames.TryGetValue(p.Name, out var existing))
            {
                var typesDiffer = !IrTypeComparer.AreSemanticallyEqual(p.Type, existing.Param.Type);
                var msg = typesDiffer
                    ? $"Operation {methodName}: duplicate path parameter '{p.Name}' with different types (keeping first, removing duplicate)."
                    : $"Operation {methodName}: duplicate path parameter '{p.Name}' (keeping first, removing duplicate).";
                warnings.Add(msg);
                pathParameters.RemoveAt(i);
                i--;
            }
            else
                pathNames[p.Name] = (p, i);
        }

        // Дубликаты внутри query — оставляем первое вхождение
        var queryNames = new Dictionary<string, (IrParameter Param, int Index)>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < queryParameters.Count; i++)
        {
            var p = queryParameters[i];
            if (queryNames.TryGetValue(p.Name, out var existing))
            {
                var typesDiffer = !IrTypeComparer.AreSemanticallyEqual(p.Type, existing.Param.Type);
                var msg = typesDiffer
                    ? $"Operation {methodName}: duplicate query parameter '{p.Name}' with different types (keeping first, removing duplicate)."
                    : $"Operation {methodName}: duplicate query parameter '{p.Name}' (keeping first, removing duplicate).";
                warnings.Add(msg);
                queryParameters.RemoveAt(i);
                i--;
            }
            else
                queryNames[p.Name] = (p, i);
        }

        // Пересечение path и query — оставляем path, удаляем из query
        foreach (var pathName in pathNames.Keys.ToList())
        {
            if (queryNames.TryGetValue(pathName, out var queryParam))
            {
                var pathParam = pathNames[pathName].Param;
                var typesDiffer = !IrTypeComparer.AreSemanticallyEqual(pathParam.Type, queryParam.Param.Type);
                var msg = typesDiffer
                    ? $"Operation {methodName}: parameter '{pathName}' exists in both path and query with different types (keeping path parameter, removing from query)."
                    : $"Operation {methodName}: parameter '{pathName}' exists in both path and query (keeping path parameter, removing from query).";
                warnings.Add(msg);
                queryParameters.RemoveAll(q => string.Equals(q.Name, pathName, StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    /// <summary>
    /// Разрешает schema параметра из components/parameters при $ref, если parameter.Schema == null.
    /// </summary>
    private static IOpenApiSchema? ResolveParameterSchemaFromReference(IOpenApiParameter parameter, OpenApiDocument document)
    {
        if (parameter.Schema != null) return null;
        if (document.Components?.Parameters == null) return null;

        string? refId = null;
        if (parameter is OpenApiParameterReference paramRef)
            refId = paramRef.Reference?.Id;

        if (string.IsNullOrEmpty(refId) || !document.Components.Parameters.TryGetValue(refId, out var resolved))
            return null;

        return resolved.Schema;
    }

    /// <summary>
    /// Строит IrParameter из OpenAPI параметра
    /// </summary>
    private IrParameter BuildParameter(IOpenApiParameter parameter, OpenApiDocument document)
    {
        var name = _namingConverter.SanitizeIdentifier(parameter.Name ?? "param");
        var defaultSchema = new OpenApiSchema { Type = JsonSchemaType.String };
        var schema = parameter.Schema ?? ResolveParameterSchemaFromReference(parameter, document) ?? defaultSchema;
        var type = BuildType(schema, document);
        var isRequired = parameter.Required;
        
        return new IrParameter
        {
            Name = name,
            // Сохраняем исходное «проводное» имя (напр. Paging.Offset) — sanitize мог превратить его в
            // валидный идентификатор (Paging_Offset), но в URL/заголовке ключ должен остаться исходным.
            OriginalName = parameter.Name ?? name,
            Type = type,
            IsRequired = isRequired,
            // Parameter-level description wins; otherwise fall back to the parameter's schema description
            // (some OpenAPI emitters put it there). Then the default comes from the parameter's schema.
            Description = !string.IsNullOrWhiteSpace(parameter.Description) ? parameter.Description : schema.Description,
            DefaultValue = ExtractDefaultValue(schema.Default),
            // Reuse the same constraint extraction as model properties so params carry lengths/ranges/patterns.
            Constraints = ExtractConstraints(schema)
        };
    }
    
    /// <summary>
    /// Строит IrRequestBody из OpenAPI request body
    /// </summary>
    private IrRequestBody BuildRequestBody(IOpenApiRequestBody requestBody, OpenApiDocument document, string? operationId = null)
    {
        // Берем первый content type (обычно application/json)
        var contentType = requestBody.Content?.Keys.FirstOrDefault();
        var content = contentType != null && requestBody.Content != null
            ? requestBody.Content[contentType]
            : null;
        
        var schema = content?.Schema;
        
        // Определяем, является ли это формой (multipart/form-data или application/x-www-form-urlencoded)
        var isForm = contentType == "multipart/form-data" || contentType == "application/x-www-form-urlencoded";
        
        var type = schema != null
            ? WithContext(
                _ctx with { Role = SchemaRole.RequestBody, ContentType = contentType },
                () => BuildType(schema!, document, propertyContext: isForm && !string.IsNullOrEmpty(operationId)
                    ? new PropertyContext
                    {
                        IsForm = true,
                        OperationId = operationId
                    }
                    : null))
            : new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
        
        // Проверяем, есть ли в схеме хотя бы одно required свойство
        // Если есть, то request body должен быть required, даже если requestBody.Required = false
        var hasRequiredProperties = schema != null && HasRequiredPropertiesInSchema(schema, document);
        var isRequired = requestBody.Required || hasRequiredProperties;
        
        return new IrRequestBody
        {
            Type = type,
            IsRequired = isRequired,
            ContentType = contentType,
            Description = requestBody.Description
        };
    }
    
    /// <summary>
    /// Извлекает тип ответа из OpenAPI response
    /// </summary>
    private IrType? ExtractResponseType(IOpenApiResponse response, string statusCode, OpenApiDocument document)
    {
        // Берем первый content type
        var content = response.Content?.FirstOrDefault();
        var schema = content.HasValue ? content.Value.Value?.Schema : null;

        if (schema == null)
            return null;

        return WithContext(
            _ctx with { Role = SchemaRole.Response, StatusCode = statusCode, ContentType = content?.Key },
            () => BuildType(schema, document));
    }
    
    /// <summary>
    /// Строит граф зависимостей типов
    /// </summary>
    private IrTypeDependencyGraph BuildTypeDependencyGraph(
        IReadOnlyList<IrModel> models,
        IReadOnlyList<IrEnum> enums,
        IReadOnlyList<IrClient> clients)
    {
        var dependencies = new Dictionary<string, HashSet<string>>();
        var allTypeNames = new HashSet<string>();
        
        // Добавляем все типы в граф
        foreach (var model in models)
        {
            allTypeNames.Add(model.Name);
            dependencies[model.Name] = new HashSet<string>();
        }
        
        foreach (var enumModel in enums)
        {
            allTypeNames.Add(enumModel.Name);
            dependencies[enumModel.Name] = new HashSet<string>();
        }
        
        // Собираем зависимости из моделей
        foreach (var model in models)
        {
            var modelDependencies = ExtractDependenciesFromModel(model);
            dependencies[model.Name] = modelDependencies;
        }
        
        // Собираем зависимости из операций клиентов
        foreach (var client in clients)
        {
            foreach (var operation in client.Operations)
            {
                // Зависимости из параметров
                foreach (var param in operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters))
                {
                    var paramDeps = ExtractDependenciesFromType(param.Type);
                    foreach (var dep in paramDeps)
                    {
                        if (allTypeNames.Contains(dep))
                        {
                            // Зависимость от другого типа в IR
                            // Можно добавить в зависимости клиента, но пока просто собираем
                        }
                    }
                }
                
                // Зависимости из request body
                if (operation.RequestBody != null)
                {
                    var bodyDeps = ExtractDependenciesFromType(operation.RequestBody.Type);
                    // Аналогично
                }
                
                // Зависимости из responses
                foreach (var response in operation.Responses)
                {
                    if (response.Type != null)
                    {
                        var responseDeps = ExtractDependenciesFromType(response.Type);
                        // Аналогично
                    }
                }
            }
        }
        
        // Топологическая сортировка
        var topologicalOrder = TopologicalSort(dependencies, allTypeNames);
        
        // Преобразуем в ImmutableDictionary
        var immutableDependencies = dependencies.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<string>)kvp.Value.ToList()
        );
        
        return new IrTypeDependencyGraph
        {
            Dependencies = immutableDependencies.ToImmutableDictionary(),
            TopologicalOrder = topologicalOrder
        };
    }
    
    /// <summary>
    /// Извлекает зависимости из модели
    /// </summary>
    private HashSet<string> ExtractDependenciesFromModel(IrModel model)
    {
        var dependencies = new HashSet<string>();
        
        foreach (var property in model.Properties)
        {
            var deps = ExtractDependenciesFromType(property.Type);
            dependencies.UnionWith(deps);
        }
        
        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
            {
                var deps = ExtractDependenciesFromType(type);
                dependencies.UnionWith(deps);
            }
        }
        
        return dependencies;
    }
    
    /// <summary>
    /// Извлекает зависимости из типа
    /// </summary>
    private HashSet<string> ExtractDependenciesFromType(IrType type)
    {
        var dependencies = new HashSet<string>();
        
        switch (type)
        {
            case IrModelReference modelRef:
                dependencies.Add(modelRef.ModelName);
                break;
            case IrArrayType arrayType:
                dependencies.UnionWith(ExtractDependenciesFromType(arrayType.ElementType));
                break;
            case IrDictionaryType dictType:
                dependencies.UnionWith(ExtractDependenciesFromType(dictType.KeyType));
                dependencies.UnionWith(ExtractDependenciesFromType(dictType.ValueType));
                break;
            case IrUnionType unionType:
                foreach (var t in unionType.Types)
                    dependencies.UnionWith(ExtractDependenciesFromType(t));
                break;
            case IrIntersectionType intersectionType:
                foreach (var t in intersectionType.Types)
                    dependencies.UnionWith(ExtractDependenciesFromType(t));
                break;
            case IrNullableType nullableType:
                dependencies.UnionWith(ExtractDependenciesFromType(nullableType.InnerType));
                break;
            case IrOptionalType optionalType:
                dependencies.UnionWith(ExtractDependenciesFromType(optionalType.InnerType));
                break;
        }
        
        return dependencies;
    }
    
    /// <summary>
    /// Топологическая сортировка графа зависимостей
    /// </summary>
    private IReadOnlyList<string> TopologicalSort(
        Dictionary<string, HashSet<string>> dependencies,
        HashSet<string> allTypeNames)
    {
        var result = new List<string>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>();
        
        void Visit(string typeName)
        {
            if (visiting.Contains(typeName))
            {
                // Циклическая зависимость - просто добавляем в конец
                return;
            }
            
            if (visited.Contains(typeName))
                return;
            
            visiting.Add(typeName);
            
            if (dependencies.TryGetValue(typeName, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (allTypeNames.Contains(dep))
                    {
                        Visit(dep);
                    }
                }
            }
            
            visiting.Remove(typeName);
            visited.Add(typeName);
            result.Add(typeName);
        }
        
        foreach (var typeName in allTypeNames)
        {
            if (!visited.Contains(typeName))
            {
                Visit(typeName);
            }
        }
        
        return result;
    }
    
    /// <summary>
    /// Генерирует имя для inline модели на основе контекста
    /// </summary>
    private string GenerateInlineModelNameFromContext(PropertyContext? propertyContext)
    {
        // Если это форма, генерируем {operationId}FormModel
        if (propertyContext?.IsForm == true && !string.IsNullOrEmpty(propertyContext.OperationId))
        {
            var operationIdPascal = _namingConverter.ToPascalCase(propertyContext.OperationId);
            return _namingConverter.SanitizeIdentifier($"{operationIdPascal}FormModel");
        }
        
        // Если есть имя свойства, генерируем {propertyName}InlineModel
        if (!string.IsNullOrEmpty(propertyContext?.PropertyName))
        {
            var propertyNamePascal = _namingConverter.ToPascalCase(propertyContext.PropertyName);
            return _namingConverter.SanitizeIdentifier($"{propertyNamePascal}InlineModel");
        }
        
        // Fallback: используем счетчик
        return _namingConverter.SanitizeIdentifier($"InlineModel{_inlineModelCounter++}");
    }

    /// <summary>
    /// Возвращает оригинальное имя схемы из ссылки или через resolver, без преобразования регистра.
    /// </summary>
    private string? GetSchemaNameRaw(IOpenApiSchema schema)
    {
        if (schema is IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> refHolder)
        {
            var reference = refHolder.Reference;
            if (reference != null && !string.IsNullOrEmpty(reference.Id))
                return reference.Id;
        }

        var schemaName = _schemaResolver.GetSchemaName(schema, string.Empty);
        if (!string.IsNullOrEmpty(schemaName) && schemaName != "object")
            return schemaName;

        return null;
    }
}
