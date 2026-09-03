# pwman — Local Encrypted Password Manager

A password manager built in C#/.NET with **three frontends sharing one core
encryption library**: a command-line interface, a WPF desktop GUI, and a
Chrome extension with browser autofill via native messaging. All three read
and write the same encrypted vault file.

## Architecture

```
PasswordManager.Core/         Shared library: crypto, storage, password generation
PasswordManager/               CLI frontend (console commands)
PasswordManager.Gui/           WPF desktop GUI frontend
PasswordManager.NativeHost/    Native messaging host — bridges the Chrome extension to Core
chrome-extension/              Chrome extension (popup UI, autofill)
```

Splitting the crypto/storage logic into a shared library (rather than
duplicating it across frontends) means every interface behaves identically
and stays in sync — a fix or improvement in `Core` automatically applies
everywhere.

## Features
- Create a vault protected by a single master password
- Add, view, list, and delete saved credentials
- Auto-generate strong passwords, or type your own (with a strength check
  and warning before saving anything weak)
- Copy a password directly to the clipboard without displaying it (GUI, extension)
- **Browser autofill**: the Chrome extension detects login forms and fills
  them directly from your vault, or copies a password to the clipboard —
  same encrypted vault as the CLI/GUI, no separate storage
- Same encrypted vault file usable from the CLI, the GUI, or the browser extension

## Screenshots

| Login / unlock | Vault view |
|---|---|
| ![Login screen](screenshots/login-screen.png) | ![Vault screen](screenshots/vault-screen.png) |

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
  you click "Lock" instead of just letting it fall out of scope. The Chrome
  extension takes this further structurally: Chrome starts the native host
  process fresh each time the extension connects, and kills it when the
  popup closes — so the derived key only exists in memory for as long as
  the popup is open, and the vault re-locks automatically every time it's closed.
- **Browser extension isolation**: the extension's JavaScript never touches
  the vault file or the encryption key directly. It only ever receives
  exactly what it explicitly requested (a list of service names, or one
  specific credential after a successful unlock) over Chrome's native
  messaging protocol, the same architecture pattern used by 1Password's and
  Bitwarden's browser extensions.

## Requirements
- [.NET 8 SDK](https://dotnet.microsoft.com/download) or later
- Windows (WPF and the native messaging host are Windows-only; the CLI is cross-platform)
- Google Chrome (for the browser extension)

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

**Chrome extension + native host:** see [chrome-extension setup](#chrome-extension-setup) below.

All three point at the same vault file:
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

## Chrome extension setup

1. **Build the native host:**
   ```bash
   cd PasswordManager.NativeHost
   dotnet publish -c Release -o ./publish
   ```
2. **Load the extension:** go to `chrome://extensions`, enable Developer
   mode, click **Load unpacked**, select the `chrome-extension` folder.
   Copy the extension ID shown on its card.
3. **Configure the host manifest:** copy
   `native-messaging-host-manifest.example.json` to
   `native-messaging-host-manifest.json`, and fill in the absolute path to
   `pwman-host.exe` from step 1 and your extension ID from step 2.
4. **Register it:**
   ```powershell
   .\register-native-host.ps1 -ManifestPath "C:\full\path\to\native-messaging-host-manifest.json"
   ```
5. Click the extension icon, unlock with your master password, and use
   **Fill on page** or **Copy password** on any saved entry.

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

PasswordManager.NativeHost/  # Native messaging host
├── Program.cs                 # message dispatch: unlock/list/getPassword
└── NativeMessaging.cs         # Chrome's native messaging wire protocol

chrome-extension/            # Chrome extension (Manifest V3)
├── manifest.json
├── popup.html / popup.js      # unlock UI, entry list, fill/copy actions
└── icons/
```

## Roadmap
- [x] CLI core with Argon2id + AES-256-GCM
- [x] Add/get/list/delete commands
- [x] Password generator + strength checking
- [x] Shared Core library refactor
- [x] WPF desktop GUI
- [x] Chrome extension + native messaging host
- [ ] Clipboard auto-clear after a timeout (currently persists until overwritten)
- [ ] Have I Been Pwned–style breach check for saved service names
