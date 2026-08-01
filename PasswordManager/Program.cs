using PasswordManager;

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

    Console.WriteLine("Creating a new vault. Choose a strong master password --");
    Console.WriteLine("this is the ONE password you'll need to remember. There is");
    Console.WriteLine("no recovery if you forget it, since it's never stored anywhere.");
    Console.WriteLine();

    var password = ReadPasswordMasked("Master password: ");
    var confirm = ReadPasswordMasked("Confirm master password: ");

    if (password != confirm)
    {
        Console.WriteLine("Passwords didn't match. Try again.");
        return;
    }

    if (password.Length < 8)
    {
        Console.WriteLine("Master password should be at least 8 characters. Try again.");
        return;
    }

    storage.Initialize(password);
    Console.WriteLine($"Vault created at {vaultPath}");
}

void HandleAdd(string service, string username)
{
    RequireVaultExists();
    var masterPassword = ReadPasswordMasked("Master password: ");
    var key = storage.Unlock(masterPassword);

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
    var masterPassword = ReadPasswordMasked("Master password: ");
    var key = storage.Unlock(masterPassword);

    var result = storage.GetEntry(key, service);
    if (result is null)
    {
        Console.WriteLine($"No entry found for '{service}'.");
        return;
    }

    Console.WriteLine($"Service:  {service}");
    Console.WriteLine($"Username: {result.Value.Username}");
    Console.WriteLine($"Password: {result.Value.Password}");
}

void HandleList()
{
    RequireVaultExists();
    var services = storage.ListServices();

    if (services.Count == 0)
    {
        Console.WriteLine("No entries saved yet.");
        return;
    }

    Console.WriteLine("Saved services:");
    foreach (var service in services)
    {
        Console.WriteLine($"  - {service}");
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
    Console.WriteLine("  pwman init                        Create a new vault");
    Console.WriteLine("  pwman add <service> <username>     Add or update an entry (prompts for password)");
    Console.WriteLine("  pwman get <service>                 Retrieve an entry");
    Console.WriteLine("  pwman list                          List all saved service names");
    Console.WriteLine("  pwman delete <service>              Delete an entry");
    Console.WriteLine("  pwman generate [length]              Print a strong random password (default 20 chars)");
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
