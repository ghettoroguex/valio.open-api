using System.Collections.Immutable;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Interfaces;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Http;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Core.Models;
using ValIO.Core.Services;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Validation;
using ValIO.Generators.TypeScript.Axios;

namespace ValIO.Generators.TypeScript.Ast.Builders;

/// <summary>
/// Построитель AST для TypeScript (Builder Pattern)
/// Использует IR для построения AST узлов
/// </summary>
public partial class TypeScriptAstBuilder : ITypeScriptAstBuilder
{
    /// <summary>
    /// Справочник моделей документа (по <see cref="IrModel.Name"/>). Нужен для резолва наследования
    /// allOf в <see cref="BuildInterface"/>: интерфейс с <c>extends Base</c> не должен переобъявлять
    /// свойства, унаследованные от базовых классов (иначе «incorrectly extends», TS2430). Устанавливается
    /// шагом моделей; по умолчанию пуст — фильтрация не применяется (безопасный фолбэк).
    /// </summary>
    public IReadOnlyDictionary<string, IrModel> ModelsByName { get; set; } = new Dictionary<string, IrModel>();

    /// <summary>
    /// Identifies the kind of generated file an import is being built for. Drives both the base
    /// import path and whether dependency imports are emitted as type-only (see <see cref="BuildImports"/>).
    /// </summary>
    private enum FileCategory
    {
        /// <summary>A model/enum declaration file under <c>models/</c>; dependency imports are type-only.</summary>
        Models,

        /// <summary>A client/endpoint file under <c>api/</c>; dependency imports may include runtime schema values.</summary>
        Clients,

        /// <summary>A validation-schema file under <c>schemas/</c>.</summary>
        Schemas
    }
    
    private readonly INamingConverter _namingConverter;
    private readonly HttpAstBuilder _httpBuilder;
    private readonly TypeScriptGenerationOptions? _options;

    /// <summary>
    /// Карта «affix-имя → исходное имя» для путей/имён файлов. Заполняется при входе в публичные
    /// Build*-методы из <c>Original*</c>-полей IR. Пустая (тождество) при отсутствии affix.
    /// </summary>
    private readonly Dictionary<string, string> _orig = new(StringComparer.Ordinal);

    /// <summary>
    /// Стратегия валидации для выбранной библиотеки (zod/valibot/typebox/none). Строит выражения
    /// валидации и runtime-импорты, не завязываясь на конкретную библиотеку.
    /// </summary>
    private readonly IClientValidationStrategy _validation;

    /// <summary>
    /// Транспорт генерируемого endpoint-файла (axios / fetch). Устанавливается в <see cref="BuildSourceFileForOperation"/>.
    /// </summary>
    private string _activeEndpointTransport = "axios";

