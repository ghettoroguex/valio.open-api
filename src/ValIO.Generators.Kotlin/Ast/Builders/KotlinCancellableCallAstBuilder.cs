using ValIO.Core.Ast.Enums;
using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Expressions;
using ValIO.Core.Ast.Nodes.Statements;
using ValIO.Core.Ast.Nodes.Types;
using ValIO.Generators.Kotlin.Ast.Nodes;

namespace ValIO.Generators.Kotlin.Ast.Builders;

/// <summary>Builds the structured bridge between an OkHttp Call and coroutine cancellation.</summary>
internal static class KotlinCancellableCallAstBuilder
{
    /// <summary>Gets the private adapter identifier shared by operation declarations.</summary>
    /// <value>The generated adapter method name.</value>
    internal const string MethodName = "awaitResponse";

    /// <summary>Builds a cancellable adapter that buffers response bytes before resuming its caller.</summary>
    /// <remarks>Keeping the continuation suspended during body reads preserves cancellation of the native call.</remarks>
    /// <returns>The structured private suspend function.</returns>
    internal static KotlinFunctionDeclarationNode Build()
    {
        var continuationType = Generic("CancellableContinuation", Type("Response"));
        var cancellation = FunctionObject([Type("Throwable", nullable: true)], ["cause"],
            new BlockNode([Statement(Call(Id("call"), "cancel"))]));
        var undelivered = FunctionObject([Type("Throwable"), Type("Response"), Type("CoroutineContext")],
            ["cause", "undelivered", "context"], new BlockNode([Statement(Call(Id("undelivered"), "close"))]));
        var failure = new KotlinFunctionDeclarationNode(new IdentifierNode("onFailure"),
            [Parameter("call", Type("Call")), Parameter("error", Type("IOException"))], Type("Unit"),
            structuredBody: new BlockNode([Statement(FailContinuation("error"))]), isOverride: true);

        var noBody = new BinaryExpressionNode(Id("sourceBody"), BinaryOperator.Equal, Null());
        var readBytes = new ConditionalExpressionNode(noBody, Call(Id("ByteArray"), null, Number(0)), Call(Id("sourceBody"), "bytes"));
        var contentType = new ConditionalExpressionNode(noBody, Null(), Call(Id("sourceBody"), "contentType"));
        var bufferedBody = Call(Id("bytes"), "toResponseBody", contentType);
        var bufferedResponse = Call(Call(Call(Id("response"), "newBuilder"), "body", bufferedBody), "build");
        var receive = new KotlinFunctionDeclarationNode(new IdentifierNode("onResponse"),
            [Parameter("call", Type("Call")), Parameter("response", Type("Response"))], Type("Unit"),
            structuredBody: new BlockNode([
                new TryCatchStatementNode(new BlockNode([
                    Variable("sourceBody", new MemberAccessExpressionNode(Id("response"), new IdentifierNode("body"))),
                    Variable("bytes", readBytes),
                    Variable("buffered", bufferedResponse),
                    Statement(Call(Id("response"), "close")),
                    Statement(Call(Id("continuation"), "resume", Id("buffered"), undelivered)),
                ]), [new CatchClauseNode(new BlockNode([
                    Statement(Call(Id("response"), "close")), Statement(FailContinuation("error")),
                ]), Type("Exception"), new IdentifierNode("error"))]),
            ]), isOverride: true);
        var callback = new KotlinObjectExpressionNode(Type("Callback"), [failure, receive]);
        var start = FunctionObject([continuationType], ["continuation"], new BlockNode([
            Variable("call", Call(Id("client"), "newCall", Id("request"))),
            Statement(Call(Id("continuation"), "invokeOnCancellation", cancellation)),
            new IfStatementNode(new UnaryExpressionNode(UnaryOperator.LogicalNot,
                new MemberAccessExpressionNode(Id("continuation"), new IdentifierNode("isActive"))),
                new BlockNode([new ReturnStatementNode()])),
            new TryCatchStatementNode(new BlockNode([Statement(Call(Id("call"), "enqueue", callback))]),
                [new CatchClauseNode(new BlockNode([Statement(FailContinuation("error"))]), Type("Exception"), new IdentifierNode("error"))]),
        ]));
        var suspend = new CallExpressionNode(Id("suspendCancellableCoroutine"), [start], genericTypeArgument: Type("Response"));
        return new KotlinFunctionDeclarationNode(new IdentifierNode(MethodName), [Parameter("request", Type("Request"))], Type("Response"),
            isSuspend: true, visibility: "private", structuredBody: new BlockNode([new ReturnStatementNode(suspend)]),
            documentation: new CommentNode("Suspends through response-body reading, cancels the native Call with its coroutine, and closes undelivered responses.", CommentKind.MultiLine));
    }

