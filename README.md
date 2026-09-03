# pwman Chrome Extension + Native Messaging Host

Browser autofill for the pwman password manager. The Chrome extension talks
to a native Windows process (`pwman-host.exe`) over Chrome's **native
messaging** protocol -- the same general architecture real password managers
(1Password, Bitwarden) and other browser-integrated desktop apps use.

The extension never touches your vault file directly, and the master
password/derived key never leave the native host process. The extension
only ever receives what it explicitly asks for (a service name, or a
specific credential after a successful unlock).

## Architecture

```
Chrome extension (popup.js)
      |  chrome.runtime.connectNative()
      v
pwman-host.exe  (native messaging protocol over stdin/stdout)
      |
      v
PasswordManager.Core  (same Argon2id + AES-256-GCM vault used by the CLI/GUI)
      |
      v
%APPDATA%\pwman\vault.json
```

Because Chrome starts `pwman-host.exe` fresh each time the extension calls
`connectNative()`, and kills it when the popup closes (disconnecting the
port), **the vault re-locks every time you close the popup** -- the derived
key only exists in that process's memory for as long as the port is open.

## Setup

### 1. Build the native host
```bash
cd PasswordManager.NativeHost
dotnet publish -c Release -o ./publish
```
This produces `publish\pwman-host.exe`. Note its full absolute path -- you'll need it in step 3.

### 2. Load the extension in Chrome
1. Go to `chrome://extensions`
2. Enable **Developer mode** (top right)
3. Click **Load unpacked**, select the `chrome-extension` folder
4. Copy the **extension ID** shown on the card (a long lowercase string)

### 3. Fill in the native messaging host manifest
Open `native-messaging-host-manifest.json` and replace both placeholders:
- `path` → the absolute path to `pwman-host.exe` from step 1
  (e.g. `C:\\Users\\you\\PasswordManagerApp\\PasswordManager.NativeHost\\publish\\pwman-host.exe`
  -- note the double backslashes, required in JSON)
- `allowed_origins` → your extension ID from step 2, as
  `chrome-extension://YOUR_EXTENSION_ID/`

### 4. Register the host with Chrome
```powershell
.\register-native-host.ps1 -ManifestPath "C:\full\path\to\native-messaging-host-manifest.json"
```

### 5. Test it
1. Make sure you already have a vault created (via the CLI's `pwman init` or the desktop GUI)
2. Click the pwman extension icon in Chrome's toolbar
3. Enter your master password, click **Unlock**
4. You should see your saved services listed
5. On a page with a login form, click **Fill on page** next to an entry

## Troubleshooting

**"Couldn't reach the pwman native host"** in the popup usually means one of:
- The `path` in the host manifest doesn't point to the actual `.exe` location
- The extension ID in `allowed_origins` doesn't match the one in `chrome://extensions`
  (this changes if you remove and re-add the unpacked extension -- update the
  manifest and re-run the registration script if that happens)
- The registry key wasn't created — re-run `register-native-host.ps1`

**"No vault found"** — create one first with `pwman init` (CLI) or by opening the desktop GUI.

**Fill doesn't work on a page** — the field-detection logic looks for the
first `input[type="password"]` and the nearest preceding text/email input.
Some sites (heavily custom login forms, multi-step logins) won't match this
pattern. Falling back to **Copy password** and pasting manually always works.

## Files
```
PasswordManager.NativeHost/
├── Program.cs                          # message dispatch: unlock/list/getPassword
├── NativeMessaging.cs                   # Chrome's native messaging wire protocol
└── PasswordManager.NativeHost.csproj    # references PasswordManager.Core

chrome-extension/
├── manifest.json                        # MV3 manifest
├── popup.html / popup.js                # unlock UI, entry list, fill/copy actions
└── icons/                               # extension icons

native-messaging-host-manifest.json      # tells Chrome how to launch the host (fill in paths)
register-native-host.ps1                 # registers the above in the Windows registry
```
