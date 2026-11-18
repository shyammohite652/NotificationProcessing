namespace NotificationProcessing.Application.Validators;

internal static class EnumParser
{
    /// <summary>
    /// Case-insensitive parse by name only. Enum.TryParse alone would also accept numbers such as "5"
    /// or "2", which must not be valid priorities.
    /// </summary>
    public static bool TryParseName<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!char.IsLetter(trimmed[0]))
        {
            return false;
        }

        return Enum.TryParse(trimmed, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}
