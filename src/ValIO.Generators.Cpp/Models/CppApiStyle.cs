namespace ValIO.Generators.Cpp.Models;

/// <summary>
/// Стиль сгенерированного API std-профиля: синхронные методы
/// (ValIO::Expected), асинхронные обёртки (std::future) или оба.
/// </summary>
public enum CppApiStyle
{
    /// <summary>Только синхронные методы: ValIO::Expected&lt;T, ValIO::Error&gt;.</summary>
    Sync,

    /// <summary>Только асинхронные методы: std::future&lt;ValIO::Expected&lt;T, ValIO::Error&gt;&gt;.</summary>
    Async,

    /// <summary>Синхронные методы + &lt;Method&gt;Async-обёртки поверх них.</summary>
    Both,
}
