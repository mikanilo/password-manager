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
- Create a vault unlocked by a short **PIN** (4+ digits), bound to your
  Windows account so the PIN alone is useless to anyone who copies the file
- Add, view, list, and delete saved credentials
- Every credential records when it was **added** and last **updated**; all
  three frontends show these dates in the same readable form
  ("Sept 1st 2026, 11:06 am"), and updating a credential keeps its original
  "added" date
- Auto-generate strong passwords, or type your own (with a strength check
  and warning before saving anything weak)
- Copy a password directly to the clipboard without displaying it (GUI, extension)
- **Browser autofill**: the Chrome extension detects login forms and fills
  them directly from your vault, or copies a password to the clipboard —
  same encrypted vault as the CLI/GUI, no separate storage
- **Save accounts from the browser**: on a site with no saved entry, the
  extension pre-fills a new-account form with the current domain and offers
  to save any username/password you've already typed into the page's login
  form (or generate a strong one)
- Same encrypted vault file usable from the CLI, the GUI, or the browser extension

## Screenshots

| Login / unlock | Vault view |
|---|---|
| ![Login screen](screenshots/login-screen.png) | ![Vault screen](screenshots/vault-screen.png) |

## Security design

- **PIN → key derivation, bound to the device**: a PIN on its own is
  low-entropy — a 4-digit PIN is only 10,000 possibilities, so anyone who
  copied `vault.json` onto their own machine could brute-force it offline no
  matter how slow the KDF is. pwman closes that hole by requiring *two*
  independent inputs to derive the key:
  1. the **PIN**, stretched with **Argon2id** (memory-hard KDF, the current
     OWASP-recommended choice over PBKDF2/bcrypt) and a random per-vault salt; and
  2. a random 32-byte **device secret**, stored in the vault encrypted with
     **Windows DPAPI** (`DataProtectionScope.CurrentUser`), so only the
     Windows account that created the vault can recover it.

  Both halves are combined with HKDF-SHA256 to produce the 256-bit AES key.
  A stolen `vault.json` is therefore not brute-forceable: guessing the PIN
  gains an attacker nothing, because the other half of the key material is
  held by the OS and is not in the file in any recoverable form. This is the
  same idea as a Windows Hello PIN — short, but only meaningful on the
  device it was set up on.

  The trade-off is deliberate: the vault will **not** open under a different
  Windows account, on another PC, or after a Windows reinstall. Treat it as
  device-local storage, not something you can copy between machines.
- **PIN verification without storing the PIN**: on vault creation, a known
  fixed string is encrypted with the derived key and stored. On future
  unlocks, if decrypting that value with the entered PIN succeeds and
  matches, the PIN was correct — the PIN itself is never written to disk.
- **Backwards compatible**: vaults created before PIN support record no auth
  mode, still unlock with their original master password, and can be
  converted in place with `pwman migrate-to-pin` (which re-encrypts every
  entry under the new PIN, preserving its added/updated dates).
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
pwman change-pin                    # change the vault PIN
pwman migrate-to-pin                # convert an old master-password vault to a PIN
```

## GUI usage

Run the app, create or unlock your vault with your PIN, then use **Add New**, **View**,
**Copy Password**, and **Delete** on the entries list. **Lock** clears the
encryption key from memory and returns to the login screen.

**Change PIN** opens a dialog to set a new PIN, re-encrypting every entry
under it while keeping the saved dates. On a vault that still uses a master
password the same button reads **Set a PIN** and performs the one-time
migration (the equivalent of `pwman migrate-to-pin`), after which the master
password no longer opens the vault.

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
5. Click the extension icon, unlock with your PIN, and use
   **Fill on page** or **Copy password** on any saved entry — or use the
   **Add an account for this site** form at the top to save a new one.

## Project structure
```
PasswordManager.Core/
├── CryptoService.cs        # Argon2id + HKDF key derivation, AES-256-GCM encrypt/decrypt
├── DeviceKeyProtector.cs   # DPAPI device binding, so a copied vault can't be brute-forced
├── PinPolicy.cs            # shared PIN rules (4+ digits), enforced by all three frontends
├── TimestampFormat.cs      # one date format for all frontends ("Sept 1st 2026, 11:06 am")
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
├── Program.cs                 # message dispatch: unlock/list/getPassword/addEntry/generate
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
- [x] Per-credential added / updated timestamps
- [x] Create credentials directly from the Chrome extension
- [x] PIN unlock with Windows DPAPI device binding (replaces the master password)
- [x] Change / set the PIN from the desktop GUI
- [ ] Clipboard auto-clear after a timeout (currently persists until overwritten)
- [ ] Have I Been Pwned–style breach check for saved service names
