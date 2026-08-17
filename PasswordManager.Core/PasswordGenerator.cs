using System.Security.Cryptography;

namespace PasswordManager.Core;

public enum PasswordStrength
{
    Weak,
    Fair,
    Strong,
}

public static class PasswordGenerator
{
    private const string Lowercase = "abcdefghijklmnopqrstuvwxyz";
    private const string Uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Digits = "0123456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}?";

    /// <summary>
    /// Generates a cryptographically random password guaranteed to contain
    /// at least one character from each category (lower, upper, digit, symbol).
    /// Uses RandomNumberGenerator (not System.Random) since System.Random is
    /// not cryptographically secure and shouldn't be used for anything
    /// security-related, including generated passwords.
    /// </summary>
    public static string Generate(int length = 20)
    {
        if (length < 8)
        {
            throw new ArgumentException("Generated passwords should be at least 8 characters.");
        }

        var allChars = Lowercase + Uppercase + Digits + Symbols;
        var passwordChars = new char[length];

        // Guarantee at least one character from each category first.
        passwordChars[0] = Lowercase[RandomNumberGenerator.GetInt32(Lowercase.Length)];
        passwordChars[1] = Uppercase[RandomNumberGenerator.GetInt32(Uppercase.Length)];
        passwordChars[2] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        passwordChars[3] = Symbols[RandomNumberGenerator.GetInt32(Symbols.Length)];

        for (var i = 4; i < length; i++)
        {
            passwordChars[i] = allChars[RandomNumberGenerator.GetInt32(allChars.Length)];
        }

        // Shuffle so the guaranteed characters aren't always in the first four slots
        // (Fisher-Yates, using the crypto RNG for the shuffle too).
        for (var i = passwordChars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (passwordChars[i], passwordChars[j]) = (passwordChars[j], passwordChars[i]);
        }

        return new string(passwordChars);
    }

    /// <summary>
    /// A lightweight heuristic strength check -- not a replacement for a full
    /// entropy calculation, but enough to catch obviously weak passwords like
    /// "123" or "password" and nudge the user before they save them.
    /// </summary>
    public static (PasswordStrength Strength, string Feedback) EvaluateStrength(string password)
    {
        var score = 0;
        var issues = new List<string>();

        if (password.Length >= 12) score++;
        else issues.Add("use at least 12 characters");

        if (password.Any(char.IsLower)) score++;
        else issues.Add("add a lowercase letter");

        if (password.Any(char.IsUpper)) score++;
        else issues.Add("add an uppercase letter");

        if (password.Any(char.IsDigit)) score++;
        else issues.Add("add a number");

        if (password.Any(c => Symbols.Contains(c))) score++;
        else issues.Add("add a symbol");

        var strength = score switch
        {
            >= 5 => PasswordStrength.Strong,
            >= 3 => PasswordStrength.Fair,
            _ => PasswordStrength.Weak,
        };

        var feedback = issues.Count == 0
            ? "Looks strong."
            : $"Consider: {string.Join(", ", issues)}.";

        return (strength, feedback);
    }
}
