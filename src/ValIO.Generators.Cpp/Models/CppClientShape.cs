namespace ValIO.Generators.Cpp.Models;

/// <summary>
/// Форма сгенерированного клиента std-профиля.
/// </summary>
public enum CppClientShape
{
    /// <summary>
    /// Один класс {Client}Client.h/.cpp без отдельного интерфейса.
    /// </summary>
    Class,

    /// <summary>
    /// Интерфейс I{Client}.h (чистые виртуальные методы, только модели + Expected/Error —
    /// без runtime-pimpl; удобен для моков и DI) + реализация {Client}Client : public I{Client}.
    /// </summary>
    InterfaceImpl,
}
