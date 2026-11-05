using System.Collections.Generic;
using System.Linq;
using System.Text;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Types;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.JsDoc;
using ValIO.Generators.TypeScript.Options;
using ValIO.Generators.TypeScript.Zod.Ast.Builders;
using ValIO.Generators.TypeScript.Zod.Ast.Nodes;
using ValIO.Generators.TypeScript.Zod.Ast.Renderers;

namespace ValIO.Generators.TypeScript.Zod;

/// <summary>
/// Рендерер для генерации Zod схем валидации через IR и AST
/// </summary>
public class ZodSchemaRenderer
{
    private readonly INamingConverter _namingConverter;
    private readonly Func<IrType, ValIO.Core.Ast.Nodes.TypeNode> _typeBuilder;
    private readonly bool _useBigInt;
    private readonly string _zodImportPath;
    private readonly string _modelsPath;
    private readonly string _uuidFormat;

    /// <summary>Карта «affix-имя → исходное имя» для путей импорта (тождество без affix).</summary>
    private readonly Dictionary<string, string> _orig = new(StringComparer.Ordinal);

    /// <summary>
    /// Справочник моделей документа (по <see cref="ValIO.Core.IR.IrModel.Name"/>) для резолва наследования
    /// allOf в собираемых z-схемах. Устанавливается шагом; по умолчанию пуст.
    /// </summary>
    public IReadOnlyDictionary<string, ValIO.Core.IR.IrModel> ModelsByName { get; set; }
        = new Dictionary<string, ValIO.Core.IR.IrModel>();