    /// <summary>Builds an anonymous Kotlin function implementation with a structured body.</summary>
    /// <param name="parameterTypes">The function parameter types.</param>
    /// <param name="parameterNames">The matching parameter identifiers.</param>
    /// <param name="body">The function body.</param>
    /// <returns>The anonymous FunctionN implementation.</returns>
    private static KotlinObjectExpressionNode FunctionObject(IReadOnlyList<TypeNode> parameterTypes,
        IReadOnlyList<string> parameterNames, BlockNode body) => new(
        Generic($"Function{parameterTypes.Count}", [.. parameterTypes, Type("Unit")]),
        [new KotlinFunctionDeclarationNode(new IdentifierNode("invoke"),
            parameterTypes.Select((type, index) => Parameter(parameterNames[index], type)).ToArray(), Type("Unit"),
            structuredBody: body, isOverride: true)]);

    /// <summary>Builds a failed continuation completion using the native exception.</summary>
    /// <param name="error">The exception parameter identifier.</param>
    /// <returns>The resumeWith failure expression.</returns>
    private static ExpressionNode FailContinuation(string error) => Call(Id("continuation"), "resumeWith", Call(Id("Result"), "failure", Id(error)));

    /// <summary>Builds a typed function parameter.</summary>
    /// <param name="name">The parameter identifier.</param>
    /// <param name="type">The parameter type.</param>
    /// <returns>The parameter AST.</returns>
    private static KotlinParameterNode Parameter(string name, TypeNode type) => new(new IdentifierNode(name), type);

    /// <summary>Builds an immutable local initialized by a structured expression.</summary>
    /// <param name="name">The local identifier.</param>
    /// <param name="value">The initializer.</param>
    /// <returns>The variable declaration.</returns>
    private static VariableDeclarationStatementNode Variable(string name, ExpressionNode value) => new(new IdentifierNode(name), initializer: value);

    /// <summary>Wraps a side-effecting expression as a statement.</summary>
    /// <param name="expression">The expression to execute.</param>
    /// <returns>The expression statement.</returns>
    private static ExpressionStatementNode Statement(ExpressionNode expression) => new(expression);

    /// <summary>Builds a function or member call without embedding target-language source text.</summary>
    /// <param name="target">The target expression.</param>
    /// <param name="member">The optional member identifier.</param>
    /// <param name="arguments">The call arguments.</param>
    /// <returns>The call expression.</returns>
    private static CallExpressionNode Call(ExpressionNode target, string? member, params ExpressionNode[] arguments) =>
        new(member == null ? target : new MemberAccessExpressionNode(target, new IdentifierNode(member)), arguments);

    /// <summary>Builds an identifier expression.</summary>
    /// <param name="name">The identifier.</param>
    /// <returns>The reference expression.</returns>
    private static IdentifierExpressionNode Id(string name) => new(new IdentifierNode(name));

    /// <summary>Builds a named Kotlin type.</summary>
    /// <param name="name">The type identifier.</param>
    /// <param name="nullable">Whether the type admits null.</param>
    /// <returns>The type AST.</returns>
    private static SimpleTypeNode Type(string name, bool nullable = false) => new(new IdentifierNode(name), isNullable: nullable);

    /// <summary>Builds a generic Kotlin type.</summary>
    /// <param name="name">The generic type identifier.</param>
    /// <param name="arguments">The ordered generic arguments.</param>
    /// <returns>The generic type AST.</returns>
    private static GenericTypeNode Generic(string name, params TypeNode[] arguments) => new(new IdentifierNode(name), arguments);

    /// <summary>Builds the null literal used at nullable transport boundaries.</summary>
    /// <returns>The null expression.</returns>
    private static LiteralExpressionNode Null() => new(null, LiteralKind.Null);

    /// <summary>Builds an integer literal for a native API argument.</summary>
    /// <param name="value">The integer value.</param>
    /// <returns>The numeric expression.</returns>
    private static LiteralExpressionNode Number(int value) => new(value, LiteralKind.Number);
}
