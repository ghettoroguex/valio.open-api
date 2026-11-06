namespace ValIO.Generators.CSharp.Models;

/// <summary>
/// Тип аксессора свойства
/// </summary>
public enum PropertyAccessorType
{
    /// <summary>
    /// get; set;
    /// </summary>
    Set,

    /// <summary>
    /// get; init;
    /// </summary>
    Init
}

/// <summary>
/// Тип модели
/// </summary>
public enum ModelType
{
    /// <summary>
    /// class
    /// </summary>
    Class,

    /// <summary>
    /// record
    /// </summary>
    Record,

    /// <summary>
    /// struct
    /// </summary>
    Struct,

    /// <summary>
    /// record struct
    /// </summary>
    RecordStruct
}

/// <summary>
/// Стиль передачи query параметров
/// </summary>
public enum QueryParametersStyle
{
    /// <summary>
    /// Передавать query параметры как отдельные аргументы метода
    /// </summary>
    IndividualParameters,

    /// <summary>
    /// Передавать query параметры в виде класса (модели)
    /// </summary>
    ModelClass
}

/// <summary>
/// Стратегия размещения интерфейсов
/// </summary>
public enum InterfaceFileStrategy
{
    /// <summary>
    /// Генерировать интерфейсы в том же файле, что и реализация
    /// </summary>
    SameFile,

    /// <summary>
    /// Генерировать интерфейсы в отдельном файле
    /// </summary>
    SeparateFile
}
