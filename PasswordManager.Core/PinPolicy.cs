namespace PasswordManager.Core;

/// <summary>
/// The rules for what counts as an acceptable PIN, kept in one place so the
/// CLI, the desktop GUI and the browser extension's native host all enforce
/// exactly the same thing.
/// </summary>
public static class PinPolicy
{
    /// <summary>
    /// Shorter PINs are rejected outright. Longer ones are encouraged: the
    /// PIN is only one half of the key (see <see cref="DeviceKeyProtector"/>),
    /// but every extra digit still multiplies the guessing effort for someone
    /// sitting at an already-unlocked machine.
    /// </summary>
    public const int MinimumLength = 4;

    public const string Requirement = "A PIN must be at least 4 digits, and digits only.";

    public static bool IsValid(string? pin) => Validate(pin) is null;

    /// <summary>
    /// Returns null when the PIN is acceptable, otherwise a message that can
    /// be shown to the user as-is.
    /// </summary>
    public static string? Validate(string? pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            return "Enter a PIN.";
        }

        if (!pin.All(char.IsAsciiDigit))
        {
            return "A PIN must contain digits only.";
        }

        if (pin.Length < MinimumLength)
        {
            return $"A PIN must be at least {MinimumLength} digits.";
        }

        return null;
    }
}
