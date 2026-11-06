using System.Text;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Utilities;
using ValIO.Generators.TypeScript.TanStackQuery.Ast.Builders;
using ValIO.Generators.TypeScript.TanStackQuery.Ast.Nodes;
using ValIO.Generators.TypeScript.TanStackQuery.Ast.Renderers;

namespace ValIO.Generators.TypeScript.TanStackQuery;

/// <summary>
/// Рендерер TanStack Query хуков для TypeScript через IR и AST
/// </summary>
public class TanStackQueryRenderer
{
    private readonly INamingConverter _namingConverter;
    private readonly Func<ValIO.Core.IR.Types.IrType, ValIO.Core.Ast.Nodes.TypeNode> _typeBuilder;
    
    /// <summary>
    /// Конструктор
    /// </summary>
    /// <param name="namingConverter">Конвертер имен</param>
    /// <param name="typeBuilder">Функция для построения TypeNode из IrType</param>
    public TanStackQueryRenderer(
        INamingConverter namingConverter,
        Func<ValIO.Core.IR.Types.IrType, ValIO.Core.Ast.Nodes.TypeNode> typeBuilder)
    {
        _namingConverter = namingConverter ?? throw new ArgumentNullException(nameof(namingConverter));
        _typeBuilder = typeBuilder ?? throw new ArgumentNullException(nameof(typeBuilder));
    }
    
    /// <summary>
    /// Рендерит TanStack Query hook для IR операции
    /// </summary>
    /// <param name="operation">IR операция</param>
    /// <param name="clientFunction">Выражение вызова функции клиента</param>
    /// <param name="isMutation">Является ли операция мутацией</param>
    /// <returns>TypeScript код TanStack Query hook</returns>
    public string RenderHook(
        IrOperation operation,
        ValIO.Core.Ast.Nodes.ExpressionNode clientFunction,
        bool isMutation = false)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        if (clientFunction == null)
            throw new ArgumentNullException(nameof(clientFunction));
        
        var sb = new StringBuilder();
        
        // Строим AST для TanStack Query
        var queryBuilder = new TanStackQueryAstBuilder(_namingConverter);
        
        if (isMutation)
        {
            var useMutation = queryBuilder.BuildUseMutation(operation, clientFunction);
            var renderer = new TanStackQueryAstRenderer();
            var code = renderer.Render(useMutation);
            sb.Append(code);
        }
        else
        {
            var useQuery = queryBuilder.BuildUseQuery(operation, clientFunction);
            var renderer = new TanStackQueryAstRenderer();
            var code = renderer.Render(useQuery);
            sb.Append(code);
        }
        
        return sb.ToString();
    }
    
    /// <summary>
    /// Формирует функцию ключа запроса для операции.
    /// </summary>
    /// <param name="operation">Исходная операция.</param>
    /// <returns>Сгенерированная функция TypeScript.</returns>
    /// <exception cref="ArgumentNullException">Операция равна null.</exception>
    public string RenderQueryKeyFunction(IrOperation operation)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));
        
        var sb = new StringBuilder();
        
        var queryBuilder = new TanStackQueryAstBuilder(_namingConverter);
        var queryKey = queryBuilder.BuildQueryKey(operation);
        
        var functionName = $"get{_namingConverter.ToPascalCase(operation.MethodName)}QueryKey";
        
        CodeFormatter.AppendLine(sb, 0, $"export function {functionName}(");
        
        // Параметры функции
        var parameters = new List<string>();
        foreach (var param in operation.PathParameters.Concat(operation.QueryParameters))
        {
            var paramType = _typeBuilder(param.Type);
            var paramTypeStr = RenderType(paramType);
            parameters.Add($"{param.Name}: {paramTypeStr}");
        }
        
        if (parameters.Count > 0)
        {
            CodeFormatter.AppendLine(sb, 1, string.Join("," + Environment.NewLine, parameters.Select(p => $"{p}")));
        }
        
        CodeFormatter.AppendLine(sb, 0, ") {");
        
        // Тело функции
        var renderer = new TanStackQueryAstRenderer();
        var queryKeyCode = renderer.VisitQueryKey(queryKey);
        CodeFormatter.AppendLine(sb, 1, $"return {queryKeyCode};");
        CodeFormatter.AppendLine(sb, 0, "}");
        
        return sb.ToString();
    }
    
    private string RenderType(ValIO.Core.Ast.Nodes.TypeNode typeNode)
    {
        // Упрощенная реализация - в реальности нужно использовать TypeScriptAstRenderer
        return typeNode.ToString() ?? "any";
    }
}
