using System.Text;
using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Core.Interfaces;
using ValIO.Core.IR;
using ValIO.Core.IR.Types;
using ValIO.Generators.FSharp.Ast.Nodes;

namespace ValIO.Generators.FSharp.Ast.Builders;

/// <summary>
/// Builds an F# module of RestSharp-based functions (one let-binding per operation)
/// for one <see cref="IrClient"/>.
/// </summary>
public sealed class RestSharpClientAstBuilder
{
    /// <summary>Converts operation and parameter names into F# identifiers.</summary>
    private readonly INamingConverter _namingConverter;
    /// <summary>Maps operation payload types into F# values.</summary>
    private readonly FSharpIrTypeMapper _typeMapper;

    /// <summary>Initializes the operation builder with shared naming and type services.</summary>
    /// <param name="namingConverter">The operation identifier converter.</param>
    /// <param name="typeMapper">The F# type mapper.</param>
    public RestSharpClientAstBuilder(INamingConverter namingConverter, FSharpIrTypeMapper typeMapper)
    {
        _namingConverter = namingConverter;
        _typeMapper = typeMapper;
    }

    /// <summary>Builds asynchronous functions using the caller-owned RestClient pipeline.</summary>
    /// <param name="client">The operations grouped into the generated API module.</param>
    /// <param name="moduleName">The fully qualified output module.</param>
    /// <returns>A client module with explicit model imports and cancellation-aware operations.</returns>
    public FSharpFileNode BuildFile(IrClient client, string moduleName)
    {
        var functions = new List<DeclarationNode>(client.Operations.Count);
        foreach (var op in client.Operations)
        {
            functions.Add(BuildFunction(op));
        }

        var apiNamespaceEnd = moduleName.LastIndexOf(".Api.", StringComparison.Ordinal);
        var modelsNamespace = apiNamespaceEnd < 0 ? "Generated.Models" : moduleName[..apiNamespaceEnd] + ".Models";
        var types = client.Operations.SelectMany(operation => operation.PathParameters
                .Concat(operation.QueryParameters).Concat(operation.HeaderParameters).Select(parameter => parameter.Type)
                .Concat(operation.Responses.Where(response => response.Type != null).Select(response => response.Type!))
                .Concat(operation.RequestBody is { } request ? [request.Type] : []));
        var opens = FSharpModelImports.Collect(types, modelsNamespace, moduleName)
            .Concat(["RestSharp", "System", "System.Threading", "System.Threading.Tasks"]).ToArray();
        return new FSharpFileNode(
            moduleName: moduleName,
            declarations: functions,
            opens: opens);
    }

    /// <summary>Builds a task-returning operation with a cancellation token for the actual HTTP request.</summary>
    /// <param name="op">The operation to generate.</param>
    /// <returns>The F# function declaration.</returns>
    private FSharpFunctionNode BuildFunction(IrOperation op)
    {
        var methodName = FSharpReservedNames.Avoid(_namingConverter.ToCamelCase(op.MethodName));
        var parameters = new List<FSharpFunctionNode.Parameter>
        {
            // First parameter is always the RestSharp client instance.
            new("client", new SimpleTypeNode(new IdentifierNode("RestClient"))),
        };

        foreach (var p in op.PathParameters)
        {
            parameters.Add(new FSharpFunctionNode.Parameter(
                FSharpReservedNames.Avoid(_namingConverter.ToCamelCase(p.Name)),
                _typeMapper.MapType(p.Type)));
        }

        if (op.RequestBody != null)
        {
            parameters.Add(new FSharpFunctionNode.Parameter(
                "body",
                _typeMapper.MapType(op.RequestBody.Type)));
        }

        foreach (var p in op.QueryParameters.Concat(op.HeaderParameters))
        {
            var mapped = _typeMapper.MapType(p.Type);
            if (!p.IsRequired && mapped is not NullableTypeNode)
                mapped = new NullableTypeNode(mapped);
            parameters.Add(new FSharpFunctionNode.Parameter(
                FSharpReservedNames.Avoid(_namingConverter.ToCamelCase(p.Name)),
                mapped));
        }

        parameters.Add(new FSharpFunctionNode.Parameter("cancellationToken", new SimpleTypeNode(new IdentifierNode("CancellationToken"))));
        var successResponse = op.Responses.FirstOrDefault(response =>
            (int.TryParse(response.StatusCode, out var code) && code is >= 200 and < 300 || response.StatusCode == "2XX") && response.Type != null);
        var payload = successResponse?.Type == null
            ? new SimpleTypeNode(new IdentifierNode("unit"))
            : _typeMapper.MapType(successResponse.Type);
        TypeNode returnType = new GenericTypeNode(new IdentifierNode("Task"), [payload]);

        var bodyLines = BuildFunctionBody(op, successResponse?.Type);

        return new FSharpFunctionNode(
            new IdentifierNode(methodName),
            parameters,
            returnType,
            bodyLines,
            documentation: BuildDocstring(op));
    }

