using System.Security.Cryptography;
using System.Text.Json;
using PasswordManager.Core.Models;

namespace PasswordManager.Core;

/// <summary>
/// Handles reading/writing vault.json and the higher-level operations
/// (init, unlock, add/get/list/delete entries) that combine storage with
/// the CryptoService. The CLI layer (Program.cs) should only ever call
/// methods here -- it never touches encryption or the raw file directly.
/// </summary>
public class VaultStorage
{
    // A fixed known value used to verify a master password attempt is
    // correct without ever storing the master password itself.
    private const string VerifierPlaintext = "PWMAN_VERIFIER_OK";

    private readonly string _vaultPath;

    public VaultStorage(string vaultPath)
    {
        _vaultPath = vaultPath;
    }

    public bool VaultExists() => File.Exists(_vaultPath);

    /// <summary>
    /// Creates a brand new vault file, deriving a key from the given master
    /// password and storing an encrypted verifier so future unlock attempts
    /// can be checked. Throws if a vault already exists at this path.
    /// </summary>
    public void Initialize(string masterPassword)
    {
        if (VaultExists())
        {
            throw new InvalidOperationException(
                $"A vault already exists at {_vaultPath}. Delete it first if you want to start over.");
        }

        var salt = CryptoService.GenerateSalt();
        var key = CryptoService.DeriveKey(masterPassword, salt);

        var (nonce, cipherText, tag) = CryptoService.Encrypt(VerifierPlaintext, key);

        var vault = new Vault
        {
            SaltBase64 = Convert.ToBase64String(salt),
            VerifierNonceBase64 = Convert.ToBase64String(nonce),
            VerifierCipherTextBase64 = Convert.ToBase64String(cipherText),
            VerifierTagBase64 = Convert.ToBase64String(tag),
            Entries = new List<VaultEntry>(),
        };

        Save(vault);
    }

    /// <summary>
    /// Attempts to unlock the vault with the given master password.
    /// Returns the derived key on success. Throws CryptographicException
    /// (via a wrapped exception) if the password is wrong.
    /// </summary>
    public byte[] Unlock(string masterPassword)
    {
        var vault = Load();
        var salt = Convert.FromBase64String(vault.SaltBase64);
        var key = CryptoService.DeriveKey(masterPassword, salt);

        try
        {
            var decryptedVerifier = CryptoService.Decrypt(
                Convert.FromBase64String(vault.VerifierNonceBase64),
                Convert.FromBase64String(vault.VerifierCipherTextBase64),
                Convert.FromBase64String(vault.VerifierTagBase64),
                key);

            if (decryptedVerifier != VerifierPlaintext)
            {
                throw new UnauthorizedAccessException("Incorrect master password.");
            }
        }
        catch (CryptographicException)
        {
            // AES-GCM throws this when the tag doesn't match -- i.e. wrong key.
            throw new UnauthorizedAccessException("Incorrect master password.");
        }

        return key;
    }

    public void AddEntry(byte[] key, string service, string username, string password)
    {
        var vault = Load();

        var (nonce, cipherText, tag) = CryptoService.Encrypt(password, key);

        // If an entry for this service already exists, replace it rather
        // than creating a duplicate.
        vault.Entries.RemoveAll(e => string.Equals(e.Service, service, StringComparison.OrdinalIgnoreCase));

        vault.Entries.Add(new VaultEntry
        {
            Service = service,
            Username = username,
            NonceBase64 = Convert.ToBase64String(nonce),
            CipherTextBase64 = Convert.ToBase64String(cipherText),
            TagBase64 = Convert.ToBase64String(tag),
        });

        Save(vault);
    }

    public (string Username, string Password)? GetEntry(byte[] key, string service)
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

        return (entry.Username, password);
    }

    public List<string> ListServices()
    {
        var vault = Load();
        return vault.Entries.Select(e => e.Service).OrderBy(s => s).ToList();
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
