namespace ValIO.Generators.Cpp.Services;

/// <summary>
/// Сервис именования std-профиля C++: PascalCase без UE-префиксов (F/U/E),
/// защита зарезервированных слов C++, пути вывода по ORIGINAL именам
/// (аффикс-стабильность — тот же контракт, что у UE-профиля).
/// </summary>
public sealed class StdNamingConvention
{
    /// <summary>
    /// Токены, которые могут ВЫЖИТЬ после ToPascalCase в исходном регистре и
    /// коллизировать с макросами потребительских TU (windows.h и др.).
    /// Ключевые слова C++ сюда не нужны: они все lowercase, а PascalCase-выход
    /// всегда начинается с заглавной — коллизия невозможна.
    /// Идентификатор получает суффикс «_».
    /// </summary>
    private static readonly HashSet<string> ReservedWords = new(StringComparer.Ordinal)
    {
        "NULL", "TRUE", "FALSE",
    };

    /// <summary>Имя модели/структуры: PascalCase без префиксов.</summary>
    public string StructName(string modelName) => Escape(UENamingConvention.ToPascalCase(modelName));

    /// <summary>Имя enum'а: PascalCase без префиксов.</summary>
    public string EnumName(string name) => Escape(UENamingConvention.ToPascalCase(name));

    /// <summary>Имя поля/свойства: PascalCase + защита зарезервированных слов.</summary>
    public string PropertyName(string name) => Escape(UENamingConvention.ToPascalCase(name));

    /// <summary>Имя метода клиента: PascalCase + защита.</summary>
    public string MethodName(string operationName) => Escape(UENamingConvention.ToPascalCase(operationName));

    /// <summary>Имя enum-значения: PascalCase + защита.</summary>
    public string EnumMemberName(string valueName) => Escape(UENamingConvention.ToPascalCase(valueName));

    /// <summary>Имя класса клиента: {Client}Client.</summary>
    public string ClientClassName(string clientName) => $"{Escape(UENamingConvention.ToPascalCase(clientName))}Client";

    /// <summary>Имя интерфейса клиента (clientShape=interface+impl): I{Client}.</summary>
    public string InterfaceName(string clientName) => $"I{Escape(UENamingConvention.ToPascalCase(clientName))}";

    /// <summary>Санитизация корневого namespace: валидный идентификатор, не зарезервирован.</summary>
    public string Namespace(string ns) => Escape(UENamingConvention.ToPascalCase(ns));

    // ----- Пути вывода (строятся по ORIGINAL именам — аффиксы не двигают файлы) -----

    public string ModelHeaderPath(string originalModelName) => $"Models/{ModelFileName(originalModelName)}";
    public string ModelFileName(string originalModelName) => $"{UENamingConvention.ToPascalCase(originalModelName)}.h";

    public string EnumHeaderPath(string originalEnumName) => $"Enums/{EnumFileName(originalEnumName)}";
    public string EnumFileName(string originalEnumName) => $"{UENamingConvention.ToPascalCase(originalEnumName)}.h";

    public string SerializerHeaderPath(string originalModelName) => $"Serializers/{SerializerFileName(originalModelName)}.h";
    public string SerializerSourcePath(string originalModelName) => $"Serializers/{SerializerFileName(originalModelName)}.cpp";
    private static string SerializerFileName(string originalModelName) => $"{UENamingConvention.ToPascalCase(originalModelName)}Json";

    public string ClientHeaderPath(string clientName) => $"Clients/{UENamingConvention.ToPascalCase(clientName)}Client.h";
    /// <summary>Gets the implementation path, separate from public concrete client headers.</summary>
    /// <param name="clientName">The stable client name.</param>
    /// <returns>The implementation source path.</returns>
    public string ClientSourcePath(string clientName) => $"Clients/Impl/{UENamingConvention.ToPascalCase(clientName)}Client.cpp";
    /// <summary>Gets the contract path, separate from concrete HTTP implementations.</summary>
    /// <param name="clientName">The stable client name.</param>
    /// <returns>The public interface header path.</returns>
    public string InterfaceHeaderPath(string clientName) => $"Interfaces/I{UENamingConvention.ToPascalCase(clientName)}.h";

    /// <summary>Суффикс «_» для зарезервированных слов; гарантирует не-пустой идентификатор.</summary>
    private static string Escape(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return "_";
        return ReservedWords.Contains(identifier) ? $"{identifier}_" : identifier;
    }
}
