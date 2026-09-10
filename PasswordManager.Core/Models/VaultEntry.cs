namespace PasswordManager.Core.Models;

/// <summary>
/// A single stored credential. The password itself is never stored in
/// plaintext -- only the AES-GCM ciphertext, the nonce used to encrypt it,
/// and the authentication tag needed to verify it wasn't tampered with.
/// </summary>
public class VaultEntry
{
    public string Service { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    // Base64-encoded AES-GCM output for the password field.
    public string CipherTextBase64 { get; set; } = string.Empty;
    public string NonceBase64 { get; set; } = string.Empty;
    public string TagBase64 { get; set; } = string.Empty;

    // When this credential was first saved, and when it was last changed
    // (a new password, a renamed username). Stored in UTC. Nullable so that
    // entries written by older versions -- which didn't record these -- load
    // cleanly and simply show as "unknown" rather than a bogus year-0001 date.
    public DateTimeOffset? CreatedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
}