    /// <summary>Builds request construction and typed asynchronous response handling.</summary>
    /// <param name="op">The operation whose parameters retain their original wire names.</param>
    /// <param name="responseType">The selected success payload, or null for bodyless operations.</param>
    /// <returns>The function body lines rendered by the F# AST renderer.</returns>
    private IReadOnlyList<string> BuildFunctionBody(IrOperation op, IrType? responseType)
    {
        var lines = new List<string> { "task {", "    cancellationToken.ThrowIfCancellationRequested()" };
        var httpMethod = ToMethodCase(op.HttpMethod);
        lines.Add($"    let req = RestRequest(\"{EscapeStringLiteral(op.Path)}\", Method.{httpMethod})");
        foreach (var parameter in op.PathParameters)
        {
            var camel = FSharpReservedNames.Avoid(_namingConverter.ToCamelCase(parameter.Name));
            lines.Add($"    req.AddUrlSegment(\"{EscapeStringLiteral(parameter.OriginalName)}\", string {camel}) |> ignore");
        }

        foreach (var p in op.QueryParameters)
        {
            var camel = FSharpReservedNames.Avoid(_namingConverter.ToCamelCase(p.Name));
            var wireName = EscapeStringLiteral(p.OriginalName);
            if (p.IsRequired)
            {
                lines.Add($"    req.AddQueryParameter(\"{wireName}\", string {camel}) |> ignore");
            }
            else
            {
                lines.Add($"    match {camel} with");
                lines.Add($"    | Some v -> req.AddQueryParameter(\"{wireName}\", string v) |> ignore");
                lines.Add("    | None -> ()");
            }
        }

        foreach (var parameter in op.HeaderParameters)
        {
            var camel = FSharpReservedNames.Avoid(_namingConverter.ToCamelCase(parameter.Name));
            var wireName = EscapeStringLiteral(parameter.OriginalName);
            if (parameter.IsRequired)
                lines.Add($"    req.AddHeader(\"{wireName}\", string {camel}) |> ignore");
            else
            {
                lines.Add($"    match {camel} with");
                lines.Add($"    | Some v -> req.AddHeader(\"{wireName}\", string v) |> ignore");
                lines.Add("    | None -> ()");
            }
        }

        if (op.RequestBody != null)
        {
            lines.Add("    req.AddJsonBody(body) |> ignore");
        }

        if (responseType == null)
            lines.Add("    let! response = client.ExecuteAsync(req, cancellationToken)");
        else
        {
            var renderedType = new Renderers.FSharpAstRenderer().Visit(_typeMapper.MapType(responseType));
            lines.Add($"    let! response = client.ExecuteAsync<{renderedType}>(req, cancellationToken)");
        }
        lines.Add("    cancellationToken.ThrowIfCancellationRequested()");
        lines.Add("    if not response.IsSuccessful then");
        lines.Add("        raise (System.Net.Http.HttpRequestException($\"HTTP {int response.StatusCode}: {response.Content}\", response.ErrorException, Nullable(response.StatusCode)))");
        if (responseType == null)
            lines.Add("    return ()");
        else
        {
            lines.Add("    if isNull (box response.Data) then");
            lines.Add("        raise (System.IO.InvalidDataException(\"The successful HTTP response did not contain a valid payload.\"))");
            lines.Add("    return response.Data");
        }
        lines.Add("}");
        return lines;
    }

    /// <summary>Maps an HTTP verb to the corresponding RestSharp enumeration member.</summary>
    /// <param name="httpMethod">The source HTTP verb.</param>
    /// <returns>The RestSharp method member name.</returns>
    private static string ToMethodCase(string httpMethod) =>
        string.IsNullOrEmpty(httpMethod)
            ? "Get"
            : char.ToUpperInvariant(httpMethod[0]) + httpMethod.Substring(1).ToLowerInvariant();

    /// <summary>Preserves operation documentation and describes the injected transport boundary.</summary>
    /// <param name="op">The source operation.</param>
    /// <returns>The generated F# XML documentation.</returns>
    private static CommentNode? BuildDocstring(IrOperation op)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(op.Summary))
            sb.Append(op.Summary!.Trim());
        if (!string.IsNullOrWhiteSpace(op.Description))
        {
            if (sb.Length > 0) sb.AppendLine().AppendLine();
            sb.Append(op.Description!.Trim());
        }
        if (sb.Length == 0)
            sb.Append($"{op.HttpMethod} {op.Path}");
        sb.AppendLine().Append("Uses the caller-owned RestClient and its interceptors/authenticator. The cancellation token reaches the HTTP transport.");
        return new CommentNode(sb.ToString(), CommentKind.MultiLine);
    }

    /// <summary>Escapes source values embedded in a generated F# string literal.</summary>
    /// <param name="s">The unescaped wire value.</param>
    /// <returns>The literal contents without surrounding quotes.</returns>
    private static string EscapeStringLiteral(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal)
         .Replace("\"", "\\\"", StringComparison.Ordinal);
}
