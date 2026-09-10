using System.Security.Cryptography;
using System.Text.Json;
using PasswordManager.Core.Models;

namespace PasswordManager.Core;

/// <summary>
/// Metadata about a stored credential, without the password itself -- safe
/// to list without decrypting every entry.
/// </summary>
public record EntrySummary(
    string Service,
    string Username,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? UpdatedUtc);

/// <summary>
/// A fully decrypted credential, returned only after a successful unlock.
/// </summary>
public record EntryDetails(
    string Service,
    string Username,
    string Password,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? UpdatedUtc);

/// <summary>
/// Handles reading/writing vault.json and the higher-level operations
/// (init, unlock, add/get/list/delete entries) that combine storage with
/// the CryptoService. The CLI layer (Program.cs) should only ever call
/// methods here -- it never touches encryption or the raw file directly.
/// </summary>
public class VaultStorage
{
    // A fixed known value used to verify an unlock attempt is correct
    // without ever storing the PIN or master password itself.
    private const string VerifierPlaintext = "PWMAN_VERIFIER_OK";

    private readonly string _vaultPath;

    public VaultStorage(string vaultPath)
    {
        _vaultPath = vaultPath;
    }

    public bool VaultExists() => File.Exists(_vaultPath);

    /// <summary>
    /// How this vault is unlocked. Vaults created before PIN support existed
    /// report MasterPassword and keep working until the user migrates them.
    /// </summary>
    public VaultAuthMode GetAuthMode() => Load().AuthMode;

    /// <summary>
    /// Creates a brand new PIN-protected vault. The PIN is stretched with
    /// Argon2id and combined with a DPAPI-protected device secret, so the
    /// vault can only be opened with both the PIN and this Windows account.
    /// Throws if a vault already exists at this path.
    /// </summary>
    public void InitializeWithPin(string pin)
    {
        if (VaultExists())
        {
            throw new InvalidOperationException(
                $"A vault already exists at {_vaultPath}. Delete it first if you want to start over.");
        }

        var validationError = PinPolicy.Validate(pin);
        if (validationError is not null)
        {
            throw new ArgumentException(validationError, nameof(pin));
        }

        Save(BuildPinVault(pin, new List<(VaultEntry Entry, string Password)>()));
    }

    /// <summary>
    /// Attempts to unlock the vault with the given PIN (or, for a legacy
    /// vault that hasn't been migrated yet, the master password). Returns the
    /// derived key on success, and throws UnauthorizedAccessException if the
    /// secret is wrong.
    /// </summary>
    public byte[] Unlock(string secret)
    {
        var vault = Load();
        var key = DeriveKey(vault, secret);

        try
        {
            var decryptedVerifier = CryptoService.Decrypt(
                Convert.FromBase64String(vault.VerifierNonceBase64),
                Convert.FromBase64String(vault.VerifierCipherTextBase64),
                Convert.FromBase64String(vault.VerifierTagBase64),
                key);

            if (decryptedVerifier != VerifierPlaintext)
            {
                throw new UnauthorizedAccessException(IncorrectSecretMessage(vault.AuthMode));
            }
        }
        catch (CryptographicException)
        {
            // AES-GCM throws this when the tag doesn't match -- i.e. wrong key.
            throw new UnauthorizedAccessException(IncorrectSecretMessage(vault.AuthMode));
        }

        return key;
    }

    /// <summary>
    /// Replaces the vault's PIN. Requires the current PIN, since every entry
    /// has to be decrypted and re-encrypted under the new key.
    /// </summary>
    public void ChangePin(string currentPin, string newPin)
    {
        if (Load().AuthMode != VaultAuthMode.Pin)
        {
            throw new InvalidOperationException(
                "This vault still uses a master password. Migrate it to a PIN first.");
        }

        Rekey(currentPin, newPin);
    }

