using ValIO.Core.Ast.Nodes;
using ValIO.Core.Ast.Nodes.Http;

namespace ValIO.Core.Ast.Visitors;

/// <summary>
/// Интерфейс посетителя AST (Visitor Pattern)
/// Объединяет все интерфейсы визиторов для поддержки всех типов узлов
/// Используется узлами AST в методе Accept для диспетчеризации
/// </summary>
/// <remarks>
/// Наследует все базовые методы из IBaseAstVisitor.
/// Добавляет только методы для диспетчеризации и общие специфичные узлы.
/// Языково-специфичные узлы (C#, TypeScript) должны быть в соответствующих интерфейсах (ICSharpAstVisitor, ITypeScriptAstVisitor).
/// </remarks>
public interface IAstVisitor<TResult> : IBaseAstVisitor<TResult>
{
    // Enum (общий для C# и TypeScript)
    TResult VisitEnumDeclaration(EnumDeclarationNode node);

    // Interface (общий для C# и TypeScript)
    TResult VisitInterfaceDeclaration(InterfaceDeclarationNode node);

    // Универсальные декларации (промоучены из C#-генератора; используются C# и C++ std-профилем)
    TResult VisitNamespaceDeclaration(NamespaceDeclarationNode node);
    TResult VisitClassDeclaration(ClassDeclarationNode node);
    TResult VisitStructDeclaration(StructDeclarationNode node);
    TResult VisitFieldDeclaration(FieldDeclarationNode node);

    // HTTP специфичные узлы
    TResult VisitHttpMethod(HttpMethodNode node);
    TResult VisitRoute(RouteNode node);
    TResult VisitPathParameter(PathParameterNode node);
    TResult VisitQueryParameter(QueryParameterNode node);
    TResult VisitHeaderParameter(HeaderParameterNode node);
    TResult VisitRequestBody(RequestBodyNode node);
    TResult VisitResponseType(ResponseTypeNode node);
}
