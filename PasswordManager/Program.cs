using PasswordManager.Core;
using PasswordManager.Core.Models;

var vaultPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "pwman", "vault.json");

Directory.CreateDirectory(Path.GetDirectoryName(vaultPath)!);
var storage = new VaultStorage(vaultPath);

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0].ToLowerInvariant();

try
{
    switch (command)
    {
        case "init":
            HandleInit();
            break;

        case "add":
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: pwman add <service> <username>");
                return 1;
            }
            HandleAdd(args[1], args[2]);
            break;

        case "get":
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: pwman get <service>");
                return 1;
            }
            HandleGet(args[1]);
            break;

        case "list":
            HandleList();
            break;

        case "delete":
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: pwman delete <service>");
                return 1;
            }
            HandleDelete(args[1]);
            break;

        case "change-pin":
            HandleChangePin();
            break;

        case "migrate-to-pin":
            HandleMigrateToPin();
            break;

        case "generate":
            var length = args.Length >= 2 && int.TryParse(args[1], out var parsedLength)
                ? parsedLength
                : 20;
            HandleGenerate(length);
            break;

        default:
            PrintUsage();
            return 1;
    }
}
catch (UnauthorizedAccessException ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine($"Unexpected error: {ex.Message}");
    return 1;
}

return 0;

void HandleInit()
{
    if (storage.VaultExists())
    {
        Console.WriteLine($"A vault already exists at {vaultPath}.");
        return;
    }

    Console.WriteLine("Creating a new vault. Choose a PIN -- at least 4 digits.");
    Console.WriteLine();
    Console.WriteLine("Your PIN alone is not enough to open this vault: it is combined");
    Console.WriteLine("with a secret that Windows releases only to your user account on");
    Console.WriteLine("this machine. That means a copied vault.json cannot be attacked");
    Console.WriteLine("offline -- but it also means the vault will NOT open under a");
    Console.WriteLine("different Windows account, on another PC, or after a Windows");
    Console.WriteLine("reinstall. There is no recovery if you forget the PIN.");
    Console.WriteLine();

    var pin = ReadPasswordMasked("New PIN: ");
    var confirm = ReadPasswordMasked("Confirm PIN: ");

    if (pin != confirm)
    {
        Console.WriteLine("PINs didn't match. Try again.");
        return;
    }

    var validationError = PinPolicy.Validate(pin);
    if (validationError is not null)
    {
        Console.WriteLine($"{validationError} Try again.");
        return;
    }

    storage.InitializeWithPin(pin);
    Console.WriteLine($"Vault created at {vaultPath}");
}

void HandleChangePin()
{
    RequireVaultExists();

    if (storage.GetAuthMode() != VaultAuthMode.Pin)
    {
        Console.WriteLine("This vault still uses a master password. Run 'pwman migrate-to-pin' first.");
        return;
    }

    var currentPin = ReadPasswordMasked("Current PIN: ");
    var newPin = ReadPasswordMasked("New PIN: ");
    var confirm = ReadPasswordMasked("Confirm new PIN: ");

    if (newPin != confirm)
    {
        Console.WriteLine("PINs didn't match. Nothing changed.");
        return;
    }

    var validationError = PinPolicy.Validate(newPin);
    if (validationError is not null)
    {
        Console.WriteLine($"{validationError} Nothing changed.");
        return;
    }

    storage.ChangePin(currentPin, newPin);
    Console.WriteLine("PIN changed. Every entry was re-encrypted under the new PIN.");
}

void HandleMigrateToPin()
{
    RequireVaultExists();

    if (storage.GetAuthMode() == VaultAuthMode.Pin)
    {
        Console.WriteLine("This vault already uses a PIN. Use 'pwman change-pin' to change it.");
        return;
    }

    Console.WriteLine("Converting this vault from a master password to a PIN.");
    Console.WriteLine("After this, the master password will no longer open it.");
    Console.WriteLine();

    var masterPassword = ReadPasswordMasked("Current master password: ");
    var newPin = ReadPasswordMasked("New PIN: ");
    var confirm = ReadPasswordMasked("Confirm new PIN: ");

    if (newPin != confirm)
    {
        Console.WriteLine("PINs didn't match. Nothing changed.");
        return;
    }

    var validationError = PinPolicy.Validate(newPin);
    if (validationError is not null)
    {
        Console.WriteLine($"{validationError} Nothing changed.");
        return;
    }

    storage.MigrateToPin(masterPassword, newPin);
    Console.WriteLine("Vault migrated to PIN unlock. Every entry was re-encrypted.");
}

