namespace ValIO.Generators.Cpp.Models;

/// <summary>Selects the application-facing representation of OpenAPI dates, times, and intervals.</summary>
public enum CppTemporalBackend
{
    /// <summary>Keeps validated wire text without imposing native calendar range or fractional precision limits.</summary>
    String,
    /// <summary>Uses C++20 standard calendar and clock-duration values with explicit wire offset metadata.</summary>
    Chrono,
    /// <summary>Uses Howard Hinnant's header-only date calendar and clock types on C++17.</summary>
    HowardDate,
    /// <summary>Uses Boost Gregorian dates and POSIX time durations without silently truncating native tick precision.</summary>
    BoostDateTime,
    /// <summary>Uses dependency-free civil values and calendar intervals preserving arbitrary decimal fractions.</summary>
    Valio,
}
