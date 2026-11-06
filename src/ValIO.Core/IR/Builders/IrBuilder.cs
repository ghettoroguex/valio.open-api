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
    
    /// <summary>Stores inline declarations allocated for the current source document.</summary>
    private readonly Dictionary<string, IrModel> _inlineModels = new();

    /// <summary>Reserves declared and inline type names before recursive inline model construction.</summary>
    private readonly HashSet<string> _allocatedTypeNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Supplies deterministic fallback names for inline schemas without a property context.</summary>
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
    /// Builds a language-independent IR and its dependency graph from an OpenAPI document.
    /// </summary>
    /// <param name="document">The source OpenAPI document.</param>
    /// <param name="warnings">An optional diagnostic sink; omitting it does not change the IR.</param>
    /// <param name="affixOptions">Optional name affixes applied before dependency extraction.</param>
    /// <returns>The complete IR, including inline models and type dependencies.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Distinct component schemas produce the same sanitized IR name.</exception>
    public IrDocument BuildFromOpenApi(
        OpenApiDocument document,
        ICollection<string>? warnings = null,
        NameAffixOptions? affixOptions = null)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        // Reset per-document inline models before reusing the builder.
        _inlineModels.Clear();
        _allocatedTypeNames.Clear();
        _inlineModelCounter = 0;
        ReserveDeclaredTypeNames(document);

        var models = ExtractModels(document);
        var enums = ExtractEnums(document);
        var clients = ExtractClients(document, warnings);

        // Inline models are declarations and participate in dependency ordering.
        IReadOnlyList<IrModel> allModels = models.Concat(_inlineModels.Values).ToList();
        IReadOnlyList<IrEnum> allEnums = enums;
        IReadOnlyList<IrClient> allClients = clients;

        // Rename declarations and references before building their dependency graph.
        if (affixOptions is { IsNoOp: false })
            (allModels, allEnums, allClients) =
                IrNameAffixer.Apply(allModels, allEnums, allClients, affixOptions, _namingConverter);

        var typeDependencies = new IrTypeDependencyGraphBuilder().Build(allModels, allEnums);

        return new IrDocument
        {
            Metadata = ExtractMetadata(document),
            Models = allModels,
            Enums = allEnums,
            Clients = allClients,
            TypeDependencies = typeDependencies
        };
    }

    /// <summary>Reserves component names and rejects ambiguous sanitized declarations before constructing any IR.</summary>
    /// <param name="document">The document containing component schema declarations.</param>
    /// <exception cref="InvalidOperationException">Two component names sanitize to the same IR identifier.</exception>
    private void ReserveDeclaredTypeNames(OpenApiDocument document)
    {
        if (document.Components?.Schemas == null)
            return;

        var originalNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in document.Components.Schemas.Keys)
        {
            var sanitizedName = _namingConverter.SanitizeIdentifier(name);
            if (originalNames.TryGetValue(sanitizedName, out var existingName))
                throw new InvalidOperationException(
                    $"Component schemas '{existingName}' and '{name}' produce the same sanitized IR type name '{sanitizedName}'. Rename one component to preserve distinct type identities.");

            originalNames.Add(sanitizedName, name);
            _allocatedTypeNames.Add(_namingConverter.ToPascalCase(sanitizedName));
        }
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
    
    /// <summary>
    /// Extracts validation bounds independently of the process culture.
    /// </summary>
    /// <param name="schema">The schema declaring constraints.</param>
    /// <param name="includeDefault">Whether to include a default value for property or parameter initialization.</param>
    /// <returns>The declared constraints, or null when none are present.</returns>
    private IrPropertyConstraints? ExtractConstraints(IOpenApiSchema schema, bool includeDefault = true)
    {
        var hasConstraints = !string.IsNullOrEmpty(schema.Minimum) ||
                            !string.IsNullOrEmpty(schema.Maximum) ||
                            !string.IsNullOrEmpty(schema.ExclusiveMinimum) ||
                            !string.IsNullOrEmpty(schema.ExclusiveMaximum) ||
                            schema.MinLength.HasValue ||
                            schema.MaxLength.HasValue ||
                            !string.IsNullOrEmpty(schema.Pattern) ||
                            (includeDefault && schema.Default != null);
        
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
            DefaultValue = includeDefault ? ExtractDefaultValue(schema.Default) : null
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
    /// Extracts model composition without duplicating inline-owned properties or discarding nested alternatives.
    /// </summary>
    /// <param name="schema">The schema declaring the model composition.</param>
    /// <param name="document">The document containing referenced declarations.</param>
    /// <param name="propertyContext">Optional inline model naming information.</param>
    /// <returns>The model composition, or null when all members are represented by owned properties.</returns>
    private IrComposition? ExtractComposition(IOpenApiSchema schema, OpenApiDocument document, PropertyContext? propertyContext = null)
    {
        if (schema.AllOf?.Count > 0)
        {
            // Plain inline object members already contribute owned properties in BuildModel.
            // Explicit nested compositions remain type members because their alternatives are semantic boundaries.
            var compositionTypes = new List<IrType>();
            foreach (var member in schema.AllOf)
            {
                if (string.IsNullOrEmpty(GetSchemaNameRaw(member)) &&
                    member.AllOf?.Count is not > 0 && member.OneOf?.Count is not > 0 && member.AnyOf?.Count is not > 0)
                    continue;

                var type = BuildType(member, document, propertyContext);
                if (!compositionTypes.Any(existing => IrTypeComparer.AreSemanticallyEqual(existing, type, ignorePresence: false)))
                    compositionTypes.Add(type);
            }

            if (compositionTypes.Count == 0)
                return null;

            return new IrComposition
            {
                Kind = IrCompositionKind.AllOf,
                Types = compositionTypes
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
                // Mapping values refer to component IDs, which must follow IR declaration sanitization.
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
    
    /// <summary>
    /// Recursively translates a schema into an IR type, allocating inline declarations when needed.
    /// </summary>
    /// <param name="schema">The schema to translate.</param>
    /// <param name="document">The document containing referenced definitions.</param>
    /// <param name="propertyContext">Optional contextual information for inline names.</param>
    /// <returns>The translated type.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> or <paramref name="document"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The schema cannot be translated into a supported IR type.</exception>
    private IrType BuildType(IOpenApiSchema schema, OpenApiDocument document, PropertyContext? propertyContext = null)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(document);

        var type = BuildTypeCore(schema, document, propertyContext);
        if (schema is IOpenApiReferenceHolder<OpenApiSchema, IOpenApiSchema, JsonSchemaReference> referenceHolder &&
            !string.IsNullOrEmpty(referenceHolder.Reference?.Id))
            return type;

        var resolvedSchema = _schemaResolver.ResolveSchema(schema);
        if ((type is IrArrayType or IrDictionaryType or IrModelReference) && SchemaAllowsJsonNull(resolvedSchema))
            type = IrCompositionNormalizer.WrapNullable(type);

        // Defaults belong to property and parameter initialization, never to alternative match predicates.
        var constraints = ExtractConstraints(resolvedSchema, includeDefault: false);
        if (constraints == null)
            return type;

        var constrained = type with { Constraints = constraints };
        if (type.Constraints == null || type.Constraints == constraints)
            return constrained;

        // A collapsed child and its enclosing schema contribute independent conjunctive predicates.
        return new IrIntersectionType { Types = [type, constrained] };
    }

    /// <summary>Builds the structural type while recursive child translations retain their own validation metadata.</summary>
    /// <param name="schema">The schema whose structure is being translated.</param>
    /// <param name="document">The document containing referenced declarations.</param>
    /// <param name="propertyContext">Optional inline model naming information.</param>
    /// <returns>The structural type before attaching this schema's validation metadata.</returns>
    /// <exception cref="InvalidOperationException">The schema has no supported structural translation.</exception>
    private IrType BuildTypeCore(IOpenApiSchema schema, OpenApiDocument document, PropertyContext? propertyContext)
    {
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
        
        // Lift null only when doing so preserves the exclusive alternative match count.
        if (resolvedSchema.OneOf?.Count > 0 || resolvedSchema.AnyOf?.Count > 0)
        {
            var kind = resolvedSchema.OneOf?.Count > 0 ? IrUnionKind.OneOf : IrUnionKind.AnyOf;
            var variants = kind == IrUnionKind.OneOf ? resolvedSchema.OneOf! : resolvedSchema.AnyOf!;
            var nullable = SchemaAllowsJsonNull(resolvedSchema);
            var nullCount = variants.Count(OpenApiNullability.IsNullOnly);
            var liftNull = kind == IrUnionKind.AnyOf ||
                nullCount == 1 && variants.Where(variant => !OpenApiNullability.IsNullOnly(variant))
                    .All(variant => OpenApiNullability.RejectsNull(variant, document));
            var members = new List<IrType>(variants.Count);
            foreach (var variant in variants)
            {
                if (liftNull && OpenApiNullability.IsNullOnly(variant))
                {
                    nullable = true;
                    continue;
                }

                members.Add(BuildType(variant, document, propertyContext));
            }

            // An all-null inclusive union (or a single exclusive null branch) accepts only null.
            if (members.Count == 0 && nullable)
                return new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Null };
            return IrCompositionNormalizer.NormalizeUnion(members, nullable, kind);
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
                        return new IrDictionaryType { KeyType = keyType, ValueType = valueType };
                    }
                    
                    // Если это не IOpenApiSchema, это может быть boolean false
                    // В этом случае это объект с фиксированными свойствами, не словарь
                    // Продолжаем обработку как inline объект ниже
                }
                
                // Это inline объект с фиксированными свойствами
                // Создаем inline модель с правильным именем на основе контекста
                var inlineModelName = GenerateInlineModelNameFromContext(propertyContext);
                var inlineModel = BuildModel(inlineModelName, resolvedSchema, document, propertyContext?.ParentModelName, propertyContext);
                _inlineModels.Add(inlineModelName, inlineModel);
                return new IrModelReference { ModelName = inlineModelName };
            }
            
            // Если есть AdditionalProperties, это словарь
            if (resolvedSchema.AdditionalProperties != null)
            {
                var keyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
                var valueType = BuildType(resolvedSchema.AdditionalProperties, document, propertyContext);
                return new IrDictionaryType { KeyType = keyType, ValueType = valueType };
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
            return BuildPrimitiveType(core, resolvedSchema.Format);
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
            return new IrArrayType { ElementType = elementType };
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
                    return new IrDictionaryType { KeyType = keyType, ValueType = valueType };
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
                _inlineModels.Add(inlineModelName, inlineModel);
                return new IrModelReference { ModelName = inlineModelName };
            }
            
            // Пустой объект без Properties и AdditionalProperties
            var emptyValueType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.Any };
            var emptyKeyType = new IrPrimitiveType { PrimitiveKind = IrPrimitiveKind.String };
            return new IrDictionaryType { KeyType = emptyKeyType, ValueType = emptyValueType };
        }
        
        var typeStr = resolvedSchema.Type?.ToString() ?? "null";
        throw new InvalidOperationException($"Unknown schema type: {typeStr} — at {_ctx.Describe()}");
    }
    
    /// <summary>Reads explicit type and legacy OpenAPI nullable annotations.</summary>
    /// <param name="schema">The schema whose annotations are inspected.</param>
    /// <returns>Whether an explicit annotation allows JSON null.</returns>
    private static bool SchemaAllowsJsonNull(IOpenApiSchema schema)
        => OpenApiNullability.HasExplicitNullAllowance(schema);
    
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
    
    /// <summary>Builds primitive type arrays while retaining nullability and member-specific format metadata.</summary>
    /// <param name="schema">The scalar schema with one or more JSON Schema type flags.</param>
    /// <returns>The scalar or composed type, or null when a different structural branch should handle the schema.</returns>
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
            
            return new IrNullableType { InnerType = BuildPrimitiveType(flags[0], ResolveFormatForUnionMember(schema, flags[0])) };
        }
        
        // string + другие примитивы: строка как сериализация (число, bool, дата…). Сначала format, затем нес-string типы.
        if (IsStringCoercionPrimitiveUnion(flags))
        {
            var kind = ResolveStringCoercionUnionPrimitiveKind(schema, flags);
            IrType inner = new IrPrimitiveType { PrimitiveKind = kind, Format = NormalizePrimitiveFormat(schema.Format) };
            return hasNull ? new IrNullableType { InnerType = inner } : inner;
        }
        
        var unionMembers = new List<IrType>(flags.Count);
        foreach (var flag in flags)
        {
            unionMembers.Add(BuildPrimitiveType(flag, ResolveFormatForUnionMember(schema, flag)));
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
    
    /// <summary>Builds a scalar type without losing its format annotation during kind selection.</summary>
    /// <param name="type">The scalar JSON Schema type.</param>
    /// <param name="format">The optional scalar format annotation.</param>
    /// <returns>The primitive kind together with its retained normalized format.</returns>
    private IrPrimitiveType BuildPrimitiveType(JsonSchemaType type, string? format) => new()
    {
        PrimitiveKind = MapPrimitive(type, format),
        Format = NormalizePrimitiveFormat(format),
    };

    /// <summary>Normalizes recognized formats while preserving case-sensitive custom identifiers.</summary>
    /// <param name="format">The original schema format annotation.</param>
    /// <returns>The normalized known format, verbatim custom format, or null for an empty annotation.</returns>
    private static string? NormalizePrimitiveFormat(string? format)
    {
        if (string.IsNullOrWhiteSpace(format))
            return null;
        var normalized = format.Trim().ToLowerInvariant();
        return TryMapSchemaFormatToPrimitiveKind(normalized, out _) || normalized is "duration" or "time-span" or "timespan"
            ? normalized
            : format;
    }

    /// <summary>Maps a scalar schema representation and compatible format to its language-independent kind.</summary>
    /// <param name="type">The scalar JSON Schema type, or an unspecified type.</param>
    /// <param name="format">The optional source format used for scalar representation selection.</param>
    /// <returns>The primitive IR representation without applying a language backend.</returns>
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
    /// Builds an HTTP operation with wire identities preserved across parameter locations.
    /// </summary>
    /// <param name="path">The original path template.</param>
    /// <param name="httpMethod">The operation's HTTP method.</param>
    /// <param name="pathItem">The path item declaring shared parameters.</param>
    /// <param name="openApiOperation">The operation-specific declarations.</param>
    /// <param name="document">The document used to resolve referenced schemas.</param>
    /// <param name="warnings">An optional diagnostic sink.</param>
    /// <returns>The operation and its generated parameter identifiers.</returns>
    private IrOperation BuildOperation(
        string path,
        HttpMethod httpMethod,
        IOpenApiPathItem pathItem,
        OpenApiOperation openApiOperation,
        OpenApiDocument document,
        ICollection<string>? warnings = null)
    {
        var operationId = openApiOperation.OperationId ?? 
            $"{httpMethod.Method.ToLowerInvariant()}{path.Replace("/", "").Replace("{", "").Replace("}", "")}";
        var methodName = _namingConverter.SanitizeIdentifier(operationId);

        // Parameter, body, and response diagnostics refine the operation context through WithContext.
        _ctx = new SchemaBuildContext
        {
            HttpMethod = httpMethod.Method.ToUpperInvariant(),
            Path = path,
            OperationId = operationId
        };

        // Operation-level parameters override path-level declarations with the same wire identity.
        var effectiveParameters = GetEffectiveParameters(pathItem, openApiOperation, methodName, warnings);
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

        EnsureUniqueParameterNames(methodName, [pathParameters, queryParameters, headerParameters], warnings);

        // Positional path formatters require parameters in the template's placeholder order.
        ReorderPathParametersToMatchPath(pathParameters, path);
        
        // Pass the operation ID to preserve contextual form model names.
        IrRequestBody? requestBody = null;
        if (openApiOperation.RequestBody != null)
        {
            requestBody = BuildRequestBody(openApiOperation.RequestBody, document, operationId);
        }
        
        // Preserve every response status and its payload type.
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
    /// Merges path-level and operation-level parameters by wire location and name.
    /// </summary>
    /// <remarks>Only HTTP header names are case-insensitive. Operation parameters override path-level entries.</remarks>
    /// <param name="pathItem">The source path item.</param>
    /// <param name="openApiOperation">The operation providing overrides.</param>
    /// <param name="methodName">The generated method name used in diagnostics.</param>
    /// <param name="warnings">An optional sink for duplicate declaration diagnostics.</param>
    /// <returns>One effective parameter per wire identity, in declaration order.</returns>
    private static IReadOnlyList<IOpenApiParameter> GetEffectiveParameters(
        IOpenApiPathItem pathItem,
        OpenApiOperation openApiOperation,
        string methodName,
        ICollection<string>? warnings)
    {
        var byKey = new Dictionary<(ParameterLocation Location, string Name), IOpenApiParameter>();
        MergeParameterScope(pathItem.Parameters, byKey, methodName, warnings);
        MergeParameterScope(openApiOperation.Parameters, byKey, methodName, warnings);
        return byKey.Values.ToList();
    }

    /// <summary>
    /// Adds one declaration scope while diagnosing repeated wire identities within that scope.
    /// </summary>
    /// <param name="parameters">The parameter declarations in one scope.</param>
    /// <param name="destination">The merged parameters, updated with later declarations.</param>
    /// <param name="methodName">The generated method name used in diagnostics.</param>
    /// <param name="warnings">An optional diagnostic sink.</param>
    private static void MergeParameterScope(
        IEnumerable<IOpenApiParameter>? parameters,
        Dictionary<(ParameterLocation Location, string Name), IOpenApiParameter> destination,
        string methodName,
        ICollection<string>? warnings)
    {
        if (parameters == null)
            return;

        var seen = new HashSet<(ParameterLocation Location, string Name)>();
        foreach (var parameter in parameters)
        {
            if (parameter.Name == null || !parameter.In.HasValue)
                continue;

            var location = parameter.In.Value;
            var name = location == ParameterLocation.Header ? parameter.Name.ToUpperInvariant() : parameter.Name;
            var key = (location, name);
            if (!seen.Add(key))
                warnings?.Add($"Operation {methodName}: duplicate {location.ToString().ToLowerInvariant()} parameter '{parameter.Name}' (keeping last declaration).");

            destination[key] = parameter;
        }
    }

    /// <summary>
    /// Extracts wire names from a path template in placeholder order.
    /// </summary>
    /// <param name="path">The path template containing brace-delimited placeholders.</param>
    /// <returns>The placeholder names in their original case.</returns>
    private static IReadOnlyList<string> ExtractPathParameterNamesInOrder(string path)
    {
        var matches = PathParamRegex.Matches(path);
        var result = new List<string>(matches.Count);
        foreach (Match match in matches)
        {
            if (match.Success && match.Groups.Count > 1)
                result.Add(match.Groups[1].Value);
        }
        return result;
    }

    /// <summary>Matches brace-delimited OpenAPI path parameter names.</summary>
    private static readonly Regex PathParamRegex = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Orders parameters by their original wire names so positional path formatting stays correct.
    /// </summary>
    /// <param name="pathParameters">The parameters to reorder in place.</param>
    /// <param name="path">The source path template.</param>
    private static void ReorderPathParametersToMatchPath(List<IrParameter> pathParameters, string path)
    {
        if (pathParameters.Count == 0)
            return;

        var pathOrder = ExtractPathParameterNamesInOrder(path);
        if (pathOrder.Count == 0)
            return;

        var byName = pathParameters.ToDictionary(parameter => parameter.OriginalName, StringComparer.Ordinal);
        var reordered = new List<IrParameter>(pathParameters.Count);
        foreach (var name in pathOrder)
        {
            if (byName.Remove(name, out var parameter))
                reordered.Add(parameter);
        }

        // Invalid declarations absent from the template remain visible to downstream diagnostics.
        reordered.AddRange(byName.Values);
        pathParameters.Clear();
        pathParameters.AddRange(reordered);
    }

    /// <summary>
    /// Allocates distinct source identifiers without discarding distinct wire parameters.
    /// </summary>
    /// <remarks>
    /// Casing and separator conversion can merge otherwise distinct identifiers in generators.
    /// Reserve their camel-case equivalents across locations and append a deterministic suffix on collision.
    /// The original wire names always remain unchanged.
    /// </remarks>
    /// <param name="methodName">The generated method name used in diagnostics.</param>
    /// <param name="parameterGroups">The path, query, and header groups in allocation order.</param>
    /// <param name="warnings">An optional diagnostic sink; allocation is independent of its presence.</param>
    private void EnsureUniqueParameterNames(
        string methodName,
        IReadOnlyList<List<IrParameter>> parameterGroups,
        ICollection<string>? warnings)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameters in parameterGroups)
        {
            for (var index = 0; index < parameters.Count; index++)
            {
                var parameter = parameters[index];
                var name = parameter.Name;
                var suffix = 2;
                while (!names.Add(_namingConverter.ToCamelCase(name)))
                    name = parameter.Name + (suffix++).ToString(CultureInfo.InvariantCulture);

                if (name != parameter.Name)
                {
                    parameters[index] = parameter with { Name = name };
                    warnings?.Add($"Operation {methodName}: parameter '{parameter.OriginalName}' uses identifier '{name}' to avoid a generated name collision.");
                }
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
    /// Builds a parameter contract preserving its wire identity, schema constraints, and normalized serialization metadata.
    /// </summary>
    /// <param name="parameter">The source parameter, including location-specific style and explode defaults.</param>
    /// <param name="document">The document used to resolve referenced parameter schemas.</param>
    /// <returns>The language-independent parameter contract.</returns>
    /// <exception cref="InvalidOperationException">The reader supplies an unrecognized serialization style.</exception>
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
            // Keep the wire name even when source identifier sanitization changes punctuation.
            OriginalName = parameter.Name ?? name,
            Type = type,
            IsRequired = isRequired,
            // Parameter-level description wins; otherwise fall back to the parameter's schema description
            // (some OpenAPI emitters put it there). Then the default comes from the parameter's schema.
            Description = !string.IsNullOrWhiteSpace(parameter.Description) ? parameter.Description : schema.Description,
            DefaultValue = ExtractDefaultValue(schema.Default),
            // Reuse the same constraint extraction as model properties so params carry lengths/ranges/patterns.
            Constraints = ExtractConstraints(schema),
            Style = NormalizeParameterStyle(parameter),
            Explode = parameter.Explode,
        };
    }

    /// <summary>Maps the reader's effective parameter style into the SDK-independent IR vocabulary.</summary>
    /// <param name="parameter">The source parameter whose location supplies an omitted style's default.</param>
    /// <returns>The normalized serialization style.</returns>
    /// <exception cref="InvalidOperationException">The reader supplies a style absent from the shared IR vocabulary.</exception>
    private static IrParameterStyle NormalizeParameterStyle(IOpenApiParameter parameter)
    {
        var style = parameter.Style ?? (parameter.In is ParameterLocation.Query or ParameterLocation.Cookie
            ? ParameterStyle.Form
            : ParameterStyle.Simple);
        return style switch
        {
            ParameterStyle.Matrix => IrParameterStyle.Matrix,
            ParameterStyle.Label => IrParameterStyle.Label,
            ParameterStyle.Form => IrParameterStyle.Form,
            ParameterStyle.Simple => IrParameterStyle.Simple,
            ParameterStyle.SpaceDelimited => IrParameterStyle.SpaceDelimited,
            ParameterStyle.PipeDelimited => IrParameterStyle.PipeDelimited,
            ParameterStyle.DeepObject => IrParameterStyle.DeepObject,
            ParameterStyle.Cookie => IrParameterStyle.Cookie,
            _ => throw new InvalidOperationException($"Parameter '{parameter.Name}' has unsupported serialization style '{style}'."),
        };
    }
    
    /// <summary>Builds a request-body contract with presence controlled only by requestBody.required.</summary>
    /// <param name="requestBody">The request-body presence and media-type contract.</param>
    /// <param name="document">The OpenAPI document used to resolve referenced schemas.</param>
    /// <param name="operationId">The optional operation identity used to name inline form models.</param>
    /// <returns>The body contract whose required flag is independent of its model's required properties.</returns>
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
        
        return new IrRequestBody
        {
            Type = type,
            IsRequired = requestBody.Required,
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
    /// Reserves an inline model name without overwriting another inline or declared type.
    /// </summary>
    /// <remarks>Preserves the historical simple name when free, then uses contextual and numeric disambiguation.</remarks>
    /// <param name="propertyContext">Optional property or form information used for readable names.</param>
    /// <returns>A unique name reserved before recursively constructing the inline model.</returns>
    private string GenerateInlineModelNameFromContext(PropertyContext? propertyContext)
    {
        string name;
        if (propertyContext?.IsForm == true && !string.IsNullOrEmpty(propertyContext.OperationId))
        {
            var operationIdPascal = _namingConverter.ToPascalCase(propertyContext.OperationId);
            name = _namingConverter.SanitizeIdentifier($"{operationIdPascal}FormModel");
        }
        else if (!string.IsNullOrEmpty(propertyContext?.PropertyName))
        {
            var propertyNamePascal = _namingConverter.ToPascalCase(propertyContext.PropertyName);
            name = _namingConverter.SanitizeIdentifier($"{propertyNamePascal}InlineModel");
        }
        else
        {
            name = _namingConverter.SanitizeIdentifier($"InlineModel{_inlineModelCounter++}");
        }

        if (_allocatedTypeNames.Add(_namingConverter.ToPascalCase(name)))
            return name;

        var owner = _ctx.RootModelName ?? _ctx.OperationId ?? propertyContext?.ParentModelName;
        var propertyPath = string.Concat(_ctx.PropertyPath.Select(_namingConverter.ToPascalCase));
        var contextualName = string.IsNullOrEmpty(owner)
            ? name
            : _namingConverter.SanitizeIdentifier(owner + (propertyPath.Length == 0 ? name : propertyPath + "InlineModel"));
        var candidate = contextualName;
        var suffix = 2;
        while (!_allocatedTypeNames.Add(_namingConverter.ToPascalCase(candidate)))
            candidate = contextualName + (suffix++).ToString(CultureInfo.InvariantCulture);

        return candidate;
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
