using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PasswordManager.Core;
using PasswordManager.Core.Models;

namespace PasswordManager.Gui;

public partial class MainWindow : Window
{
    private readonly VaultStorage _storage;
    private byte[]? _currentKey;
    private bool _isCreatingVault;

    // A vault written before PIN support still expects its master password.
    // The auth panel follows the file rather than assuming, so an existing
    // user isn't locked out by the change.
    private bool _isLegacyMasterPasswordVault;

    public MainWindow()
    {
        InitializeComponent();

        var vaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "pwman", "vault.json");
        Directory.CreateDirectory(Path.GetDirectoryName(vaultPath)!);

        _storage = new VaultStorage(vaultPath);

        SetupAuthPanel();
    }

    private void SetupAuthPanel()
    {
        _isCreatingVault = !_storage.VaultExists();

        _isLegacyMasterPasswordVault =
            !_isCreatingVault && _storage.GetAuthMode() != VaultAuthMode.Pin;

        if (_isCreatingVault)
        {
            AuthModeLabel.Text = "Choose a PIN for your new vault (at least 4 digits)";
            AuthActionButton.Content = "Create Vault";
            ConfirmPasswordBox.Visibility = Visibility.Visible;
            ConfirmLabel.Visibility = Visibility.Visible;
        }
        else
        {
            AuthModeLabel.Text = _isLegacyMasterPasswordVault
                ? "Enter your master password"
                : "Enter your PIN";
            AuthActionButton.Content = "Unlock";
            ConfirmPasswordBox.Visibility = Visibility.Collapsed;
            ConfirmLabel.Visibility = Visibility.Collapsed;
        }
    }

    private void MasterPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AuthActionButton_Click(sender, e);
        }
    }

    private void ConfirmPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        // No live validation needed here -- checked on submit.
    }

    private void AuthActionButton_Click(object sender, RoutedEventArgs e)
    {
        AuthStatusText.Text = string.Empty;
        var password = MasterPasswordBox.Password;

        if (_isCreatingVault)
        {
            var validationError = PinPolicy.Validate(password);
            if (validationError is not null)
            {
                AuthStatusText.Text = validationError;
                return;
            }

            if (password != ConfirmPasswordBox.Password)
            {
                AuthStatusText.Text = "PINs didn't match.";
                return;
            }

            try
            {
                _storage.InitializeWithPin(password);
                _currentKey = _storage.Unlock(password);
                ShowVaultPanel();
            }
            catch (Exception ex)
            {
                AuthStatusText.Text = $"Couldn't create vault: {ex.Message}";
            }
        }
        else
        {
            try
            {
                _currentKey = _storage.Unlock(password);
                ShowVaultPanel();
            }
            catch (UnauthorizedAccessException ex)
            {
                AuthStatusText.Text = ex.Message;
            }
            catch (DeviceBindingException ex)
            {
                // Retyping the PIN will never help here, so say what actually
                // went wrong instead of a generic "incorrect" message.
                AuthStatusText.Text = ex.Message;
            }
            catch (Exception ex)
            {
                AuthStatusText.Text = $"Error: {ex.Message}";
            }
        }
    }

    private void ShowVaultPanel()
    {
        MasterPasswordBox.Password = string.Empty;
        ConfirmPasswordBox.Password = string.Empty;

        AuthPanel.Visibility = Visibility.Collapsed;
        VaultPanel.Visibility = Visibility.Visible;

        // A vault that still uses a master password has no PIN to change yet,
        // so the button offers to set one instead.
        ChangePinButton.Content = _isLegacyMasterPasswordVault ? "Set a PIN" : "Change PIN";

        RefreshServiceList();
    }

    private void RefreshServiceList()
    {
        ServiceListBox.DisplayMemberPath = nameof(ServiceListItem.Display);
        ServiceListBox.ItemsSource = null;
        ServiceListBox.ItemsSource = _storage.ListEntries()
            .Select(e => new ServiceListItem(
                e.Service,
                $"{e.Service}    ·  added {TimestampFormat.Format(e.CreatedUtc)}"))
            .ToList();
    }

    /// <summary>
    /// One row in the vault list: the raw service name (used by the action
    /// buttons) plus a human-readable label showing when it was added.
    /// </summary>
    private sealed record ServiceListItem(string Service, string Display);

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        // Discard the key from memory rather than just letting it fall out of scope.
        if (_currentKey != null)
        {
            Array.Clear(_currentKey, 0, _currentKey.Length);
            _currentKey = null;
        }

        VaultPanel.Visibility = Visibility.Collapsed;
        AddOverlay.Visibility = Visibility.Collapsed;
        ChangePinOverlay.Visibility = Visibility.Collapsed;
        AuthPanel.Visibility = Visibility.Visible;

        SetupAuthPanel();
    }


    // ===================== Change PIN =====================

    private void ChangePinButton_Click(object sender, RoutedEventArgs e)
    {
        CurrentSecretBox.Password = string.Empty;
        NewPinBox.Password = string.Empty;
        ConfirmPinBox.Password = string.Empty;
        ChangePinStatusText.Text = string.Empty;

        if (_isLegacyMasterPasswordVault)
        {
            ChangePinTitle.Text = "Set a PIN";
            ChangePinIntro.Text =
                "This vault still opens with a master password. Setting a PIN re-encrypts " +
                "every entry, and the master password will no longer open it. The PIN is " +
                "tied to this Windows account on this PC.";
            CurrentSecretLabel.Text = "Current master password";
            ConfirmChangePinButton.Content = "Set PIN";
        }
        else
        {
            ChangePinTitle.Text = "Change PIN";
            ChangePinIntro.Text =
                "Every entry is re-encrypted under the new PIN. Saved dates are kept.";
            CurrentSecretLabel.Text = "Current PIN";
            ConfirmChangePinButton.Content = "Change PIN";
        }

        ChangePinOverlay.Visibility = Visibility.Visible;
        CurrentSecretBox.Focus();
    }

    private void ConfirmPinBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveChangePinButton_Click(sender, e);
        }
    }

    private void CancelChangePinButton_Click(object sender, RoutedEventArgs e)
    {
        ClearChangePinFields();
        ChangePinOverlay.Visibility = Visibility.Collapsed;
    }

    private void SaveChangePinButton_Click(object sender, RoutedEventArgs e)
    {
        ChangePinStatusText.Text = string.Empty;

        var currentSecret = CurrentSecretBox.Password;
        var newPin = NewPinBox.Password;
        var wasLegacy = _isLegacyMasterPasswordVault;

        if (newPin != ConfirmPinBox.Password)
        {
            ChangePinStatusText.Text = "PINs didn't match.";
            return;
        }

        var validationError = PinPolicy.Validate(newPin);
        if (validationError is not null)
        {
            ChangePinStatusText.Text = validationError;
            return;
        }

        try
        {
            if (wasLegacy)
            {
                _storage.MigrateToPin(currentSecret, newPin);
            }
            else
            {
                _storage.ChangePin(currentSecret, newPin);
            }

            // Re-keying re-encrypted every entry, so the key this window is
            // holding is now stale -- View and Copy would fail with it. Swap
            // in a key derived from the new PIN before touching the vault again.
            if (_currentKey != null)
            {
                Array.Clear(_currentKey, 0, _currentKey.Length);
                _currentKey = null;
            }

            _currentKey = _storage.Unlock(newPin);
            _isLegacyMasterPasswordVault = false;
            ChangePinButton.Content = "Change PIN";
        }
        catch (UnauthorizedAccessException ex)
        {
            ChangePinStatusText.Text = ex.Message;
            return;
        }
        catch (DeviceBindingException ex)
        {
            ChangePinStatusText.Text = ex.Message;
            return;
        }
        catch (Exception ex)
        {
            ChangePinStatusText.Text = $"Error: {ex.Message}";
            return;
        }

        ClearChangePinFields();
        ChangePinOverlay.Visibility = Visibility.Collapsed;
        RefreshServiceList();

        MessageBox.Show(
            wasLegacy
                ? "PIN set. Your master password no longer opens this vault."
                : "PIN changed. Every entry was re-encrypted under the new PIN.",
            "Done", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ClearChangePinFields()
    {
        CurrentSecretBox.Password = string.Empty;
        NewPinBox.Password = string.Empty;
        ConfirmPinBox.Password = string.Empty;
        ChangePinStatusText.Text = string.Empty;
    }

    // ===================== Add entry =====================

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        NewServiceBox.Text = string.Empty;
        NewUsernameBox.Text = string.Empty;
        NewPasswordBox.Password = string.Empty;
        GeneratePasswordCheck.IsChecked = false;
        PasswordStrengthText.Text = string.Empty;
        NewPasswordBox.IsEnabled = true;

        AddOverlay.Visibility = Visibility.Visible;
    }

    private void GeneratePasswordCheck_Changed(object sender, RoutedEventArgs e)
    {
        var generate = GeneratePasswordCheck.IsChecked == true;
        NewPasswordBox.IsEnabled = !generate;
        NewPasswordLabel.Text = generate ? "Password (auto-generated on save)" : "Password";
        PasswordStrengthText.Text = string.Empty;

        if (generate)
        {
            NewPasswordBox.Password = string.Empty;
        }
    }

    private void CancelAddButton_Click(object sender, RoutedEventArgs e)
    {
        AddOverlay.Visibility = Visibility.Collapsed;
    }

    private void SaveAddButton_Click(object sender, RoutedEventArgs e)
    {
        var service = NewServiceBox.Text.Trim();
        var username = NewUsernameBox.Text.Trim();

        if (string.IsNullOrEmpty(service) || string.IsNullOrEmpty(username))
        {
            MessageBox.Show("Service and username are both required.", "Missing info",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string password;

        if (GeneratePasswordCheck.IsChecked == true)
        {
            password = PasswordGenerator.Generate();
        }
        else
        {
            password = NewPasswordBox.Password;

            if (string.IsNullOrEmpty(password))
            {
                MessageBox.Show("Enter a password, or check 'Generate a strong password for me'.",
                    "Missing password", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var (strength, feedback) = PasswordGenerator.EvaluateStrength(password);
            if (strength != PasswordStrength.Strong)
            {
                var result = MessageBox.Show(
                    $"This password is rated {strength}. {feedback}\n\nSave it anyway?",
                    "Weak password", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }
        }

        _storage.AddEntry(_currentKey!, service, username, password);
        AddOverlay.Visibility = Visibility.Collapsed;
        RefreshServiceList();

        if (GeneratePasswordCheck.IsChecked == true)
        {
            MessageBox.Show($"Saved. Generated password for {service}:\n\n{password}",
                "Password generated", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    // ===================== View / Copy / Delete =====================

    private string? GetSelectedService()
    {
        var selected = ServiceListBox.SelectedItem as ServiceListItem;
        if (selected == null)
        {
            MessageBox.Show("Select a service from the list first.", "Nothing selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        return selected?.Service;
    }

    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        var service = GetSelectedService();
        if (service == null) return;

        var entry = _storage.GetEntry(_currentKey!, service);
        if (entry == null)
        {
            MessageBox.Show("Couldn't find that entry.", "Not found",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(
            $"Service:  {service}\nUsername: {entry.Username}\nPassword: {entry.Password}\n\n" +
            $"Added:    {TimestampFormat.Format(entry.CreatedUtc)}\nUpdated:  {TimestampFormat.Format(entry.UpdatedUtc)}",
            "Credential", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CopyPasswordButton_Click(object sender, RoutedEventArgs e)
    {
        var service = GetSelectedService();
        if (service == null) return;

        var entry = _storage.GetEntry(_currentKey!, service);
        if (entry == null)
        {
            MessageBox.Show("Couldn't find that entry.", "Not found",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Clipboard.SetText(entry.Password);
        MessageBox.Show($"Password for '{service}' copied to clipboard.", "Copied",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var service = GetSelectedService();
        if (service == null) return;

        var confirm = MessageBox.Show($"Delete the saved credential for '{service}'?",
            "Confirm delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        _storage.DeleteEntry(service);
        RefreshServiceList();
    }
}
