# pwman — Local Encrypted Password Manager

A password manager built in C#/.NET, with **two frontends sharing one core
encryption library**: a command-line interface and a WPF desktop GUI. Both
read and write the same encrypted vault file, so entries added in one show
up in the other.

## Architecture

```
PasswordManager.Core/    Shared library: crypto, storage, password generation
PasswordManager/          CLI frontend (console commands)
PasswordManager.Gui/      WPF desktop GUI frontend
```

Splitting the crypto/storage logic into a shared library (rather than
duplicating it in each frontend) means both interfaces are guaranteed to
behave identically and stay in sync — a bug fix or security improvement in
`Core` automatically applies to both the CLI and the GUI.

## Screenshots

| Login / unlock | Vault view |
|---|---|
| ![Login screen](screenshots/login-screen.png) | ![Vault screen](screenshots/vault-screen.png) |

## Features
- Create a vault protected by a single master password
- Add, view, list, and delete saved credentials
- Auto-generate strong passwords, or type your own (with a strength check
  and warning before saving anything weak)
- Copy a password directly to the clipboard without displaying it (GUI)
- Same encrypted vault file usable from either the CLI or the GUI

## Security design

- **Master password → key derivation**: your master password is never stored
  anywhere. It's run through **Argon2id** (memory-hard KDF, the current
  OWASP-recommended choice over PBKDF2/bcrypt) along with a random salt to
  derive a 256-bit encryption key. Each vault has its own random salt.
- **Password verification without storing the password**: on vault creation,
  a known fixed string is encrypted with the derived key and stored. On
  future unlocks, if decrypting that value with the entered password
  succeeds and matches, the password was correct — the master password
  itself is never written to disk.
- **Entry encryption**: each saved password is encrypted individually with
  **AES-256-GCM**, an authenticated encryption mode that detects tampering
  via an auth tag, unlike plain AES-CBC.
- **Password generation**: uses `RandomNumberGenerator` (cryptographically
  secure), not `System.Random`, and guarantees at least one lowercase,
  uppercase, digit, and symbol character.
- **Key hygiene**: the GUI explicitly zeroes the derived key in memory when
  you click "Lock" instead of just letting it fall out of scope.

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- Windows (the GUI uses WPF, which is Windows-only; the CLI is cross-platform)

## Build & run

**CLI:**
```bash
cd PasswordManager
dotnet run -- <command> [args]
```

**GUI:**
```bash
cd PasswordManager.Gui
dotnet run
```

Both point at the same vault file:
- Windows: `%APPDATA%\pwman\vault.json`
- macOS/Linux (CLI only): `~/.config/pwman/vault.json`

## CLI usage

```bash
pwman init                          # create a new vault
pwman add gmail myemail@gmail.com   # add or update a credential
pwman get gmail                     # retrieve a credential
pwman list                          # list saved service names
pwman delete gmail                  # delete a credential
pwman generate [length]             # print a standalone strong password
```

## GUI usage

Run the app, create or unlock your vault, then use **Add New**, **View**,
**Copy Password**, and **Delete** on the entries list. **Lock** clears the
encryption key from memory and returns to the login screen.

## Project structure
```
PasswordManager.Core/
├── CryptoService.cs        # Argon2id key derivation + AES-256-GCM encrypt/decrypt
├── PasswordGenerator.cs    # cryptographically secure password generation + strength check
├── VaultStorage.cs         # vault.json read/write, higher-level vault operations
└── Models/
    ├── Vault.cs             # top-level vault file schema
    └── VaultEntry.cs        # single credential entry schema

PasswordManager/             # CLI
└── Program.cs                # argument parsing, prompts

PasswordManager.Gui/         # WPF GUI
├── MainWindow.xaml           # UI layout
└── MainWindow.xaml.cs        # event handlers, wired to PasswordManager.Core
```

## Roadmap
- [x] CLI core with Argon2id + AES-256-GCM
- [x] Add/get/list/delete commands
- [x] Password generator + strength checking
- [x] Shared Core library refactor
- [x] WPF desktop GUI
- [ ] Chrome extension + native messaging host for browser autofill
- [ ] Clipboard auto-clear after a timeout (currently persists until overwritten)
