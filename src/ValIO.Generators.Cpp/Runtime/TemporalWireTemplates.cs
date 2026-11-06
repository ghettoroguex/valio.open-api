namespace ValIO.Generators.Cpp.Runtime;

/// <summary>Shares lexical interval parsing between validated string values and selected native temporal codecs.</summary>
internal static class TemporalWireTemplates
{
    /// <summary>The C++ interval parser support inserted inside the checked JSON detail namespace.</summary>
    /// <remarks>Components stay decimal text until a selected application provider requests a bounded numeric representation.</remarks>
    public const string ValidationSupport = """
/** @brief Preserves calendar interval components without averaging years or months into elapsed seconds. */
struct IntervalParts {
    bool Negative = false;
    std::string Years, Months, Weeks, Days, Hours, Minutes, Seconds, Fraction;
};

/** @brief Checks a nonempty unsigned decimal component without imposing a native integer range. */
inline bool IsDecimalDigits(const std::string_view text) {
    if (text.empty()) return false;
    for (const char value : text) if (value < '0' || value > '9') return false;
    return true;
}

/** @brief Parses ISO 8601 duration components with optional fractional seconds and a separate week form. */
inline bool ParseDurationParts(const std::string_view text, IntervalParts& result) {
    if (text.size() < 3 || (text[0] != 'P' && text[0] != 'p')) return false;
    result = IntervalParts{};
    std::size_t offset = 1;
    bool time = false;
    bool any = false;
    bool timeValue = false;
    int previous = 0;
    while (offset < text.size()) {
        if (text[offset] == 'T' || text[offset] == 't') {
            if (time) return false;
            time = true;
            previous = 0;
            if (++offset == text.size()) return false;
        }
        const std::size_t start = offset;
        while (offset < text.size() && text[offset] >= '0' && text[offset] <= '9') ++offset;
        if (offset == start) return false;
        const auto digits = std::string(text.substr(start, offset - start));
        std::string fraction;
        if (offset < text.size() && text[offset] == '.') {
            const std::size_t first = ++offset;
            while (offset < text.size() && text[offset] >= '0' && text[offset] <= '9') ++offset;
            if (first == offset) return false;
            fraction = std::string(text.substr(first, offset - first));
        }
        if (offset == text.size()) return false;
        char unit = text[offset++];
        if (unit >= 'a' && unit <= 'z') unit = static_cast<char>(unit - 'a' + 'A');
        int order = 0;
        std::string* target = nullptr;
        if (time) {
            if (unit == 'H') { order = 1; target = &result.Hours; }
            else if (unit == 'M') { order = 2; target = &result.Minutes; }
            else if (unit == 'S') { order = 3; target = &result.Seconds; }
            else return false;
            timeValue = true;
        } else {
            if (unit == 'Y') { order = 1; target = &result.Years; }
            else if (unit == 'M') { order = 2; target = &result.Months; }
            else if (unit == 'W') { order = 3; target = &result.Weeks; }
            else if (unit == 'D') { order = 4; target = &result.Days; }
            else return false;
        }
        if (order <= previous || (!fraction.empty() && (!time || unit != 'S'))) return false;
        previous = order;
        *target = digits;
        if (!fraction.empty()) result.Fraction = fraction;
        any = true;
    }
    if (!result.Weeks.empty() && (!result.Years.empty() || !result.Months.empty() || !result.Days.empty() || time)) return false;
    return any && (!time || timeValue);
}

/** @brief Parses the explicit time-span wire convention [-][days.]hh:mm:ss[.fraction]. */
inline bool ParseTimeSpanParts(std::string_view text, IntervalParts& result) {
    result = IntervalParts{};
    if (!text.empty() && text[0] == '-') { result.Negative = true; text.remove_prefix(1); }
    const auto colon = text.find(':');
    if (colon == std::string_view::npos) return false;
    const auto dot = text.find('.');
    std::size_t hour = 0;
    if (dot != std::string_view::npos && dot < colon) {
        if (!IsDecimalDigits(text.substr(0, dot))) return false;
        result.Days = std::string(text.substr(0, dot));
        hour = dot + 1;
    }
    if (colon != hour + 2 || text.size() < hour + 8 || text[hour + 5] != ':') return false;
    const int hours = Digits(text, hour, 2);
    const int minutes = Digits(text, hour + 3, 2);
    const int seconds = Digits(text, hour + 6, 2);
    if (hours < 0 || hours > 23 || minutes < 0 || minutes > 59 || seconds < 0 || seconds > 59) return false;
    result.Hours = std::string(text.substr(hour, 2));
    result.Minutes = std::string(text.substr(hour + 3, 2));
    result.Seconds = std::string(text.substr(hour + 6, 2));
    if (text.size() == hour + 8) return true;
    if (text[hour + 8] != '.' || !IsDecimalDigits(text.substr(hour + 9))) return false;
    result.Fraction = std::string(text.substr(hour + 9));
    return true;
}

/** @brief Validates an interval without applying a native provider's range or fractional precision limits. */
inline bool IsInterval(const std::string_view text, const JsonStringFormat format) {
    IntervalParts parts;
    return format == JsonStringFormat::TimeSpan ? ParseTimeSpanParts(text, parts) : ParseDurationParts(text, parts);
}
""";
}