    /// <summary>
    /// Converts a legacy master-password vault to a PIN. The master password
    /// is needed one last time to read the existing entries; afterwards the
    /// vault is re-encrypted under the PIN plus this machine's device secret,
    /// and the master password no longer opens it.
    /// </summary>
    public void MigrateToPin(string masterPassword, string newPin)
    {
        if (Load().AuthMode == VaultAuthMode.Pin)
        {
            throw new InvalidOperationException("This vault already uses a PIN.");
        }

        Rekey(masterPassword, newPin);
    }

    /// <summary>
    /// Re-encrypts the whole vault under a new PIN. The old secret is verified
    /// first, then every entry is decrypted with the old key and written back
    /// under a freshly derived one. Everything is decrypted before anything is
    /// written, so a failure part-way through leaves the original file intact
    /// rather than half-converted.
    /// </summary>
    private void Rekey(string currentSecret, string newPin)
    {
        var validationError = PinPolicy.Validate(newPin);
        if (validationError is not null)
        {
            throw new ArgumentException(validationError, nameof(newPin));
        }

        var oldKey = Unlock(currentSecret);

        try
        {
            var vault = Load();

            var plaintextEntries = new List<(VaultEntry Entry, string Password)>();
            foreach (var entry in vault.Entries)
            {
                var password = CryptoService.Decrypt(
                    Convert.FromBase64String(entry.NonceBase64),
                    Convert.FromBase64String(entry.CipherTextBase64),
                    Convert.FromBase64String(entry.TagBase64),
                    oldKey);

                plaintextEntries.Add((entry, password));
            }

            Save(BuildPinVault(newPin, plaintextEntries));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(oldKey);
        }
    }