/// <summary>
/// Prompts for whichever secret this vault actually uses and returns the
/// derived key. Vaults created before PIN support still expect a master
/// password, so the prompt has to follow the file rather than assume.
/// </summary>
byte[] UnlockInteractive()
{
    var prompt = storage.GetAuthMode() == VaultAuthMode.Pin ? "PIN: " : "Master password: ";
    return storage.Unlock(ReadPasswordMasked(prompt));
}

void HandleAdd(string service, string username)
{
    RequireVaultExists();
    var key = UnlockInteractive();

    Console.Write($"Generate a strong password for {service}? (y/n): ");
    var choice = Console.ReadLine()?.Trim().ToLowerInvariant();

    string entryPassword;
    if (choice == "y" || choice == "yes")
    {
        entryPassword = PasswordGenerator.Generate();
        Console.WriteLine($"Generated password: {entryPassword}");
        Console.WriteLine("(shown once here -- retrieve it later with 'pwman get')");
    }
    else
    {
        entryPassword = ReadPasswordMasked($"Password for {service}: ");
        var (strength, feedback) = PasswordGenerator.EvaluateStrength(entryPassword);

        if (strength != PasswordStrength.Strong)
        {
            Console.WriteLine($"Password strength: {strength}. {feedback}");
            Console.Write("Save it anyway? (y/n): ");
            var confirmWeak = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (confirmWeak != "y" && confirmWeak != "yes")
            {
                Console.WriteLine("Cancelled. Nothing was saved.");
                return;
            }
        }
    }

    storage.AddEntry(key, service, username, entryPassword);
    Console.WriteLine($"Saved credentials for '{service}'.");
}

void HandleGenerate(int length)
{
    try
    {
        var password = PasswordGenerator.Generate(length);
        Console.WriteLine(password);
    }
    catch (ArgumentException ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
    }
}

void HandleGet(string service)
{
    RequireVaultExists();
    var key = UnlockInteractive();

    var result = storage.GetEntry(key, service);
    if (result is null)
    {
        Console.WriteLine($"No entry found for '{service}'.");
        return;
    }

    Console.WriteLine($"Service:  {service}");
    Console.WriteLine($"Username: {result.Username}");
    Console.WriteLine($"Password: {result.Password}");
    Console.WriteLine($"Added:    {TimestampFormat.Format(result.CreatedUtc)}");
    Console.WriteLine($"Updated:  {TimestampFormat.Format(result.UpdatedUtc)}");
}

void HandleList()
{
    RequireVaultExists();
    var entries = storage.ListEntries();

    if (entries.Count == 0)
    {
        Console.WriteLine("No entries saved yet.");
        return;
    }

    Console.WriteLine("Saved services:");
    var width = entries.Max(e => e.Service.Length);
    foreach (var entry in entries)
    {
        Console.WriteLine($"  - {entry.Service.PadRight(width)}   added {TimestampFormat.Format(entry.CreatedUtc)}");
    }
}

void HandleDelete(string service)
{
    RequireVaultExists();
    var deleted = storage.DeleteEntry(service);
    Console.WriteLine(deleted
        ? $"Deleted entry for '{service}'."
        : $"No entry found for '{service}'.");
}

void RequireVaultExists()
{
    if (!storage.VaultExists())
    {
        throw new InvalidOperationException("No vault found. Run 'pwman init' first.");
    }
}

void PrintUsage()
{
    Console.WriteLine("pwman - a local encrypted password manager");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  pwman init                        Create a new vault (PIN protected)");
    Console.WriteLine("  pwman add <service> <username>     Add or update an entry (prompts for password)");
    Console.WriteLine("  pwman get <service>                 Retrieve an entry");
    Console.WriteLine("  pwman list                          List all saved service names");
    Console.WriteLine("  pwman delete <service>              Delete an entry");
    Console.WriteLine("  pwman generate [length]              Print a strong random password (default 20 chars)");
    Console.WriteLine("  pwman change-pin                    Change the vault PIN");
    Console.WriteLine("  pwman migrate-to-pin                Convert an old master-password vault to a PIN");
}

/// <summary>
/// Reads a line of input from the console without echoing it to the
/// screen, so passwords typed at the prompt aren't visible or left in
/// terminal scrollback / screen-share recordings.
/// </summary>
string ReadPasswordMasked(string prompt)
{
    Console.Write(prompt);
    var password = string.Empty;

    ConsoleKeyInfo key;
    do
    {
        key = Console.ReadKey(intercept: true);

        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password = password[..^1];
            Console.Write("\b \b");
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password += key.KeyChar;
            Console.Write("*");
        }
    } while (key.Key != ConsoleKey.Enter);

    Console.WriteLine();
    return password;
}
