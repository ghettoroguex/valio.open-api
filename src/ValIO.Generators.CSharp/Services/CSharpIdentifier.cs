using System.Collections.Frozen;

namespace ValIO.Generators.CSharp.Services;

/// <summary>
/// Приведение имён из схемы к идентификаторам, допустимым в C#.
/// </summary>
/// <remarks>
/// Имя свойства в OpenAPI ничем не ограничено, и схемы регулярно приносят слова, которые в C#
/// зарезервированы (<c>class</c>, <c>object</c>, <c>event</c>). Такое имя нельзя ни объявить, ни
/// прочитать без префикса <c>@</c> — без экранирования сгенерированный файл не компилируется.
/// Контекстные ключевые слова (<c>value</c>, <c>record</c>, <c>async</c>) идентификаторами быть
/// могут, поэтому в список не входят.
/// </remarks>
public static class CSharpIdentifier
{
    private static readonly FrozenSet<string> ReservedKeywords = new[]
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Возвращает имя, пригодное для подстановки в код: зарезервированное слово получает префикс
    /// <c>@</c>, остальные имена возвращаются как есть.
    /// </summary>
    /// <param name="name">Имя из схемы или промежуточного представления.</param>
    /// <returns>Идентификатор, допустимый в C#.</returns>
    public static string Escape(string name)
        => ReservedKeywords.Contains(name) ? "@" + name : name;
}
