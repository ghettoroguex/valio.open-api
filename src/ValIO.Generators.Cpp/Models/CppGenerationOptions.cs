namespace ValIO.Generators.Cpp.Models;

/// <summary>
/// Настройки генерации C++ кода. Общие поля описывают UE-профиль (по умолчанию);
/// поля ниже маркера «std-профиль» управляют standalone-профилем
/// (cpp-httplib + nlohmann_json).
/// </summary>
public sealed class CppGenerationOptions
{
    /// <summary>
    /// Runtime-профиль: UE (USTRUCT/UCLASS + внешний UE-runtime) или std
    /// (cpp-httplib + nlohmann_json + сгенерированный runtime). По умолчанию UE.
    /// </summary>
    public CppRuntimeProfile Runtime { get; set; } = CppRuntimeProfile.Ue;

    // ======== std-профиль (cpp-httplib + nlohmann_json) ========

    /// <summary>
    /// Gets or sets the target C++ standard used by generated code and its CMake target.
    /// </summary>
    /// <value>17 by default; supported values are 17, 20, and 23.</value>
    /// <remarks>
    /// Chrono and modern-uuid providers require C++20 or later. ValIO::Expected uses
    /// std::expected when available and otherwise uses the bundled implementation.
    /// </remarks>
    public int CxxStandard { get; set; } = 17;

    /// <summary>
    /// Стиль API: sync (ValIO::Expected), async (std::future) или оба.
    /// </summary>
    public CppApiStyle ApiStyle { get; set; } = CppApiStyle.Sync;

    /// <summary>
    /// Форма клиента: один класс или интерфейс I{Client} + реализация.
    /// </summary>
    public CppClientShape ClientShape { get; set; } = CppClientShape.Class;

    /// <summary>
    /// Корневой namespace сгенерированных моделей/enum'ов/клиентов std-профиля.
    /// null → берётся Namespace конфигурации проекта, иначе PascalCase имени проекта.
    /// </summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// Include http-библиотеки (переопределяется потребителями с врапперами).
    /// </summary>
    public string HttpInclude { get; set; } = "httplib.h";

    /// <summary>
    /// Include полного nlohmann/json (используется только в .cpp-файлах).
    /// </summary>
    public string JsonInclude { get; set; } = "nlohmann/json.hpp";

    /// <summary>
    /// Include forward-declaration nlohmann/json_fwd (используется в сериализаторных хедерах).
    /// </summary>
    public string JsonFwdInclude { get; set; } = "nlohmann/json_fwd.hpp";

    /// <summary>
    /// Имя модели ошибки бэкенда для разбора не-2xx ответов; если схемы с таким
    /// именем нет, эмитится минимальная модель + кодек.
    /// </summary>
    public string ErrorModelName { get; set; } = "RestApiErrorResponse";

    /// <summary>
    /// Эмитить CMakeLists-сниппет (file-list + find_package(httplib)/find_package(nlohmann_json)).
    /// </summary>
    public bool EmitCmake { get; set; }

    /// <summary>Whether standalone models and enums also emit their separate JSON codec files.</summary>
    /// <value>True by default; disable together with client generation for dependency-free DTO output.</value>
    public bool EmitSerializers { get; set; } = true;

    /// <summary>Gets or sets the explicitly selected application temporal provider for standalone models.</summary>
    /// <value>Validated strings by default; native providers retain wire offsets and reject unrepresentable values.</value>
    public CppTemporalBackend TemporalBackend { get; set; } = CppTemporalBackend.String;

    /// <summary>Gets or sets the independent application UUID provider for standalone models.</summary>
    /// <value>Validated canonical UUID strings by default.</value>
    public CppUuidBackend UuidBackend { get; set; } = CppUuidBackend.String;

    /// <summary>
    /// Макрос API модуля, вставляемый в UCLASS (например WIREFRAME_API, MYPROJECT_API).
    /// </summary>
    public string ModuleApiMacro { get; set; } = "MODULE_API";

    /// <summary>
    /// Суффикс для generated header (UHT требует .generated.h)
    /// </summary>
    public string GeneratedHeaderSuffix { get; set; } = ".generated.h";

    /// <summary>
    /// Категория по умолчанию для UPROPERTY/UFUNCTION в Blueprint Editor
    /// </summary>
    public string BlueprintCategory { get; set; } = "ValIO";

    /// <summary>
    /// Директория для вывода сгенерированных файлов
    /// </summary>
    public string OutputDirectory { get; set; } = "Generated";

    /// <summary>
    /// Текст copyright в заголовке файлов (null = без copyright)
    /// </summary>
    public string? Copyright { get; set; }

    /// <summary>
    /// Имя базового класса для async actions (наследник UBlueprintAsyncActionBase)
    /// </summary>
    public string BaseAsyncActionClassName { get; set; } = "UValIOAsyncActionBase";

    /// <summary>
    /// Include path для базового async action класса
    /// </summary>
    public string BaseAsyncActionIncludePath { get; set; } = "Blueprint/ValIOTypedAsyncActionBase.h";

    /// <summary>
    /// Include path для ValIO client
    /// </summary>
    public string ValIOClientIncludePath { get; set; } = "Client/ValIOClient.h";

    /// <summary>
    /// Include path для ValIO request builder
    /// </summary>
    public string ValIORequestBuilderIncludePath { get; set; } = "Request/ValIORequestBuilder.h";

    /// <summary>
    /// Include path для ValIO multipart form data
    /// </summary>
    public string ValIOMultipartFormDataIncludePath { get; set; } = "Request/ValIOMultipartFormData.h";

    /// <summary>
    /// Include path для ValIO types
    /// </summary>
    public string ValIOTypesIncludePath { get; set; } = "ValIOTypes.h";

    /// <summary>
    /// Include path для ValIO response (FValIOResponse)
    /// </summary>
    public string ValIOResponseIncludePath { get; set; } = "Response/ValIOResponse.h";

    /// <summary>
    /// Добавлять UE-инклюды (CoreMinimal, Async/Future, ValIOResponse) в headers.
    /// Если true — добавляются в endpoint headers; CoreMinimal — в enums, models, params, result.
    /// Если false — не добавляются (предполагается PCH или явные инклюды в проекте).
    /// </summary>
    public bool AddUnrealEngineIncludesInHeaders { get; set; } = true;
}
