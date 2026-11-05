using System.Collections.Immutable;
using ValIO.Core.Ast.Builders;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Http;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Generators.TypeScript.Ast.Builders;
using ValIO.Generators.TypeScript.Ast.Nodes;
using ValIO.Generators.TypeScript.Ast.Nodes.Expressions;
using ValIO.Generators.TypeScript.Axios.Ast.Nodes;
using ValIO.Generators.TypeScript.Axios.Models;

namespace ValIO.Generators.TypeScript.Axios.Ast.Builders;

/// <summary>
/// Построитель AST для Axios специфичных узлов
/// Использует IR для построения AST узлов
/// </summary>
public class AxiosAstBuilder
{
    private readonly HttpAstBuilder _httpBuilder;
    private readonly INamingConverter _namingConverter;
    private readonly Func<ValIO.Core.IR.Types.IrType, TypeNode> _typeBuilder;
    
    /// <summary>
    /// Конструктор
    /// </summary>
    /// <param name="namingConverter">Конвертер имен</param>
    /// <param name="typeBuilder">Функция для построения TypeNode из IrType</param>
    public AxiosAstBuilder(
        INamingConverter namingConverter,
        Func<ValIO.Core.IR.Types.IrType, TypeNode> typeBuilder)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
        _httpBuilder = new HttpAstBuilder();
    }
    
    /// <summary>
    /// Строит AxiosCallExpressionNode из IrOperation
    /// </summary>
    /// <param name="operation">IR операция</param>
    /// <param name="axiosInstance">Axios instance (опционально, по умолчанию "axios")</param>
    /// <returns>AST узел вызова Axios</returns>
    public AxiosCallExpressionNode BuildAxiosCall(
        IrOperation operation,
        ExpressionNode? axiosInstance = null)
    {
        var instance = axiosInstance ?? new IdentifierExpressionNode(new IdentifierNode("axios"));
        var config = BuildAxiosConfig(operation);
        
        return new AxiosCallExpressionNode(instance, config);
    }

    /// <summary>
    /// Строит AxiosCallExpressionNode с использованием params interface
    /// </summary>
    public AxiosCallExpressionNode BuildAxiosCallWithParams(
        IrOperation operation,
        AxiosConfigParamsSource paramsSource,
        ExpressionNode? axiosInstance = null)
    {
        var instance = axiosInstance ?? new IdentifierExpressionNode(new IdentifierNode("axios"));
        var config = BuildAxiosConfigWithParams(operation, paramsSource);
        return new AxiosCallExpressionNode(instance, config);
    }
    
    /// <summary>
    /// Builds an Axios configuration using the supplied parameter sources.
    /// </summary>
    /// <param name="operation">The operation being generated.</param>
    /// <param name="paramsSource">Expressions supplying operation parameters.</param>
    /// <returns>The generated Axios configuration.</returns>
    public AxiosConfigNode BuildAxiosConfigWithParams(
        IrOperation operation,
        AxiosConfigParamsSource paramsSource)
    {
        var method = _httpBuilder.BuildHttpMethod(operation.HttpMethod);
        var url = BuildUrlWithSource(operation, paramsSource.RouteParamsSource);

        var queryParameters = operation.QueryParameters
            .Select(p => _httpBuilder.BuildQueryParameter(p, _typeBuilder))
            .ToImmutableList();

        var headers = operation.HeaderParameters
            .Select(p => _httpBuilder.BuildHeaderParameter(p, _typeBuilder))
            .ToImmutableList();

        var body = operation.RequestBody != null
            ? _httpBuilder.BuildRequestBody(operation.RequestBody, _typeBuilder)
            : null;

        var properties = new List<PropertyAssignmentNode>();

        properties.Add(new PropertyAssignmentNode(
            new IdentifierNode("method"),
            new LiteralExpressionNode(method.Method.ToString().ToUpperInvariant(), LiteralKind.String)));

        properties.Add(new PropertyAssignmentNode(
            new IdentifierNode("url"),
            url));

        if (paramsSource.QueryParamsSource != null && operation.QueryParameters.Count > 0)
        {
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("params"),
                TypeScriptQueryString.BuildWireQueryObject(operation, paramsSource.QueryParamsSource)));
        }

        if (paramsSource.HeadersSource != null && operation.HeaderParameters.Count > 0)
        {
            // Axios.headers ожидает Record<string, AxiosHeaderValue> | AxiosHeaders.
            // Узкоспециализированный *HeaderParams интерфейс не имеет index‑signature,
            // поэтому приводим через `as AxiosRequestConfig["headers"]`.
            var headersAsAxiosType = new SimpleTypeNode(
                new IdentifierNode("AxiosRequestConfig[\"headers\"]"),
                isPrimitive: false);
            var headersCast = new CastExpressionNode(
                headersAsAxiosType,
                paramsSource.HeadersSource,
                useAsOperator: true);
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("headers"),
                headersCast));
        }

        if (paramsSource.BodySource != null && operation.RequestBody != null)
        {
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("data"),
                paramsSource.BodySource));
        }

        return new AxiosConfigNode(
            method: method,
            url: url,
            queryParameters: queryParameters,
            headers: headers,
            body: body,
            properties: properties.ToImmutableList());
    }

    /// <summary>
    /// Строит AxiosConfigNode из IrOperation
    /// </summary>
    /// <param name="operation">IR операция</param>
    /// <returns>AST узел конфигурации Axios</returns>
    public AxiosConfigNode BuildAxiosConfig(IrOperation operation)
    {
        var method = _httpBuilder.BuildHttpMethod(operation.HttpMethod);
        var url = BuildUrl(operation);
        
        var queryParameters = operation.QueryParameters
            .Select(p => _httpBuilder.BuildQueryParameter(p, _typeBuilder))
            .ToImmutableList();
        
        var headers = operation.HeaderParameters
            .Select(p => _httpBuilder.BuildHeaderParameter(p, _typeBuilder))
            .ToImmutableList();
        
        var body = operation.RequestBody != null
            ? _httpBuilder.BuildRequestBody(operation.RequestBody, _typeBuilder)
            : null;
        
        // Строим properties для AxiosConfigNode
        var properties = new List<PropertyAssignmentNode>();
        
        // method
        properties.Add(new PropertyAssignmentNode(
            new IdentifierNode("method"),
            new LiteralExpressionNode(method.Method.ToString().ToUpperInvariant(), LiteralKind.String)));
        
        // url
        properties.Add(new PropertyAssignmentNode(
            new IdentifierNode("url"),
            url));
        
        // params (query parameters)
        if (queryParameters.Count > 0)
        {
            var paramsObject = BuildQueryParamsObject(queryParameters);
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("params"),
                paramsObject));
        }
        
        // headers
        if (headers.Count > 0)
        {
            var headersObject = BuildHeadersObject(headers);
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("headers"),
                headersObject));
        }
        
        // data (body)
        if (body != null && operation.RequestBody != null)
        {
            var bodyParam = new IdentifierExpressionNode(new IdentifierNode("request"));
            properties.Add(new PropertyAssignmentNode(
                new IdentifierNode("data"),
                bodyParam));
        }
        
        return new AxiosConfigNode(
            method: method,
            url: url,
            queryParameters: queryParameters,
            headers: headers,
            body: body,
            properties: properties.ToImmutableList());
    }
    
    /// <summary>
    /// Строит URL выражение с использованием routeParamsSource
    /// </summary>
    /// <summary>
    /// Строит выражение URL (литерал или template) с подстановкой route-параметров.
    /// </summary>
    public ExpressionNode BuildUrlWithSource(IrOperation operation, ExpressionNode? routeParamsSource)
        // Path templating (placeholder → route access) is shared with every other transport; see
        // TypeScriptRequestPath.BuildUrl(operation, routeSource). Passing a rebound source (e.g. a validated
        // parsedRoute) is supported there, matching the Axios/fetch validation flow.
        => TypeScriptRequestPath.BuildUrl(operation, routeParamsSource);

    /// <summary>
    /// Строит URL выражение из IrOperation (может быть template literal)
    /// </summary>
    /// <param name="operation">IR операция</param>
    /// <returns>AST узел выражения URL</returns>
    private ExpressionNode BuildUrl(IrOperation operation)
    {
        var path = operation.Path;
        var pathParams = operation.PathParameters;
        
        if (pathParams.Count == 0)
        {
            // Простой строковый литерал
            return new LiteralExpressionNode(path, LiteralKind.String);
        }
        
        // Template literal с подстановкой параметров
        var parts = new List<TemplateLiteralPart>();
        var currentPath = path;
        
        foreach (var param in pathParams)
        {
            // Path parameters в OpenAPI имеют формат {paramName}, в TypeScript template literal - ${variableName}
            var paramPlaceholder = $"{{{param.Name}}}";
            // Спецификация может задавать casing плейсхолдера в пути (`{dictionaryId}`)
            // иначе, чем имя параметра (`DictionaryId`); сопоставляем без учёта регистра.
            var index = currentPath.IndexOf(paramPlaceholder, StringComparison.OrdinalIgnoreCase);
            
            if (index >= 0)
            {
                // Добавляем текст до параметра
                if (index > 0)
                {
                    parts.Add(new TemplateLiteralPart(currentPath.Substring(0, index)));
                }
                
                // Добавляем выражение параметра
                parts.Add(new TemplateLiteralPart(
                    new IdentifierExpressionNode(new IdentifierNode(param.Name))));
                
                // Обновляем путь
                currentPath = currentPath.Substring(index + paramPlaceholder.Length);
            }
        }
        
        // Добавляем оставшийся текст
        if (currentPath.Length > 0)
        {
            parts.Add(new TemplateLiteralPart(currentPath));
        }
        
        // Если не было найдено ни одного параметра, возвращаем простой литерал
        if (parts.Count == 0)
        {
            return new LiteralExpressionNode(path, LiteralKind.String);
        }
        
        return new TemplateLiteralExpressionNode(parts.ToImmutableList());
    }
    
    /// <summary>
    /// Строит объект для query parameters
    /// </summary>
    /// <param name="queryParameters">Список query параметров</param>
    /// <returns>AST узел объектного литерала</returns>
    private ObjectLiteralExpressionNode BuildQueryParamsObject(IReadOnlyList<QueryParameterNode> queryParameters)
    {
        var properties = queryParameters.Select(qp =>
            new PropertyAssignmentNode(
                qp.Name,
                new IdentifierExpressionNode(qp.Name))).ToImmutableList();
        
        return new ObjectLiteralExpressionNode(properties);
    }
    
    /// <summary>
    /// Строит объект для headers
    /// </summary>
    /// <param name="headers">Список header параметров</param>
    /// <returns>AST узел объектного литерала</returns>
    private ObjectLiteralExpressionNode BuildHeadersObject(IReadOnlyList<HeaderParameterNode> headers)
    {
        var properties = headers.Select(h =>
            new PropertyAssignmentNode(
                h.Name,
                new IdentifierExpressionNode(h.Name))).ToImmutableList();
        
        return new ObjectLiteralExpressionNode(properties);
    }
}
