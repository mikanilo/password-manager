using System.Text.Json;
using PasswordManager.Core;

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
                    return new { success = true, exists = storage.VaultExists() };

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

                    var password = request.GetProperty("password").GetString() ?? string.Empty;
                    currentKey = storage.Unlock(password);
                    return new { success = true };
                }

                case "list":
                {
                    if (currentKey is null)
                    {
                        return new { success = false, error = "Vault is locked." };
                    }

                    var services = storage.ListServices();
                    return new { success = true, services };
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

                    return new { success = true, username = entry.Value.Username, password = entry.Value.Password };
                }

                default:
                    return new { success = false, error = $"Unknown action: {action}" };
            }
        }
        catch (UnauthorizedAccessException)
        {
            return new { success = false, error = "Incorrect master password." };
        }
        catch (Exception ex)
        {
            return new { success = false, error = $"Unexpected error: {ex.Message}" };
        }
    }
}