    /// <summary>
    /// Конструктор
    /// </summary>
    /// <param name="namingConverter">Конвертер имен</param>
    /// <param name="options">Опции генерации TypeScript (опционально)</param>
    public TypeScriptAstBuilder(
        INamingConverter namingConverter,
        TypeScriptGenerationOptions? options = null)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _options = options;
        _httpBuilder = new HttpAstBuilder();
        _validation = ClientValidationStrategyFactory.Create(options);
    }
    
    public TypeAliasDeclarationNode BuildTypeAlias(string name, TypeNode type)
    {
        return new TypeAliasDeclarationNode(
            name: new IdentifierNode(name),
            type: type);
    }

    // Приватные вспомогательные методы
    
    private IReadOnlyList<ImportNode> BuildImports(
        IReadOnlyList<string> dependencies, 
        string? importPath,
        FileOrganization? fileOrganization = null,
        FileCategory? currentFileCategory = null,
        string? currentModelOrEnumName = null,
        string? currentOperationName = null)
    {
        if (dependencies.Count == 0)
        {
            return ImmutableList<ImportNode>.Empty;
        }
        
        var importsByFile = new Dictionary<string, List<string>>();
        var modelsPath = fileOrganization?.ModelsPath ?? "./models";
        var clientsPath = fileOrganization?.ClientsPath ?? "./api";
        
        var currentBasePath = !string.IsNullOrEmpty(currentModelOrEnumName)
            ? $"{modelsPath}/{GetModelFileName(currentModelOrEnumName)}"
            : !string.IsNullOrEmpty(currentOperationName)
                ? $"{clientsPath}/{GetModelFileName(currentOperationName)}"
                : currentFileCategory switch
                {
                    FileCategory.Models => modelsPath,
                    FileCategory.Clients => clientsPath,
                    FileCategory.Schemas => "./schemas",
                    _ => "./"
                };
        
        foreach (var dependency in dependencies.Distinct().OrderBy(d => d))
        {
            var fileName = GetModelFileName(dependency);
            var dependencyDir = $"{modelsPath}/{fileName}";
            
            string importPathForFile;
            if (!string.IsNullOrEmpty(importPath))
            {
                importPathForFile = $"{importPath}/{fileName}";
            }
            else if (!string.IsNullOrEmpty(currentModelOrEnumName) || !string.IsNullOrEmpty(currentOperationName))
            {
                importPathForFile = CalculateRelativePath(currentBasePath, dependencyDir, fileName);
            }
            else if (currentBasePath == modelsPath)
            {
                importPathForFile = $"./{fileName}/{fileName}";
            }
            else
            {
                importPathForFile = CalculateRelativePath(currentBasePath, dependencyDir, fileName);
            }
            
            importPathForFile = NormalizePath(importPathForFile);
            
            if (!importsByFile.ContainsKey(importPathForFile))
            {
                importsByFile[importPathForFile] = new List<string>();
            }
            
            importsByFile[importPathForFile].Add(dependency);
        }
        
        // Model files reference their dependencies (other models, enum string-literal-union aliases)
        // exclusively in type position, so under verbatimModuleSyntax those imports MUST be `import type`
        // (otherwise TS1484). Client/schema files are left as value imports because they also pull in
        // runtime Zod/Valibot schema values, which must not be type-only.
        var dependenciesAreTypeOnly = currentFileCategory == FileCategory.Models;

        // Создаем ImportNode для каждой группы
        var imports = new List<ImportNode>();
        foreach (var kvp in importsByFile.OrderBy(kvp => kvp.Key))
        {
            var items = kvp.Value.Select(name => new IdentifierNode(name)).ToImmutableList();
            var importNode = new ImportNode(
                items: items,
                from: kvp.Key,
                kind: ImportKind.Named,
                isTypeOnly: dependenciesAreTypeOnly);
            imports.Add(importNode);
        }
        
        return imports;
    }
    
    /// <summary>
    /// Вычисляет относительный путь между двумя папками
    /// </summary>
    private string CalculateRelativePath(string fromPath, string toPath, string fileName)
    {
        // Нормализуем пути
        fromPath = NormalizePath(fromPath);
        toPath = NormalizePath(toPath);
        
        // Если пути одинаковые, используем относительный путь в той же папке
        if (fromPath == toPath)
        {
            return $"./{fileName}";
        }
        
        // Разбиваем пути на части
        var fromParts = fromPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var toParts = toPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        // Находим общий префикс
        int commonLength = 0;
        int minLength = Math.Min(fromParts.Length, toParts.Length);
        while (commonLength < minLength && fromParts[commonLength] == toParts[commonLength])
        {
            commonLength++;
        }
        
        // Вычисляем количество уровней вверх
        int upLevels = fromParts.Length - commonLength;
        
        // Формируем относительный путь
        var relativeParts = new List<string>();
        
        // Добавляем уровни вверх
        for (int i = 0; i < upLevels; i++)
        {
            relativeParts.Add("..");
        }
        
        // Добавляем оставшиеся части пути к целевой папке
        for (int i = commonLength; i < toParts.Length; i++)
        {
            relativeParts.Add(toParts[i]);
        }
        
        // Добавляем имя файла
        relativeParts.Add(fileName);
        
        return string.Join("/", relativeParts);
    }
    
    /// <summary>
    /// Нормализует путь (убирает двойные слеши, заменяет обратные слеши)
    /// </summary>
    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "./";
        
        // Заменяем обратные слеши на прямые
        path = path.Replace("\\", "/");
        
        // Убираем двойные слеши
        while (path.Contains("//"))
        {
            path = path.Replace("//", "/");
        }
        
        // Убираем начальный слеш, если это не абсолютный путь
        if (path.StartsWith("/") && !path.StartsWith("//") && !path.StartsWith("http"))
        {
            path = path.Substring(1);
        }
        
        // Убираем конечный слеш
        if (path.EndsWith("/") && path.Length > 1)
        {
            path = path.Substring(0, path.Length - 1);
        }
        
        return path;
    }
    
    /// <summary>
    /// Получает имя файла для модели или enum'а
    /// </summary>
    private string GetModelFileName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return string.Empty;

        // Имя файла строится от ИСХОДНОГО (до affix) имени, чтобы пути не менялись;
        // при отсутствии affix карта тождественна.
        var original = _orig.GetValueOrDefault(typeName, typeName);
        return _namingConverter.ToCamelCase(original);
    }
    
    private static LiteralExpressionNode BuildLiteralFromValue(object value)
    {
        var kind = value switch
        {
            string => LiteralKind.String,
            bool => LiteralKind.Boolean,
            null => LiteralKind.Null,
            _ => LiteralKind.Number
        };
        return new LiteralExpressionNode(value, kind);
    }

    private SimpleTypeNode BuildSimpleType(string typeName)
    {
        return new SimpleTypeNode(new IdentifierNode(typeName), isPrimitive: false);
    }
    
    // IR → AST методы
    
    /// <summary>
    /// Строит TypeNode из IrType
    /// </summary>
    public TypeNode BuildType(IrType irType)
    {
        return irType switch
        {
            IrPrimitiveType p => BuildPrimitiveType(p),
            IrModelReference r => BuildModelReference(r),
            IrArrayType arr => BuildArrayType(arr),
            IrDictionaryType dict => BuildDictionaryType(dict),
            IrUnionType union => BuildUnionType(union),
            IrIntersectionType intersection => BuildIntersectionType(intersection),
            IrNullableType nullable => BuildNullableType(nullable),
            IrOptionalType optional => BuildOptionalType(optional),
            _ => throw new NotSupportedException($"IrType {irType.GetType().Name} is not supported")
        };
    }
    
    /// <summary>Builds a property value type without conflating absence and JSON null.</summary>
    /// <param name="property">The property whose type may carry an optional wrapper.</param>
    /// <returns>The value type; optionality is represented by the property marker.</returns>
    private TypeNode BuildInterfacePropertyTypeNode(IrProperty property)
    {
        return BuildType(UnwrapMemberOptional(property.Type));
    }

    /// <summary>Removes member-level optional wrappers while preserving nullable wrappers.</summary>
    /// <param name="type">The source member type.</param>
    /// <returns>The value type without member-level optional wrappers.</returns>
    private static IrType UnwrapMemberOptional(IrType type) => type switch
    {
        IrOptionalType optional => UnwrapMemberOptional(optional.InnerType),
        IrNullableType nullable => nullable with { InnerType = UnwrapMemberOptional(nullable.InnerType) },
        _ => type
    };

    /// <summary>Detects optionality through nested nullable and optional wrappers.</summary>
    /// <param name="type">The source member type.</param>
    /// <returns>Whether the member permits omission.</returns>
    internal static bool IsOptionalMemberType(IrType type) => type switch
    {
        IrOptionalType => true,
        IrNullableType nullable => IsOptionalMemberType(nullable.InnerType),
        _ => false
    };

    /// <summary>
    /// Builds the source file for a model.
    /// </summary>
    /// <param name="model">The model to generate.</param>
    /// <param name="importPath">The optional import path override.</param>
    /// <param name="fileOrganization">The generated file organization.</param>
    /// <returns>The model source file.</returns>
    public SourceFileNode BuildSourceFile(IrModel model, string? importPath = null, FileOrganization? fileOrganization = null)
    {
        OriginalNameMap.AddModel(_orig, model);
        var dependencies = ExtractDependencies(model);
        dependencies = dependencies.Where(d => d != model.Name).ToList();
        var imports = BuildImports(dependencies, importPath, fileOrganization, FileCategory.Models, model.Name);
        
        var declarations = new List<DeclarationNode> { BuildInterface(model) };
        var constraintsDecl = BuildConstraintsDeclaration(model);
        if (constraintsDecl != null)
            declarations.Add(constraintsDecl);
        
        var exports = declarations.Select(d => new ExportNode(d, ExportKind.Named)).ToImmutableList();

        var modelFileBase = _namingConverter.ToCamelCase(model.OriginalName);
        return new SourceFileNode(
            imports: imports,
            exports: exports,
            declarations: declarations.ToImmutableList(),
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{modelFileBase}.ts",
                $"models/{modelFileBase}",
                string.IsNullOrWhiteSpace(model.Description) ? $"{model.Name} model." : model.Description));
    }

    /// <summary>
    /// Строит VariableDeclarationNode с const объектом метаданных валидации для Zod схем.
    /// Возвращает null если нет свойств с constraints.
    /// </summary>
    private VariableDeclarationNode? BuildConstraintsDeclaration(IrModel model)
    {
        // Shared with the params builder so model and params constants can't drift (see TypeScriptConstraints).
        var entries = model.Properties
            .Where(p => p.Constraints != null)
            .Select(p => (p.Name, Constraints: p.Constraints!));
        return TypeScriptConstraints.BuildConstraintsObject(
            $"{model.Name}Constraints",
            entries,
            TypeScriptDoc.Block(
                $"Validation constraints for {model.Name} properties (lengths, ranges, patterns).",
                ("constant", "")));
    }

    /// <summary>
    /// Builds an interface declaration from a model.
    /// </summary>
    /// <param name="model">The model to generate.</param>
    /// <returns>The interface declaration.</returns>
    public InterfaceDeclarationNode BuildInterface(IrModel model)
    {
        var name = new IdentifierNode(model.Name);
        var comment = !string.IsNullOrEmpty(model.Description)
            ? new CommentNode(model.Description, CommentKind.JSDoc)
            : null;
        
        // allOf intersects inherited and local constraints instead of discarding local overrides.
        var inherited = CollectInheritedProperties(model);
        var properties = model.Properties.Select(property =>
            inherited.TryGetValue(property.OriginalName, out var bases)
                ? BuildIntersectedProperty(property, bases)
                : BuildProperty(property)).ToList<DeclarationNode>();

        var baseTypes = ImmutableList<TypeNode>.Empty;
        if (model.Composition?.Kind == IrCompositionKind.AllOf && model.Composition.Types.Count > 0)
        {
            // AllOf в TypeScript - это intersection типов
            baseTypes = model.Composition.Types
                .Where(t => t is IrModelReference)
                .Select(t => BuildType(t))
                .ToImmutableList();
        }
        
        return new InterfaceDeclarationNode(
            name: name,
            accessModifier: AccessModifier.Public,
            baseTypes: baseTypes,
            members: properties.ToImmutableList(),
            documentation: comment);
    }
    
    /// <summary>Collects each inherited property declaration along the allOf inheritance graph.</summary>
    /// <param name="model">The model whose bases are inspected.</param>
    /// <returns>Inherited declarations indexed by their original wire name.</returns>
    private Dictionary<string, List<IrProperty>> CollectInheritedProperties(IrModel model)
    {
        var result = new Dictionary<string, List<IrProperty>>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Collect(IrModel current)
        {
            if (current.Composition is not { Kind: IrCompositionKind.AllOf } composition)
                return;
            foreach (var reference in composition.Types.OfType<IrModelReference>())
            {
                if (!visited.Add(reference.ModelName) || !ModelsByName.TryGetValue(reference.ModelName, out var parent))
                    continue;
                foreach (var property in parent.Properties)
                {
                    if (!result.TryGetValue(property.OriginalName, out var declarations))
                        result[property.OriginalName] = declarations = [];
                    declarations.Add(property);
                }
                Collect(parent);
            }
        }
        Collect(model);
        return result;
    }

    /// <summary>Intersects a local property with every inherited value and presence constraint.</summary>
    /// <param name="property">The locally declared property.</param>
    /// <param name="bases">Inherited declarations with the same wire name.</param>
    /// <returns>A compatible property that preserves every allOf constraint.</returns>
    private PropertyDeclarationNode BuildIntersectedProperty(IrProperty property, IReadOnlyList<IrProperty> bases)
    {
        var declarations = bases.Append(property).ToArray();
        var types = declarations.Select(item => BuildType(UnwrapMemberOptional(item.Type))).ToImmutableList();
        var required = declarations.Any(item => item.IsRequired && !IsOptionalMemberType(item.Type));
        var comment = string.IsNullOrEmpty(property.Description) ? null : new CommentNode(property.Description, CommentKind.JSDoc);
        var defaultValue = property.Constraints?.DefaultValue is { } value ? BuildLiteralFromValue(value) : null;
        return new PropertyDeclarationNode(new IdentifierNode(property.Name), new IntersectionTypeNode(types),
            isRequired: required, accessor: PropertyAccessorKind.Get, defaultValue: defaultValue,
            documentation: comment, constraints: property.Constraints);
    }

    /// <summary>
    /// Builds a model property with independent presence and nullability semantics.
    /// </summary>
    /// <param name="property">The source property.</param>
    /// <returns>The corresponding property declaration.</returns>
    public PropertyDeclarationNode BuildProperty(IrProperty property)
    {
        var name = new IdentifierNode(property.Name);
        var type = BuildInterfacePropertyTypeNode(property);
        
        var comment = !string.IsNullOrEmpty(property.Description)
            ? new CommentNode(property.Description, CommentKind.JSDoc)
            : null;
        
        var defaultValue = property.Constraints?.DefaultValue != null
            ? BuildLiteralFromValue(property.Constraints.DefaultValue)
            : null;
        
        return new PropertyDeclarationNode(
            name: name,
            type: type,
            isRequired: property.IsRequired && !IsOptionalMemberType(property.Type),
            accessor: PropertyAccessorKind.Get,
            defaultValue: defaultValue,
            documentation: comment,
            constraints: property.Constraints);
    }
    
    /// <summary>
    /// Строит SourceFile из IrEnum
    /// </summary>
    public SourceFileNode BuildSourceFile(IrEnum enumModel, string? importPath = null, FileOrganization? fileOrganization = null)
    {
        OriginalNameMap.AddEnum(_orig, enumModel);
        var imports = BuildImports(Array.Empty<string>(), importPath, fileOrganization, FileCategory.Models, enumModel.Name);
        var declarations = BuildEnumDeclaration(enumModel);
        var exports = declarations.Select(d => new ExportNode(d, ExportKind.Named)).ToImmutableList();

        var enumFileBase = _namingConverter.ToCamelCase(enumModel.OriginalName);
        return new SourceFileNode(
            imports: imports,
            exports: exports,
            declarations: declarations,
            fileDocumentation: TypeScriptDoc.FileHeader(
                $"{enumFileBase}.ts",
                $"models/{enumFileBase}",
                string.IsNullOrWhiteSpace(enumModel.Description) ? $"{enumModel.Name} enum (string-literal union + values + type guard)." : enumModel.Description));
    }

    /// <summary>
    /// Строит DeclarationNode из IrEnum (может быть TypeAlias для union или enum)
    /// Также генерирует массив значений enum'а
    /// </summary>
    private IReadOnlyList<DeclarationNode> BuildEnumDeclaration(IrEnum enumModel)
    {
        var declarations = new List<DeclarationNode>();

        var allowed = string.Join(" | ", enumModel.Values.Select(v => enumModel.BaseType == IrEnumBaseType.String ? $"\"{v.Value}\"" : v.Value?.ToString()));
        var enumDoc = TypeScriptDoc.Block(
            string.IsNullOrWhiteSpace(enumModel.Description) ? $"{enumModel.Name} enumeration." : enumModel.Description,
            ("enum", enumModel.BaseType == IrEnumBaseType.String ? "{string}" : "{number}"),
            ("remarks", string.IsNullOrEmpty(allowed) ? null : $"Allowed values: {allowed}."));

        // Генерируем union type или enum (с JSDoc-документацией)
        var unionType = BuildEnumUnionType(enumModel);
        declarations.Add(new TypeAliasDeclarationNode(
            name: new IdentifierNode(enumModel.Name),
            type: unionType,
            documentation: enumDoc));
        
        // Генерируем массив значений enum'а
        var valuesArray = BuildEnumValuesArray(enumModel);
        declarations.Add(valuesArray);
        
        // Генерируем helper функцию для проверки значений enum'а
        var typeGuardFunction = BuildEnumTypeGuard(enumModel);
        declarations.Add(typeGuardFunction);
        
        return declarations;
    }
    
    /// <summary>
    /// Строит VariableDeclarationNode с массивом значений enum'а
    /// Например: export const ChessboardProjectPropertyTypeValues = ["Property", "Apartment", ...] as const;
    /// </summary>
    private VariableDeclarationNode BuildEnumValuesArray(IrEnum enumModel)
    {
        // Создаем массив литералов для значений enum'а
        var elements = enumModel.Values.Select(v =>
        {
            // Получаем строковое представление значения
            var valueStr = v.Value?.ToString()?.Trim() ?? "";
            
            if (enumModel.BaseType == IrEnumBaseType.String)
            {
                // Для строковых enum'ов создаем строковый литерал
                return new LiteralExpressionNode(valueStr, LiteralKind.String) as ExpressionNode;
            }
            else
            {
                // Для числовых enum'ов создаем числовой литерал
                if (long.TryParse(valueStr, out var longValue))
                {
                    return new LiteralExpressionNode(longValue, LiteralKind.Number) as ExpressionNode;
                }
                return new LiteralExpressionNode(valueStr, LiteralKind.String) as ExpressionNode;
            }
        }).ToImmutableList();
        
        // Создаем ArrayLiteralExpressionNode
        var arrayLiteral = new ArrayLiteralExpressionNode(elements);

        // Имя переменной: EnumName + "Values"
        var variableName = $"{enumModel.Name}Values";

        // Без явной аннотации типа: "as const satisfies readonly EnumType[]" сохраняет
        // литеральный кортеж (readonly ["A", "B"]), а не расширяет до readonly string[].
        // Это позволяет переиспользовать массив в z.enum(...) / v.picklist(...) /
        // Type.Union(...) внутри схем валидации, не дублируя значения.
        var enumTypeNode = new SimpleTypeNode(new IdentifierNode(enumModel.Name), isPrimitive: false);
        var satisfiesType = new ArrayTypeNode(enumTypeNode, isReadOnly: true);

        return new VariableDeclarationNode(
            name: new IdentifierNode(variableName),
            declarationKind: VariableDeclarationKind.Const,
            type: null,
            initializer: arrayLiteral,
            documentation: TypeScriptDoc.Block($"All valid {enumModel.Name} values, in declaration order."),
            isExported: true,
            asConstSatisfies: true,
            satisfiesType: satisfiesType);
    }
    
    private UnionTypeNode BuildEnumUnionType(IrEnum enumModel)
    {
        var types = enumModel.Values.Select(v =>
        {
            // Получаем строковое представление значения
            // v.Value может быть IOpenApiAny или другим типом, поэтому используем ToString()
            var valueStr = v.Value?.ToString()?.Trim() ?? "";
            
            if (enumModel.BaseType == IrEnumBaseType.String)
            {
                return new SimpleTypeNode(
                    new IdentifierNode("string"),
                    isPrimitive: true,
                    literalValue: valueStr) as TypeNode;
            }
            
            if (enumModel.BaseType == IrEnumBaseType.Integer && long.TryParse(valueStr, out var longValue))
            {
                return new SimpleTypeNode(
                    new IdentifierNode("number"),
                    isPrimitive: true,
                    literalValue: longValue) as TypeNode;
            }
            
            // Fallback - используем строку (не должно происходить для правильно определенных enum'ов)
            return BuildSimpleType("string");
        }).ToImmutableList();
        
        return new UnionTypeNode(types);
    }
    
    /// <summary>
    /// Строит helper функцию для проверки значений enum'а (type guard)
    /// Например: export const isChangeSource = (value: unknown): value is ChangeSource => ChangeSourceValues.includes(value as ChangeSource);
    /// </summary>
    private FunctionDeclarationNode BuildEnumTypeGuard(IrEnum enumModel)
    {
        // Имя функции: is{EnumName}
        var functionName = $"is{enumModel.Name}";
        
        // Параметр: value: unknown
        var valueParameter = new ParameterDeclarationNode(
            name: new IdentifierNode("value"),
            type: new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true));
        
        // Возвращаемый тип: value is EnumType (type predicate)
        var enumTypeNode = new SimpleTypeNode(new IdentifierNode(enumModel.Name), isPrimitive: false);
        
        // Тело функции: ChangeSourceValues.includes(value as ChangeSource)
        // 1. ChangeSourceValues - идентификатор массива значений
        var valuesArrayIdentifier = new IdentifierExpressionNode(new IdentifierNode($"{enumModel.Name}Values"));
        
        // 2. .includes - доступ к методу
        var includesMember = new MemberAccessExpressionNode(
            @object: valuesArrayIdentifier,
            member: new IdentifierNode("includes"));
        
        // 3. value as ChangeSource - приведение типа
        var valueIdentifier = new IdentifierExpressionNode(new IdentifierNode("value"));
        var castExpression = new CastExpressionNode(
            targetType: enumTypeNode,
            expression: valueIdentifier,
            useAsOperator: true); // Используем "as" оператор для TypeScript
        
        // 4. .includes(value as ChangeSource) - вызов метода
        var includesCall = new CallExpressionNode(
            target: includesMember,
            arguments: new[] { castExpression as ExpressionNode }.ToImmutableList());
        
        // 5. return ChangeSourceValues.includes(value as ChangeSource);
        var returnStatement = new ReturnStatementNode(value: includesCall);
        
        // 6. Блок с return statement
        var body = new BlockNode(statements: new[] { returnStatement }.ToImmutableList());
        
        // Создаем функцию как arrow function
        return new FunctionDeclarationNode(
            name: new IdentifierNode(functionName),
            returnType: enumTypeNode,
            parameters: new[] { valueParameter }.ToImmutableList(),
            body: body,
            isArrowFunction: true,
            documentation: new CommentNode($"Type guard function to check if a value is a valid {enumModel.Name}."),
            isExported: true,
            isTypePredicate: true,
            typePredicateParameterName: "value");
    }
    
    // Приватные вспомогательные методы для IR
    
    private TypeNode BuildPrimitiveType(IrPrimitiveType primitiveType)
    {
        var typeName = MapPrimitiveToTypeScript(primitiveType.PrimitiveKind);
        return new SimpleTypeNode(
            name: new IdentifierNode(typeName),
            isPrimitive: true,
            isNullable: false,
            isOptional: false);
    }
    
    private TypeNode BuildModelReference(IrModelReference modelReference)
    {
        return new SimpleTypeNode(
            name: new IdentifierNode(modelReference.ModelName),
            isPrimitive: false,
            isNullable: false,
            isOptional: false);
    }
    
    private TypeNode BuildArrayType(IrArrayType arrayType)
    {
        var elementType = BuildType(arrayType.ElementType);
        return new ArrayTypeNode(elementType, isNullable: false, isOptional: false);
    }
    
    private TypeNode BuildDictionaryType(IrDictionaryType dictionaryType)
    {
        var keyType = BuildType(dictionaryType.KeyType);
        var valueType = BuildType(dictionaryType.ValueType);
        return new DictionaryTypeNode(keyType, valueType, isNullable: false, isOptional: false);
    }
    
    private TypeNode BuildUnionType(IrUnionType unionType)
    {
        var types = unionType.Types.Select(BuildType).ToImmutableList();
        if (types.Count == 0)
            return new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true);
        if (types.Count == 1)
            return types[0];
        return new UnionTypeNode(types);
    }

    private TypeNode BuildIntersectionType(IrIntersectionType intersectionType)
    {
        var types = intersectionType.Types.Select(BuildType).ToImmutableList();
        if (types.Count == 0)
            return new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true);
        if (types.Count == 1)
            return types[0];
        return new IntersectionTypeNode(types);
    }
    
    private TypeNode BuildNullableType(IrNullableType nullableType)
    {
        var innerType = BuildType(nullableType.InnerType);
        return new NullableTypeNode(innerType);
    }
    
    private TypeNode BuildOptionalType(IrOptionalType optionalType)
    {
        var innerType = BuildType(optionalType.InnerType);
        return new OptionalTypeNode(innerType);
    }
    
    /// <summary>
    /// Проверяет, допускает ли TypeNode значение null (в объединении или как NullableType).
    /// </summary>
    private static bool IsTypeNullable(TypeNode type)
    {
        return type switch
        {
            NullableTypeNode => true,
            UnionTypeNode union when union.Types.Any(t =>
                t is NullableTypeNode ||
                t is SimpleTypeNode st && st.Name.Name is "null" or "undefined") => true,
            SimpleTypeNode simple when simple.IsNullable => true,
            ArrayTypeNode array when array.IsNullable => true,
            GenericTypeNode generic when generic.IsNullable => true,
            DictionaryTypeNode dict when dict.IsNullable => true,
            IntersectionTypeNode intersection when intersection.IsNullable => true,
            RawTypeNode raw when raw.IsNullable => true,
            _ => false
        };
    }
    
    private string MapPrimitiveToTypeScript(IrPrimitiveKind primitiveKind)
    {
        return primitiveKind switch
        {
            IrPrimitiveKind.String => "string",
            IrPrimitiveKind.Number => "number",
            IrPrimitiveKind.Integer => "number",
            IrPrimitiveKind.Long => "number",
            IrPrimitiveKind.Boolean => "boolean",
            IrPrimitiveKind.DateTime => "string", // ISO string в TypeScript
            IrPrimitiveKind.Date => "string",
            IrPrimitiveKind.Time => "string",
            IrPrimitiveKind.Binary => "Blob",
            IrPrimitiveKind.ByteArray => "Uint8Array",
            IrPrimitiveKind.Uuid => "string",
            IrPrimitiveKind.Any => "any",
            IrPrimitiveKind.Void => "void",
            IrPrimitiveKind.Null => "null",
            IrPrimitiveKind.File => "Blob",
            _ => "unknown"
        };
    }
    
    /// <summary>Collects all model references used by the emitted declaration.</summary>
    /// <param name="model">The model to inspect.</param>
    /// <returns>The names of referenced models.</returns>
    private IReadOnlyList<string> ExtractDependencies(IrModel model)
    {
        var dependencies = new HashSet<string>();
        
        var inherited = CollectInheritedProperties(model);
        foreach (var property in model.Properties)
        {
            ExtractDependenciesFromType(property.Type, dependencies);
            if (!inherited.TryGetValue(property.OriginalName, out var bases)) continue;
            foreach (var parentProperty in bases)
            {
                OriginalNameMap.AddType(_orig, parentProperty.Type);
                ExtractDependenciesFromType(parentProperty.Type, dependencies);
            }
        }
        
        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
            {
                ExtractDependenciesFromType(type, dependencies);
            }
        }
        
        return dependencies.ToList();
    }
    
    private void ExtractDependenciesFromType(IrType type, HashSet<string> dependencies)
    {
        switch (type)
        {
            case IrModelReference modelRef:
                dependencies.Add(modelRef.ModelName);
                break;
            case IrArrayType arrayType:
                ExtractDependenciesFromType(arrayType.ElementType, dependencies);
                break;
            case IrDictionaryType dictType:
                ExtractDependenciesFromType(dictType.KeyType, dependencies);
                ExtractDependenciesFromType(dictType.ValueType, dependencies);
                break;
            case IrUnionType unionType:
                foreach (var t in unionType.Types)
                    ExtractDependenciesFromType(t, dependencies);
                break;
            case IrIntersectionType intersectionType:
                foreach (var t in intersectionType.Types)
                    ExtractDependenciesFromType(t, dependencies);
                break;
            case IrNullableType nullableType:
                ExtractDependenciesFromType(nullableType.InnerType, dependencies);
                break;
            case IrOptionalType optionalType:
                ExtractDependenciesFromType(optionalType.InnerType, dependencies);
                break;
        }
    }
    
    /// <summary>
    /// Строит FunctionDeclarationNode из IrOperation
    /// </summary>
    public FunctionDeclarationNode BuildFunction(IrOperation operation, bool isExported = false)
    {
        var name = new IdentifierNode(operation.MethodName);
        
        var useResultType = UseParamsStyleEndpointTransport()
            && operation.Responses.Any(r => r.Type != null);
        var returnType = useResultType
            ? new GenericTypeNode(
                new IdentifierNode("Promise"),
                new[] { new SimpleTypeNode(new IdentifierNode(TypeScriptResultsAstBuilder.ComposeResultTypeName(_namingConverter, operation.MethodName, _options?.ResultTypePrefix, _options?.ResultTypeSuffix)), isPrimitive: false) }.ToImmutableList())
            : ExtractReturnType(operation.Responses);
        
        var comment = !string.IsNullOrEmpty(operation.Description)
            ? new CommentNode(operation.Description, CommentKind.JSDoc)
            : !string.IsNullOrEmpty(operation.Summary)
                ? new CommentNode(operation.Summary, CommentKind.JSDoc)
                : null;
        
        var parameters = BuildFunctionParameters(operation);
        var body = BuildFunctionBody(operation);
        
        return new FunctionDeclarationNode(
            name: name,
            returnType: returnType,
            parameters: parameters,
            body: body,
            isArrowFunction: false,
            isAsync: true,
            documentation: comment,
            isExported: isExported);
    }
    
    /// <summary>
    /// Строит список SourceFile из IrDocument (модели, enum'ы и опционально endpoint-клиенты).
    /// </summary>
    /// <param name="includeOperationEndpointFiles">Если false — не генерировать файлы операций (их пишет <see cref="TypeScriptCodeGenerator"/> с суффиксами .axios/.fetch).</param>
    public IReadOnlyList<SourceFileNode> BuildSourceFiles(
        IrDocument document,
        string? importPath = null,
        FileOrganization? fileOrganization = null,
        bool includeOperationEndpointFiles = false)
    {
        var sourceFiles = new List<SourceFileNode>();
        
        // Генерируем модели в топологическом порядке
        var orderedModelNames = document.TypeDependencies.TopologicalOrder;
        var modelsByName = document.Models.ToDictionary(m => m.Name);
        
        foreach (var modelName in orderedModelNames)
        {
            if (modelsByName.TryGetValue(modelName, out var model))
            {
                sourceFiles.Add(BuildSourceFile(model, importPath, fileOrganization));
            }
        }
        
        // Добавляем модели, которые не были в топологическом порядке
        foreach (var model in document.Models)
        {
            if (!orderedModelNames.Contains(model.Name))
            {
                sourceFiles.Add(BuildSourceFile(model, importPath, fileOrganization));
            }
        }
        
        // Генерируем enum'ы
        foreach (var enumModel in document.Enums)
        {
            sourceFiles.Add(BuildSourceFile(enumModel, importPath, fileOrganization));
        }
        
        if (includeOperationEndpointFiles)
        {
            foreach (var client in document.Clients)
            {
                foreach (var operation in client.Operations)
                {
                    var transport = InferDefaultEndpointTransport();
                    sourceFiles.Add(BuildSourceFileForOperation(operation, importPath, fileOrganization, transport));
                }
            }
        }

        return sourceFiles;
    }

    private bool UseParamsStyleEndpointTransport() =>
        _activeEndpointTransport is "axios" or "fetch";

    private string InferDefaultEndpointTransport()
    {
        if (_options?.Transports?.Contains("axios") == true)
            return "axios";
        return "fetch";
    }
    
    /// <summary>
    /// Строит SourceFile из IrClient (один файл со всеми операциями)
    /// </summary>
    public SourceFileNode BuildSourceFile(IrClient client, string? importPath = null, FileOrganization? fileOrganization = null)
    {
        OriginalNameMap.AddClient(_orig, client);
        var imports = BuildImports(ExtractDependencies(client), importPath, fileOrganization, FileCategory.Clients);
        var transport = InferDefaultEndpointTransport();
        _activeEndpointTransport = transport;
        try
        {
            var declarations = client.Operations.Select(op => (DeclarationNode)BuildFunction(op)).ToList();
            var exports = declarations.Select(d => new ExportNode(d, ExportKind.Named)).ToImmutableList();

            var clientFileBase = _namingConverter.ToCamelCase(client.Name);
            return new SourceFileNode(
                imports: imports,
                exports: exports,
                declarations: declarations,
                fileDocumentation: TypeScriptDoc.FileHeader(
                    $"{clientFileBase}.ts",
                    $"endpoints/{clientFileBase}",
                    $"HTTP client functions for the {client.Name} API."));
        }
        finally
        {
            _activeEndpointTransport = "axios";
        }
    }

    /// <summary>
    /// Строит SourceFile для одной операции (endpoints/operationName/operationName.axios.ts или .fetch.ts).
    /// </summary>
    public SourceFileNode BuildSourceFileForOperation(
        IrOperation operation,
        string? importPath = null,
        FileOrganization? fileOrganization = null,
        string transport = "axios")
    {
        var client = new IrClient { Name = "Temp", Operations = new[] { operation } };
        OriginalNameMap.AddOperation(_orig, operation);
        _activeEndpointTransport = transport;
        try
        {
            var resultsAstBuilder = new TypeScriptResultsAstBuilder(_namingConverter, BuildType, _options?.ResultTypeSuffix, _options?.ResultTypePrefix);
            var dependencies = UseParamsStyleEndpointTransport()
                ? Array.Empty<string>()
                : ExtractDependencies(client, excludeResponseModels: false);
            var imports = BuildImports(dependencies, importPath, fileOrganization, FileCategory.Clients, currentOperationName: operation.MethodName);

            imports = transport switch
            {
                "axios" => AddAxiosAndParamsImports(imports, operation, fileOrganization),
                "fetch" => AddFetchAndParamsImports(imports, operation, fileOrganization),
                _ => imports
            };

            var declarations = new List<DeclarationNode> { BuildFunction(operation, isExported: true) };
            var exports = declarations.Select(d => new ExportNode(d, ExportKind.Named)).ToImmutableList();

            var endpointFileBase = _namingConverter.ToCamelCase(operation.OriginalMethodName);
            return new SourceFileNode(
                imports: imports,
                exports: exports,
                declarations: declarations,
                fileDocumentation: TypeScriptDoc.FileHeader(
                    $"{endpointFileBase}.{transport}.ts",
                    $"endpoints/{endpointFileBase}",
                    $"{transport} client for the {operation.MethodName} operation."));
        }
        finally
        {
            _activeEndpointTransport = "axios";
        }
    }

    /// <summary>Builds the configured Axios default expression and its import.</summary>
    /// <returns>The configured client expression and its matching module import.</returns>
    private (ExpressionNode Instance, ImportNode Import) GetAxiosInstanceImport()
    {
        var path = _options?.Axios?.CustomInstancePath;
        var name = _options?.Axios?.CustomInstanceName;

        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(name))
        {
            return (new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("axiosDefaults")), new IdentifierNode(name)),
                new ImportNode(items: ImmutableList.Create(new IdentifierNode("axiosDefaults")),
                    from: NormalizeModuleSpecifier(path), kind: ImportKind.Namespace));
        }

        return (new IdentifierExpressionNode(new IdentifierNode("axios")), new ImportNode(
            items: ImmutableList.Create(new IdentifierNode("axios")), from: "axios", kind: ImportKind.Default));
    }

    /// <summary>
    /// Превращает сконфигурированный путь в TS import specifier: нормализует разделители и
    /// убирает завершающее расширение <c>.ts</c>/<c>.tsx</c> (specifier'ы в TS пишутся без него).
    /// </summary>
    private static string NormalizeModuleSpecifier(string path)
    {
        var specifier = path.Trim().Replace('\\', '/');
        if (specifier.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            return specifier[..^4];
        if (specifier.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
            return specifier[..^3];
        return specifier;
    }

    /// <summary>
    /// Добавляет импорты axios и params
    /// </summary>
    private IReadOnlyList<ImportNode> AddAxiosAndParamsImports(
        IReadOnlyList<ImportNode> existingImports,
        IrOperation operation,
        FileOrganization? fileOrganization)
    {
        var result = new List<ImportNode>();

        var (_, axiosInstanceImport) = GetAxiosInstanceImport();
        result.Add(axiosInstanceImport);

        result.Add(new ImportNode(
            items: new[] { new IdentifierNode("AxiosRequestConfig"), new IdentifierNode("AxiosInstance") }.ToImmutableList(),
            from: "axios",
            kind: ImportKind.Named,
            isTypeOnly: true));

        AddCommonEndpointImports(result, operation, fileOrganization);

        result.AddRange(existingImports);
        return result;
    }

    /// <summary>
    /// Импорты для fetch endpoint: params, results, zod — по смыслу как axios; опционально кастомный fetcher.
    /// </summary>
    private IReadOnlyList<ImportNode> AddFetchAndParamsImports(
        IReadOnlyList<ImportNode> existingImports,
        IrOperation operation,
        FileOrganization? fileOrganization)
    {
        var result = new List<ImportNode>();

        AddCommonEndpointImports(result, operation, fileOrganization);

        result.AddRange(existingImports);
        return result;
    }

    /// <summary>
    /// Общие импорты endpoint-файла (axios/fetch): типы params/results и схемы валидации
    /// выбранной библиотеки. Транспорт-специфичные импорты добавляются вызывающим.
    /// </summary>
    private void AddCommonEndpointImports(
        List<ImportNode> result,
        IrOperation operation,
        FileOrganization? fileOrganization)
    {
        var operationFileName = GetModelFileName(operation.MethodName);

        var paramsBuilder = new TypeScriptParamsAstBuilder(_namingConverter, BuildType);
        if (paramsBuilder.HasParameters(operation))
        {
            result.Add(new ImportNode(
                items: new[] { new IdentifierNode(paramsBuilder.ParamsInterfaceName(operation.MethodName)) }.ToImmutableList(),
                from: $"./{operationFileName}.params",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        var resultsBuilder = new TypeScriptResultsAstBuilder(_namingConverter, BuildType, _options?.ResultTypeSuffix, _options?.ResultTypePrefix);
        if (resultsBuilder.HasResponsesWithBody(operation))
        {
            result.Add(new ImportNode(
                items: new[] { new IdentifierNode(resultsBuilder.ResultTypeName(operation.MethodName)) }.ToImmutableList(),
                from: $"./{operationFileName}.results",
                kind: ImportKind.Named,
                isTypeOnly: true));
        }

        AddValidationSchemaImports(result, operation, fileOrganization);
    }

    /// <summary>Adds the validation imports needed by the operation's generated client.</summary>
    /// <param name="result">The imports to extend.</param>
    /// <param name="operation">The operation whose inputs and responses are validated.</param>
    /// <param name="fileOrganization">The configured output paths.</param>
    private void AddValidationSchemaImports(
        List<ImportNode> result,
        IrOperation operation,
        FileOrganization? fileOrganization)
    {
        if (!_validation.IsEnabled)
            return;

        var opName = _namingConverter.ToPascalCase(operation.MethodName);
        var operationFileName = GetModelFileName(operation.MethodName);
        // Model schemas are co-located with their models under modelsPath (not a separate schemas/ tree).
        var modelsPath = fileOrganization?.ModelsPath ?? "models";
        var hasParams = operation.PathParameters.Count > 0 || operation.QueryParameters.Count > 0;

        // Params: единообразно для всех библиотек импортируем именованные схемы из {op}.params.schema.ts.
        var paramsSchemaImports = new List<IdentifierNode>();
        if (operation.PathParameters.Count > 0)
            paramsSchemaImports.Add(new IdentifierNode($"{opName}RouteParamsSchema"));
        if (operation.QueryParameters.Count > 0)
            paramsSchemaImports.Add(new IdentifierNode($"{opName}QueryParamsSchema"));

        if (paramsSchemaImports.Count > 0)
        {
            result.Add(new ImportNode(
                items: paramsSchemaImports.ToImmutableList(),
                from: $"./{operationFileName}.params.schema",
                kind: ImportKind.Named));
        }

        // Вынесенные result-схемы (constructed-ответы: union/array/...) из {op}.results.schemas.
        var resultSchemaNames = ClientResultSchemaNames(operation);
        if (resultSchemaNames.Count > 0)
        {
            result.Add(new ImportNode(
                items: resultSchemaNames.OrderBy(n => n, StringComparer.Ordinal)
                    .Select(n => new IdentifierNode(n)).ToImmutableList(),
                from: $"./{operationFileName}.results.schemas",
                kind: ImportKind.Named));
        }

        // Model-схемы тела/голых-ответов. Схемы enum/model, на которые ссылаются типы параметров,
        // импортируются файлом {op}.params.schema.ts — клиенту они не нужны.
        var modelSchemaNames = ClientModelSchemaNames(operation);

        foreach (var modelName in modelSchemaNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            var modelFileName = GetModelFileName(modelName);
            result.Add(new ImportNode(
                items: new[] { new IdentifierNode($"{modelName}Schema") }.ToImmutableList(),
                from: $"../../{modelsPath}/{modelFileName}/{modelFileName}.schema",
                kind: ImportKind.Named));
        }

        if (_validation.UuidConstName is { } uuidConst
            && TypeScriptOptionIds.UuidFormat.Resolve(_options?.UuidFormat) == TypeScriptOptionIds.UuidFormat.Regex
            && operation.RequestBody is { Type: var bodyType }
            && ValidationTypes.IsValidatable(bodyType)
            && ValidationTypes.NeedsConstructedSchema(bodyType)
            && UuidFormatSupport.ContainsUuid(bodyType))
        {
            result.Add(new ImportNode(
                items: ImmutableList.Create(new IdentifierNode(uuidConst)),
                from: $"../../{modelsPath}/{TypeScriptOptionIds.UuidFormat.ConstFileName}",
                kind: ImportKind.Named));
        }

        // Runtime-импорты библиотеки (z / v / Type / Value) — зависят от генерируемых выражений.
        result.AddRange(_validation.RuntimeImports(
            ClientInlineConstructedTypes(operation), hasParams, ClientHasResponseValidation(operation)));
    }

    // Приватные вспомогательные методы для IR операций

    /// <summary>Builds endpoint parameters in transport-specific calling order.</summary>
    /// <param name="operation">The operation to expose.</param>
    /// <returns>Parameters including optional request controls and native client.</returns>
    private IReadOnlyList<ParameterDeclarationNode> BuildFunctionParameters(IrOperation operation)
    {
        if (UseParamsStyleEndpointTransport())
        {
            var paramsBuilder = new TypeScriptParamsAstBuilder(_namingConverter, BuildType);
            var endpointParams = new List<ParameterDeclarationNode>();
            if (paramsBuilder.HasParameters(operation))
            {
                var paramsTypeName = paramsBuilder.ParamsInterfaceName(operation.MethodName);
                endpointParams.Add(new ParameterDeclarationNode(
                        name: new IdentifierNode("params"),
                        type: new SimpleTypeNode(new IdentifierNode(paramsTypeName), isPrimitive: false, isNullable: false, isOptional: false),
                        isRequired: true));
            }

            var optionsTypeName = _activeEndpointTransport == "axios"
                ? "AxiosRequestConfig"
                : "RequestInit";
            var optionsType = new SimpleTypeNode(new IdentifierNode(optionsTypeName), isPrimitive: false, isNullable: false, isOptional: false);
            var optionsDoc = _activeEndpointTransport == "axios"
                ? "Optional Axios request config (headers, timeout, etc.)"
                : "Optional RequestInit (headers, credentials, etc.)";
            endpointParams.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("options"),
                type: optionsType,
                isRequired: false,
                documentation: new CommentNode(optionsDoc, CommentKind.JSDoc)));
            endpointParams.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("signal"),
                type: new SimpleTypeNode(new IdentifierNode("AbortSignal"), isPrimitive: false, isNullable: false, isOptional: false),
                isRequired: false,
                documentation: new CommentNode("Optional AbortSignal for request cancellation", CommentKind.JSDoc)));
            if (_activeEndpointTransport == "axios")
                endpointParams.Add(new ParameterDeclarationNode(
                    name: new IdentifierNode("client"),
                    type: new SimpleTypeNode(new IdentifierNode("AxiosInstance"), isPrimitive: false),
                    isRequired: false,
                    documentation: new CommentNode("Optional configured native HTTP client, including authentication and interceptors.", CommentKind.JSDoc)));
            return endpointParams.ToImmutableList();
        }

        var parameters = new List<ParameterDeclarationNode>();

        foreach (var param in operation.PathParameters)
        {
            var type = BuildType(param.Type);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode(param.Name),
                type: type,
                isRequired: true));
        }

        foreach (var param in operation.QueryParameters)
        {
            var type = BuildType(param.Type);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode(param.Name),
                type: type,
                isRequired: param.IsRequired));
        }

        foreach (var param in operation.HeaderParameters)
        {
            var type = BuildType(param.Type);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode(param.Name),
                type: type,
                isRequired: param.IsRequired));
        }

        if (operation.RequestBody != null)
        {
            var bodyType = BuildType(operation.RequestBody.Type);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("request"),
                type: bodyType,
                isRequired: operation.RequestBody.IsRequired));
        }

        return parameters.ToImmutableList();
    }
    
    /// <summary>
    /// Строит тело функции из IrOperation
    /// </summary>
    /// <param name="operation">IR операция</param>
    /// <returns>AST узел блока с statements или null для type-only функции</returns>
    private BlockNode? BuildFunctionBody(IrOperation operation)
    {
        return _activeEndpointTransport switch
        {
            "axios" => BuildAxiosFunctionBody(operation),
            "fetch" => BuildFetchFunctionBody(operation),
            _ => null
        };
    }
    
    /// <summary>Builds the Axios request and maps documented status variants.</summary>
    /// <param name="operation">The operation whose request and responses are emitted.</param>
    /// <returns>The endpoint function body.</returns>
    private BlockNode BuildAxiosFunctionBody(IrOperation operation)
    {
        var statements = new List<StatementNode>();
        var ctx = PrepareHttpParamSourcesWithOptionalZod(operation);
        statements.AddRange(ctx.LeadingStatements);

        var routeSource = ctx.RouteSource;
        var querySource = ctx.QuerySource;
        var bodySource = ctx.BodySource;
        var headersSource = ctx.HeadersSource;

        var (defaultAxiosInstance, _) = GetAxiosInstanceImport();
        var axiosInstanceType = new SimpleTypeNode(new IdentifierNode("AxiosInstance"), isPrimitive: false);
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("axiosInstance"),
            type: axiosInstanceType,
            initializer: new BinaryExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("client")),
                BinaryOperator.NullishCoalescing,
                defaultAxiosInstance)));
        statements.Add(CreateBlankLineStatement());

        var paramsSource = new Axios.Models.AxiosConfigParamsSource(routeSource, querySource, bodySource, headersSource);
        var axiosBuilder = new Axios.Ast.Builders.AxiosAstBuilder(_namingConverter, BuildType);
        var configNode = axiosBuilder.BuildAxiosConfigWithParams(operation, paramsSource);

        // Используем unknown — не добавляем импорт модели body, работаем с params
        var bodyTypeNode = new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: true);
        var axiosConfigGeneric = new GenericTypeNode(
            new IdentifierNode("AxiosRequestConfig"),
            new[] { bodyTypeNode }.ToImmutableList());

        var useResultType = operation.Responses.Any(r => r.Type != null);
        var operationReturnType = useResultType
            ? new SimpleTypeNode(new IdentifierNode("unknown"), isPrimitive: false)
            : ExtractReturnType(operation.Responses);

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("baseConfig"),
            type: axiosConfigGeneric,
            initializer: configNode));

        var optionsRef = new IdentifierExpressionNode(new IdentifierNode("options"));
        var signalRef = new IdentifierExpressionNode(new IdentifierNode("signal"));
        var emptyObject = new ObjectLiteralExpressionNode(ImmutableList<PropertyAssignmentNode>.Empty);
        var optionsSpread = new BinaryExpressionNode(optionsRef, BinaryOperator.NullishCoalescing, emptyObject);
        var signalSpread = new ConditionalExpressionNode(
            signalRef,
            new ObjectLiteralExpressionNode(new[]
            {
                new PropertyAssignmentNode(new IdentifierNode("signal"), signalRef)
            }.ToImmutableList()),
            emptyObject);

        var placeholderId = new IdentifierNode("_");
        var mergedConfigProps = new List<PropertyAssignmentNode>
        {
            new(placeholderId, new IdentifierExpressionNode(new IdentifierNode("baseConfig")), isSpread: true),
            new(placeholderId, optionsSpread, isSpread: true),
            new(placeholderId, signalSpread, isSpread: true)
        };
        if (useResultType)
        {
            mergedConfigProps.Add(new PropertyAssignmentNode(
                new IdentifierNode("validateStatus"),
                new ArrowFunctionExpressionNode(
                    ImmutableList<ParameterDeclarationNode>.Empty,
                    new LiteralExpressionNode(true, LiteralKind.Boolean))));
        }

        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("axiosConfig"),
            type: axiosConfigGeneric,
            initializer: new ObjectLiteralExpressionNode(mergedConfigProps.ToImmutableList(), type: null, metadata: null, preferMultilineFormat: true)));

        var axiosCall = new Axios.Ast.Nodes.AxiosCallExpressionNode(
            new IdentifierExpressionNode(new IdentifierNode("axiosInstance")),
            new IdentifierExpressionNode(new IdentifierNode("axiosConfig")));

        statements.Add(CreateBlankLineStatement());
        statements.Add(new VariableDeclarationStatementNode(
            new IdentifierNode("response"),
            type: null,
            initializer: new AwaitExpressionNode(axiosCall)));
        statements.Add(CreateBlankLineStatement());

        if (useResultType)
        {
            var responseDataRef = new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("response")),
                new IdentifierNode("data"));
            var responseStatusRef = new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("response")),
                new IdentifierNode("status"));
            var responsesWithType = operation.Responses
                .Where(r => int.TryParse(r.StatusCode, out var code) &&
                    (r.Type != null || code >= 200 && code < 300))
                .ToList();

            foreach (var resp in responsesWithType)
            {
                var statusCode = int.Parse(resp.StatusCode);
                var isSuccessful = statusCode >= 200 && statusCode < 300;
                var dataAccess = new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("response")),
                    new IdentifierNode("data"));
                var parseCall = resp.Type is null
                    ? new IdentifierExpressionNode(new IdentifierNode("undefined"))
                    : BuildStatusResultBody(operation, resp, dataAccess);

                var returnObj = new ObjectLiteralExpressionNode(new[]
                {
                    new PropertyAssignmentNode(new IdentifierNode("status"),
                        new LiteralExpressionNode(statusCode, LiteralKind.Number)),
                    new PropertyAssignmentNode(new IdentifierNode("body"), parseCall),
                    new PropertyAssignmentNode(new IdentifierNode("isSuccessful"),
                        new LiteralExpressionNode(isSuccessful, LiteralKind.Boolean)),
                    new PropertyAssignmentNode(new IdentifierNode("isFailed"),
                        new LiteralExpressionNode(!isSuccessful, LiteralKind.Boolean))
                }.ToImmutableList());

                var statusLiteral = new LiteralExpressionNode(statusCode, LiteralKind.Number);
                var condition = new BinaryExpressionNode(responseStatusRef, BinaryOperator.Equal, statusLiteral);

                statements.Add(new IfStatementNode(
                    condition,
                    new BlockNode(new[] { new ReturnStatementNode(returnObj) }.ToImmutableList()),
                    null));
            }

            statements.Add(new ReturnStatementNode(
                new ObjectLiteralExpressionNode(new[]
                {
                    new PropertyAssignmentNode(new IdentifierNode("status"), responseStatusRef),
                    new PropertyAssignmentNode(new IdentifierNode("body"), responseDataRef),
                    // Undocumented status → catch-all `…ResultOther`, typed as a literal failure
                    // (isSuccessful: false, isFailed: true) so the result union stays discriminable.
                    new PropertyAssignmentNode(new IdentifierNode("isSuccessful"),
                        new LiteralExpressionNode(false, LiteralKind.Boolean)),
                    new PropertyAssignmentNode(new IdentifierNode("isFailed"),
                        new LiteralExpressionNode(true, LiteralKind.Boolean))
                }.ToImmutableList())));
        }
        else if (operationReturnType is not SimpleTypeNode { Name.Name: "void" })
        {
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode("responseData"),
                type: null,
                initializer: new MemberAccessExpressionNode(
                    new IdentifierExpressionNode(new IdentifierNode("response")),
                    new IdentifierNode("data"))));

            if (_validation.IsEnabled)
            {
                var successResponse = operation.Responses.FirstOrDefault(r =>
                    int.TryParse(r.StatusCode, out var code) && code >= 200 && code < 300);
                if (successResponse?.Type is IrModelReference responseModelRef)
                {
                    var parseCall = CreateParseCall($"{responseModelRef.ModelName}Schema",
                        new IdentifierExpressionNode(new IdentifierNode("responseData")));
                    statements.Add(new VariableDeclarationStatementNode(
                        new IdentifierNode("parsedResponse"),
                        type: null,
                        initializer: parseCall));
                    statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("parsedResponse"))));
                }
                else
                {
                    statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("responseData"))));
                }
            }
            else
            {
                statements.Add(new ReturnStatementNode(new IdentifierExpressionNode(new IdentifierNode("responseData"))));
            }
        }

        return new BlockNode(statements.ToImmutableList());
    }

    /// <summary>
    /// Создаёт statement для пустой строки (форматирование)
    /// </summary>
    private static BlankLineStatementNode CreateBlankLineStatement()
    {
        return new BlankLineStatementNode();
    }

    /// <summary>
    /// Создаёт выражение Schema.parse(input)
    /// </summary>
    /// <summary>
    /// Валидирует <paramref name="input"/> импортируемой именованной схемой <paramref name="schemaName"/>
    /// согласно выбранной библиотеке валидации (делегирует в стратегию).
    /// </summary>
    private ExpressionNode CreateParseCall(string schemaName, ExpressionNode input) =>
        _validation.ValidateNamed(schemaName, input);

    /// <summary>
    /// Строит выражение тела (body) для per-status result-ветки клиента. Тип, требующий построения
    /// схемы (union/array/...), валидируется ВЫНЕСЕННОЙ именованной схемой <c>{Op}Result{Status}Schema</c>
    /// из <c>{op}.results.schemas.ts</c> (строится один раз, а не на каждый вызов); голая ссылка на модель —
    /// напрямую <c>{Model}Schema</c>. Для unknown/void и при выключенной валидации возвращает сырые данные.
    /// </summary>
    private ExpressionNode BuildStatusResultBody(IrOperation operation, IrResponse response, ExpressionNode dataRef)
    {
        var type = response.Type!;
        if (!_validation.IsEnabled || !ValidationTypes.IsValidatable(type))
            return dataRef;

        if (ValidationTypes.NeedsConstructedSchema(type))
            return _validation.ValidateNamed(ResultSchemaName(operation.MethodName, response.StatusCode), dataRef);

        return _validation.Validate(type, dataRef);
    }

    private string ResultSchemaName(string operationName, string statusCode)
        => TypeScriptResultsAstBuilder.ResultVariantName(_namingConverter, operationName, statusCode) + "Schema";

    /// <summary>
    /// Типы, которые КЛИЕНТ строит inline (тело запроса). Ответы используют именованные/вынесенные схемы.
    /// </summary>
    private static List<IrType> ClientInlineConstructedTypes(IrOperation operation)
    {
        var types = new List<IrType>();
        if (operation.RequestBody != null && ValidationTypes.IsValidatable(operation.RequestBody.Type))
            types.Add(operation.RequestBody.Type);
        return types;
    }

    /// <summary>Есть ли валидируемые типизированные ответы (любого статуса).</summary>
    private static bool ClientHasResponseValidation(IrOperation operation)
        => operation.Responses.Any(r => r.Type != null && int.TryParse(r.StatusCode, out _) && ValidationTypes.IsValidatable(r.Type!));

    /// <summary>
    /// Model-схемы, импортируемые КЛИЕНТОМ: тело (рекурсивно) + ответы-голые-ссылки. Ответы с
    /// constructed-схемой импортируют свою result-схему (их члены живут в <c>{op}.results.schemas.ts</c>).
    /// </summary>
    private static HashSet<string> ClientModelSchemaNames(IrOperation operation)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (operation.RequestBody != null && ValidationTypes.IsValidatable(operation.RequestBody.Type))
            ValidationTypes.CollectModelSchemaNames(operation.RequestBody.Type, names);

        foreach (var response in operation.Responses)
            if (response.Type != null && int.TryParse(response.StatusCode, out _)
                && ValidationTypes.IsValidatable(response.Type!) && !ValidationTypes.NeedsConstructedSchema(response.Type!))
                ValidationTypes.CollectModelSchemaNames(response.Type!, names);

        return names;
    }

    /// <summary>Имена вынесенных result-схем (для constructed-ответов), импортируемых клиентом.</summary>
    private List<string> ClientResultSchemaNames(IrOperation operation)
        => operation.Responses
            .Where(r => r.Type != null && int.TryParse(r.StatusCode, out _) && ValidationTypes.NeedsConstructedSchema(r.Type!))
            .Select(r => ResultSchemaName(operation.MethodName, r.StatusCode))
            .ToList();

    private TypeNode ExtractReturnType(IReadOnlyList<IrResponse> responses)
    {
        // Ищем первый успешный ответ (200-299)
        var successResponse = responses.FirstOrDefault(r =>
        {
            if (int.TryParse(r.StatusCode, out var code))
                return code >= 200 && code < 300;
            return false;
        });
        
        if (successResponse?.Type != null)
        {
            return BuildType(successResponse.Type);
        }
        
        // Если нет успешного ответа, возвращаем void
        return new SimpleTypeNode(
            name: new IdentifierNode("void"),
            isPrimitive: true,
            isNullable: false,
            isOptional: false);
    }
    
    private IReadOnlyList<string> ExtractDependencies(IrClient client, bool excludeResponseModels = false)
    {
        var dependencies = new HashSet<string>();
        
        foreach (var operation in client.Operations)
        {
            // Зависимости из параметров
            foreach (var param in operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters))
            {
                ExtractDependenciesFromType(param.Type, dependencies);
            }
            
            // Зависимости из request body
            if (operation.RequestBody != null)
            {
                ExtractDependenciesFromType(operation.RequestBody.Type, dependencies);
            }
            
            // Зависимости из responses (пропускаем при excludeResponseModels — используется Result type)
            if (!excludeResponseModels)
            {
                foreach (var response in operation.Responses)
                {
                    if (response.Type != null)
                    {
                        ExtractDependenciesFromType(response.Type, dependencies);
                    }
                }
            }
        }
        
        return dependencies.ToList();
    }
}
