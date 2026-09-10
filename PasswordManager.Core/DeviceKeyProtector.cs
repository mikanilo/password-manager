using System.Security.Cryptography;

namespace PasswordManager.Core;

/// <summary>
/// Binds a vault to the current Windows user account using DPAPI.
///
/// This is what makes a short PIN safe enough to use. A PIN on its own is
/// low-entropy -- a 4-digit PIN is only 10,000 possibilities, so an attacker
/// who copies vault.json onto their own machine could brute-force it offline
/// no matter how slow Argon2id is made.
///
/// To prevent that, the vault also stores a 32-byte random "device secret"
/// that is required to derive the encryption key. That secret is encrypted
/// with DPAPI under DataProtectionScope.CurrentUser, which means only the
/// logged-in Windows user on this machine can decrypt it -- the key material
/// is held by the OS, not by the vault file. A stolen vault.json is therefore
/// useless on its own: without the device secret there is nothing to brute
/// force, because the PIN alone never produces the encryption key.
///
/// This mirrors how a Windows Hello PIN works: the PIN is short, but it only
/// has value on the specific device it was set up on.
/// </summary>
public static class DeviceKeyProtector
{
    public const int DeviceSecretSizeBytes = 32;

    public static byte[] GenerateDeviceSecret()
        => RandomNumberGenerator.GetBytes(DeviceSecretSizeBytes);

    /// <summary>
    /// Encrypts the device secret so only the current Windows user can read
    /// it back. The vault salt is passed as additional entropy, so the blob
    /// is bound to this specific vault as well as to this user account.
    /// </summary>
    public static byte[] Protect(byte[] deviceSecret, byte[] salt)
    {
        // Guarded inline rather than via a helper so the platform-compatibility
        // analyzer can see that the DPAPI calls below are Windows-only reachable.
        if (!OperatingSystem.IsWindows())
        {
            throw NotOnWindows();
        }

        return ProtectedData.Protect(deviceSecret, salt, DataProtectionScope.CurrentUser);
    }

    /// <summary>
    /// Reverses <see cref="Protect"/>. Throws <see cref="DeviceBindingException"/>
    /// if the blob was created by a different Windows user or on a different
    /// machine -- which is exactly the case we want to fail closed on.
    /// </summary>
    public static byte[] Unprotect(byte[] protectedBlob, byte[] salt)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw NotOnWindows();
        }

        try
        {
            return ProtectedData.Unprotect(protectedBlob, salt, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException ex)
        {
            throw new DeviceBindingException(
                "This vault is bound to a different Windows user account or machine, " +
                "so its PIN cannot unlock it here.", ex);
        }
    }

    private static PlatformNotSupportedException NotOnWindows()
        => new("PIN unlock relies on Windows DPAPI to bind the vault to your user account.");
}

/// <summary>
/// Thrown when the vault's device secret cannot be unwrapped on this machine
/// or under this Windows account -- distinct from a simply-wrong PIN, and
/// worth reporting differently since retyping the PIN will never help.
/// </summary>
public class DeviceBindingException : Exception
{
    public DeviceBindingException(string message, Exception? inner = null)
        : base(message, inner) { }
}