    /// <summary>
    /// Builds a complete PIN-mode vault: a new salt, a new DPAPI-protected
    /// device secret, a fresh verifier, and every supplied entry re-encrypted
    /// under the newly derived key (preserving its original dates).
    /// </summary>
    private static Vault BuildPinVault(string pin, List<(VaultEntry Entry, string Password)> entries)
    {
        var salt = CryptoService.GenerateSalt();
        var deviceSecret = DeviceKeyProtector.GenerateDeviceSecret();

        byte[] key;
        byte[] protectedDeviceSecret;
        try
        {
            key = CryptoService.DeriveKeyFromPin(pin, salt, deviceSecret);
            protectedDeviceSecret = DeviceKeyProtector.Protect(deviceSecret, salt);
        }
        finally
        {
            // Only the DPAPI-protected copy is kept; the raw secret just needs
            // to live long enough to derive the key and be wrapped.
            CryptographicOperations.ZeroMemory(deviceSecret);
        }

        try
        {
            var (verifierNonce, verifierCipherText, verifierTag) =
                CryptoService.Encrypt(VerifierPlaintext, key);

            var vault = new Vault
            {
                AuthMode = VaultAuthMode.Pin,
                SaltBase64 = Convert.ToBase64String(salt),
                DeviceSecretProtectedBase64 = Convert.ToBase64String(protectedDeviceSecret),
                VerifierNonceBase64 = Convert.ToBase64String(verifierNonce),
                VerifierCipherTextBase64 = Convert.ToBase64String(verifierCipherText),
                VerifierTagBase64 = Convert.ToBase64String(verifierTag),
                Entries = new List<VaultEntry>(),
            };

            foreach (var (entry, password) in entries)
            {
                var (nonce, cipherText, tag) = CryptoService.Encrypt(password, key);

                vault.Entries.Add(new VaultEntry
                {
                    Service = entry.Service,
                    Username = entry.Username,
                    NonceBase64 = Convert.ToBase64String(nonce),
                    CipherTextBase64 = Convert.ToBase64String(cipherText),
                    TagBase64 = Convert.ToBase64String(tag),
                    CreatedUtc = entry.CreatedUtc,
                    UpdatedUtc = entry.UpdatedUtc,
                });
            }

            return vault;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Derives the encryption key for an unlock attempt, using whichever
    /// scheme this vault was written with.
    /// </summary>
    private static byte[] DeriveKey(Vault vault, string secret)
    {
        var salt = Convert.FromBase64String(vault.SaltBase64);

        if (vault.AuthMode != VaultAuthMode.Pin)
        {
            return CryptoService.DeriveKey(secret, salt);
        }

        if (string.IsNullOrEmpty(vault.DeviceSecretProtectedBase64))
        {
            throw new InvalidDataException(
                "This vault is marked as PIN-protected but has no device secret. " +
                "The file may be corrupted.");
        }

        var deviceSecret = DeviceKeyProtector.Unprotect(
            Convert.FromBase64String(vault.DeviceSecretProtectedBase64), salt);

        try
        {
            return CryptoService.DeriveKeyFromPin(secret, salt, deviceSecret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(deviceSecret);
        }
    }

    private static string IncorrectSecretMessage(VaultAuthMode mode)
        => mode == VaultAuthMode.Pin ? "Incorrect PIN." : "Incorrect master password.";

    public void AddEntry(byte[] key, string service, string username, string password)
    {
        var vault = Load();

        var (nonce, cipherText, tag) = CryptoService.Encrypt(password, key);

        // If an entry for this service already exists, replace it rather
        // than creating a duplicate -- but carry its original creation date
        // forward so "date added" still reflects when the credential first
        // entered the vault, not the most recent edit.
        var existing = vault.Entries.FirstOrDefault(
            e => string.Equals(e.Service, service, StringComparison.OrdinalIgnoreCase));
        vault.Entries.RemoveAll(e => string.Equals(e.Service, service, StringComparison.OrdinalIgnoreCase));

        var now = DateTimeOffset.UtcNow;

        vault.Entries.Add(new VaultEntry
        {
            Service = service,
            Username = username,
            NonceBase64 = Convert.ToBase64String(nonce),
            CipherTextBase64 = Convert.ToBase64String(cipherText),
            TagBase64 = Convert.ToBase64String(tag),
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now,
        });

        Save(vault);
    }

    public EntryDetails? GetEntry(byte[] key, string service)
    {
        var vault = Load();
        var entry = vault.Entries.FirstOrDefault(
            e => string.Equals(e.Service, service, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return null;
        }

        var password = CryptoService.Decrypt(
            Convert.FromBase64String(entry.NonceBase64),
            Convert.FromBase64String(entry.CipherTextBase64),
            Convert.FromBase64String(entry.TagBase64),
            key);

        return new EntryDetails(entry.Service, entry.Username, password, entry.CreatedUtc, entry.UpdatedUtc);
    }

    public List<string> ListServices()
    {
        var vault = Load();
        return vault.Entries.Select(e => e.Service).OrderBy(s => s).ToList();
    }

    /// <summary>
    /// Lists every stored credential with its metadata (username, dates) but
    /// without decrypting the passwords -- so it works with just the vault
    /// file and needs no key.
    /// </summary>
    public List<EntrySummary> ListEntries()
    {
        var vault = Load();
        return vault.Entries
            .OrderBy(e => e.Service, StringComparer.OrdinalIgnoreCase)
            .Select(e => new EntrySummary(e.Service, e.Username, e.CreatedUtc, e.UpdatedUtc))
            .ToList();
    }

    public bool DeleteEntry(string service)
    {
        var vault = Load();
        var removed = vault.Entries.RemoveAll(
            e => string.Equals(e.Service, service, StringComparison.OrdinalIgnoreCase));

        if (removed > 0)
        {
            Save(vault);
            return true;
        }

        return false;
    }

    private Vault Load()
    {
        var json = File.ReadAllText(_vaultPath);
        return JsonSerializer.Deserialize<Vault>(json)
            ?? throw new InvalidDataException("Vault file is corrupted or unreadable.");
    }

    private void Save(Vault vault)
    {
        var json = JsonSerializer.Serialize(vault, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_vaultPath, json);
    }
}
