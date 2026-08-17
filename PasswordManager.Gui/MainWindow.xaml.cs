using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using PasswordManager.Core;

namespace PasswordManager.Gui;

public partial class MainWindow : Window
{
    private readonly VaultStorage _storage;
    private byte[]? _currentKey;
    private bool _isCreatingVault;

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

        if (_isCreatingVault)
        {
            AuthModeLabel.Text = "Create a master password for your new vault";
            AuthActionButton.Content = "Create Vault";
            ConfirmPasswordBox.Visibility = Visibility.Visible;
            ConfirmLabel.Visibility = Visibility.Visible;
        }
        else
        {
            AuthModeLabel.Text = "Enter your master password";
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
            if (password.Length < 8)
            {
                AuthStatusText.Text = "Master password should be at least 8 characters.";
                return;
            }

            if (password != ConfirmPasswordBox.Password)
            {
                AuthStatusText.Text = "Passwords didn't match.";
                return;
            }

            try
            {
                _storage.Initialize(password);
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
            catch (UnauthorizedAccessException)
            {
                AuthStatusText.Text = "Incorrect master password.";
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

        RefreshServiceList();
    }

    private void RefreshServiceList()
    {
        ServiceListBox.ItemsSource = null;
        ServiceListBox.ItemsSource = _storage.ListServices();
    }

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
        AuthPanel.Visibility = Visibility.Visible;

        SetupAuthPanel();
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
        var selected = ServiceListBox.SelectedItem as string;
        if (selected == null)
        {
            MessageBox.Show("Select a service from the list first.", "Nothing selected",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        return selected;
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
            $"Service:  {service}\nUsername: {entry.Value.Username}\nPassword: {entry.Value.Password}",
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

        Clipboard.SetText(entry.Value.Password);
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
