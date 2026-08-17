using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace PasswordManager.Core;

/// <summary>
/// All encryption/decryption logic lives here, isolated from CLI and
/// storage code so it's easy to review, test, and reason about on its own.
///
/// Design choices (worth knowing cold for interviews):
///   - Argon2id for deriving the encryption key from the master password.
///     Argon2id is the current recommended choice (OWASP) over PBKDF2/bcrypt
///     because it's memory-hard, making GPU/ASIC brute-force attacks far
///     more expensive than with older algorithms.
///   - AES-256-GCM for encrypting actual entry data. GCM is an authenticated
///     encryption mode: it doesn't just hide the data, it also detects if
///     the ciphertext was tampered with (via the auth tag), which plain
///     AES-CBC does not provide on its own.
///   - The master password itself is NEVER stored, logged, or written to
///     disk in any form. Only the derived key exists in memory, and only
///     for as long as the process needs it.
/// </summary>
public static class CryptoService
{
    private const int SaltSizeBytes = 16;
    private const int KeySizeBytes = 32; // 256-bit key for AES-256
    private const int NonceSizeBytes = 12; // standard for AES-GCM
    private const int TagSizeBytes = 16;

    // Argon2id cost parameters. These control how expensive key derivation
    // is -- higher is more secure but slower. These values are a reasonable
    // balance for a desktop app (roughly a few hundred milliseconds per
    // derivation on typical hardware).
    private const int Argon2IterationCount = 4;
    private const int Argon2MemorySizeKb = 65536; // 64 MB
    private const int Argon2DegreeOfParallelism = 2;

    public static byte[] GenerateSalt()
    {
        return RandomNumberGenerator.GetBytes(SaltSizeBytes);
    }

    /// <summary>
    /// Derives a 256-bit encryption key from the master password and salt
    /// using Argon2id. This is deliberately slow -- that's the point, it's
    /// what makes brute-forcing the master password expensive for an attacker
    /// even if they steal the vault file.
    /// </summary>
    public static byte[] DeriveKey(string masterPassword, byte[] salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(masterPassword);

        using var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            Iterations = Argon2IterationCount,
            MemorySize = Argon2MemorySizeKb,
            DegreeOfParallelism = Argon2DegreeOfParallelism,
        };

        return argon2.GetBytes(KeySizeBytes);
    }

    /// <summary>
    /// Encrypts plaintext with AES-256-GCM under the given key.
    /// Returns the nonce, ciphertext, and auth tag separately since all
    /// three are needed to decrypt and verify later.
    /// </summary>
    public static (byte[] Nonce, byte[] CipherText, byte[] Tag) Encrypt(string plaintext, byte[] key)
    {
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var cipherText = new byte[plaintextBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plaintextBytes, cipherText, tag);

        return (nonce, cipherText, tag);
    }

    /// <summary>
    /// Decrypts AES-256-GCM ciphertext. Throws CryptographicException if the
    /// key is wrong or the data was tampered with -- GCM's auth tag check
    /// fails closed, it does not return corrupted/garbage plaintext silently.
    /// </summary>
    public static string Decrypt(byte[] nonce, byte[] cipherText, byte[] tag, byte[] key)
    {
        var plaintextBytes = new byte[cipherText.Length];

        using var aesGcm = new AesGcm(key, TagSizeBytes);
        aesGcm.Decrypt(nonce, cipherText, tag, plaintextBytes);

        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