    public ZodSchemaRenderer(
        INamingConverter namingConverter,
        Func<IrType, TypeNode> typeBuilder,
        bool useBigInt = false,
        string zodVersion = "zod3",
        string modelsPath = "models",
        string? uuidFormat = null)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
        _useBigInt = useBigInt;
        _zodImportPath = zodVersion == "zod4" ? "zod/v4" : "zod";
        _modelsPath = string.IsNullOrWhiteSpace(modelsPath) ? "models" : modelsPath;
        _uuidFormat = TypeScriptOptionIds.UuidFormat.Resolve(uuidFormat);
    }

    // `ZodSchema` — только тип: под `verbatimModuleSyntax` его импортируем type-only (иначе TS1485),
    // отдельной строкой от значения `z`.
    /// <summary>Emits value and type imports with the platform line separator.</summary>
    /// <value>The two Zod import declarations.</value>
    private string ZodImportLine => $"import {{ z }} from '{_zodImportPath}';{Environment.NewLine}import type {{ ZodSchema }} from '{_zodImportPath}';";

    private bool UsesUuidRegex => _uuidFormat == TypeScriptOptionIds.UuidFormat.Regex;

    /// <summary>Prepends the standard generated-file JSDoc header (@file/@module/@generated) to the buffer.</summary>
    private static void AppendFileHeader(StringBuilder sb, string fileName, string module, string description)
    {
        CommentGenerator.GenerateJSDocComment(sb, 0, TypeScriptDoc.FileHeader(fileName, module, description).Text);
        sb.AppendLine();
    }

    private static string FormatsImportLine(string relativeDir) =>
        $"import {{ {TypeScriptOptionIds.UuidFormat.RegexConstName} }} from \"{relativeDir}/{TypeScriptOptionIds.UuidFormat.ConstFileName}\";";
    
    /// <summary>
    /// Рендерит Zod схему для IR модели.
    /// </summary>
    /// <remarks>
    /// Design note — the schema is emitted as <c>export const XSchema: ZodSchema&lt;X&gt; = …</c>, annotated
    /// against the <b>separately generated</b> interface <c>X</c>, instead of deriving the type from the schema
    /// via <c>export type X = z.infer&lt;typeof XSchema&gt;</c>. Reason: the generated interface carries
    /// <b>per-field JSDoc</b> sourced from OpenAPI <c>description</c>s (and hover docs in editors), whereas a
    /// <c>z.infer</c>-derived type is a structural alias that <b>cannot</b> hold per-property JSDoc — those
    /// comments would be lost. Keeping interface + schema as two artifacts (the schema annotated with the
    /// interface) preserves documentation while the <c>ZodSchema&lt;X&gt;</c> annotation still guarantees the
    /// schema's output type matches the interface at compile time.
    /// </remarks>
    /// <param name="model">IR модель.</param>
    /// <param name="importPath">Путь для импортов (опционально).</param>
    /// <param name="currentFileName">Имя текущего файла (для вычисления относительных путей).</param>
    /// <param name="schemasPath">Путь к папке со схемами (для вычисления относительных путей).</param>
    /// <returns>TypeScript код Zod схемы.</returns>
    public string RenderFromIr(IrModel model, string? importPath = null, string? currentFileName = null, string? schemasPath = null)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));
        
        var sb = new StringBuilder(4096);

        ValIO.Generators.TypeScript.Ast.Builders.OriginalNameMap.AddModel(_orig, model);

        var schemaName = $"{model.Name}Schema";
        var modelFileName = _namingConverter.ToCamelCase(model.OriginalName);

        AppendFileHeader(sb, $"{modelFileName}.schema.ts", $"{_modelsPath}/{modelFileName}", $"Zod validation schema for {model.Name}.");
        sb.AppendLine(ZodImportLine);
        sb.AppendLine();

        // Путь к модели от файла схемы. Если importPath передан — используем его как
        // полный путь к файлу модели (без расширения), иначе строим относительный.
        var modelImportPath = !string.IsNullOrEmpty(importPath)
            ? $"{NormalizePath(importPath)}/{modelFileName}/{modelFileName}"
            : BuildModelImportPathFromSchema(currentFileName, schemasPath, modelFileName);

        var hasConstraints = model.Properties.Any(p => p.Constraints != null && HasAnyConstraint(p.Constraints));
        if (hasConstraints)
        {
            sb.AppendLine($"import type {{ {model.Name} }} from \"{modelImportPath}\";");
            sb.AppendLine($"import {{ {model.Name}Constraints }} from \"{modelImportPath}\";");
        }
        else
        {
            sb.AppendLine($"import type {{ {model.Name} }} from \"{modelImportPath}\";");
        }

        // Shared UUID regex constant (regex mode): schema sits in {schemas}/{name}/ → ../formats.
        if (UsesUuidRegex && UuidFormatSupport.ModelUsesUuid(model))
            sb.AppendLine(FormatsImportLine(".."));

        var dependencies = ExtractDependencies(model);
        if (dependencies.Count > 0)
        {
            foreach (var dep in dependencies.OrderBy(d => d))
            {
                var depFileName = SchemaNameToFileName(dep);
                var relativePath = CalculateImportPath(currentFileName, depFileName, schemasPath, importPath: null);
                sb.AppendLine($"import {{ {dep} }} from '{relativePath}';");
            }
            sb.AppendLine();
        }
        
        if (!string.IsNullOrEmpty(model.Description))
        {
            CommentGenerator.GenerateJSDocComment(sb, 0, model.Description);
        }
        
        var zodBuilder = new ZodAstBuilder(_namingConverter, _typeBuilder, _useBigInt, _uuidFormat)
        {
            ModelsByName = ModelsByName
        };
        var zodSchema = zodBuilder.BuildZodSchemaFromIrModel(model);
        
        var zodRenderer = new ZodAstRenderer();
        var zodSchemaCode = zodRenderer.RenderSchema(zodSchema);
        
        CodeFormatter.AppendLine(sb, 0, $"export const {schemaName}: ZodSchema<{model.Name}> = {zodSchemaCode};");
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Рендерит Zod схему для IR enum
    /// </summary>
    /// <param name="enumModel">IR enum</param>
    /// <param name="importPath">Путь для импортов (опционально)</param>
    /// <returns>TypeScript код Zod enum схемы</returns>
    public string RenderEnumFromIr(IrEnum enumModel, string? importPath = null, string? currentFileName = null, string? schemasPath = null)
    {
        if (enumModel == null)
            throw new ArgumentNullException(nameof(enumModel));
        
        var sb = new StringBuilder();

        ValIO.Generators.TypeScript.Ast.Builders.OriginalNameMap.AddEnum(_orig, enumModel);

        var schemaName = $"{enumModel.Name}Schema";
        var typeName = enumModel.Name;
        var enumFileName = _namingConverter.ToCamelCase(enumModel.OriginalName);
        var isStringEnum = enumModel.BaseType == IrEnumBaseType.String;
        var constArrayName = $"{typeName}Values";

        AppendFileHeader(sb, $"{enumFileName}.schema.ts", $"{_modelsPath}/{enumFileName}", $"Zod validation schema for the {typeName} enum.");
        sb.AppendLine(ZodImportLine);
        sb.AppendLine();
        var enumImportPath = !string.IsNullOrEmpty(importPath)
            ? $"{NormalizePath(importPath)}/{enumFileName}/{enumFileName}"
            : BuildModelImportPathFromSchema(currentFileName, schemasPath, enumFileName);
        sb.AppendLine($"import type {{ {typeName} }} from \"{enumImportPath}\";");

        if (isStringEnum)
        {
            // Переиспользуем массив значений из модели enum вместо повторного объявления:
            // z.enum принимает кортеж строковых литералов, который даёт {Name}Values из модели.
            sb.AppendLine($"import {{ {constArrayName} }} from \"{enumImportPath}\";");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(enumModel.Description))
            {
                CommentGenerator.GenerateJSDocComment(sb, 0, enumModel.Description);
            }

            CodeFormatter.AppendLine(sb, 0, $"export const {schemaName}: ZodSchema<{typeName}> = z.enum({constArrayName});");
            return sb.ToString();
        }

        // Числовые enum: z.enum работает только со строками, поэтому строим схему через ZodAstBuilder.
        // Массив значений {Name}Values здесь НЕ объявляем и НЕ реэкспортируем — единственный источник
        // в модели enum (models/{enum}/{enum}.ts). Иначе баррел index.ts реэкспортирует {Name}Values
        // из обоих модулей → TS2308 (ambiguous re-export). Схема не использует этот массив.
        sb.AppendLine();

        if (!string.IsNullOrEmpty(enumModel.Description))
        {
            CommentGenerator.GenerateJSDocComment(sb, 0, enumModel.Description);
        }

        var zodBuilder = new ZodAstBuilder(_namingConverter, _typeBuilder, _useBigInt, _uuidFormat);
        var zodEnum = zodBuilder.BuildZodEnum(enumModel);
        var zodRenderer = new ZodAstRenderer();
        var zodEnumCode = zodRenderer.VisitZodEnum(zodEnum);

        CodeFormatter.AppendLine(sb, 0, $"export const {schemaName}: ZodSchema<{typeName}> = {zodEnumCode};");

        return sb.ToString();
    }
    
    /// <summary>
    /// Извлекает зависимости из IR модели
    /// </summary>
    private static bool HasAnyConstraint(IrPropertyConstraints c)
    {
        return c.Min.HasValue || c.Max.HasValue || c.ExclusiveMin.HasValue || c.ExclusiveMax.HasValue
            || c.MinLength.HasValue || c.MaxLength.HasValue || !string.IsNullOrEmpty(c.Pattern);
    }

    private IReadOnlyList<string> ExtractDependencies(IrModel model)
    {
        var dependencies = new HashSet<string>();
        
        foreach (var property in model.Properties)
        {
            ExtractDependenciesFromIrType(property.Type, dependencies, model.Name);
        }
        
        if (model.Composition != null)
        {
            foreach (var type in model.Composition.Types)
            {
                ExtractDependenciesFromIrType(type, dependencies, model.Name);
            }
        }
        
        return dependencies.ToList();
    }
    
    /// <summary>
    /// Рекурсивно извлекает зависимости из IR типа
    /// </summary>
    private void ExtractDependenciesFromIrType(
        ValIO.Core.IR.Types.IrType irType, 
        HashSet<string> dependencies, 
        string currentModelName)
    {
        switch (irType)
        {
            case ValIO.Core.IR.Types.IrModelReference modelRef:
                if (!modelRef.ModelName.Equals(currentModelName, StringComparison.OrdinalIgnoreCase))
                {
                    var schemaName = $"{modelRef.ModelName}Schema";
                    dependencies.Add(schemaName);
                }
                break;
            case ValIO.Core.IR.Types.IrArrayType arrayType:
                ExtractDependenciesFromIrType(arrayType.ElementType, dependencies, currentModelName);
                break;
            case ValIO.Core.IR.Types.IrDictionaryType dictType:
                ExtractDependenciesFromIrType(dictType.KeyType, dependencies, currentModelName);
                ExtractDependenciesFromIrType(dictType.ValueType, dependencies, currentModelName);
                break;
            case ValIO.Core.IR.Types.IrUnionType unionType:
                foreach (var t in unionType.Types)
                    ExtractDependenciesFromIrType(t, dependencies, currentModelName);
                break;
            case ValIO.Core.IR.Types.IrIntersectionType intersectionType:
                foreach (var t in intersectionType.Types)
                    ExtractDependenciesFromIrType(t, dependencies, currentModelName);
                break;
            case ValIO.Core.IR.Types.IrNullableType nullableType:
                ExtractDependenciesFromIrType(nullableType.InnerType, dependencies, currentModelName);
                break;
            case ValIO.Core.IR.Types.IrOptionalType optionalType:
                ExtractDependenciesFromIrType(optionalType.InnerType, dependencies, currentModelName);
                break;
        }
    }
    
    private string SchemaNameToFileName(string schemaName)
    {
        var typeName = schemaName.EndsWith("Schema", StringComparison.OrdinalIgnoreCase)
            ? schemaName.Substring(0, schemaName.Length - 6)
            : schemaName;

        if (string.IsNullOrEmpty(typeName))
            return schemaName.ToLowerInvariant();

        // Имя файла — от исходного (до affix) имени, чтобы путь не менялся.
        return _namingConverter.ToCamelCase(_orig.GetValueOrDefault(typeName, typeName));
    }
    
    /// <summary>
    /// Builds the relative import path from a Zod schema file to its model/enum interface file.
    /// </summary>
    /// <remarks>
    /// Model schemas are co-located with the model: the schema lives at
    /// <c>{modelsPath}/{name}/{name}.schema.ts</c> (or <c>{modelsPath}/{name}.schema.ts</c> when
    /// <c>separateModels</c> is false) right next to <c>{name}.ts</c>, so the interface is always a
    /// sibling in the same directory — <c>./{name}</c>.
    /// </remarks>
    /// <param name="currentFileName">The schema's own file-name stem (unused; retained for symmetry).</param>
    /// <param name="schemasPath">The schema base directory (unused; retained for symmetry).</param>
    /// <param name="modelFileName">The model/enum file-name stem.</param>
    /// <returns>A same-directory relative specifier, e.g. <c>./coefficientsInlineModel</c>.</returns>
    private string BuildModelImportPathFromSchema(string? currentFileName, string? schemasPath, string modelFileName)
        => $"./{modelFileName}";

    /// <summary>
    /// Вычисляет относительный путь для импорта схемы.
    /// При структуре models/modelName/ — из currentModel к depModel: ../depModel/depModel.schema
    /// </summary>
    private string CalculateImportPath(string? currentFileName, string dependencyFileName, string? schemasPath, string? importPath)
    {
        if (!string.IsNullOrEmpty(importPath))
        {
            var normalizedPath = NormalizePath(importPath);
            return $"{normalizedPath}/{dependencyFileName}/{dependencyFileName}.schema";
        }
        
        if (!string.IsNullOrEmpty(schemasPath) && !string.IsNullOrEmpty(currentFileName))
        {
            var modelsPath = NormalizePath(schemasPath);
            var currentBasePath = $"{modelsPath}/{currentFileName}";
            var dependencyDir = $"{modelsPath}/{dependencyFileName}";
            var relativePath = CalculateRelativePath(currentBasePath, dependencyDir, $"{dependencyFileName}.schema");
            return NormalizePath(relativePath);
        }
        
        return $"./{dependencyFileName}.schema";
    }
    
    /// <summary>
    /// Вычисляет относительный путь между двумя файлами
    /// </summary>
    private string CalculateRelativePath(string fromPath, string toPath, string fileName)
    {
        fromPath = NormalizePath(fromPath);
        toPath = NormalizePath(toPath);
        
        // Если пути одинаковые, возвращаем просто имя файла
        if (fromPath.Equals(toPath, StringComparison.OrdinalIgnoreCase))
        {
            return $"./{fileName}";
        }
        
        // Упрощенная логика: если оба пути в одной папке, используем относительный путь
        var fromParts = fromPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var toParts = toPath.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        
        // Находим общий префикс
        int commonLength = 0;
        int minLength = Math.Min(fromParts.Length, toParts.Length);
        for (int i = 0; i < minLength; i++)
        {
            if (fromParts[i].Equals(toParts[i], StringComparison.OrdinalIgnoreCase))
            {
                commonLength++;
            }
            else
            {
                break;
            }
        }
        
        // Если есть общий префикс, строим относительный путь
        if (commonLength > 0)
        {
            var upLevels = fromParts.Length - commonLength;
            var downPath = string.Join("/", toParts.Skip(commonLength));
            var relativePath = string.Join("/", Enumerable.Repeat("..", upLevels));
            if (!string.IsNullOrEmpty(downPath))
            {
                relativePath = string.IsNullOrEmpty(relativePath) ? downPath : $"{relativePath}/{downPath}";
            }
            return $"{relativePath}/{fileName}";
        }
        
        // Если нет общего префикса, используем абсолютный путь
        return $"{toPath}/{fileName}";
    }
    
    /// <summary>
    /// Нормализует путь (убирает двойные слеши, заменяет обратные слеши)
    /// </summary>
    private string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        
        return path
            .Replace('\\', '/')
            .Replace("//", "/")
            .TrimEnd('/');
    }

}
