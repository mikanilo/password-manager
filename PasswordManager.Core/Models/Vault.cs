namespace PasswordManager.Core.Models;

/// <summary>
/// How a vault is unlocked. Vaults created before PIN support existed have no
/// AuthMode recorded in their JSON, so the enum's default value has to remain
/// MasterPassword -- that way an older vault.json deserializes into exactly
/// the behaviour it was written with, and can be migrated on the user's terms
/// rather than silently breaking.
/// </summary>
public enum VaultAuthMode
{
    MasterPassword = 0,
    Pin = 1,
}

/// <summary>
/// The full contents of vault.json on disk.
///
/// SaltBase64 is the random salt used with Argon2id to derive the
/// encryption key from the user's PIN (or, for older vaults, their master
/// password). It's safe to store in plaintext -- a salt isn't a secret, its
/// job is just to make sure two people using the same secret get different
/// derived keys, and to defeat precomputed rainbow-table attacks.
///
/// VerifierCipherTextBase64 / VerifierNonceBase64 / VerifierTagBase64 hold
/// an encrypted "known value" (a fixed string) used purely to check whether
/// an unlock attempt is correct, WITHOUT ever storing the PIN or master
/// password itself anywhere. If decrypting the verifier succeeds and matches
/// the known value, the secret was correct.
/// </summary>
public class Vault
{
    public VaultAuthMode AuthMode { get; set; } = VaultAuthMode.MasterPassword;

    public string SaltBase64 { get; set; } = string.Empty;

    /// <summary>
    /// The vault's device secret, encrypted with Windows DPAPI so that only
    /// the Windows user who created the vault can recover it. Half of the
    /// PIN-mode key derivation depends on this value, which is what stops a
    /// copied vault.json from being brute-forced offline. Empty for legacy
    /// master-password vaults, which don't use device binding.
    /// </summary>
    public string DeviceSecretProtectedBase64 { get; set; } = string.Empty;

    public string VerifierCipherTextBase64 { get; set; } = string.Empty;
    public string VerifierNonceBase64 { get; set; } = string.Empty;
    public string VerifierTagBase64 { get; set; } = string.Empty;

    public List<VaultEntry> Entries { get; set; } = new();
}
