using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Http;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Constraints;
using ValIO.Core.IR.Enums;
using ValIO.Core.IR.Types;
using ValIO.Core.IR.Utilities;
using ValIO.Generators.CSharp.Ast.Nodes;
using ValIO.Generators.CSharp.Models;
using ValIO.Generators.CSharp.Options;

namespace ValIO.Generators.CSharp.Ast.Builders;

/// <summary>
/// Builds C# declarations using one required, immutable document context.
/// </summary>
public class CSharpAstBuilder : AstBuilderBase, ICSharpAstBuilder
{
    /// <summary>Converts external names to valid C# identifiers.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Controls C# declarations and optional generated features.</summary>
    private readonly CSharpGenerationOptions _options;
    /// <summary>Provides the single IR-to-C# type mapping used throughout generation.</summary>
    private readonly CSharpIrTypeMapper _typeMapper = new();
    /// <summary>Identifies the namespace used to qualify enum defaults that can shadow property names.</summary>
    private string? _modelNamespace;

    /// <summary>Initializes a builder with validated language options and shared naming rules.</summary>
    /// <param name="namingConverter">The C# identifier converter.</param>
    /// <param name="documentContext">The immutable indexes shared by this document's generation steps.</param>
    /// <param name="options">The canonical generator options, or default settings.</param>
    /// <exception cref="ArgumentNullException">The naming converter or document context is null.</exception>
    /// <exception cref="ArgumentException">A requested generation mode is not implemented.</exception>
    public CSharpAstBuilder(INamingConverter namingConverter, IrDocumentContext documentContext, CSharpGenerationOptions? options = null)
        : base(documentContext)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _options = options ?? new CSharpGenerationOptions();
        var errors = _options.ValidateSupportedFeatures();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(options));
    }
    
    public NamespaceDeclarationNode BuildNamespace(string name, IReadOnlyList<DeclarationNode> declarations)
    {
        var nameIdentifier = new IdentifierNode(name);
        return new NamespaceDeclarationNode(
            name: nameIdentifier,
            declarations: declarations.ToImmutableList());
    }
    
    // Приватные вспомогательные методы
    
    private IReadOnlyList<UsingDirectiveNode> BuildUsingDirectives(IReadOnlyList<string> dependencies)
    {
        return dependencies
            .Select(dep => new UsingDirectiveNode(new IdentifierNode(dep)))
            .ToImmutableList();
    }
    
    private SimpleTypeNode BuildSimpleType(string typeName)
    {
        return new SimpleTypeNode(new IdentifierNode(typeName), isPrimitive: false);
    }

    /// <summary>Places declarations in a named namespace or directly in the global compilation unit.</summary>
    /// <param name="usings">The imports required by the declarations.</param>
    /// <param name="declarations">The declarations emitted by this file.</param>
    /// <param name="namespace">The namespace name, or null/empty to use the global namespace.</param>
    /// <returns>A unit that never represents the global namespace as an empty identifier.</returns>
    private CompilationUnitNode CreateCompilationUnit(
        IReadOnlyList<UsingDirectiveNode> usings, IReadOnlyList<DeclarationNode> declarations, string? @namespace) =>
        string.IsNullOrEmpty(@namespace)
            ? new CompilationUnitNode(usings: usings, declarations: declarations.ToImmutableList())
            : new CompilationUnitNode(usings: usings,
                namespaces: new[] { BuildNamespace(@namespace, declarations) }.ToImmutableList());
    
    // IR → AST методы
    
    /// <summary>Builds C# syntax using the canonical language type mapper.</summary>
    /// <param name="irType">The normalized IR type.</param>
    /// <returns>The mapped C# type node.</returns>
    public TypeNode BuildType(IrType irType) => _typeMapper.MapType(irType);
    
    /// <summary>Builds a model compilation unit with qualified defaults and required imports.</summary>
    /// <param name="model">The normalized model.</param>
    /// <param name="namespace">The generated namespace, or the global namespace.</param>
    /// <returns>The complete model compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnit(IrModel model, string? @namespace = null)
    {
        _modelNamespace = @namespace;
        var usings = BuildUsingDirectives(ExtractDependencies(model));
        var classNode = BuildClass(model);
        return CreateCompilationUnit(usings, [classNode], @namespace);
    }
    
    /// <summary>Builds a class using the shared inheritance or flattening plan.</summary>
    /// <param name="model">The model whose effective properties are generated.</param>
    /// <returns>A complete class declaration with coherent equality members.</returns>
    public ClassDeclarationNode BuildClass(IrModel model)
    {
        if (model.Composition is { } composition &&
            (composition.Kind != IrCompositionKind.AllOf || composition.Types.Any(type => type is not IrModelReference)))
            throw new NotSupportedException($"C# model '{model.Name}' requires a composed value representation; only allOf model references are supported.");
        var name = new IdentifierNode(model.Name);
        var comment = !string.IsNullOrEmpty(model.Description)
            ? new CommentNode(model.Description, CommentKind.Xml)
            : null;
        
        var inheritance = IrInheritancePlan.Resolve(model, ModelsByName);
        var effectiveModel = model with { Properties = inheritance.EffectiveProperties };
        var properties = inheritance.EffectiveProperties
            .Select(property => BuildProperty(property, effectiveModel))
            .ToList<DeclarationNode>();
        var baseTypes = new List<TypeNode>();
        if (inheritance.BaseModelName is { } baseModelName)
        {
            baseTypes.Add(BuildSimpleType(baseModelName));
        }

        var hasDerivedModels = ModelsByName.Values.Any(candidate =>
            candidate.Name != model.Name &&
            IrInheritancePlan.Resolve(candidate, ModelsByName).BaseModelName == model.Name);
        var shouldBeSealed = _options.UseSealed && !hasDerivedModels;
        
        // Добавляем вложенные классы с константами валидации, если включено
        if (_options.GenerateValidationConstants)
        {
            var constantsClasses = BuildValidationConstantsClasses(effectiveModel);
            properties.AddRange(constantsClasses.Cast<DeclarationNode>());
        }
        
        // Добавляем интерфейс IEquatable<TModel> и методы Equals/GetHashCode, если включено
        if (_options.GenerateEqualityMembers)
        {
            // Добавляем интерфейс IEquatable<TModel>
            var modelType = new SimpleTypeNode(new IdentifierNode(model.Name), isPrimitive: false);
            var equatableInterface = new GenericTypeNode(
                name: new IdentifierNode("IEquatable"),
                typeArguments: new[] { modelType }.ToImmutableList(),
                isNullable: false);
            baseTypes.Add(equatableInterface);
            
            var equalityMethods = BuildEqualityMembers(effectiveModel, baseTypes);
            properties.AddRange(equalityMethods.Cast<DeclarationNode>());
        }
        
        return new ClassDeclarationNode(
            name: name,
            accessModifier: AccessModifier.Public,
            isSealed: shouldBeSealed,
            isStatic: false,
            isPartial: false,
            baseTypes: baseTypes.ToImmutableList(),
            members: properties.ToImmutableList(),
            documentation: comment);
    }
    
    /// <summary>Builds a typed property with wire metadata, validation, and safe initialization.</summary>
    /// <param name="property">The normalized schema property.</param>
    /// <param name="model">The containing model used for validation metadata.</param>
    /// <returns>The generated C# property declaration.</returns>
    public PropertyDeclarationNode BuildProperty(IrProperty property, IrModel? model = null)
    {
        var name = new IdentifierNode(property.Name);
        var type = BuildType(property.Type);

        // Необязательное свойство-enum должно быть nullable: enum — value-type, а сервер (по контракту
        // «свойство не в required») вправе прислать null. OpenAPI 3.0 не умеет ставить nullable рядом с
        // $ref, поэтому для примитивов признак приходит из схемы, а для enum-$ref теряется — компенсируем
        // здесь. Reference-типы ($ref на модель) не трогаем: им null присваивается штатно.
        if (!property.IsRequired && type is not NullableTypeNode && IsEnumReference(property.Type))
        {
            type = new NullableTypeNode(type);
        }
        var comment = !string.IsNullOrEmpty(property.Description)
            ? new CommentNode(property.Description, CommentKind.Xml)
            : null;
        
        ExpressionNode? defaultValue = BuildPropertyDefault(property);
        
        // Собираем атрибуты
        var attributes = new List<AttributeNode>();
        
        // Добавляем JsonPropertyName атрибут с оригинальным именем, если имя изменилось
        if (property.Name != property.OriginalName)
        {
            var jsonPropertyNameAttr = new AttributeNode(
                name: new IdentifierNode("JsonPropertyName"),
                arguments: new[] { new LiteralExpressionNode(property.OriginalName, LiteralKind.String) }.ToImmutableList());
            attributes.Add(jsonPropertyNameAttr);
        }
        
        // Добавляем атрибуты валидации, если включено
        if (_options.GenerateValidationAttributes && model != null)
        {
            AddValidationAttributes(property, model, attributes);
        }
        
        // Определяем тип аксессора на основе опций
        var accessor = _options.PropertyAccessor == PropertyAccessorType.Init
            ? PropertyAccessorKind.GetInit
            : PropertyAccessorKind.GetSet;
        
        // Определяем, нужно ли добавлять required keyword
        var isRequiredKeyword = _options.UseRequired && property.IsRequired;

        // Non-nullable ссылочные свойства без required и без явного default вызывают CS8618 в
        // проектах с <Nullable>enable</Nullable> ("must contain a non-null value when exiting
        // constructor"). Раз required не используется по умолчанию (UseRequired = false), глушим
        // предупреждение стандартной идиомой `= default!;`.
        if (defaultValue == null && !isRequiredKeyword && RequiresNonNullableReferenceInitializer(type))
        {
            defaultValue = new DefaultExpressionNode(suppressNullableWarning: true);
        }

        return new PropertyDeclarationNode(
            name: name,
            type: type,
            isRequired: isRequiredKeyword,
            accessor: accessor,
            defaultValue: defaultValue,
            documentation: comment,
            attributes: attributes.Count > 0 ? attributes.ToImmutableList() : null);
    }
    
    /// <summary>Builds data annotations using schema bounds and invariant numeric literals.</summary>
    /// <param name="property">The schema property to validate.</param>
    /// <param name="model">The containing generated model.</param>
    /// <param name="attributes">The destination attribute collection.</param>
    private void AddValidationAttributes(IrProperty property, IrModel model, List<AttributeNode> attributes)
    {
        if (property.Constraints == null)
            return;

        var constraints = property.Constraints;
        
        // Определяем имя класса констант (если генерируем константы)
        string? constantsClassName = null;
        if (_options.GenerateValidationConstants)
        {
            var propertyNamePascalCase = _namingConverter.ToPascalCase(property.OriginalName);
            constantsClassName = $"{propertyNamePascalCase}Constants";
        }

        // Required атрибут для обязательных свойств
        if (property.IsRequired)
        {
            attributes.Add(new AttributeNode(new IdentifierNode("Required")));
        }

        // MinLength / MaxLength для строк
        if (constraints.MinLength.HasValue)
        {
            ExpressionNode minLengthValue;
            if (_options.GenerateValidationConstants && constantsClassName != null)
            {
                // Используем константу из класса констант
                var constantsClassAccess = new IdentifierExpressionNode(new IdentifierNode(constantsClassName));
                var minLengthAccess = new MemberAccessExpressionNode(
                    @object: constantsClassAccess,
                    member: new IdentifierNode("MinLength"));
                minLengthValue = minLengthAccess;
            }
            else
            {
                // Используем inline значение
                minLengthValue = new LiteralExpressionNode(constraints.MinLength.Value.ToString(CultureInfo.InvariantCulture), LiteralKind.Number);
            }
            
            var minLengthAttr = new AttributeNode(
                name: new IdentifierNode("MinLength"),
                arguments: new[] { minLengthValue }.ToImmutableList());
            attributes.Add(minLengthAttr);
        }

        if (constraints.MaxLength.HasValue)
        {
            ExpressionNode maxLengthValue;
            if (_options.GenerateValidationConstants && constantsClassName != null)
            {
                // Используем константу из класса констант
                var constantsClassAccess = new IdentifierExpressionNode(new IdentifierNode(constantsClassName));
                var maxLengthAccess = new MemberAccessExpressionNode(
                    @object: constantsClassAccess,
                    member: new IdentifierNode("MaxLength"));
                maxLengthValue = maxLengthAccess;
            }
            else
            {
                // Используем inline значение
                maxLengthValue = new LiteralExpressionNode(constraints.MaxLength.Value.ToString(CultureInfo.InvariantCulture), LiteralKind.Number);
            }
            
            var maxLengthAttr = new AttributeNode(
                name: new IdentifierNode("MaxLength"),
                arguments: new[] { maxLengthValue }.ToImmutableList());
            attributes.Add(maxLengthAttr);
        }

        if (constraints.Min.HasValue || constraints.Max.HasValue ||
            constraints.ExclusiveMin.HasValue || constraints.ExclusiveMax.HasValue)
        {
            attributes.Add(BuildNumericRangeAttribute(property));
        }

        // RegularExpression для паттернов
        if (!string.IsNullOrEmpty(constraints.Pattern))
        {
            ExpressionNode patternValue;
            if (_options.GenerateValidationConstants && constantsClassName != null)
            {
                // Используем константу из класса констант
                var constantsClassAccess = new IdentifierExpressionNode(new IdentifierNode(constantsClassName));
                var patternAccess = new MemberAccessExpressionNode(
                    @object: constantsClassAccess,
                    member: new IdentifierNode("Pattern"));
                patternValue = patternAccess;
            }
            else
            {
                // Используем inline значение
                patternValue = new LiteralExpressionNode(constraints.Pattern, LiteralKind.String);
            }
            
            var regexAttr = new AttributeNode(
                name: new IdentifierNode("RegularExpression"),
                arguments: new[] { patternValue }.ToImmutableList());
            attributes.Add(regexAttr);
        }
    }
    
    /// <summary>Collects each effective and inherited wire property once for equality and hashing.</summary>
    /// <param name="model">The model whose full state is compared.</param>
    /// <param name="baseTypes">The emitted bases; language interfaces do not contribute properties.</param>
    /// <returns>The stable, deduplicated property list including the generated base class.</returns>
    private IReadOnlyList<IrProperty> GetAllPropertiesIncludingInherited(IrModel model, IReadOnlyList<TypeNode> baseTypes)
    {
        var properties = new Dictionary<string, IrProperty>(StringComparer.Ordinal);
        foreach (var baseType in baseTypes.OfType<SimpleTypeNode>())
        {
            if (!ModelsByName.TryGetValue(baseType.Name.Name, out var baseModel))
                continue;

            var plan = IrInheritancePlan.Resolve(baseModel, ModelsByName);
            var inheritedBases = plan.BaseModelName is { } name
                ? new TypeNode[] { BuildSimpleType(name) }
                : Array.Empty<TypeNode>();
            foreach (var property in GetAllPropertiesIncludingInherited(
                baseModel with { Properties = plan.EffectiveProperties }, inheritedBases))
            {
                properties[property.OriginalName] = property;
            }
        }

        foreach (var property in model.Properties)
            properties[property.OriginalName] = property;

        return properties.Values.ToList();
    }

    /// <summary>
    /// Строит методы Equals и GetHashCode для класса модели
    /// </summary>
    private IReadOnlyList<MethodDeclarationNode> BuildEqualityMembers(IrModel model, IReadOnlyList<TypeNode> baseTypes)
    {
        var methods = new List<MethodDeclarationNode>();
        
        // Метод Equals(TModel? other) - реализация IEquatable<T>
        var modelType = new SimpleTypeNode(new IdentifierNode(model.Name), isPrimitive: false);
        var nullableModelType = new NullableTypeNode(modelType);
        
        var equalsTParam = new ParameterDeclarationNode(
            name: new IdentifierNode("other"),
            type: nullableModelType,
            isRequired: false);
        
        var allProperties = GetAllPropertiesIncludingInherited(model, baseTypes);
        var equalsTBody = BuildEqualsTMethodBody(model, allProperties);
        var equalsTMethod = new MethodDeclarationNode(
            name: new IdentifierNode("Equals"),
            returnType: new SimpleTypeNode(new IdentifierNode("bool"), isPrimitive: true),
            parameters: new[] { equalsTParam }.ToImmutableList(),
            body: equalsTBody,
            accessModifier: AccessModifier.Public,
            isAsync: false,
            documentation: new CommentNode("Определяет, равен ли указанный объект текущему объекту.", CommentKind.Xml));
        methods.Add(equalsTMethod);
        
        // Метод Equals(object? obj) - переопределение из System.Object
        var equalsObjParam = new ParameterDeclarationNode(
            name: new IdentifierNode("obj"),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("object"), isPrimitive: true)),
            isRequired: false);
        
        var equalsObjBody = BuildEqualsObjectMethodBody(model);
        var equalsObjMethod = new MethodDeclarationNode(
            name: new IdentifierNode("Equals"),
            returnType: new SimpleTypeNode(new IdentifierNode("bool"), isPrimitive: true),
            parameters: new[] { equalsObjParam }.ToImmutableList(),
            body: equalsObjBody,
            accessModifier: AccessModifier.Public,
            isAsync: false,
            documentation: new CommentNode("Определяет, равен ли указанный объект текущему объекту.", CommentKind.Xml),
            isOverride: true);
        methods.Add(equalsObjMethod);
        
        // Метод GetHashCode()
        var getHashCodeBody = BuildGetHashCodeMethodBody(model, allProperties);
        var getHashCodeMethod = new MethodDeclarationNode(
            name: new IdentifierNode("GetHashCode"),
            returnType: new SimpleTypeNode(new IdentifierNode("int"), isPrimitive: true),
            parameters: ImmutableList<ParameterDeclarationNode>.Empty,
            body: getHashCodeBody,
            accessModifier: AccessModifier.Public,
            isAsync: false,
            documentation: new CommentNode("Служит хэш-функцией по умолчанию.", CommentKind.Xml),
            isOverride: true);
        methods.Add(getHashCodeMethod);
        
        return methods;
    }
    
    /// <summary>Delegates object equality through a safe nullable model cast.</summary>
    /// <param name="model">The model declaring the equality member.</param>
    /// <returns>The object equality method body.</returns>
    private BlockNode BuildEqualsObjectMethodBody(IrModel model)
    {
        var statements = new List<StatementNode>();
        
        var objParam = new IdentifierExpressionNode(new IdentifierNode("obj"));
        var modelType = new SimpleTypeNode(new IdentifierNode(model.Name), isPrimitive: false);
        var asExpression = new CastExpressionNode(
            targetType: modelType,
            expression: objParam,
            useAsOperator: true);
        
        var equalsCall = new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode("Equals")),
            arguments: new[] { asExpression }.ToImmutableList());
        
        statements.Add(new ReturnStatementNode(equalsCall));
        
        return new BlockNode(statements.ToImmutableList());
    }
    
    /// <summary>Builds null-safe property comparison for a typed model value.</summary>
    /// <param name="model">The model being compared.</param>
    /// <param name="allProperties">The effective properties, including inherited properties once.</param>
    /// <returns>The typed equality method body.</returns>
    private BlockNode BuildEqualsTMethodBody(IrModel model, IReadOnlyList<IrProperty> allProperties)
    {
        var statements = new List<StatementNode>();
        
        // if (other == null) return false;
        var otherParam = new IdentifierExpressionNode(new IdentifierNode("other"));
        var nullCheck = new BinaryExpressionNode(
            left: otherParam,
            @operator: BinaryOperator.Equal,
            right: new LiteralExpressionNode("null", LiteralKind.Null));
        var ifNullReturn = new IfStatementNode(
            condition: nullCheck,
            thenBlock: new BlockNode(new[] { new ReturnStatementNode(new LiteralExpressionNode("false", LiteralKind.Boolean)) }.ToImmutableList()),
            elseBlock: null);
        statements.Add(ifNullReturn);
        
        // if (ReferenceEquals(this, other)) return true;
        var thisExpr = new IdentifierExpressionNode(new IdentifierNode("this"));
        var objectType = new IdentifierExpressionNode(new IdentifierNode("object"));
        var referenceEqualsMethod = new MemberAccessExpressionNode(
            @object: objectType,
            member: new IdentifierNode("ReferenceEquals"));
        var referenceEqualsCall = new CallExpressionNode(
            target: referenceEqualsMethod,
            arguments: new[] { thisExpr, otherParam }.ToImmutableList());
        var ifReferenceEqualsReturn = new IfStatementNode(
            condition: referenceEqualsCall,
            thenBlock: new BlockNode(new[] { new ReturnStatementNode(new LiteralExpressionNode("true", LiteralKind.Boolean)) }.ToImmutableList()),
            elseBlock: null);
        statements.Add(ifReferenceEqualsReturn);
        
        // Параметр "other" уже имеет тип TModel?, так что не нужно приводить тип
        
        // Сравниваем все свойства (включая наследуемые) через Equals
        var equalityChecks = new List<ExpressionNode>();
        foreach (var property in allProperties)
        {
            var thisProperty = new MemberAccessExpressionNode(
                @object: thisExpr,
                member: new IdentifierNode(property.Name));
            var otherProperty = new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("other")),
                member: new IdentifierNode(property.Name));
            
            // Static object equality accepts null reference properties without dereferencing them.
            var equalsMethod = new MemberAccessExpressionNode(
                @object: objectType,
                member: new IdentifierNode("Equals"));
            ExpressionNode propertyEquals = new CallExpressionNode(
                target: equalsMethod,
                arguments: new[] { thisProperty, otherProperty }.ToImmutableList());
            
            equalityChecks.Add(propertyEquals);
        }
        
        // Объединяем все проверки через &&
        if (equalityChecks.Count > 0)
        {
            ExpressionNode combinedCheck = equalityChecks[0];
            for (int i = 1; i < equalityChecks.Count; i++)
            {
                combinedCheck = new BinaryExpressionNode(
                    left: combinedCheck,
                    @operator: BinaryOperator.LogicalAnd,
                    right: equalityChecks[i]);
            }
            
            statements.Add(new ReturnStatementNode(combinedCheck));
        }
        else
        {
            statements.Add(new ReturnStatementNode(new LiteralExpressionNode("true", LiteralKind.Boolean)));
        }
        
        return new BlockNode(statements.ToImmutableList());
    }
    
    /// <summary>
    /// Строит тело метода GetHashCode
    /// </summary>
    private BlockNode BuildGetHashCodeMethodBody(IrModel model, IReadOnlyList<IrProperty> allProperties)
    {
        var statements = new List<StatementNode>();
        
        if (allProperties.Count == 0)
        {
            // return 0;
            statements.Add(new ReturnStatementNode(new LiteralExpressionNode("0", LiteralKind.Number)));
            return new BlockNode(statements.ToImmutableList());
        }
        
        // Создаем переменную HashCode hashCode = new();
        var hashCodeType = new SimpleTypeNode(new IdentifierNode("HashCode"), isPrimitive: false);
        var hashCodeVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode("hashCode"),
            type: hashCodeType,
            initializer: new CallExpressionNode(
                target: new IdentifierExpressionNode(new IdentifierNode("HashCode")),
                arguments: ImmutableList<ExpressionNode>.Empty,
                isNewExpression: true));
        statements.Add(hashCodeVar);
        
        // Добавляем все свойства (включая наследуемые) через hashCode.Add()
        var hashCodeIdentifier = new IdentifierExpressionNode(new IdentifierNode("hashCode"));
        var addMethod = new MemberAccessExpressionNode(
            @object: hashCodeIdentifier,
            member: new IdentifierNode("Add"));
        
        foreach (var property in allProperties)
        {
            var thisProperty = new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("this")),
                member: new IdentifierNode(property.Name));
            
            var addCall = new CallExpressionNode(
                target: addMethod,
                arguments: new[] { thisProperty }.ToImmutableList());
            
            statements.Add(new ExpressionStatementNode(addCall));
        }
        
        // return hashCode.ToHashCode();
        var toHashCodeMethod = new MemberAccessExpressionNode(
            @object: hashCodeIdentifier,
            member: new IdentifierNode("ToHashCode"));
        var toHashCodeCall = new CallExpressionNode(
            target: toHashCodeMethod,
            arguments: ImmutableList<ExpressionNode>.Empty);
        
        statements.Add(new ReturnStatementNode(toHashCodeCall));
        
        return new BlockNode(statements.ToImmutableList());
    }
    
    /// <summary>
    /// Строит классы с константами валидации для свойств модели
    /// </summary>
    private IReadOnlyList<ClassDeclarationNode> BuildValidationConstantsClasses(IrModel model)
    {
        var constantsClasses = new List<ClassDeclarationNode>();
        
        foreach (var property in model.Properties)
        {
            if (property.Constraints == null)
                continue;
            
            var constraints = property.Constraints;
            var hasConstants = constraints.MinLength.HasValue ||
                              constraints.MaxLength.HasValue ||
                              constraints.Min.HasValue ||
                              constraints.Max.HasValue ||
                              constraints.ExclusiveMin.HasValue ||
                              constraints.ExclusiveMax.HasValue ||
                              !string.IsNullOrEmpty(constraints.Pattern) ||
                              constraints.DefaultValue != null;
            
            if (!hasConstants)
                continue;
            
            // Создаем вложенный класс с константами для свойства
            var propertyNamePascalCase = _namingConverter.ToPascalCase(property.OriginalName);
            var constantsClassName = $"{propertyNamePascalCase}Constants";
            
            // Добавляем XML комментарий с see cref на свойство
            var constantsClassComment = new CommentNode(
                $"Константы валидации для <see cref=\"{model.Name}.{property.Name}\"/>", 
                CommentKind.Xml);
            
            var constantsClass = new ClassDeclarationNode(
                name: new IdentifierNode(constantsClassName),
                accessModifier: AccessModifier.Public,
                isSealed: false, // static классы не могут быть sealed
                isStatic: true,
                isPartial: false,
                baseTypes: ImmutableList<TypeNode>.Empty,
                members: BuildConstantsClassMembers(model, property, constraints, constantsClassName).ToImmutableList(),
                documentation: constantsClassComment);
            
            constantsClasses.Add(constantsClass);
        }
        
        return constantsClasses;
    }
    
    /// <summary>Preserves exact integer bounds and exclusive numeric constraints in data annotations.</summary>
    /// <param name="property">The numeric property and its decimal schema bounds.</param>
    /// <returns>A typed, invariant-culture range attribute.</returns>
    /// <exception cref="NotSupportedException">Numeric bounds are attached to a nonnumeric property.</exception>
    /// <exception cref="InvalidOperationException">The bounds cannot match any value of the generated type.</exception>
    private AttributeNode BuildNumericRangeAttribute(IrProperty property)
    {
        var constraints = property.Constraints!;
        var minimum = constraints.Min;
        var maximum = constraints.Max;
        var minimumExclusive = constraints.ExclusiveMin is { } exclusiveMin &&
            (!minimum.HasValue || exclusiveMin >= minimum.Value);
        var maximumExclusive = constraints.ExclusiveMax is { } exclusiveMax &&
            (!maximum.HasValue || exclusiveMax <= maximum.Value);
        if (minimumExclusive) minimum = constraints.ExclusiveMin;
        if (maximumExclusive) maximum = constraints.ExclusiveMax;
        var kind = UnwrapNullableOrOptional(property.Type) is IrPrimitiveType primitive
            ? primitive.PrimitiveKind : IrPrimitiveKind.Any;
        string minimumText;
        string maximumText;
        if (kind is IrPrimitiveKind.Integer or IrPrimitiveKind.Long)
        {
            var typeMinimum = kind == IrPrimitiveKind.Integer ? (decimal)int.MinValue : long.MinValue;
            var typeMaximum = kind == IrPrimitiveKind.Integer ? (decimal)int.MaxValue : long.MaxValue;
            if (minimum > typeMaximum || maximum < typeMinimum)
                throw new InvalidOperationException($"Numeric bounds for '{property.Name}' exclude every value of its C# type.");
            var lower = minimum.HasValue && minimum.Value >= typeMinimum
                ? decimal.Ceiling(minimum.Value) + (minimumExclusive && decimal.Truncate(minimum.Value) == minimum.Value ? 1 : 0)
                : typeMinimum;
            var upper = maximum.HasValue && maximum.Value <= typeMaximum
                ? decimal.Floor(maximum.Value) - (maximumExclusive && decimal.Truncate(maximum.Value) == maximum.Value ? 1 : 0)
                : typeMaximum;
            if (lower > upper)
                throw new InvalidOperationException($"Numeric bounds for '{property.Name}' have no permitted integer value.");
            minimumText = lower.ToString(CultureInfo.InvariantCulture);
            maximumText = upper.ToString(CultureInfo.InvariantCulture);
            minimumExclusive = false;
            maximumExclusive = false;
        }
        else if (kind == IrPrimitiveKind.Number)
        {
            if (minimum > maximum || minimum == maximum && minimum.HasValue && (minimumExclusive || maximumExclusive))
                throw new InvalidOperationException($"Numeric bounds for '{property.Name}' have no permitted value.");
            minimumText = minimum?.ToString(CultureInfo.InvariantCulture) ?? double.MinValue.ToString("R", CultureInfo.InvariantCulture);
            maximumText = maximum?.ToString(CultureInfo.InvariantCulture) ?? double.MaxValue.ToString("R", CultureInfo.InvariantCulture);
        }
        else
        {
            throw new NotSupportedException($"Numeric bounds on '{property.Name}' require an integer, long, or number schema.");
        }
        var arguments = new List<ExpressionNode>
        {
            new TypeOfExpressionNode(BuildType(UnwrapNullableOrOptional(property.Type))),
            new LiteralExpressionNode(minimumText, LiteralKind.String),
            new LiteralExpressionNode(maximumText, LiteralKind.String)
        };
        foreach (var (name, value) in new[]
        {
            ("ParseLimitsInInvariantCulture", true), ("ConvertValueInInvariantCulture", true),
            ("MinimumIsExclusive", minimumExclusive), ("MaximumIsExclusive", maximumExclusive)
        })
        {
            arguments.Add(new BinaryExpressionNode(new IdentifierExpressionNode(new IdentifierNode(name)),
                BinaryOperator.Assign, new LiteralExpressionNode(value, LiteralKind.Boolean)));
        }
        return new AttributeNode(new IdentifierNode("Range"), arguments);
    }

    /// <summary>Builds typed validation constants for one model property.</summary>
    /// <param name="model">The containing model.</param>
    /// <param name="property">The property whose constants are exposed.</param>
    /// <param name="constraints">The schema constraints to project.</param>
    /// <param name="constantsClassName">The generated constants container name.</param>
    /// <returns>The emitted constants.</returns>
    private IReadOnlyList<DeclarationNode> BuildConstantsClassMembers(IrModel model, IrProperty property, IrPropertyConstraints constraints, string constantsClassName)
    {
        var members = new List<DeclarationNode>();
        
        // MinLength константа
        if (constraints.MinLength.HasValue)
        {
            var comment = new CommentNode($"Минимальная длина для <see cref=\"{model.Name}.{property.Name}\"/>", CommentKind.Xml);
            var minLengthField = new FieldDeclarationNode(
                name: new IdentifierNode("MinLength"),
                type: new SimpleTypeNode(new IdentifierNode("int"), isPrimitive: true),
                accessModifier: AccessModifier.Public,
                isReadOnly: false, // const не требует readonly
                isStatic: true,
                defaultValue: new LiteralExpressionNode(constraints.MinLength.Value.ToString(CultureInfo.InvariantCulture), LiteralKind.Number),
                documentation: comment);
            members.Add(minLengthField);
        }
        
        // MaxLength константа
        if (constraints.MaxLength.HasValue)
        {
            var comment = new CommentNode($"Максимальная длина для <see cref=\"{model.Name}.{property.Name}\"/>", CommentKind.Xml);
            var maxLengthField = new FieldDeclarationNode(
                name: new IdentifierNode("MaxLength"),
                type: new SimpleTypeNode(new IdentifierNode("int"), isPrimitive: true),
                accessModifier: AccessModifier.Public,
                isReadOnly: false, // const не требует readonly
                isStatic: true,
                defaultValue: new LiteralExpressionNode(constraints.MaxLength.Value.ToString(CultureInfo.InvariantCulture), LiteralKind.Number),
                documentation: comment);
            members.Add(maxLengthField);
        }
        
        // Min константа
        if (constraints.Min.HasValue)
        {
            var comment = new CommentNode($"Минимальное значение для <see cref=\"{model.Name}.{property.Name}\"/>", CommentKind.Xml);
            var minField = new FieldDeclarationNode(
                name: new IdentifierNode("Min"),
                type: new SimpleTypeNode(new IdentifierNode("decimal"), isPrimitive: true),
                accessModifier: AccessModifier.Public,
                isReadOnly: false, // const не требует readonly
                isStatic: true,
                defaultValue: new LiteralExpressionNode(constraints.Min.Value.ToString(CultureInfo.InvariantCulture) + "m", LiteralKind.Number),
                documentation: comment);
            members.Add(minField);
        }
        
        // Max константа
        if (constraints.Max.HasValue)
        {
            var comment = new CommentNode($"Максимальное значение для <see cref=\"{model.Name}.{property.Name}\"/>", CommentKind.Xml);
            var maxField = new FieldDeclarationNode(
                name: new IdentifierNode("Max"),
                type: new SimpleTypeNode(new IdentifierNode("decimal"), isPrimitive: true),
                accessModifier: AccessModifier.Public,
                isReadOnly: false, // const не требует readonly
                isStatic: true,
                defaultValue: new LiteralExpressionNode(constraints.Max.Value.ToString(CultureInfo.InvariantCulture) + "m", LiteralKind.Number),
                documentation: comment);
            members.Add(maxField);
        }
        
        foreach (var (name, value) in new[] { ("ExclusiveMin", constraints.ExclusiveMin), ("ExclusiveMax", constraints.ExclusiveMax) })
        {
            if (value is null) continue;
            members.Add(new FieldDeclarationNode(new IdentifierNode(name),
                new SimpleTypeNode(new IdentifierNode("decimal"), isPrimitive: true), AccessModifier.Public,
                isReadOnly: false, isStatic: true,
                defaultValue: new LiteralExpressionNode(value.Value.ToString(CultureInfo.InvariantCulture) + "m", LiteralKind.Number),
                documentation: new CommentNode($"Exclusive numeric bound for {property.Name}.", CommentKind.Xml)));
        }

        // Pattern константа
        if (!string.IsNullOrEmpty(constraints.Pattern))
        {
            var comment = new CommentNode($"Регулярное выражение для <see cref=\"{model.Name}.{property.Name}\"/>", CommentKind.Xml);
            var patternField = new FieldDeclarationNode(
                name: new IdentifierNode("Pattern"),
                type: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
                accessModifier: AccessModifier.Public,
                isReadOnly: false, // const не требует readonly
                isStatic: true,
                defaultValue: new LiteralExpressionNode(constraints.Pattern, LiteralKind.String),
                documentation: comment);
            members.Add(patternField);
        }
        
        // DefaultValue константа
        if (constraints.DefaultValue != null)
        {
            var defaultValueLiteral = BuildPropertyDefault(property);
            // Если распарсить дефолт под тип свойства не удалось — не эмитим константу вовсе,
            // а не типизированный "на глаз" литерал, который может не совпасть с type (CS0029).
            if (defaultValueLiteral != null)
            {
                var comment = new CommentNode($"Значение по умолчанию для <see cref=\"{model.Name}.{property.Name}\"/>", CommentKind.Xml);
                var defaultValueField = new FieldDeclarationNode(
                    name: new IdentifierNode("DefaultValue"),
                    type: BuildType(property.Type),
                    accessModifier: AccessModifier.Public,
                    isReadOnly: false, // const не требует readonly, если примитивный тип
                    isStatic: true,
                    defaultValue: defaultValueLiteral,
                    documentation: comment);
                members.Add(defaultValueField);
            }
        }
        
        return members;
    }
    
    
    /// <summary>Builds an enum source file with its required JSON and serialization imports.</summary>
    /// <param name="enumModel">The normalized enum declaration.</param>
    /// <param name="namespace">The namespace name, or null/empty for the global namespace.</param>
    /// <returns>The enum compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnit(IrEnum enumModel, string? @namespace = null)
    {
        var dependencies = new HashSet<string>();

        // Если enum имеет строковые значения, добавляем using для EnumMember и для JsonConverter/
        // JsonStringEnumMemberName (System.Text.Json игнорирует [EnumMember] — см. BuildEnum)
        if (enumModel.BaseType == IrEnumBaseType.String)
        {
            dependencies.Add("System.Runtime.Serialization");
            dependencies.Add("System.Text.Json.Serialization");
        }

        var usings = BuildUsingDirectives(dependencies.ToList());
        return CreateCompilationUnit(usings, [BuildEnum(enumModel)], @namespace);
    }
    
    /// <summary>Builds wire-aware enums without truncating numeric values to 32 bits.</summary>
    /// <param name="enumModel">The normalized enum declaration.</param>
    /// <returns>The enum with a sufficient underlying integer type.</returns>
    /// <exception cref="NotSupportedException">A numeric value is fractional or outside signed 64-bit range.</exception>
    public EnumDeclarationNode BuildEnum(IrEnum enumModel)
    {
        var name = new IdentifierNode(enumModel.Name);
        var comment = !string.IsNullOrEmpty(enumModel.Description)
            ? new CommentNode(enumModel.Description, CommentKind.Xml)
            : null;
        
        var numericValues = enumModel.BaseType == IrEnumBaseType.Integer
            ? enumModel.Values.Select(value => NormalizeRawValue(value.Value) is { } raw && ToInvariantIntegerText(raw) is { } integer
                ? long.Parse(integer, CultureInfo.InvariantCulture)
                : throw new NotSupportedException($"Numeric enum '{enumModel.Name}' member '{value.Name}' is not a signed 64-bit integer.")).ToArray()
            : [];
        var numericKind = numericValues.Any(value => value is < int.MinValue or > int.MaxValue)
            ? IrPrimitiveKind.Long : IrPrimitiveKind.Integer;
        var underlyingType = enumModel.BaseType == IrEnumBaseType.Integer
            ? BuildType(new IrPrimitiveType { PrimitiveKind = numericKind })
            : null;
        
        var isStringEnum = enumModel.BaseType == IrEnumBaseType.String;

        var members = enumModel.Values.Select(v =>
        {
            ExpressionNode? value = null;
            IReadOnlyList<AttributeNode>? attributes = null;

            if (isStringEnum)
            {
                var originalValue = v.Value?.ToString() ?? string.Empty;
                var memberAttributes = new List<AttributeNode>();

                // [EnumMember(Value = "...")] — для DataContract-совместимых сериализаторов
                // (Newtonsoft.Json, WCF и т.п.)
                var enumMemberAttributeName = new IdentifierNode("EnumMember");
                var namedArg = new BinaryExpressionNode(
                    left: new IdentifierExpressionNode(new IdentifierNode("Value")),
                    @operator: BinaryOperator.Add, // Временно, будет обработано в рендерере как именованный аргумент
                    right: new LiteralExpressionNode(originalValue, LiteralKind.String),
                    isNamedArgument: true);
                memberAttributes.Add(new AttributeNode(enumMemberAttributeName, new[] { (ExpressionNode)namedArg }.ToImmutableList()));

                // [JsonStringEnumMemberName("...")] — System.Text.Json игнорирует [EnumMember], но
                // (начиная с .NET 9, при [JsonConverter(typeof(JsonStringEnumConverter))] на самом
                // enum'е — см. ниже) уважает этот атрибут, поэтому исходное строковое значение
                // из схемы (даже если оно не совпадает с PascalCase-именем члена) сериализуется верно.
                memberAttributes.Add(new AttributeNode(
                    new IdentifierNode("JsonStringEnumMemberName"),
                    new[] { (ExpressionNode)new LiteralExpressionNode(originalValue, LiteralKind.String) }.ToImmutableList()));

                attributes = memberAttributes.ToImmutableList();
            }
            else
            {
                // Для числовых enum'ов используем обычное присваивание значения (BaseType уже
                // гарантирует Integer здесь — String уходит в ветку выше)
                value = BuildTypedLiteral(v.Value, numericKind);
            }

            return new EnumMemberDeclarationNode(
                name: new IdentifierNode(v.Name),
                value: value,
                documentation: !string.IsNullOrEmpty(v.Description)
                    ? new CommentNode(v.Description, CommentKind.Xml)
                    : null,
                attributes: attributes);
        }).ToList<DeclarationNode>();

        // [JsonConverter(typeof(JsonStringEnumConverter))] на самом типе — System.Text.Json по
        // умолчанию сериализует enum как число; без этого атрибута строковые значения из API
        // (например, "in_progress") не проходят round-trip через JsonSerializer.Serialize/Deserialize.
        IReadOnlyList<AttributeNode>? enumAttributes = null;
        if (isStringEnum)
        {
            var jsonConverterAttribute = new AttributeNode(
                new IdentifierNode("JsonConverter"),
                new ExpressionNode[] { new TypeOfExpressionNode(BuildSimpleType("JsonStringEnumConverter")) }.ToImmutableList());
            enumAttributes = new[] { jsonConverterAttribute }.ToImmutableList();
        }

        return new EnumDeclarationNode(
            name: name,
            accessModifier: AccessModifier.Public,
            underlyingType: underlyingType,
            members: members.ToImmutableList(),
            documentation: comment,
            attributes: enumAttributes);
    }
    
    // Приватные вспомогательные методы для IR
    
    /// <summary>
    /// Имена C#-типов, отображаемых на value-типы (struct/enum) — для них <c>= default!;</c> не
    /// требуется по смыслу (default — валидное значение, а не null), но и не вредит.
    /// </summary>
    private static readonly HashSet<string> KnownValueTypeNames = new(StringComparer.Ordinal)
    {
        "int", "uint", "long", "ulong", "short", "ushort", "byte", "sbyte",
        "double", "float", "decimal", "bool", "char",
        "DateTime", "DateOnly", "TimeOnly", "TimeSpan", "Guid", "void"
    };

    /// <summary>
    /// Является ли IR-тип ссылкой на enum документа (напрямую или под nullable/optional-обёрткой).
    /// Enum в C# — value-type, поэтому необязательное enum-свойство обязано быть <c>T?</c>.
    /// </summary>
    /// <param name="type">IR-тип свойства.</param>
    /// <returns><see langword="true"/>, если тип разворачивается в <see cref="IrModelReference"/> на enum.</returns>
    private bool IsEnumReference(IrType type)
    {
        return type switch
        {
            IrModelReference reference => EnumNames.Contains(reference.ModelName),
            IrNullableType nullable => IsEnumReference(nullable.InnerType),
            IrOptionalType optional => IsEnumReference(optional.InnerType),
            _ => false
        };
    }

    /// <summary>
    /// Определяет, нужно ли non-nullable свойству <c>= default!;</c>, чтобы не словить CS8618
    /// ("non-nullable property must contain a non-null value...") в проектах с
    /// <c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c>. Nullable-типы (<c>T?</c>) не нуждаются в
    /// инициализаторе — предупреждение про них не возникает.
    /// </summary>
    private bool RequiresNonNullableReferenceInitializer(TypeNode type)
    {
        return type switch
        {
            NullableTypeNode => false,
            SimpleTypeNode simple => !KnownValueTypeNames.Contains(simple.Name.Name),
            _ => true // массивы, словари, generic-коллекции (List<T> и т.п.) — reference-типы
        };
    }

    /// <summary>Finds namespaces required by effective model properties and generated members.</summary>
    /// <param name="model">The model whose inheritance plan contributes emitted properties.</param>
    /// <returns>The distinct namespace names required by the generated source.</returns>
    private IReadOnlyList<string> ExtractDependencies(IrModel model)
    {
        model = model with { Properties = IrInheritancePlan.Resolve(model, ModelsByName).EffectiveProperties };
        // Не добавляем зависимости для типов моделей, так как они генерируются в том же namespace
        // Используем только системные типы и типы из других namespace'ов, если они нужны
        var dependencies = new HashSet<string>();
        
        // Проверяем, используется ли JsonPropertyName атрибут
        foreach (var property in model.Properties)
        {
            if (property.Name != property.OriginalName)
            {
                dependencies.Add("System.Text.Json.Serialization");
                break; // Достаточно одного использования
            }
        }
        
        // Добавляем using для атрибутов валидации, если они используются
        if (_options.GenerateValidationAttributes)
        {
            var hasValidationAttributes = model.Properties.Any(p => 
                p.IsRequired || 
                (p.Constraints != null && (
                    p.Constraints.MinLength.HasValue ||
                    p.Constraints.MaxLength.HasValue ||
                    p.Constraints.Min.HasValue ||
                    p.Constraints.Max.HasValue ||
                    p.Constraints.ExclusiveMin.HasValue ||
                    p.Constraints.ExclusiveMax.HasValue ||
                    !string.IsNullOrEmpty(p.Constraints.Pattern))));
            
            if (hasValidationAttributes)
            {
                dependencies.Add("System.ComponentModel.DataAnnotations");
            }
        }
        
        if (_options.GenerateEqualityMembers)
            dependencies.Add("System");
        foreach (var property in model.Properties)
            ExtractDependenciesFromType(property.Type, dependencies);

        return dependencies.Order(StringComparer.Ordinal).ToList();
    }
    
    /// <summary>Adds system namespaces from structural types without importing model identifiers.</summary>
    /// <param name="type">The normalized type to traverse.</param>
    /// <param name="dependencies">The namespace set being populated.</param>
    private void ExtractDependenciesFromType(IrType type, HashSet<string> dependencies)
    {
        switch (type)
        {
            case IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.File }:
                dependencies.Add("System.IO");
                break;
            case IrPrimitiveType { PrimitiveKind: IrPrimitiveKind.Date or IrPrimitiveKind.DateTime or IrPrimitiveKind.Time or IrPrimitiveKind.Uuid }:
                dependencies.Add("System");
                break;
            case IrArrayType arrayType:
                ExtractDependenciesFromType(arrayType.ElementType, dependencies);
                break;
            case IrDictionaryType dictType:
                dependencies.Add("System.Collections.Generic");
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
    /// Разворачивает Nullable/Optional обёртки, чтобы добраться до содержательного IrType
    /// (например, чтобы понять, что query-параметр — массив, даже если он объявлен как nullable-массив).
    /// </summary>
    private static IrType UnwrapNullableOrOptional(IrType type)
    {
        while (true)
        {
            switch (type)
            {
                case IrNullableType nullable:
                    type = nullable.InnerType;
                    continue;
                case IrOptionalType optional:
                    type = optional.InnerType;
                    continue;
                default:
                    return type;
            }
        }
    }

    /// <summary>Builds escaped URL scalar text with invariant formatting and enum wire values.</summary>
    /// <param name="valueExpr">The source value expression.</param>
    /// <returns>An expression producing one percent-encoded URL component.</returns>
    private static ExpressionNode BuildQueryValueExpression(ExpressionNode valueExpr)
    {
        ExpressionNode text = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("HttpWireValueFormatter")),
                member: new IdentifierNode("Format")),
            arguments: new[] { valueExpr }.ToImmutableList());
        return new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("Uri")),
                member: new IdentifierNode("EscapeDataString")),
            arguments: new[] { text }.ToImmutableList());
    }

    /// <summary>Executes statements only when an optional input or activity is present.</summary>
    /// <param name="paramName">The identifier tested for null.</param>
    /// <param name="statements">The guarded statements.</param>
    /// <returns>The conditional statement.</returns>
    private static IfStatementNode BuildNotNullGuard(string paramName, params StatementNode[] statements)
    {
        var notNullCheck = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode(paramName)),
            @operator: BinaryOperator.NotEqual,
            right: new LiteralExpressionNode("null", LiteralKind.Null));

        return new IfStatementNode(
            condition: notNullCheck,
            thenBlock: new BlockNode(statements.ToImmutableList()));
    }
    
    /// <summary>
    /// Определяет примитивный тип, под который нужно типизировать default-значение свойства
    /// (<see cref="BuildTypedLiteral"/>) — по ДЕКЛАРИРОВАННОМУ типу схемы (после снятия
    /// Nullable/Optional), а не по CLR-типу самого значения.
    /// </summary>
    private static IrPrimitiveKind GetDefaultValueTargetKind(IrType type)
    {
        return UnwrapNullableOrOptional(type) is IrPrimitiveType primitive ? primitive.PrimitiveKind : IrPrimitiveKind.Any;
    }

    /// <summary>Resolves a schema default to a typed primitive literal or a declared enum member.</summary>
    /// <param name="property">The property whose schema declares the default.</param>
    /// <returns>The typed default expression, or null when no valid default exists.</returns>
    private ExpressionNode? BuildPropertyDefault(IrProperty property)
    {
        if (property.Constraints?.DefaultValue is not { } rawDefault)
            return null;
        if (UnwrapNullableOrOptional(property.Type) is IrModelReference reference &&
            EnumsByName.TryGetValue(reference.ModelName, out var enumModel))
        {
            var target = NormalizeRawValue(rawDefault);
            var member = enumModel.Values.FirstOrDefault(value =>
                enumModel.BaseType == IrEnumBaseType.String
                    ? string.Equals(Convert.ToString(NormalizeRawValue(value.Value), CultureInfo.InvariantCulture),
                        Convert.ToString(target, CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    : target is not null && NormalizeRawValue(value.Value) is { } enumValue &&
                        ToInvariantIntegerText(enumValue) is { } integer && integer == ToInvariantIntegerText(target));
            var enumTypeName = string.IsNullOrEmpty(_modelNamespace)
                ? $"global::{reference.ModelName}"
                : $"global::{_modelNamespace}.{reference.ModelName}";
            return member is null ? null : new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode(enumTypeName)), new IdentifierNode(member.Name));
        }

        return BuildTypedLiteral(rawDefault, GetDefaultValueTargetKind(property.Type));
    }

    /// <summary>Builds a schema-typed literal only when its value fits the emitted C# type.</summary>
    /// <param name="value">The boxed primitive or JSON value extracted from the schema.</param>
    /// <param name="targetKind">The declared schema primitive kind.</param>
    /// <returns>A valid C# literal, or null when the value cannot be represented faithfully.</returns>
    private static LiteralExpressionNode? BuildTypedLiteral(object? value, IrPrimitiveKind targetKind)
    {
        value = NormalizeRawValue(value);
        if (value == null)
            return null;

        switch (targetKind)
        {
            case IrPrimitiveKind.Integer:
            {
                var text = ToInvariantIntegerText(value);
                return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                    ? new LiteralExpressionNode(text, LiteralKind.Number)
                    : null;
            }
            case IrPrimitiveKind.Long:
            {
                var text = ToInvariantIntegerText(value);
                return text != null ? new LiteralExpressionNode(text + "L", LiteralKind.Number) : null;
            }
            case IrPrimitiveKind.Number:
            {
                var text = ToInvariantDoubleText(value);
                return text != null ? new LiteralExpressionNode(text + "d", LiteralKind.Number) : null;
            }
            case IrPrimitiveKind.Boolean:
            {
                var text = ToInvariantBoolText(value);
                return text != null ? new LiteralExpressionNode(text, LiteralKind.Boolean) : null;
            }
            case IrPrimitiveKind.String:
                return new LiteralExpressionNode(value.ToString() ?? string.Empty, LiteralKind.String);
            default:
                // Uuid/DateTime/Date/Time/Any/Binary/ByteArray/File/Void/Null — голый строковый литерал
                // сюда не подходит (например, Guid), а конвертирующих выражений (Guid.Parse(...)) мы не
                // строим. Пропускаем инициализатор — не рискуем некомпилируемым кодом.
                return null;
        }
    }

    /// <summary>
    /// Разворачивает "сырое" значение default/enum до plain CLR-примитива или строки.
    /// Источники расходятся по представлению: property-дефолты (<c>IrBuilder.ExtractDefaultValue</c>)
    /// дают <c>JsonValue.GetValue&lt;object&gt;()</c>, который для значений, распарсенных из текста
    /// (<c>JsonNode.Parse</c>), возвращает не boxed int/bool, а <b>boxed <see cref="JsonElement"/></b>
    /// (проверено эмпирически: <c>JsonNode.Parse("0")</c> → <c>JsonValue.GetValue&lt;object&gt;()</c> →
    /// <see cref="JsonElement"/> с <see cref="JsonValueKind.Number"/>, а не <see cref="int"/>) — именно
    /// это боксирование под <c>_ =&gt; LiteralKind.String</c> в старом коде и превращало числовой default
    /// в квотированную строку. Сырые значения enum'ов (<see cref="IrEnumValue.Value"/>) — это сам
    /// <see cref="JsonNode"/>, ещё не развёрнутый через <c>GetValue&lt;object&gt;()</c>.
    /// </summary>
    private static object? NormalizeRawValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case JsonElement element:
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    // GetRawText() для числа — это ровно исходный JSON-текст ("0", "30", "1.5"),
                    // валидный и как C#-числовой литерал.
                    JsonValueKind.Number => element.GetRawText(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null,
                    _ => element.ToString()
                };
            case JsonValue jsonValue:
                return NormalizeRawValue(TryGetJsonValue(jsonValue));
            case JsonNode jsonNode:
                // JsonObject/JsonArray как default примитивного свойства — не наш случай, best-effort.
                return jsonNode.ToString();
            default:
                return value;
        }
    }

    private static object? TryGetJsonValue(JsonValue jsonValue)
    {
        try
        {
            return jsonValue.GetValue<object>();
        }
        catch
        {
            return jsonValue.ToString();
        }
    }

    /// <summary>Formats an exact integer without rounding, overflow, or floating-point precision loss.</summary>
    /// <param name="value">The boxed numeric value or invariant schema text.</param>
    /// <returns>A signed 64-bit integer literal, or null for fractional and out-of-range inputs.</returns>
    private static string? ToInvariantIntegerText(object value)
    {
        if (value is bool)
            return null;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (!decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
            decimal.Truncate(number) != number || number < long.MinValue || number > long.MaxValue)
        {
            return null;
        }

        return ((long)number).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Formats finite floating-point defaults as invariant round-trip literals.</summary>
    /// <param name="value">The boxed numeric value or invariant schema text.</param>
    /// <returns>A finite double literal, or null when the input cannot be represented.</returns>
    private static string? ToInvariantDoubleText(object value)
    {
        if (value is bool)
            return null;

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number)
            ? number.ToString("R", CultureInfo.InvariantCulture)
            : null;
    }

    private static string? ToInvariantBoolText(object value)
    {
        return value switch
        {
            bool b => b ? "true" : "false",
            string s when bool.TryParse(s.Trim(), out var parsed) => parsed ? "true" : "false",
            _ => null
        };
    }


    /// <summary>
    /// Builds a complete client method for an HTTP operation.
    /// </summary>
    /// <param name="operation">The normalized operation contract.</param>
    /// <returns>A method declaration containing documentation and an executable body.</returns>
    public MethodDeclarationNode BuildMethod(IrOperation operation)
    {
        return BuildMethod(operation, includeBody: true, includeDocumentation: true);
    }
    
    /// <summary>
    /// Builds a client method while allowing interface and implementation projections to share one path.
    /// </summary>
    /// <param name="operation">The normalized operation contract.</param>
    /// <param name="includeBody">Whether to emit the executable HTTP body.</param>
    /// <param name="includeDocumentation">Whether to emit operation documentation.</param>
    /// <returns>The projected method declaration.</returns>
    private MethodDeclarationNode BuildMethod(IrOperation operation, bool includeBody, bool includeDocumentation)
    {
        operation = ResolveMethodParameterCollisions(operation);
        var name = new IdentifierNode(operation.MethodName);
        
        // Определяем тип возвращаемого значения из responses
        var returnType = ExtractReturnType(operation.Responses);
        
        CommentNode? comment = null;
        if (includeDocumentation)
        {
            comment = !string.IsNullOrEmpty(operation.Description)
                ? new CommentNode(operation.Description, CommentKind.Xml)
                : !string.IsNullOrEmpty(operation.Summary)
                    ? new CommentNode(operation.Summary, CommentKind.Xml)
                    : null;
        }
        
        var parameters = BuildMethodParameters(operation);
        var body = includeBody ? BuildMethodBody(operation) : null;
        
        return new MethodDeclarationNode(
            name: name,
            returnType: returnType,
            parameters: parameters,
            body: body,
            accessModifier: AccessModifier.Public,
            isAsync: true,
            documentation: comment,
            metadata: null,
            isInterfaceMethod: !includeBody,
            useInheritDoc: false);
    }
    
    /// <summary>
    /// Строит MethodDeclarationNode для интерфейса (без тела)
    /// </summary>
    private MethodDeclarationNode BuildInterfaceMethod(IrOperation operation)
    {
        return BuildMethod(operation, includeBody: false, includeDocumentation: true);
    }
    
    /// <summary>Builds source for the document whose indexes were supplied at construction.</summary>
    /// <param name="document">The normalized document to generate.</param>
    /// <param name="namespace">The optional generated namespace.</param>
    /// <returns>The generated source compilation units.</returns>
    /// <exception cref="ArgumentException">The document differs from the constructor context.</exception>
    public IReadOnlyList<CompilationUnitNode> BuildCompilationUnits(IrDocument document, string? @namespace = null)
    {
        if (!ReferenceEquals(document, DocumentContext.Document))
            throw new ArgumentException("The document must match the context supplied to this builder.", nameof(document));

        return BuildCompilationUnitsInternal(document, @namespace);
    }
    
    /// <summary>Builds each model once in the shared dependency order before enums and clients.</summary>
    /// <param name="document">The document already checked against the constructor context.</param>
    /// <param name="namespace">The namespace of generated declarations.</param>
    /// <returns>The complete ordered compilation units.</returns>
    private IReadOnlyList<CompilationUnitNode> BuildCompilationUnitsInternal(IrDocument document, string? @namespace = null)
    {
        var compilationUnits = new List<CompilationUnitNode>();
        
        foreach (var model in DocumentContext.ModelsInDependencyOrder)
            compilationUnits.Add(BuildCompilationUnit(model, @namespace));

        // Генерируем enum'ы
        foreach (var enumModel in document.Enums)
        {
            compilationUnits.Add(BuildCompilationUnit(enumModel, @namespace));
        }
        
        // Генерируем IHttpClientMetrics и HttpClientMetrics один раз, если включены метрики и есть хотя бы один клиент
        if (_options.GenerateMetrics && document.Clients.Count > 0)
        {
            compilationUnits.Add(BuildCompilationUnitForIHttpClientMetrics(@namespace));
            compilationUnits.Add(BuildCompilationUnitForHttpClientMetrics(@namespace));
        }
        
        // Генерируем клиенты
        foreach (var client in document.Clients)
        {
            // Если интерфейсы в отдельном файле, генерируем их отдельно
            if (_options.GenerateInterfaces && _options.InterfaceFileStrategy == InterfaceFileStrategy.SeparateFile)
            {
                compilationUnits.Add(BuildCompilationUnitForInterface(client, @namespace));
            }
            
            compilationUnits.Add(BuildCompilationUnit(client, @namespace));
            
            // Если включен OpenTelemetry (traces или metrics), генерируем декоратор
            if (_options.GenerateTraces || _options.GenerateMetrics)
            {
                compilationUnits.Add(BuildCompilationUnitForDecorator(client, @namespace));
            }
        }
        
        // Генерируем ApiException один раз, если есть хотя бы один клиент
        if (document.Clients.Count > 0)
        {
            compilationUnits.Add(BuildCompilationUnitApiException(@namespace));
        }
        
        return compilationUnits;
    }
    
    /// <summary>
    /// Строит CompilationUnit из IrClient
    /// </summary>
    /// <summary>
    /// Разводит имена операции и клиента: в C# член не может называться так же, как содержащий его
    /// тип (CS0542), а такое совпадение возникает всякий раз, когда operationId повторяет имя
    /// группы. Совпавшая операция получает префикс HTTP-метода — он есть у каждой операции, поэтому
    /// имя остаётся однозначным и предсказуемым.
    /// </summary>
    /// <param name="client">Клиент из промежуточного представления.</param>
    /// <returns>Тот же клиент либо его копия с переименованной операцией.</returns>
    private static IrClient ResolveMemberNameCollisions(IrClient client)
    {
        if (!client.Operations.Any(operation => string.Equals(operation.MethodName, client.Name, StringComparison.Ordinal)))
        {
            return client;
        }

        var operations = client.Operations
            .Select(operation => string.Equals(operation.MethodName, client.Name, StringComparison.Ordinal)
                ? operation with { MethodName = ToPascalCase(operation.HttpMethod) + operation.MethodName }
                : operation)
            .ToList();

        return client with { Operations = operations };
    }

    /// <summary>Приводит слово к PascalCase: <c>post</c> -> <c>Post</c>.</summary>
    /// <param name="value">Исходное слово.</param>
    /// <returns>Слово с заглавной первой буквой.</returns>
    private static string ToPascalCase(string value)
        => string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    /// <summary>Builds an injectable HTTP client and its optional colocated interface.</summary>
    /// <param name="client">The normalized client operations.</param>
    /// <param name="namespace">The namespace name, or null/empty for the global namespace.</param>
    /// <returns>The client compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnit(IrClient client, string? @namespace = null)
    {
        client = ResolveMemberNameCollisions(client);

        var usings = BuildUsingDirectives(ExtractDependencies(client));
        var declarations = new List<DeclarationNode>();
        
        // Если нужно генерировать интерфейсы и они в том же файле
        if (_options.GenerateInterfaces && _options.InterfaceFileStrategy == InterfaceFileStrategy.SameFile)
        {
            var interfaceNode = BuildClientInterface(client);
            declarations.Add(interfaceNode);
        }
        
        var classNode = BuildClientClass(client);
        declarations.Add(classNode);
        
        return CreateCompilationUnit(usings, declarations, @namespace);
    }
    
    /// <summary>Builds a standalone interface for the generated HTTP client.</summary>
    /// <param name="client">The normalized client operations.</param>
    /// <param name="namespace">The namespace name, or null/empty for the global namespace.</param>
    /// <returns>The interface compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnitForInterface(IrClient client, string? @namespace = null)
    {
        client = ResolveMemberNameCollisions(client);

        var usings = BuildUsingDirectives(ExtractDependencies(client));
        var interfaceNode = BuildClientInterface(client);
        return CreateCompilationUnit(usings, [interfaceNode], @namespace);
    }
    
    /// <summary>Builds the shared asynchronous metrics contract.</summary>
    /// <param name="namespace">The generated namespace.</param>
    /// <returns>The metrics interface compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnitForIHttpClientMetrics(string? @namespace = null)
    {
        var dependencies = new HashSet<string> { "System", "System.Threading.Tasks" };
        var usings = BuildUsingDirectives(dependencies.ToList());
        var metricsInterface = BuildIHttpClientMetricsInterface();
        return CreateCompilationUnit(usings, [metricsInterface], @namespace);
    }
    
    /// <summary>Builds shared metrics with the namespaces required by Meter and its instruments.</summary>
    /// <param name="namespace">The generated namespace.</param>
    /// <returns>The metrics implementation compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnitForHttpClientMetrics(string? @namespace = null)
    {
        var dependencies = new HashSet<string> { "System", "System.Collections.Generic", "System.Diagnostics.Metrics", "System.Threading.Tasks" };
        var usings = BuildUsingDirectives(dependencies.ToList());
        var metricsClass = BuildHttpClientMetricsClass();
        return CreateCompilationUnit(usings, [metricsClass], @namespace);
    }
    
    /// <summary>Builds the tracing and metrics decorator for a generated client.</summary>
    /// <param name="client">The operations wrapped by the decorator.</param>
    /// <param name="namespace">The namespace name, or null/empty for the global namespace.</param>
    /// <returns>The telemetry decorator compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnitForDecorator(IrClient client, string? @namespace = null)
    {
        client = ResolveMemberNameCollisions(client);

        var dependencies = ExtractDependencies(client).ToList();
        // Добавляем зависимости для OpenTelemetry
        dependencies.Add("System.Diagnostics");
        
        var usings = BuildUsingDirectives(dependencies);
        var decoratorNode = BuildOpenTelemetryDecorator(client);
        return CreateCompilationUnit(usings, [decoratorNode], @namespace);
    }
    
    /// <summary>Builds a decorator whose metrics dependency uses the public metrics contract.</summary>
    /// <param name="client">The client operations wrapped by telemetry.</param>
    /// <returns>The telemetry decorator class.</returns>
    private ClassDeclarationNode BuildOpenTelemetryDecorator(IrClient client)
    {
        var decoratorName = new IdentifierNode($"{client.Name}TelemetryDecorator");
        var baseClientName = new IdentifierNode(client.Name);
        var interfaceName = _options.GenerateInterfaces 
            ? new IdentifierNode($"I{client.Name}")
            : null;
        
        var members = new List<DeclarationNode>();
        
        // Поле для базового клиента
        var baseClientField = new FieldDeclarationNode(
            name: new IdentifierNode("_baseClient"),
            type: interfaceName != null 
                ? new SimpleTypeNode(interfaceName, isPrimitive: false)
                : new SimpleTypeNode(baseClientName, isPrimitive: false),
            accessModifier: AccessModifier.Private,
            isReadOnly: true,
            defaultValue: null);
        members.Add(baseClientField);
        
        // Поле для ActivitySource
        var activitySourceField = new FieldDeclarationNode(
            name: new IdentifierNode("_activitySource"),
            type: new SimpleTypeNode(new IdentifierNode("ActivitySource"), isPrimitive: false),
            accessModifier: AccessModifier.Private,
            isReadOnly: true,
            defaultValue: null);
        members.Add(activitySourceField);
        
        // Поле для метрик (если включены)
        if (_options.GenerateMetrics)
        {
            var metricsField = new FieldDeclarationNode(
                name: new IdentifierNode("_metrics"),
                type: new SimpleTypeNode(new IdentifierNode("IHttpClientMetrics"), isPrimitive: false),
                accessModifier: AccessModifier.Private,
                isReadOnly: true,
                defaultValue: null);
            members.Add(metricsField);
        }
        
        // Конструктор
        var constructorParams = new List<ParameterDeclarationNode>
        {
            new ParameterDeclarationNode(
                name: new IdentifierNode("baseClient"),
                type: interfaceName != null 
                    ? new SimpleTypeNode(interfaceName, isPrimitive: false)
                    : new SimpleTypeNode(baseClientName, isPrimitive: false),
                isRequired: true),
            new ParameterDeclarationNode(
                name: new IdentifierNode("activitySource"),
                type: new SimpleTypeNode(new IdentifierNode("ActivitySource"), isPrimitive: false),
                isRequired: true)
        };
        
        // Добавляем параметры для метрик, если включены
        if (_options.GenerateMetrics)
        {
            constructorParams.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("metrics"),
                type: new SimpleTypeNode(new IdentifierNode("IHttpClientMetrics"), isPrimitive: false),
                isRequired: true));
        }
        
        var constructorBodyStatements = new List<StatementNode>();
        // _baseClient = baseClient;
        var baseClientAssignment = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("_baseClient")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("baseClient")));
        constructorBodyStatements.Add(new ExpressionStatementNode(baseClientAssignment));
        
        // _activitySource = activitySource;
        var activitySourceAssignment = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("_activitySource")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("activitySource")));
        constructorBodyStatements.Add(new ExpressionStatementNode(activitySourceAssignment));
        
        // Инициализация метрик, если включены
        if (_options.GenerateMetrics)
        {
            // _metrics = metrics;
            var metricsAssignment = new BinaryExpressionNode(
                left: new IdentifierExpressionNode(new IdentifierNode("_metrics")),
                @operator: BinaryOperator.Assign,
                right: new IdentifierExpressionNode(new IdentifierNode("metrics")));
            constructorBodyStatements.Add(new ExpressionStatementNode(metricsAssignment));
        }
        
        var constructorBody = new BlockNode(constructorBodyStatements.ToImmutableList());
        var constructor = new ConstructorDeclarationNode(
            name: decoratorName,
            parameters: constructorParams.ToImmutableList(),
            body: constructorBody,
            accessModifier: AccessModifier.Public);
        members.Add(constructor);
        
        // Базовые типы (интерфейс если есть, иначе пусто)
        var baseTypes = new List<TypeNode>();
        if (interfaceName != null)
        {
            baseTypes.Add(new SimpleTypeNode(interfaceName, isPrimitive: false));
        }
        
        // Методы декоратора (оборачивают вызовы базового клиента в Activity)
        var decoratorMethods = client.Operations
            .Select(op => BuildDecoratorMethod(op, client.Name))
            .ToList<DeclarationNode>();
        members.AddRange(decoratorMethods);
        
        return new ClassDeclarationNode(
            name: decoratorName,
            accessModifier: AccessModifier.Public,
            isSealed: true,
            isStatic: false,
            isPartial: false,
            baseTypes: baseTypes.ToImmutableList(),
            members: members.ToImmutableList());
    }
    
    /// <summary>Wraps an operation with optional tracing and metrics for typed or bodyless tasks.</summary>
    /// <param name="operation">The operation with the original HTTP contract.</param>
    /// <param name="clientName">The client identity used by telemetry tags.</param>
    /// <returns>A decorator method matching the generated client signature.</returns>
    private MethodDeclarationNode BuildDecoratorMethod(IrOperation operation, string clientName)
    {
        operation = ResolveMethodParameterCollisions(operation);
        var name = new IdentifierNode(operation.MethodName);
        var returnType = ExtractReturnType(operation.Responses);
        var resultType = ExtractReturnTypeGenericArgument(returnType);
        var parameters = BuildMethodParameters(operation);
        var usedNames = parameters.Select(parameter => parameter.Name.Name).ToHashSet(StringComparer.Ordinal);
        var activityName = "activity";
        while (!usedNames.Add(activityName)) activityName += "_";
        var exceptionName = "exception";
        while (!usedNames.Add(exceptionName)) exceptionName += "_";
        var activity = new IdentifierExpressionNode(new IdentifierNode(activityName));
        var exception = new IdentifierExpressionNode(new IdentifierNode(exceptionName));
        var statements = new List<StatementNode>();
        if (_options.GenerateTraces)
        {
            var traceName = string.IsNullOrWhiteSpace(_options.ServiceName)
                ? $"{clientName}.{operation.MethodName}"
                : $"{_options.ServiceName}.{clientName}.{operation.MethodName}";
            var startActivity = new CallExpressionNode(
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("this._activitySource")), new IdentifierNode("StartActivity")),
                new ExpressionNode[] { new LiteralExpressionNode(traceName, LiteralKind.String) });
            statements.Add(new VariableDeclarationStatementNode(
                new IdentifierNode(activityName), new NullableTypeNode(BuildSimpleType("Activity")), startActivity));
        }

        ExpressionNode invocation = new CallExpressionNode(
            new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("this._baseClient")), name),
            parameters.Select(parameter => new IdentifierExpressionNode(parameter.Name)).ToImmutableList<ExpressionNode>());
        if (_options.GenerateMetrics)
        {
            var callback = new CallExpressionNode(new IdentifierExpressionNode(new IdentifierNode("async")),
                isLambda: true, lambdaBody: new AwaitExpressionNode(invocation));
            invocation = new CallExpressionNode(
                new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("this._metrics")), new IdentifierNode("Measure")),
                new ExpressionNode[] { new LiteralExpressionNode(clientName, LiteralKind.String), new LiteralExpressionNode(operation.MethodName, LiteralKind.String), callback },
                genericTypeArgument: resultType);
        }
        var awaitedInvocation = new AwaitExpressionNode(invocation);
        var tryStatements = resultType is null
            ? new StatementNode[] { new ExpressionStatementNode(awaitedInvocation) }
            : new StatementNode[] { new ReturnStatementNode(awaitedInvocation) };
        var catchStatements = new List<StatementNode>();
        var finallyStatements = new List<StatementNode>();
        if (_options.GenerateTraces)
        {
            var status = new CallExpressionNode(new MemberAccessExpressionNode(activity, new IdentifierNode("SetStatus")),
                new ExpressionNode[]
                {
                    new MemberAccessExpressionNode(new IdentifierExpressionNode(new IdentifierNode("ActivityStatusCode")), new IdentifierNode("Error")),
                    new MemberAccessExpressionNode(exception, new IdentifierNode("Message"))
                });
            var tag = new CallExpressionNode(new MemberAccessExpressionNode(activity, new IdentifierNode("SetTag")),
                new ExpressionNode[] { new LiteralExpressionNode("exception.stacktrace", LiteralKind.String),
                    new CallExpressionNode(new MemberAccessExpressionNode(exception, new IdentifierNode("ToString"))) });
            catchStatements.Add(BuildNotNullGuard(activityName, new ExpressionStatementNode(status), new ExpressionStatementNode(tag)));
            finallyStatements.Add(BuildNotNullGuard(activityName,
                new ExpressionStatementNode(new CallExpressionNode(new MemberAccessExpressionNode(activity, new IdentifierNode("Dispose"))))));
        }
        catchStatements.Add(new ThrowStatementNode(null));
        statements.Add(new TryCatchStatementNode(
            new BlockNode(tryStatements.ToImmutableList()),
            new[] { new CatchClauseNode(new BlockNode(catchStatements.ToImmutableList()), BuildSimpleType("Exception"),
                _options.GenerateTraces ? new IdentifierNode(exceptionName) : null) }.ToImmutableList(),
            new BlockNode(finallyStatements.ToImmutableList())));
        return new MethodDeclarationNode(name, returnType, parameters, new BlockNode(statements.ToImmutableList()),
            isAsync: true, accessModifier: AccessModifier.Public);
    }

    // Приватные вспомогательные методы для IR операций
    
    /// <summary>Allocates schema parameters around generated request and cancellation arguments.</summary>
    /// <param name="operation">The operation whose source parameter names are made unique.</param>
    /// <returns>The operation with unchanged wire names and collision-free source identifiers.</returns>
    private static IrOperation ResolveMethodParameterCollisions(IrOperation operation)
    {
        var usedNames = new HashSet<string>(["request", "cancellationToken"], StringComparer.Ordinal);
        IrParameter Resolve(IrParameter parameter)
        {
            var name = parameter.Name;
            while (!usedNames.Add(name))
                name += "_";
            return parameter with { Name = name, OriginalName = parameter.OriginalName };
        }

        return operation with
        {
            PathParameters = operation.PathParameters.Select(Resolve).ToArray(),
            QueryParameters = operation.QueryParameters.Select(Resolve).ToArray(),
            HeaderParameters = operation.HeaderParameters.Select(Resolve).ToArray()
        };
    }

    /// <summary>Builds method parameters without duplicating nullable wrappers.</summary>
    /// <param name="operation">The normalized HTTP operation.</param>
    /// <returns>The ordered parameters and cancellation token.</returns>
    private IReadOnlyList<ParameterDeclarationNode> BuildMethodParameters(IrOperation operation)
    {
        var parameters = new List<ParameterDeclarationNode>();
        
        // Path parameters
        foreach (var param in operation.PathParameters)
        {
            var type = BuildType(param.Type);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode(param.Name),
                type: type,
                isRequired: true));
        }
        
        // Query parameters
        foreach (var param in operation.QueryParameters)
        {
            var type = BuildType(param.Type);
            // В C# опциональные параметры делаются через nullable или default значения
            var nullableType = param.IsRequired || type is NullableTypeNode ? type : new NullableTypeNode(type);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode(param.Name),
                type: nullableType,
                isRequired: param.IsRequired));
        }
        
        // Header parameters
        foreach (var param in operation.HeaderParameters)
        {
            var type = BuildType(param.Type);
            var nullableType = param.IsRequired || type is NullableTypeNode ? type : new NullableTypeNode(type);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode(param.Name),
                type: nullableType,
                isRequired: param.IsRequired));
        }
        
        // Request body
        if (operation.RequestBody != null)
        {
            var bodyType = BuildType(operation.RequestBody.Type);
            // Логика проверки required свойств теперь на уровне IR
            // IrRequestBody.IsRequired уже учитывает наличие required свойств в модели
            var nullableType = operation.RequestBody.IsRequired || bodyType is NullableTypeNode
                ? bodyType
                : new NullableTypeNode(bodyType);
            parameters.Add(new ParameterDeclarationNode(
                name: new IdentifierNode("request"),
                type: nullableType,
                isRequired: operation.RequestBody.IsRequired));
        }
        
        // CancellationToken с default value
        var cancellationTokenType = new SimpleTypeNode(new IdentifierNode("CancellationToken"), isPrimitive: false);
        var defaultCancellationToken = new DefaultExpressionNode();
        parameters.Add(new ParameterDeclarationNode(
            name: new IdentifierNode("cancellationToken"),
            type: cancellationTokenType,
            isRequired: false,
            defaultValue: defaultCancellationToken));
        
        return parameters.ToImmutableList();
    }
    
    /// <summary>Builds an HTTP implementation with allocated locals and typed transport boundaries.</summary>
    /// <param name="operation">The operation with unique C# parameter names.</param>
    /// <returns>The complete asynchronous request implementation.</returns>
    private BlockNode? BuildMethodBody(IrOperation operation)
    {
        // Служебные переменные тела запроса именуются генератором, а параметры приходят из схемы,
        // и они сталкиваются: операция с параметром url давала "string url = urlBuilder.ToString()"
        // рядом с параметром того же имени. Совпавшее служебное имя получает подчёркивание.
        var parameterNames = operation.PathParameters
            .Concat(operation.QueryParameters)
            .Concat(operation.HeaderParameters)
            .Select(parameter => parameter.Name)
            .ToHashSet(StringComparer.Ordinal);

        parameterNames.UnionWith(["request", "cancellationToken"]);
        var localNames = new Dictionary<string, string>(StringComparer.Ordinal);
        string Local(string name)
        {
            if (localNames.TryGetValue(name, out var existing))
                return existing;
            var candidate = name;
            while (!parameterNames.Add(candidate))
                candidate += "_";
            localNames[name] = candidate;
            return candidate;
        }

        var statements = new List<StatementNode>
        {
            new ExpressionStatementNode(new CallExpressionNode(
                target: new MemberAccessExpressionNode(
                    @object: new IdentifierExpressionNode(new IdentifierNode("cancellationToken")),
                    member: new IdentifierNode("ThrowIfCancellationRequested"))))
        };

        var successResponse = SelectSuccessResponse(operation.Responses);
        var successType = successResponse?.Type is { } responseType ? BuildType(responseType) : null;

        // Подсчитываем capacity для StringBuilder
        var estimatedCapacity = EstimateStringBuilderCapacity(operation);
        
        // 1. Строим URL через StringBuilder с предварительно рассчитанной capacity
        var capacityLiteral = new LiteralExpressionNode(estimatedCapacity.ToString(CultureInfo.InvariantCulture), LiteralKind.Number);
        var urlBuilderVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("urlBuilder")),
            type: new SimpleTypeNode(new IdentifierNode("StringBuilder"), isPrimitive: false),
            initializer: new CallExpressionNode(
                target: new IdentifierExpressionNode(new IdentifierNode("StringBuilder")),
                arguments: new ExpressionNode[] { capacityLiteral }.ToImmutableList(),
                isNewExpression: true));
        statements.Add(urlBuilderVar);
        
        // Строим URL по частям, заменяя {param} на значения
        var basePath = operation.Path;
        var appendMethod = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("urlBuilder"))),
            member: new IdentifierNode("Append"));
        
        // Парсим путь и добавляем части в StringBuilder
        var pathParts = new List<(string? Text, string? ParamName)>();
        var currentIndex = 0;
        
        while (currentIndex < basePath.Length)
        {
            var paramStart = basePath.IndexOf('{', currentIndex);
            if (paramStart == -1)
            {
                // Остаток пути без параметров
                if (currentIndex < basePath.Length)
                {
                    pathParts.Add((basePath.Substring(currentIndex), null));
                }
                break;
            }
            
            // Добавляем текст до параметра
            if (paramStart > currentIndex)
            {
                pathParts.Add((basePath.Substring(currentIndex, paramStart - currentIndex), null));
            }
            
            // Находим конец параметра
            var paramEnd = basePath.IndexOf('}', paramStart);
            if (paramEnd == -1)
            {
                // Некорректный формат, добавляем как есть
                pathParts.Add((basePath.Substring(paramStart), null));
                break;
            }
            
            // Извлекаем имя параметра
            var paramName = basePath.Substring(paramStart + 1, paramEnd - paramStart - 1);
            pathParts.Add((Text: (string?)null, ParamName: paramName));
            
            currentIndex = paramEnd + 1;
        }
        
        // Добавляем части в StringBuilder
        foreach (var (text, paramName) in pathParts)
        {
            if (text != null)
            {
                // Исправляем двойное экранирование - убираем лишние кавычки
                var appendTextCall = new CallExpressionNode(
                    target: appendMethod,
                    arguments: new[] { new LiteralExpressionNode(text, LiteralKind.String) }.ToImmutableList());
                statements.Add(new ExpressionStatementNode(appendTextCall));
            }
            else if (paramName != null)
            {
                // Находим соответствующий path parameter
                var pathParam = operation.PathParameters.FirstOrDefault(p => p.OriginalName == paramName);
                if (pathParam != null)
                {
                    var paramExpr = new IdentifierExpressionNode(new IdentifierNode(pathParam.Name));
                    var paramValue = BuildQueryValueExpression(paramExpr);

                    var appendParamCall = new CallExpressionNode(
                        target: appendMethod,
                        arguments: new[] { paramValue }.ToImmutableList());
                    statements.Add(new ExpressionStatementNode(appendParamCall));
                }
            }
        }
        
        // Добавляем query parameters — только непустые (null-safe: опциональные параметры не должны
        // попадать в URL как "null"/бросать NRE), массивы разворачиваются в повторяющиеся пары
        // key=v1&key=v2 (form/explode=true style, как ждёт ASP.NET model binding).
        if (operation.QueryParameters.Count > 0)
        {
            var queryPartsType = new GenericTypeNode(
                new IdentifierNode("List"),
                new TypeNode[] { new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true) }.ToImmutableList());
            var queryPartsDecl = new VariableDeclarationStatementNode(
                name: new IdentifierNode(Local("queryParts")),
                type: queryPartsType,
                initializer: new CallExpressionNode(
                    target: new IdentifierExpressionNode(new IdentifierNode("List")),
                    arguments: ImmutableList<ExpressionNode>.Empty,
                    isNewExpression: true,
                    genericTypeArgument: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true)));
            statements.Add(queryPartsDecl);

            var queryPartsAdd = new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode(Local("queryParts"))),
                member: new IdentifierNode("Add"));

            foreach (var queryParam in operation.QueryParameters)
            {
                var unwrappedType = UnwrapNullableOrOptional(queryParam.Type);

                if (unwrappedType is IrArrayType arrayType)
                {
                    var itemVar = new IdentifierNode(Local("item"));
                    var itemExpr = new IdentifierExpressionNode(itemVar);

                    var itemPair = new BinaryExpressionNode(
                        // Ключ в URL — исходное «проводное» имя параметра (Paging.Offset), не sanitized
                        // идентификатор метода (Paging_Offset): иначе ASP.NET model binding не находит его.
                        left: new LiteralExpressionNode($"{Uri.EscapeDataString(queryParam.OriginalName)}=", LiteralKind.String),
                        @operator: BinaryOperator.Add,
                        right: BuildQueryValueExpression(itemExpr));

                    var addItemStatement = new ExpressionStatementNode(new CallExpressionNode(
                        target: queryPartsAdd,
                        arguments: new[] { (ExpressionNode)itemPair }.ToImmutableList()));

                    var foreachStatement = new ForEachStatementNode(
                        variable: itemVar,
                        collection: new IdentifierExpressionNode(new IdentifierNode(queryParam.Name)),
                        body: new BlockNode(new StatementNode[] { addItemStatement }.ToImmutableList()));

                    statements.Add(queryParam.IsRequired
                        ? foreachStatement
                        : BuildNotNullGuard(queryParam.Name, foreachStatement));
                }
                else
                {
                    var paramExpr = new IdentifierExpressionNode(new IdentifierNode(queryParam.Name));

                    var pair = new BinaryExpressionNode(
                        // Ключ в URL — исходное «проводное» имя параметра (Paging.Offset), не sanitized
                        // идентификатор метода (Paging_Offset): иначе ASP.NET model binding не находит его.
                        left: new LiteralExpressionNode($"{Uri.EscapeDataString(queryParam.OriginalName)}=", LiteralKind.String),
                        @operator: BinaryOperator.Add,
                        right: BuildQueryValueExpression(paramExpr));

                    var addStatement = new ExpressionStatementNode(new CallExpressionNode(
                        target: queryPartsAdd,
                        arguments: new[] { (ExpressionNode)pair }.ToImmutableList()));

                    statements.Add(queryParam.IsRequired
                        ? addStatement
                        : BuildNotNullGuard(queryParam.Name, addStatement));
                }
            }

            var queryPartsCountCheck = new BinaryExpressionNode(
                left: new MemberAccessExpressionNode(
                    @object: new IdentifierExpressionNode(new IdentifierNode(Local("queryParts"))),
                    member: new IdentifierNode("Count")),
                @operator: BinaryOperator.GreaterThan,
                right: new LiteralExpressionNode("0", LiteralKind.Number));

            var appendQuestionMark = new ExpressionStatementNode(new CallExpressionNode(
                target: appendMethod,
                arguments: new ExpressionNode[] { new LiteralExpressionNode("?", LiteralKind.String) }.ToImmutableList()));

            var joinCall = new CallExpressionNode(
                target: new MemberAccessExpressionNode(
                    @object: new IdentifierExpressionNode(new IdentifierNode("string")),
                    member: new IdentifierNode("Join")),
                arguments: new ExpressionNode[]
                {
                    new LiteralExpressionNode("&", LiteralKind.String),
                    new IdentifierExpressionNode(new IdentifierNode(Local("queryParts")))
                }.ToImmutableList());

            var appendJoined = new ExpressionStatementNode(new CallExpressionNode(
                target: appendMethod,
                arguments: new[] { (ExpressionNode)joinCall }.ToImmutableList()));

            statements.Add(new IfStatementNode(
                condition: queryPartsCountCheck,
                thenBlock: new BlockNode(new StatementNode[] { appendQuestionMark, appendJoined }.ToImmutableList())));
        }
        
        // Получаем финальный URL: urlBuilder.ToString()
        var urlBuilderToString = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("urlBuilder"))),
            member: new IdentifierNode("ToString"));
        var urlStringVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("url")),
            type: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
            initializer: new CallExpressionNode(
                target: urlBuilderToString,
                arguments: ImmutableList<ExpressionNode>.Empty));
        statements.Add(urlStringVar);
        
        // 2-5. Создаем HttpRequestMessage и отправляем запрос через HttpClient.SendAsync с using
        // Используем HttpMethod.Get, HttpMethod.Post и т.д. вместо new HttpMethod("GET")
        var httpMethodName = GetHttpMethodPropertyName(operation.HttpMethod);
        var httpMethodStaticProperty = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode("HttpMethod")),
            member: new IdentifierNode(httpMethodName));
        
        var requestMessageType = new IdentifierExpressionNode(new IdentifierNode("HttpRequestMessage"));
        var urlExpr = new IdentifierExpressionNode(new IdentifierNode(Local("url")));
        var requestMessageCtorArgs = new ExpressionNode[] { httpMethodStaticProperty, urlExpr };
        var requestMessageCtor = new CallExpressionNode(
            target: requestMessageType,
            arguments: requestMessageCtorArgs.ToImmutableList(),
            isNewExpression: true);
        
        var requestMessageVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("requestMessage")),
            type: new SimpleTypeNode(new IdentifierNode("HttpRequestMessage"), isPrimitive: false),
            initializer: requestMessageCtor);
        
        // Создаем блок для using statement requestMessage
        var requestMessageUsingBodyStatements = new List<StatementNode>();
        
        // 3. Устанавливаем Headers (Accept: application/json)
        var requestMessageHeaders = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("requestMessage"))),
            member: new IdentifierNode("Headers"));
        var acceptHeader = new MemberAccessExpressionNode(
            @object: requestMessageHeaders,
            member: new IdentifierNode("Accept"));
        var acceptAdd = new MemberAccessExpressionNode(
            @object: acceptHeader,
            member: new IdentifierNode("Add"));
        var mediaTypeJson = new IdentifierExpressionNode(new IdentifierNode("MediaTypeWithQualityHeaderValue"));
        var mediaTypeJsonCtor = new CallExpressionNode(
            target: mediaTypeJson,
            arguments: new ExpressionNode[] { new LiteralExpressionNode("application/json", LiteralKind.String) }.ToImmutableList(),
            isNewExpression: true);
        var acceptAddCall = new CallExpressionNode(
            target: acceptAdd,
            arguments: new[] { mediaTypeJsonCtor }.ToImmutableList());
        requestMessageUsingBodyStatements.Add(new ExpressionStatementNode(acceptAddCall));

        foreach (var parameter in operation.HeaderParameters)
        {
            var addHeader = new MemberAccessExpressionNode(
                @object: requestMessageHeaders,
                member: new IdentifierNode("TryAddWithoutValidation"));
            var value = new CallExpressionNode(
                target: new MemberAccessExpressionNode(
                    @object: new IdentifierExpressionNode(new IdentifierNode("HttpWireValueFormatter")),
                    member: new IdentifierNode("Format")),
                arguments: new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode(parameter.Name)) }.ToImmutableList());
            var add = new ExpressionStatementNode(new CallExpressionNode(
                target: addHeader,
                arguments: new ExpressionNode[] { new LiteralExpressionNode(parameter.OriginalName, LiteralKind.String), value }.ToImmutableList()));
            requestMessageUsingBodyStatements.Add(parameter.IsRequired ? add : BuildNotNullGuard(parameter.Name, add));
        }

        // Add the request content using the media type declared by the operation.
        if (operation.RequestBody != null)
        {
            var requestContentStatements = new List<StatementNode>();
            var requestBodyParam = new IdentifierExpressionNode(new IdentifierNode("request"));
            var requestMessageContent = new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode(Local("requestMessage"))),
                member: new IdentifierNode("Content"));

            if (operation.RequestBody.IsMultipartFormData)
            {
                var multipartFactory = new MemberAccessExpressionNode(
                    @object: new IdentifierExpressionNode(new IdentifierNode("MultipartFormDataContentFactory")),
                    member: new IdentifierNode("Create"));
                var multipartContent = new CallExpressionNode(
                    target: multipartFactory,
                    arguments: new[] { requestBodyParam }.ToImmutableList());
                var multipartAssign = new BinaryExpressionNode(
                    left: requestMessageContent,
                    @operator: BinaryOperator.Assign,
                    right: multipartContent);
                requestContentStatements.Add(new ExpressionStatementNode(multipartAssign));
            }
            else
            {
                // JsonContent defers serialization until the token-aware HTTP content write.
                var jsonContent = new CallExpressionNode(
                    target: new MemberAccessExpressionNode(
                        @object: new IdentifierExpressionNode(new IdentifierNode("JsonContent")),
                        member: new IdentifierNode("Create")),
                    arguments: new ExpressionNode[]
                    {
                        requestBodyParam,
                        new CastExpressionNode(
                            new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("MediaTypeHeaderValue"), isPrimitive: false)),
                            new LiteralExpressionNode(null, LiteralKind.Null)),
                        new MemberAccessExpressionNode(
                            new IdentifierExpressionNode(new IdentifierNode("JsonSerializerOptions")),
                            new IdentifierNode("Default"))
                    }.ToImmutableList());
                var contentAssign = new BinaryExpressionNode(
                    left: requestMessageContent,
                    @operator: BinaryOperator.Assign,
                    right: jsonContent);
                requestContentStatements.Add(new ExpressionStatementNode(contentAssign));
            }
            if (operation.RequestBody.IsRequired)
                requestMessageUsingBodyStatements.AddRange(requestContentStatements);
            else
                requestMessageUsingBodyStatements.Add(new IfStatementNode(
                    new BinaryExpressionNode(requestBodyParam, BinaryOperator.NotEqual, new LiteralExpressionNode(null, LiteralKind.Null)),
                    new BlockNode(requestContentStatements.ToImmutableList())));
        }

        // Извлекаем данные из requestMessage для ApiException (до отправки запроса, чтобы были доступны в блоках обработки ошибок)
        var requestUriAccess = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("requestMessage"))),
            member: new IdentifierNode("RequestUri"));
        var requestUriVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("requestUri")),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("Uri"), isPrimitive: false)),
            initializer: requestUriAccess);
        requestMessageUsingBodyStatements.Add(requestUriVar);
        
        var requestMethodAccess = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("requestMessage"))),
            member: new IdentifierNode("Method"));
        var requestMethodVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("httpMethod")),
            type: new SimpleTypeNode(new IdentifierNode("HttpMethod"), isPrimitive: false),
            initializer: requestMethodAccess);
        requestMessageUsingBodyStatements.Add(requestMethodVar);
        
        // 5. Отправляем запрос через HttpClient.SendAsync
        var httpClientField = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode("this")),
            member: new IdentifierNode("_httpClient"));
        var sendAsyncMethod = new MemberAccessExpressionNode(
            @object: httpClientField,
            member: new IdentifierNode("SendAsync"));
        var cancellationTokenParam = new IdentifierExpressionNode(new IdentifierNode("cancellationToken"));
        var requestMessageExpr = new IdentifierExpressionNode(new IdentifierNode(Local("requestMessage")));
        var sendAsyncCall = new CallExpressionNode(
            target: sendAsyncMethod,
            arguments: new[] { requestMessageExpr, cancellationTokenParam }.ToImmutableList());
        var awaitSend = new AwaitExpressionNode(sendAsyncCall);
        
        // Создаем блок для вложенного using statement (responseMessage внутри requestMessage)
        // Сначала создаем переменную responseMessage
        var responseMessageVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("responseMessage")),
            type: new SimpleTypeNode(new IdentifierNode("HttpResponseMessage"), isPrimitive: false),
            initializer: awaitSend);
        
        var responseMessageUsingBodyStatements = new List<StatementNode>();
        
        // ReadAsStringAsync (переименовано content -> responseContent)
        var responseMessageContentAccess = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("responseMessage"))),
            member: new IdentifierNode("Content"));
        var readAsString = new MemberAccessExpressionNode(
            @object: responseMessageContentAccess,
            member: new IdentifierNode("ReadAsStringAsync"));
        var cancellationTokenParamForRead = new IdentifierExpressionNode(new IdentifierNode("cancellationToken"));
        var readAsStringCall = new CallExpressionNode(
            target: readAsString,
            arguments: new[] { cancellationTokenParamForRead }.ToImmutableList());
        var awaitRead = new AwaitExpressionNode(readAsStringCall);
        
        var responseContentVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("responseContent")),
            type: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
            initializer: awaitRead);
        responseMessageUsingBodyStatements.Add(responseContentVar);
        
        // Извлекаем данные из responseMessage для ApiException
        var statusCodeAccess = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("responseMessage"))),
            member: new IdentifierNode("StatusCode"));
        var statusCodeVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("statusCode")),
            type: new SimpleTypeNode(new IdentifierNode("HttpStatusCode"), isPrimitive: false),
            initializer: statusCodeAccess);
        responseMessageUsingBodyStatements.Add(statusCodeVar);
        
        var reasonPhraseAccess = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("responseMessage"))),
            member: new IdentifierNode("ReasonPhrase"));
        var reasonPhraseVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("reasonPhrase")),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true)),
            initializer: reasonPhraseAccess);
        responseMessageUsingBodyStatements.Add(reasonPhraseVar);
        
        var versionAccess = new MemberAccessExpressionNode(
            @object: new IdentifierExpressionNode(new IdentifierNode(Local("responseMessage"))),
            member: new IdentifierNode("Version"));
        var versionVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("version")),
            type: new SimpleTypeNode(new IdentifierNode("Version"), isPrimitive: false),
            initializer: versionAccess);
        responseMessageUsingBodyStatements.Add(versionVar);
        
        // Используем CastExpressionNode вместо metadata
        var statusCodeExpr = new IdentifierExpressionNode(new IdentifierNode(Local("statusCode")));
        var intType = new SimpleTypeNode(new IdentifierNode("int"), isPrimitive: true);
        var castExpr = new CastExpressionNode(
            targetType: intType,
            expression: statusCodeExpr);
        var intStatusCodeVar = new VariableDeclarationStatementNode(
            name: new IdentifierNode(Local("statusCodeInt")),
            type: intType,
            initializer: castExpr);
        responseMessageUsingBodyStatements.Add(intStatusCodeVar);
        
        var isSuccess = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode(Local("statusCodeInt"))),
            @operator: BinaryOperator.GreaterThanOrEqual,
            right: new LiteralExpressionNode("200", LiteralKind.Number));
        var isSuccessAnd = new BinaryExpressionNode(
            left: isSuccess,
            @operator: BinaryOperator.LogicalAnd,
            right: new BinaryExpressionNode(
                left: new IdentifierExpressionNode(new IdentifierNode(Local("statusCodeInt"))),
                @operator: BinaryOperator.LessThan,
                right: new LiteralExpressionNode("300", LiteralKind.Number)));
        
        // If statement для обработки success/error
        var ifStatements = new List<StatementNode>();
        var elseStatements = new List<StatementNode>();
        
        // Success branch: десериализуем в success type и помещаем в промежуточную переменную response
        if (successType != null)
        {
            // Пустое тело на успешном статусе (напр. 204 No Content или 200 без body) — не пытаемся
            // десериализовать (иначе JsonException «input does not contain any JSON tokens»); возвращаем
            // null. Возвращаемый тип метода — nullable success type, поэтому default! корректен.
            ifStatements.Add(new IfStatementNode(
                condition: BuildResponseContentIsEmptyExpression(Local("responseContent")),
                thenBlock: new BlockNode(new StatementNode[]
                {
                    new ReturnStatementNode(new DefaultExpressionNode(suppressNullableWarning: true))
                }.ToImmutableList()),
                elseBlock: null));

            var deserializeAccess = new MemberAccessExpressionNode(
                @object: responseMessageContentAccess,
                member: new IdentifierNode("ReadFromJsonAsync"));
            var serializerOptions = new MemberAccessExpressionNode(
                new IdentifierExpressionNode(new IdentifierNode("JsonSerializerOptions")),
                new IdentifierNode("Default"));
            var deserializeCall = new CallExpressionNode(
                target: deserializeAccess,
                arguments: new ExpressionNode[] { serializerOptions, cancellationTokenParam }.ToImmutableList(),
                genericTypeArgument: successType);
            
            // Десериализуем в nullable тип
            var nullableSuccessType = successType is NullableTypeNode ? successType : new NullableTypeNode(successType);
            
            // Помещаем десериализацию в промежуточную переменную response
            var responseVar = new VariableDeclarationStatementNode(
                name: new IdentifierNode(Local("response")),
                type: nullableSuccessType,
                initializer: new AwaitExpressionNode(deserializeCall));
            ifStatements.Add(responseVar);
            
            // Проверяем response на null
            var responseExpr = new IdentifierExpressionNode(new IdentifierNode(Local("response")));
            var nullCheck = new BinaryExpressionNode(
                left: responseExpr,
                @operator: BinaryOperator.Equal,
                right: new LiteralExpressionNode("null", LiteralKind.Null));
            
            var throwIfNullStatements = new List<StatementNode>();
            var apiExceptionForNullCtor = BuildApiExceptionCall(
                requestUri: new IdentifierExpressionNode(new IdentifierNode(Local("requestUri"))),
                httpMethod: new IdentifierExpressionNode(new IdentifierNode(Local("httpMethod"))),
                statusCode: new CastExpressionNode(
                    targetType: new SimpleTypeNode(new IdentifierNode("HttpStatusCode"), isPrimitive: false),
                    expression: new IdentifierExpressionNode(new IdentifierNode(Local("statusCodeInt")))),
                reasonPhrase: new IdentifierExpressionNode(new IdentifierNode(Local("reasonPhrase"))),
                version: new IdentifierExpressionNode(new IdentifierNode(Local("version"))),
                content: new IdentifierExpressionNode(new IdentifierNode(Local("responseContent"))),
                message: new LiteralExpressionNode("Response deserialization returned null", LiteralKind.String),
                innerException: null);
            throwIfNullStatements.Add(new ThrowStatementNode(apiExceptionForNullCtor));
            var throwIfNullBlock = new BlockNode(throwIfNullStatements.ToImmutableList());
            
            var ifNullCheck = new IfStatementNode(
                condition: nullCheck,
                thenBlock: throwIfNullBlock,
                elseBlock: null);
            ifStatements.Add(ifNullCheck);
            
            var returnStatement = new ReturnStatementNode(new CastExpressionNode(successType, responseExpr));
            ifStatements.Add(returnStatement);
        }
        else
        {
            var nullReturn = new ReturnStatementNode(null);
            ifStatements.Add(nullReturn);
        }
        
        // Пустое тело на не-2xx статусе (напр. незадекларированный в swagger 401 гейта с пустым body):
        // десериализовать нечего — сразу кидаем ApiException с сырым (пустым) телом и статусом, а не
        // JsonException из-за парсинга пустой строки в error type.
        elseStatements.Add(new IfStatementNode(
            condition: BuildResponseContentIsEmptyExpression(Local("responseContent")),
            thenBlock: new BlockNode(new StatementNode[]
            {
                new ThrowStatementNode(BuildApiExceptionCall(
                    requestUri: new IdentifierExpressionNode(new IdentifierNode(Local("requestUri"))),
                    httpMethod: new IdentifierExpressionNode(new IdentifierNode(Local("httpMethod"))),
                    statusCode: new CastExpressionNode(
                        targetType: new SimpleTypeNode(new IdentifierNode("HttpStatusCode"), isPrimitive: false),
                        expression: new IdentifierExpressionNode(new IdentifierNode(Local("statusCodeInt")))),
                    reasonPhrase: new IdentifierExpressionNode(new IdentifierNode(Local("reasonPhrase"))),
                    version: new IdentifierExpressionNode(new IdentifierNode(Local("version"))),
                    content: new IdentifierExpressionNode(new IdentifierNode(Local("responseContent"))),
                    message: new LiteralExpressionNode("Unexpected status code", LiteralKind.String),
                    innerException: null))
            }.ToImmutableList()),
            elseBlock: null));

        // Error payloads can be JSON, text, or HTML. Preserve their raw content and HTTP status.
        elseStatements.Add(new ThrowStatementNode(BuildApiExceptionCall(
            requestUri: new IdentifierExpressionNode(new IdentifierNode(Local("requestUri"))),
            httpMethod: new IdentifierExpressionNode(new IdentifierNode(Local("httpMethod"))),
            statusCode: new IdentifierExpressionNode(new IdentifierNode(Local("statusCode"))),
            reasonPhrase: new IdentifierExpressionNode(new IdentifierNode(Local("reasonPhrase"))),
            version: new IdentifierExpressionNode(new IdentifierNode(Local("version"))),
            content: new IdentifierExpressionNode(new IdentifierNode(Local("responseContent"))),
            message: new LiteralExpressionNode("Unexpected status code", LiteralKind.String),
            innerException: null)));

        var thenBlock = new BlockNode(ifStatements.ToImmutableList());
        var elseBlock = new BlockNode(elseStatements.ToImmutableList());
        var ifStatement = new IfStatementNode(
            condition: isSuccessAnd,
            thenBlock: thenBlock,
            elseBlock: elseBlock);
        responseMessageUsingBodyStatements.Add(ifStatement);
        
        // Создаем using statement для responseMessage
        var responseMessageUsingBlock = new BlockNode(responseMessageUsingBodyStatements.ToImmutableList());
        // Используем metadata для указания, что это using с переменной
        var responseMessageUsingResource = new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode(Local("responseMessage"))),
            arguments: ImmutableList<ExpressionNode>.Empty,
            isUsingVariable: true,
            variableDeclaration: responseMessageVar);
        var responseMessageUsing = new UsingStatementNode(
            resource: responseMessageUsingResource,
            body: responseMessageUsingBlock);
        
        // Добавляем using для responseMessage в requestMessageUsingBodyStatements (который уже содержит объявления requestUri и httpMethod)
        requestMessageUsingBodyStatements.Add(responseMessageUsing);
        
        // Создаем using statement для requestMessage, который содержит объявления переменных и using для responseMessage
        var requestMessageUsingBlock = new BlockNode(requestMessageUsingBodyStatements.ToImmutableList());
        var requestMessageUsingResource = new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode(Local("requestMessage"))),
            arguments: ImmutableList<ExpressionNode>.Empty,
            isUsingVariable: true,
            variableDeclaration: requestMessageVar);
        var requestMessageUsing = new UsingStatementNode(
            resource: requestMessageUsingResource,
            body: requestMessageUsingBlock);
        
        statements.Add(requestMessageUsing);
        
        return new BlockNode(statements.ToImmutableList());
    }
    
    /// <summary>
    /// Оценивает необходимую capacity для StringBuilder на основе длины URL и параметров
    /// </summary>
    private int EstimateStringBuilderCapacity(IrOperation operation)
    {
        var capacity = operation.Path.Length;
        
        // Добавляем место для path parameters
        foreach (var pathParam in operation.PathParameters)
        {
            var paramType = BuildType(pathParam.Type);
            if (paramType is SimpleTypeNode simple)
            {
                // Оценка длины для разных типов
                capacity += simple.Name.Name switch
                {
                    "int" => 8, // Максимальная длина int: -2147483648
                    "long" => 16, // Максимальная длина long: -9223372036854775808
                    "string" => 32, // Средняя оценка для строки
                    "bool" => 5, // "true" или "false"
                    "float" => 10,
                    "double" => 15,
                    "decimal" => 20,
                    _ => 16 // Дефолтная оценка
                };
            }
            else
            {
                capacity += 16; // Дефолтная оценка для сложных типов
            }
        }
        
        // Добавляем место для query parameters
        if (operation.QueryParameters.Count > 0)
        {
            capacity += 1; // "?"
            foreach (var queryParam in operation.QueryParameters)
            {
                capacity += queryParam.Name.Length + 1; // "name="
                var paramType = BuildType(queryParam.Type);
                if (paramType is SimpleTypeNode simple)
                {
                    capacity += simple.Name.Name switch
                    {
                        "int" => 8,
                        "long" => 16,
                        "string" => 32,
                        "bool" => 5,
                        "float" => 10,
                        "double" => 15,
                        "decimal" => 20,
                        _ => 16
                    };
                }
                else
                {
                    capacity += 16;
                }
                capacity += 1; // "&" между параметрами
            }
        }
        
        // Добавляем запас 20% для безопасности
        return (int)(capacity * 1.2);
    }
    
    /// <summary>
    /// Преобразует HTTP метод в имя статического свойства HttpMethod (Get, Post, Put и т.д.)
    /// </summary>
    private string GetHttpMethodPropertyName(string httpMethod)
    {
        return httpMethod.ToUpperInvariant() switch
        {
            "GET" => "Get",
            "POST" => "Post",
            "PUT" => "Put",
            "DELETE" => "Delete",
            "PATCH" => "Patch",
            "HEAD" => "Head",
            "OPTIONS" => "Options",
            "TRACE" => "Trace",
            _ => throw new NotSupportedException($"HTTP method {httpMethod} is not supported")
        };
    }
    
    /// <summary>
    /// Строит выражение <c>string.IsNullOrWhiteSpace(responseContent)</c> — общий guard пустого тела
    /// ответа (см. success/error-ветки <see cref="BuildMethodBody"/>).
    /// </summary>
    /// <param name="contentName">The allocated local containing the response text.</param>
    /// <returns>A check for an absent or whitespace-only response body.</returns>
    private static ExpressionNode BuildResponseContentIsEmptyExpression(string contentName)
    {
        return new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("string")),
                member: new IdentifierNode("IsNullOrWhiteSpace")),
            arguments: new ExpressionNode[]
            {
                new IdentifierExpressionNode(new IdentifierNode(contentName))
            }.ToImmutableList());
    }

    /// <summary>Selects the shared body type from successful responses independently of schema order.</summary>
    /// <param name="responses">The operation's declared responses.</param>
    /// <returns>The first typed success response, or a bodyless success when no body is declared.</returns>
    /// <exception cref="NotSupportedException">Successful responses have incompatible result types.</exception>
    private static IrResponse? SelectSuccessResponse(IReadOnlyList<IrResponse> responses)
    {
        var successes = responses.Where(response =>
            string.Equals(response.StatusCode, "2XX", StringComparison.OrdinalIgnoreCase) ||
            int.TryParse(response.StatusCode, out var code) && code is >= 200 and < 300).ToArray();
        var typed = successes.Where(response => response.Type is not null).ToArray();
        if (typed.Skip(1).Any(response => !IrTypeComparer.AreSemanticallyEqual(typed[0].Type, response.Type, ignorePresence: false)))
            throw new NotSupportedException("C# operations with different success body types require an explicit result union, which is not implemented.");
        return typed.FirstOrDefault() ?? successes.FirstOrDefault();
    }

    /// <summary>Wraps the selected operation result in the appropriate asynchronous C# return type.</summary>
    /// <param name="responses">The declared operation responses.</param>
    /// <returns>Task or Task of the selected body type.</returns>
    private TypeNode ExtractReturnType(IReadOnlyList<IrResponse> responses)
    {
        var successResponse = SelectSuccessResponse(responses);

        if (successResponse?.Type != null)
        {
            // Для async методов оборачиваем тип в Task<T>
            var responseType = BuildType(successResponse.Type);
            return new GenericTypeNode(
                name: new IdentifierNode("Task"),
                typeArguments: new[] { responseType }.ToImmutableList(),
                isNullable: false);
        }
        
        // Если нет успешного ответа, возвращаем Task (для async методов без возвращаемого значения)
        return new SimpleTypeNode(
            name: new IdentifierNode("Task"),
            isPrimitive: false,
            isNullable: false,
            isOptional: false);
    }
    
    /// <summary>
    /// Строит InterfaceDeclarationNode из IrClient
    /// </summary>
    private InterfaceDeclarationNode BuildClientInterface(IrClient client)
    {
        var interfaceName = new IdentifierNode($"I{client.Name}");
        // IrClient не имеет Description, используем null
        CommentNode? comment = null;
        
        // Методы интерфейса (без тела, с документацией)
        var interfaceMethods = client.Operations
            .Select(BuildInterfaceMethod)
            .ToList<DeclarationNode>();
        
        return new InterfaceDeclarationNode(
            name: interfaceName,
            accessModifier: AccessModifier.Public,
            baseTypes: ImmutableList<TypeNode>.Empty,
            members: interfaceMethods.ToImmutableList(),
            documentation: comment);
    }
    
    private ClassDeclarationNode BuildClientClass(IrClient client)
    {
        var name = new IdentifierNode(client.Name);
        var members = new List<DeclarationNode>();
        
        // Определяем базовые типы (интерфейсы)
        var baseTypes = new List<TypeNode>();
        if (_options.GenerateInterfaces)
        {
            var interfaceType = new SimpleTypeNode(new IdentifierNode($"I{client.Name}"), isPrimitive: false);
            baseTypes.Add(interfaceType);
        }
        
        // Добавляем поле _httpClient
        var httpClientField = new FieldDeclarationNode(
            name: new IdentifierNode("_httpClient"),
            type: new SimpleTypeNode(new IdentifierNode("HttpClient"), isPrimitive: false),
            accessModifier: AccessModifier.Private,
            isReadOnly: true,
            defaultValue: null);
        members.Add(httpClientField);
        
        // Добавляем конструктор
        var constructorParams = new List<ParameterDeclarationNode>
        {
            new ParameterDeclarationNode(
                name: new IdentifierNode("httpClient"),
                type: new SimpleTypeNode(new IdentifierNode("HttpClient"), isPrimitive: false),
                isRequired: true)
        };
        
        // _httpClient = httpClient;
        var assignmentExpr = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("_httpClient")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("httpClient")));
        
        var constructorBody = new BlockNode(new List<StatementNode>
        {
            new ExpressionStatementNode(assignmentExpr)
        }.ToImmutableList());
        
        var constructor = new ConstructorDeclarationNode(
            name: name,
            parameters: constructorParams.ToImmutableList(),
            body: constructorBody,
            accessModifier: AccessModifier.Public);
        members.Add(constructor);
        
        // Добавляем методы (без документации, будет inheritdoc)
        var methods = client.Operations
            .Select(op =>
            {
                var method = BuildMethod(op, includeBody: true, includeDocumentation: false);
                
                // Создаем новый метод с UseInheritDoc = true
                return new MethodDeclarationNode(
                    name: method.Name,
                    returnType: method.ReturnType,
                    parameters: method.Parameters,
                    body: method.Body,
                    isAsync: method.IsAsync,
                    accessModifier: method.AccessModifier,
                    documentation: method.Documentation,
                    attributes: method.Attributes,
                    metadata: method.Metadata,
                    isInterfaceMethod: method.IsInterfaceMethod,
                    useInheritDoc: true);
            })
            .ToList<DeclarationNode>();
        
        members.AddRange(methods);
        
        return new ClassDeclarationNode(
            name: name,
            accessModifier: _options.ClientAccessModifier,
            isSealed: _options.UseSealed,
            isStatic: false,
            isPartial: false,
            baseTypes: baseTypes.ToImmutableList(),
            members: members.ToImmutableList());
    }
    
    /// <summary>Builds the shared exception retaining HTTP status and raw response content.</summary>
    /// <param name="namespace">The namespace name, or null/empty for the global namespace.</param>
    /// <returns>The API exception compilation unit.</returns>
    public CompilationUnitNode BuildCompilationUnitApiException(string? @namespace = null)
    {
        var usings = BuildUsingDirectives(new[] { "System", "System.Net", "System.Net.Http" });
        var classNode = BuildApiExceptionClass();
        return CreateCompilationUnit(usings, [classNode], @namespace);
    }
    
    /// <summary>
    /// Строит ClassDeclarationNode для ApiException
    /// </summary>
    private ClassDeclarationNode BuildApiExceptionClass()
    {
        var name = new IdentifierNode("ApiException");
        var comment = new CommentNode("Исключение, выбрасываемое при ошибках HTTP запросов", CommentKind.Xml);
        
        var members = new List<DeclarationNode>();
        
        // Свойства (get; set; для присваивания в конструкторе)
        var requestUriProperty = new PropertyDeclarationNode(
            name: new IdentifierNode("RequestUri"),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("Uri"), isPrimitive: false)),
            isRequired: false,
            accessor: PropertyAccessorKind.GetSet,
            defaultValue: null,
            documentation: new CommentNode("URI запроса", CommentKind.Xml));
        members.Add(requestUriProperty);
        
        var httpMethodProperty = new PropertyDeclarationNode(
            name: new IdentifierNode("HttpMethod"),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("HttpMethod"), isPrimitive: false)),
            isRequired: false,
            accessor: PropertyAccessorKind.GetSet,
            defaultValue: null,
            documentation: new CommentNode("HTTP метод запроса", CommentKind.Xml));
        members.Add(httpMethodProperty);
        
        var statusCodeProperty = new PropertyDeclarationNode(
            name: new IdentifierNode("StatusCode"),
            type: new SimpleTypeNode(new IdentifierNode("HttpStatusCode"), isPrimitive: false),
            // Не "required": оба конструктора ниже присваивают StatusCode в теле, а не через
            // object-initializer/[SetsRequiredMembers] — с required-модификатором это CS9035.
            // HttpStatusCode — enum (value type), поэтому и CS8618 без "required" не грозит.
            isRequired: false,
            accessor: PropertyAccessorKind.GetSet,
            defaultValue: null,
            documentation: new CommentNode("HTTP статус код ответа", CommentKind.Xml));
        members.Add(statusCodeProperty);
        
        var reasonPhraseProperty = new PropertyDeclarationNode(
            name: new IdentifierNode("ReasonPhrase"),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true)),
            isRequired: false,
            accessor: PropertyAccessorKind.GetSet,
            defaultValue: null,
            documentation: new CommentNode("Фраза причины статус кода", CommentKind.Xml));
        members.Add(reasonPhraseProperty);
        
        var versionProperty = new PropertyDeclarationNode(
            name: new IdentifierNode("Version"),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("Version"), isPrimitive: false)),
            isRequired: false,
            accessor: PropertyAccessorKind.GetSet,
            defaultValue: null,
            documentation: new CommentNode("Версия HTTP протокола", CommentKind.Xml));
        members.Add(versionProperty);
        
        var contentProperty = new PropertyDeclarationNode(
            name: new IdentifierNode("Content"),
            type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true)),
            isRequired: false,
            accessor: PropertyAccessorKind.GetSet,
            defaultValue: null,
            documentation: new CommentNode("Содержимое ответа", CommentKind.Xml));
        members.Add(contentProperty);
        
        // Конструкторы
        // Конструктор с основными параметрами
        var constructor1Params = new List<ParameterDeclarationNode>
        {
            new ParameterDeclarationNode(
                name: new IdentifierNode("statusCode"),
                type: new SimpleTypeNode(new IdentifierNode("HttpStatusCode"), isPrimitive: false),
                isRequired: true),
            new ParameterDeclarationNode(
                name: new IdentifierNode("message"),
                type: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
                isRequired: false)
        };
        
        var constructor1Body = new BlockNode(new List<StatementNode>
        {
            new ExpressionStatementNode(new CallExpressionNode(
                target: new IdentifierExpressionNode(new IdentifierNode("base")),
                arguments: new ExpressionNode[] { new IdentifierExpressionNode(new IdentifierNode("message")) }.ToImmutableList(),
                isConstructorBaseCall: true)),
            new ExpressionStatementNode(new BinaryExpressionNode(
                left: new IdentifierExpressionNode(new IdentifierNode("StatusCode")),
                @operator: BinaryOperator.Assign,
                right: new IdentifierExpressionNode(new IdentifierNode("statusCode"))))
        }.ToImmutableList());
        
        var constructor1 = new ConstructorDeclarationNode(
            name: name,
            parameters: constructor1Params.ToImmutableList(),
            body: constructor1Body,
            accessModifier: AccessModifier.Public);
        members.Add(constructor1);
        
        // Конструктор с полным набором параметров
        var constructor2Params = new List<ParameterDeclarationNode>
        {
            new ParameterDeclarationNode(
                name: new IdentifierNode("requestUri"),
                type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("Uri"), isPrimitive: false)),
                isRequired: false),
            new ParameterDeclarationNode(
                name: new IdentifierNode("httpMethod"),
                type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("HttpMethod"), isPrimitive: false)),
                isRequired: false),
            new ParameterDeclarationNode(
                name: new IdentifierNode("statusCode"),
                type: new SimpleTypeNode(new IdentifierNode("HttpStatusCode"), isPrimitive: false),
                isRequired: true),
            new ParameterDeclarationNode(
                name: new IdentifierNode("reasonPhrase"),
                type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true)),
                isRequired: false),
            new ParameterDeclarationNode(
                name: new IdentifierNode("version"),
                type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("Version"), isPrimitive: false)),
                isRequired: false),
            new ParameterDeclarationNode(
                name: new IdentifierNode("content"),
                type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true)),
                isRequired: false),
            new ParameterDeclarationNode(
                name: new IdentifierNode("message"),
                type: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
                isRequired: false),
            new ParameterDeclarationNode(
                name: new IdentifierNode("innerException"),
                type: new NullableTypeNode(new SimpleTypeNode(new IdentifierNode("Exception"), isPrimitive: false)),
                isRequired: false)
        };
        
        var constructor2BodyStatements = new List<StatementNode>();
        constructor2BodyStatements.Add(new ExpressionStatementNode(new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode("base")),
            arguments: new ExpressionNode[] { 
                new IdentifierExpressionNode(new IdentifierNode("message")),
                new IdentifierExpressionNode(new IdentifierNode("innerException"))
            }.ToImmutableList(),
            isConstructorBaseCall: true)));
        constructor2BodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("RequestUri")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("requestUri")))));
        constructor2BodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("HttpMethod")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("httpMethod")))));
        constructor2BodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("StatusCode")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("statusCode")))));
        constructor2BodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("ReasonPhrase")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("reasonPhrase")))));
        constructor2BodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("Version")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("version")))));
        constructor2BodyStatements.Add(new ExpressionStatementNode(new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("Content")),
            @operator: BinaryOperator.Assign,
            right: new IdentifierExpressionNode(new IdentifierNode("content")))));
        
        var constructor2Body = new BlockNode(constructor2BodyStatements.ToImmutableList());
        
        var constructor2 = new ConstructorDeclarationNode(
            name: name,
            parameters: constructor2Params.ToImmutableList(),
            body: constructor2Body,
            accessModifier: AccessModifier.Public);
        members.Add(constructor2);
        
        var baseTypes = new[] { new SimpleTypeNode(new IdentifierNode("ApplicationException"), isPrimitive: false) }.ToImmutableList();
        
        return new ClassDeclarationNode(
            name: name,
            accessModifier: AccessModifier.Public,
            isSealed: false,
            isStatic: false,
            isPartial: false,
            baseTypes: baseTypes,
            members: members.ToImmutableList(),
            documentation: comment);
    }
    
    /// <summary>
    /// Создает вызов конструктора ApiException с полным набором параметров
    /// </summary>
    private CallExpressionNode BuildApiExceptionCall(
        ExpressionNode requestUri,
        ExpressionNode httpMethod,
        ExpressionNode statusCode,
        ExpressionNode reasonPhrase,
        ExpressionNode version,
        ExpressionNode content,
        ExpressionNode message,
        ExpressionNode? innerException)
    {
        var apiExceptionType = new IdentifierExpressionNode(new IdentifierNode("ApiException"));
        
        var arguments = new List<ExpressionNode>
        {
            requestUri,
            httpMethod,
            statusCode,
            reasonPhrase,
            version,
            content,
            message,
            innerException ?? new LiteralExpressionNode("null", LiteralKind.Null)
        };
        
        return new CallExpressionNode(
            target: apiExceptionType,
            arguments: arguments.ToImmutableList(),
            isNewExpression: true);
    }
    
    /// <summary>Finds namespaces required by HTTP support and every parameter or response type.</summary>
    /// <param name="client">The client whose operation types are traversed.</param>
    /// <returns>The distinct namespaces for the generated client.</returns>
    private IReadOnlyList<string> ExtractDependencies(IrClient client)
    {
        var dependencies = new HashSet<string>();
        
        // Добавляем только необходимые системные using для HTTP клиентов
        // Модели (IrModelReference) не добавляются, так как они генерируются в том же namespace
        dependencies.Add("System");
        dependencies.Add("System.Collections.Generic"); // List<string> queryParts (null-safe query-string builder)
        dependencies.Add("System.Net"); // HttpStatusCode
        dependencies.Add("System.Net.Http"); // HttpClient, HttpRequestMessage, HttpResponseMessage
        dependencies.Add("System.Net.Http.Json"); // JsonContent and charset-aware asynchronous JSON reads
        dependencies.Add("System.Net.Http.Headers"); // MediaTypeWithQualityHeaderValue
        dependencies.Add("System.Text"); // StringBuilder, Encoding
        dependencies.Add("System.Text.Json"); // JsonSerializer
        dependencies.Add("System.Threading"); // CancellationToken
        dependencies.Add("System.Threading.Tasks"); // Task, Task<T>

        foreach (var operation in client.Operations)
        {
            foreach (var parameter in operation.PathParameters.Concat(operation.QueryParameters).Concat(operation.HeaderParameters))
                ExtractDependenciesFromType(parameter.Type, dependencies);
            if (operation.RequestBody is { } body)
                ExtractDependenciesFromType(body.Type, dependencies);
            foreach (var response in operation.Responses)
            {
                if (response.Type is { } type)
                    ExtractDependenciesFromType(type, dependencies);
            }
        }

        return dependencies.Order(StringComparer.Ordinal).ToList();
    }
    
    /// <summary>Builds metrics overloads for callbacks with and without a response body.</summary>
    /// <returns>The asynchronous metrics interface.</returns>
    private InterfaceDeclarationNode BuildIHttpClientMetricsInterface()
    {
        var interfaceName = new IdentifierNode("IHttpClientMetrics");
        var members = new List<DeclarationNode>();
        
        // Метод Measure<T>
        members.Add(BuildMeasureMethodSignature(hasResult: true));
        members.Add(BuildMeasureMethodSignature(hasResult: false));
        
        return new InterfaceDeclarationNode(
            name: interfaceName,
            accessModifier: AccessModifier.Public,
            baseTypes: ImmutableList<TypeNode>.Empty,
            members: members.ToImmutableList());
    }
    
    /// <summary>Builds the metrics contract from the same declaration as its implementation.</summary>
    /// <param name="hasResult">Whether the measured task returns a typed value.</param>
    /// <returns>A bodyless interface signature.</returns>
    private MethodDeclarationNode BuildMeasureMethodSignature(bool hasResult)
    {
        var implementation = BuildMeasureMethod(hasResult);
        return new MethodDeclarationNode(implementation.Name, implementation.ReturnType,
            implementation.Parameters, body: null, accessModifier: AccessModifier.Public,
            isInterfaceMethod: true, genericTypeParameters: implementation.GenericTypeParameters);
    }

    /// <summary>
    /// Получает название метрики с префиксом ServiceName
    /// </summary>
    private string GetMetricName(string metricName)
    {
        var serviceName = _options.ServiceName;
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            return metricName;
        }
        return $"{serviceName}.{metricName}";
    }
    
    /// <summary>Builds success, error, and duration instruments with both callback shapes.</summary>
    /// <returns>The shared metrics implementation.</returns>
    private ClassDeclarationNode BuildHttpClientMetricsClass()
    {
        var className = new IdentifierNode("HttpClientMetrics");
        var members = new List<DeclarationNode>();
        
        // Поля для счетчиков
        var successCounterField = new FieldDeclarationNode(
            name: new IdentifierNode("_successCounter"),
            type: new GenericTypeNode(
                name: new IdentifierNode("Counter"),
                typeArguments: new[] { new SimpleTypeNode(new IdentifierNode("long"), isPrimitive: true) }.ToImmutableList(),
                isNullable: false),
            accessModifier: AccessModifier.Private,
            isReadOnly: true,
            defaultValue: null);
        members.Add(successCounterField);
        
        var errorCounterField = new FieldDeclarationNode(
            name: new IdentifierNode("_errorCounter"),
            type: new GenericTypeNode(
                name: new IdentifierNode("Counter"),
                typeArguments: new[] { new SimpleTypeNode(new IdentifierNode("long"), isPrimitive: true) }.ToImmutableList(),
                isNullable: false),
            accessModifier: AccessModifier.Private,
            isReadOnly: true,
            defaultValue: null);
        members.Add(errorCounterField);
        
        var durationHistogramField = new FieldDeclarationNode(
            name: new IdentifierNode("_durationHistogram"),
            type: new GenericTypeNode(
                name: new IdentifierNode("Histogram"),
                typeArguments: new[] { new SimpleTypeNode(new IdentifierNode("double"), isPrimitive: true) }.ToImmutableList(),
                isNullable: false),
            accessModifier: AccessModifier.Private,
            isReadOnly: true,
            defaultValue: null);
        members.Add(durationHistogramField);
        
        // Конструктор
        var constructorParams = new List<ParameterDeclarationNode>
        {
            new ParameterDeclarationNode(
                name: new IdentifierNode("meter"),
                type: new SimpleTypeNode(new IdentifierNode("Meter"), isPrimitive: false),
                isRequired: true)
        };
        
        var constructorBodyStatements = new List<StatementNode>();
        
        // _successCounter = meter.CreateCounter<long>("{serviceName}.requests.success", "requests", "Total number of successful HTTP requests");
        var successMetricName = GetMetricName("requests.success");
        var successCounterCreate = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("meter")),
                member: new IdentifierNode("CreateCounter")),
            arguments: new ExpressionNode[]
            {
                new LiteralExpressionNode(successMetricName, LiteralKind.String),
                new LiteralExpressionNode("requests", LiteralKind.String),
                new LiteralExpressionNode("Total number of successful HTTP requests", LiteralKind.String)
            }.ToImmutableList<ExpressionNode>(),
            genericTypeArgument: new SimpleTypeNode(new IdentifierNode("long"), isPrimitive: true));
        
        var successCounterAssignment = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("_successCounter")),
            @operator: BinaryOperator.Assign,
            right: successCounterCreate);
        constructorBodyStatements.Add(new ExpressionStatementNode(successCounterAssignment));
        
        // _errorCounter = meter.CreateCounter<long>("{serviceName}.requests.error", "requests", "Total number of failed HTTP requests");
        var errorMetricName = GetMetricName("requests.error");
        var errorCounterCreate = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("meter")),
                member: new IdentifierNode("CreateCounter")),
            arguments: new ExpressionNode[]
            {
                new LiteralExpressionNode(errorMetricName, LiteralKind.String),
                new LiteralExpressionNode("requests", LiteralKind.String),
                new LiteralExpressionNode("Total number of failed HTTP requests", LiteralKind.String)
            }.ToImmutableList<ExpressionNode>(),
            genericTypeArgument: new SimpleTypeNode(new IdentifierNode("long"), isPrimitive: true));
        
        var errorCounterAssignment = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("_errorCounter")),
            @operator: BinaryOperator.Assign,
            right: errorCounterCreate);
        constructorBodyStatements.Add(new ExpressionStatementNode(errorCounterAssignment));
        
        // _durationHistogram = meter.CreateHistogram<double>("{serviceName}.request.duration", "seconds", "Duration of HTTP requests");
        var durationMetricName = GetMetricName("request.duration");
        var durationHistogramCreate = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("meter")),
                member: new IdentifierNode("CreateHistogram")),
            arguments: new ExpressionNode[]
            {
                new LiteralExpressionNode(durationMetricName, LiteralKind.String),
                new LiteralExpressionNode("seconds", LiteralKind.String),
                new LiteralExpressionNode("Duration of HTTP requests", LiteralKind.String)
            }.ToImmutableList<ExpressionNode>(),
            genericTypeArgument: new SimpleTypeNode(new IdentifierNode("double"), isPrimitive: true));
        
        var durationHistogramAssignment = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(new IdentifierNode("_durationHistogram")),
            @operator: BinaryOperator.Assign,
            right: durationHistogramCreate);
        constructorBodyStatements.Add(new ExpressionStatementNode(durationHistogramAssignment));
        
        var constructorBody = new BlockNode(constructorBodyStatements.ToImmutableList());
        var constructor = new ConstructorDeclarationNode(
            name: className,
            parameters: constructorParams.ToImmutableList(),
            body: constructorBody,
            accessModifier: AccessModifier.Public);
        members.Add(constructor);
        
        // Метод Measure<T>
        members.Add(BuildMeasureMethod(hasResult: true));
        members.Add(BuildMeasureMethod(hasResult: false));
        
        // Базовые типы (интерфейс)
        var baseTypes = new List<TypeNode>
        {
            new SimpleTypeNode(new IdentifierNode("IHttpClientMetrics"), isPrimitive: false)
        };
        
        return new ClassDeclarationNode(
            name: className,
            accessModifier: AccessModifier.Internal,
            isSealed: true,
            isStatic: false,
            isPartial: false,
            baseTypes: baseTypes.ToImmutableList(),
            members: members.ToImmutableList());
    }
    
    /// <summary>Measures typed and bodyless tasks with identical success, error, and duration recording.</summary>
    /// <param name="hasResult">Whether the measured callback returns a typed result.</param>
    /// <returns>The asynchronous metrics implementation.</returns>
    private MethodDeclarationNode BuildMeasureMethod(bool hasResult)
    {
        var methodName = new IdentifierNode("Measure");
        TypeNode taskType = hasResult
            ? new GenericTypeNode(new IdentifierNode("Task"), new TypeNode[] { BuildSimpleType("T") })
            : BuildSimpleType("Task");
        
        // Параметры метода
        var parameters = new List<ParameterDeclarationNode>
        {
            new ParameterDeclarationNode(
                name: new IdentifierNode("clientName"),
                type: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
                isRequired: true),
            new ParameterDeclarationNode(
                name: new IdentifierNode("methodName"),
                type: new SimpleTypeNode(new IdentifierNode("string"), isPrimitive: true),
                isRequired: true),
            new ParameterDeclarationNode(
                name: new IdentifierNode("func"),
                type: new GenericTypeNode(
                    name: new IdentifierNode("Func"),
                    typeArguments: new[]
                    {
                        taskType
                    }.ToImmutableList(),
                    isNullable: false),
                isRequired: true)
        };
        
        // Тело метода
        var statements = new List<StatementNode>();
        
        // var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stopwatchVar = new IdentifierNode("stopwatch");
        var stopwatchType = new SimpleTypeNode(new IdentifierNode("System.Diagnostics.Stopwatch"), isPrimitive: false);
        var stopwatchStartNew = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("System.Diagnostics.Stopwatch")),
                member: new IdentifierNode("StartNew")),
            arguments: ImmutableList<ExpressionNode>.Empty);
        var stopwatchDeclaration = new VariableDeclarationStatementNode(
            name: stopwatchVar,
            type: stopwatchType,
            initializer: stopwatchStartNew);
        statements.Add(stopwatchDeclaration);
        
        // var isSuccess = true;
        var isSuccessVar = new IdentifierNode("isSuccess");
        var isSuccessDeclaration = new VariableDeclarationStatementNode(
            name: isSuccessVar,
            type: new SimpleTypeNode(new IdentifierNode("bool"), isPrimitive: true),
            initializer: new LiteralExpressionNode(true, LiteralKind.Boolean));
        statements.Add(isSuccessDeclaration);
        
        // try {
        var tryStatements = new List<StatementNode>();
        
        // var result = await func();
        var funcCall = new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode("func")),
            arguments: ImmutableList<ExpressionNode>.Empty);
        var awaitExpression = new AwaitExpressionNode(funcCall);
        var resultVar = new IdentifierNode("result");
        // Для generic параметра типа T используем SimpleTypeNode, а не GenericTypeNode
        var resultType = new SimpleTypeNode(
            name: new IdentifierNode("T"),
            isPrimitive: false);
        var resultDeclaration = new VariableDeclarationStatementNode(
            name: resultVar,
            type: resultType,
            initializer: awaitExpression);
        if (hasResult) tryStatements.Add(resultDeclaration);
        else tryStatements.Add(new ExpressionStatementNode(awaitExpression));
        
        // return result;
        if (hasResult) tryStatements.Add(new ReturnStatementNode(new IdentifierExpressionNode(resultVar)));
        
        var tryBlock = new BlockNode(tryStatements.ToImmutableList());
        
        // catch (Exception) {
        var catchStatements = new List<StatementNode>();
        
        // isSuccess = false;
        var isSuccessAssignment = new BinaryExpressionNode(
            left: new IdentifierExpressionNode(isSuccessVar),
            @operator: BinaryOperator.Assign,
            right: new LiteralExpressionNode(false, LiteralKind.Boolean));
        catchStatements.Add(new ExpressionStatementNode(isSuccessAssignment));
        
        // throw;
        catchStatements.Add(new ThrowStatementNode(null));
        
        var catchBlock = new BlockNode(catchStatements.ToImmutableList());
        var catchClause = new CatchClauseNode(
            body: catchBlock,
            exceptionType: new SimpleTypeNode(new IdentifierNode("Exception"), isPrimitive: false),
            exceptionVariable: null);
        
        // finally {
        var finallyStatements = new List<StatementNode>();
        
        // var duration = stopwatch.Elapsed.TotalSeconds;
        var durationVar = new IdentifierNode("duration");
        var durationType = new SimpleTypeNode(new IdentifierNode("double"), isPrimitive: true);
        var elapsedTotalSeconds = new MemberAccessExpressionNode(
            @object: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(stopwatchVar),
                member: new IdentifierNode("Elapsed")),
            member: new IdentifierNode("TotalSeconds"));
        var durationDeclaration = new VariableDeclarationStatementNode(
            name: durationVar,
            type: durationType,
            initializer: elapsedTotalSeconds);
        finallyStatements.Add(durationDeclaration);
        
        // Создаем теги для метрик
        var clientTag = new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode("KeyValuePair")),
            arguments: new ExpressionNode[]
            {
                new LiteralExpressionNode("client", LiteralKind.String),
                new IdentifierExpressionNode(new IdentifierNode("clientName"))
            }.ToImmutableList<ExpressionNode>(),
            isNewExpression: true,
            genericTypeName: "KeyValuePair<string, object?>");
        
        var methodTag = new CallExpressionNode(
            target: new IdentifierExpressionNode(new IdentifierNode("KeyValuePair")),
            arguments: new ExpressionNode[]
            {
                new LiteralExpressionNode("method", LiteralKind.String),
                new IdentifierExpressionNode(new IdentifierNode("methodName"))
            }.ToImmutableList<ExpressionNode>(),
            isNewExpression: true,
            genericTypeName: "KeyValuePair<string, object?>");
        
        // _durationHistogram.Record(duration, clientTag, methodTag);
        var recordCall = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("_durationHistogram")),
                member: new IdentifierNode("Record")),
            arguments: new ExpressionNode[]
            {
                new IdentifierExpressionNode(durationVar),
                clientTag,
                methodTag
            }.ToImmutableList<ExpressionNode>());
        finallyStatements.Add(new ExpressionStatementNode(recordCall));
        
        // Используем два отдельных счетчика вместо одного с тегами
        // if (isSuccess) { _successCounter.Add(1, clientTag, methodTag); } else { _errorCounter.Add(1, clientTag, methodTag); }
        var successCounterAddCall = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("_successCounter")),
                member: new IdentifierNode("Add")),
            arguments: new ExpressionNode[]
            {
                new LiteralExpressionNode(1L, LiteralKind.Number),
                clientTag,
                methodTag
            }.ToImmutableList<ExpressionNode>());
        
        var errorCounterAddCall = new CallExpressionNode(
            target: new MemberAccessExpressionNode(
                @object: new IdentifierExpressionNode(new IdentifierNode("_errorCounter")),
                member: new IdentifierNode("Add")),
            arguments: new ExpressionNode[]
            {
                new LiteralExpressionNode(1L, LiteralKind.Number),
                clientTag,
                methodTag
            }.ToImmutableList<ExpressionNode>());
        
        var ifSuccessBlock = new BlockNode(new[] { new ExpressionStatementNode(successCounterAddCall) }.ToImmutableList());
        var ifErrorBlock = new BlockNode(new[] { new ExpressionStatementNode(errorCounterAddCall) }.ToImmutableList());
        
        var ifSuccessStatement = new IfStatementNode(
            condition: new IdentifierExpressionNode(isSuccessVar),
            thenBlock: ifSuccessBlock,
            elseBlock: ifErrorBlock);
        finallyStatements.Add(ifSuccessStatement);
        
        var finallyBlock = new BlockNode(finallyStatements.ToImmutableList());
        
        // Создаем try-catch-finally
        var tryCatchStatement = new TryCatchStatementNode(
            tryBlock: tryBlock,
            catchClauses: new[] { catchClause }.ToImmutableList(),
            finallyBlock: finallyBlock);
        statements.Add(tryCatchStatement);
        
        var methodBody = new BlockNode(statements.ToImmutableList());
        
        return new MethodDeclarationNode(
            name: methodName,
            returnType: taskType,
            parameters: parameters.ToImmutableList(),
            body: methodBody,
            isAsync: true,
            accessModifier: AccessModifier.Public,
            genericTypeParameters: hasResult ? new[] { "T" } : null);
    }
    
    /// <summary>Extracts a typed task result, leaving bodyless Task without a generic argument.</summary>
    /// <param name="returnType">The generated asynchronous return type.</param>
    /// <returns>The task result type, or null for bodyless tasks.</returns>
    private TypeNode? ExtractReturnTypeGenericArgument(TypeNode? returnType)
    {
        if (returnType is GenericTypeNode genericType && genericType.Name.Name == "Task")
        {
            return genericType.TypeArguments.FirstOrDefault();
        }
        return null;
    }
}
