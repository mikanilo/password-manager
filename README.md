# pwman — Local Encrypted Password Manager (CLI)

A command-line password manager written in C#/.NET. This is Phase 1 of a larger
project (desktop GUI + Chrome extension planned next) — but it's fully
functional and secure on its own.

## Features
- `pwman init` — create a new vault protected by a single master password
- `pwman add <service> <username>` — save a credential, with the option to
  auto-generate a strong password or type your own (weak passwords trigger
  a warning before saving)
- `pwman get <service>` — retrieve a saved credential
- `pwman list` — list all saved service names
- `pwman delete <service>` — remove a saved credential
- `pwman generate [length]` — print a cryptographically random strong
  password on its own (default 20 characters)

## Security design

- **Master password → key derivation**: your master password is never stored
  anywhere. Instead, it's run through **Argon2id** (memory-hard KDF, the
  current OWASP-recommended choice over PBKDF2/bcrypt) along with a random
  salt to derive a 256-bit encryption key. Each vault has its own random salt.
- **Password verification without storing the password**: on `init`, a known
  fixed string is encrypted with the derived key and stored. On future
  unlocks, if decrypting that value with the newly-entered password succeeds
  and matches, the password was correct. If it fails, the password was wrong.
  At no point is the actual master password written to disk.
- **Entry encryption**: each saved password is encrypted individually with
  **AES-256-GCM**, an authenticated encryption mode. Unlike plain AES-CBC,
  GCM also detects tampering — if the ciphertext or tag is altered, decryption
  fails loudly instead of silently returning garbage.
- **Password generation**: uses `RandomNumberGenerator` (cryptographically
  secure), not `System.Random`, and guarantees at least one lowercase,
  uppercase, digit, and symbol character.
- **No plaintext passwords in memory longer than needed, no plaintext on
  disk, ever.**

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download) (or later — built and tested on .NET 10)

## Build & run

```bash
cd PasswordManager
dotnet restore
dotnet build
dotnet run -- <command> [args]
```

Or build a standalone executable:
```bash
dotnet publish -c Release -o ./publish
./publish/pwman <command> [args]
```

## Usage

```bash
# First-time setup — creates a new vault, prompts for a master password
pwman init

# Add or update a credential (offers to generate a strong password for you)
pwman add gmail myemail@gmail.com

# Retrieve a credential
pwman get gmail

# List all saved service names (no master password needed — names aren't secret)
pwman list

# Delete an entry
pwman delete gmail

# Generate a standalone strong password (e.g. for something outside the vault)
pwman generate
pwman generate 24
```

The vault is stored at:
- Windows: `%APPDATA%\pwman\vault.json`
- macOS/Linux: `~/.config/pwman/vault.json` (via `ApplicationData` special folder)

## Project structure
```
PasswordManager/
├── Program.cs              # CLI entry point, argument parsing, prompts
├── CryptoService.cs         # Argon2id key derivation + AES-256-GCM encrypt/decrypt
├── PasswordGenerator.cs      # cryptographically secure password generation + strength check
├── VaultStorage.cs          # vault.json read/write, higher-level vault operations
└── Models/
    ├── Vault.cs             # top-level vault file schema
    └── VaultEntry.cs        # single credential entry schema
```

## Roadmap
- [x] CLI core with Argon2id + AES-256-GCM
- [x] Add/get/list/delete commands
- [x] Password generator + strength checking
- [ ] WPF desktop GUI wrapping the same CryptoService/VaultStorage logic
- [ ] Chrome extension + native messaging host for browser autofill
- [ ] Clipboard copy with auto-clear timeout instead of printing to console
