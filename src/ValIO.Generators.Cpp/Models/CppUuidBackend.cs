namespace ValIO.Generators.Cpp.Models;

/// <summary>Selects the application representation used for OpenAPI UUID values in standard C++ models.</summary>
/// <remarks>The string representation keeps DTOs dependency-free; other representations expose the explicitly selected provider.</remarks>
public enum CppUuidBackend
{
    /// <summary>Uses <c>std::string</c> and validates canonical UUID syntax at JSON boundaries.</summary>
    String,

    /// <summary>Uses <c>boost::uuids::uuid</c> from Boost.Uuid.</summary>
    BoostUuid,

    /// <summary>Uses <c>uuids::uuid</c> from the C++17 stduuid library.</summary>
    StdUuid,

    /// <summary>Uses <c>muuid::uuid</c> from modern-uuid, which requires C++20 or later.</summary>
    ModernUuid,
}
