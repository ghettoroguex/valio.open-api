namespace ValIO.Generators.Cpp.Models;

/// <summary>
/// Runtime-профиль генерации C++: набор библиотек и форма emitted-кода.
/// Выбирается опцией <c>runtime</c> ("ue" | "std"); UE-профиль — по умолчанию.
/// </summary>
public enum CppRuntimeProfile
{
    /// <summary>
    /// Unreal Engine: USTRUCT/UCLASS/UFUNCTION + внешний UE-runtime
    /// (FValIOClient и др., поставляется проектом-потребителем через include-пути).
    /// </summary>
    Ue,

    /// <summary>
    /// Standalone std-C++: транспорт cpp-httplib, формат моделей nlohmann_json,
    /// самодостаточный сгенерированный runtime (namespace ValIO).
    /// Хедеры моделей не зависят от JSON-библиотеки (model/codec separation).
    /// </summary>
    Std,
}
