namespace PasswordManager.Core.Models;

/// <summary>
/// The full contents of vault.json on disk.
///
/// SaltBase64 is the random salt used with Argon2id to derive the
/// encryption key from the user's master password. It's safe to store
/// in plaintext -- a salt isn't a secret, its job is just to make sure
/// two people using the same master password get different derived keys,
/// and to defeat precomputed rainbow-table attacks.
///
/// VerifierCipherTextBase64 / VerifierNonceBase64 / VerifierTagBase64 hold
/// an encrypted "known value" (a fixed string) used purely to check whether
/// a master password attempt is correct, WITHOUT ever storing the master
/// password itself anywhere. If decrypting the verifier succeeds and matches
/// the known value, the password was correct.
/// </summary>
public class Vault
{
    public string SaltBase64 { get; set; } = string.Empty;

    public string VerifierCipherTextBase64 { get; set; } = string.Empty;
    public string VerifierNonceBase64 { get; set; } = string.Empty;
    public string VerifierTagBase64 { get; set; } = string.Empty;

    public List<VaultEntry> Entries { get; set; } = new();
}
