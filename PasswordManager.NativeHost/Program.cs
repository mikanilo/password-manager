using System.Text.Json;
using PasswordManager.Core;
using PasswordManager.Core.Models;

namespace PasswordManager.NativeHost;

public static class Program
{
    public static async Task Main()
    {
        var vaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "pwman", "vault.json");

        var storage = new VaultStorage(vaultPath);

        // The derived encryption key lives only in this process's memory,
        // and only for as long as this process is alive. Chrome starts this
        // process fresh each time the extension calls connectNative(), and
        // kills it when the port disconnects (e.g. the popup closes) -- so
        // in practice, the vault re-locks every time the popup is closed,
        // same security property as the CLI/GUI locking on exit.
        byte[]? currentKey = null;

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        while (true)
        {
            JsonDocument? message;
            try
            {
                message = await NativeMessaging.ReadMessageAsync(stdin);
            }
            catch (Exception)
            {
                // Malformed input -- nothing safe to do but exit.
                break;
            }

            if (message is null)
            {
                break; // Chrome disconnected the port; shut down cleanly.
            }

            using (message)
            {
                var response = HandleMessage(message.RootElement, storage, ref currentKey);
                await NativeMessaging.WriteMessageAsync(stdout, response);
            }
        }

        // Zero the key from memory on the way out, same hygiene as the GUI's Lock button.
        if (currentKey != null)
        {
            Array.Clear(currentKey, 0, currentKey.Length);
        }
    }

    private static object HandleMessage(JsonElement request, VaultStorage storage, ref byte[]? currentKey)
    {
        var action = request.TryGetProperty("action", out var actionProp) ? actionProp.GetString() : null;

        try
        {
            switch (action)
            {
                case "ping":
                    return new { success = true, message = "pwman native host is running" };

                case "vaultExists":
                {
                    var exists = storage.VaultExists();

                    // The popup labels its input from this, so an un-migrated
                    // master-password vault still prompts for the right thing.
                    var authMode = exists
                        ? storage.GetAuthMode().ToString()
                        : VaultAuthMode.Pin.ToString();

                    return new { success = true, exists, authMode };
                }

                case "unlock":
                {
                    if (!storage.VaultExists())
                    {
                        return new
                        {
                            success = false,
                            error = "No vault found. Create one first using the pwman CLI or desktop app."
                        };
                    }

                    // "pin" is what the popup sends now; "password" is still
                    // accepted so an older popup build keeps working.
                    var secret =
                        (request.TryGetProperty("pin", out var pinProp) ? pinProp.GetString() : null)
                        ?? (request.TryGetProperty("password", out var pwProp) ? pwProp.GetString() : null)
                        ?? string.Empty;

                    currentKey = storage.Unlock(secret);
                    return new { success = true, authMode = storage.GetAuthMode().ToString() };
                }

                case "list":
                {
                    if (currentKey is null)
                    {
                        return new { success = false, error = "Vault is locked." };
                    }

                    var entries = storage.ListEntries()
                        .Select(e => new
                        {
                            service = e.Service,
                            username = e.Username,
                            createdUtc = e.CreatedUtc?.ToString("o"),
                            updatedUtc = e.UpdatedUtc?.ToString("o"),
                        })
                        .ToList();

                    // "services" kept for backwards compatibility with older popups.
                    return new { success = true, entries, services = entries.Select(e => e.service).ToList() };
                }

                case "getPassword":
                {
                    if (currentKey is null)
                    {
                        return new { success = false, error = "Vault is locked." };
                    }

                    var service = request.GetProperty("service").GetString() ?? string.Empty;
                    var entry = storage.GetEntry(currentKey, service);

                    if (entry is null)
                    {
                        return new { success = false, error = $"No entry found for '{service}'." };
                    }

                    return new
                    {
                        success = true,
                        username = entry.Username,
                        password = entry.Password,
                        createdUtc = entry.CreatedUtc?.ToString("o"),
                        updatedUtc = entry.UpdatedUtc?.ToString("o"),
                    };
                }

                case "addEntry":
                {
                    if (currentKey is null)
                    {
                        return new { success = false, error = "Vault is locked." };
                    }

                    var service = request.GetProperty("service").GetString()?.Trim() ?? string.Empty;
                    var username = request.TryGetProperty("username", out var u) ? u.GetString()?.Trim() ?? string.Empty : string.Empty;
                    var password = request.GetProperty("password").GetString() ?? string.Empty;

                    if (string.IsNullOrEmpty(service) || string.IsNullOrEmpty(password))
                    {
                        return new { success = false, error = "A service name and a password are both required." };
                    }

                    var existed = storage.ListServices()
                        .Any(s => string.Equals(s, service, StringComparison.OrdinalIgnoreCase));

                    storage.AddEntry(currentKey, service, username, password);

                    var (strength, feedback) = PasswordGenerator.EvaluateStrength(password);
                    return new
                    {
                        success = true,
                        replaced = existed,
                        strength = strength.ToString(),
                        feedback,
                    };
                }

                case "generate":
                {
                    var length = request.TryGetProperty("length", out var l) && l.TryGetInt32(out var n) ? n : 20;
                    return new { success = true, password = PasswordGenerator.Generate(length) };
                }

                default:
                    return new { success = false, error = $"Unknown action: {action}" };
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            return new { success = false, error = ex.Message };
        }
        catch (DeviceBindingException ex)
        {
            return new { success = false, error = ex.Message };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Unexpected error: {ex.Message}" };
        }
    }
}
