using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Sunshine.Interop;
using Sunshine.Models;
using Sunshine.Services;

namespace Sunshine;

/// <summary>One row in the saved-accounts dropdown.</summary>
public sealed record AccountItem(string Name, string Kind, string? MicrosoftId, bool CanRemove = true)
{
    public Visibility RemoveVisibility => CanRemove ? Visibility.Visible : Visibility.Collapsed;
}

public partial class MainWindow : Window
{
    private readonly string _minecraftDir;
    private readonly VersionResolver _versionResolver;
    private readonly GameLauncher _gameLauncher;
    private readonly SettingsStore _settingsStore = new();
    private LaunchProfile _profile = new();
    private CancellationTokenSource? _signInCts;

    public MainWindow()
    {
        InitializeComponent();

        _minecraftDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        _versionResolver = new VersionResolver(_minecraftDir);
        _gameLauncher = new GameLauncher(_minecraftDir);

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyDarkAcrylicChrome(hwnd);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _profile = _settingsStore.Load();

        var versions = _versionResolver.ListInstalledVersionIds();
        VersionComboBox.ItemsSource = versions;

        if (versions.Count == 0)
        {
            SetStatus($"No installed versions found under {_minecraftDir}\\versions.", isError: true);
        }
        else
        {
            VersionComboBox.SelectedItem = versions.Contains(_profile.VersionId) ? _profile.VersionId : versions[0];
        }

        UsernameTextBox.Text = string.IsNullOrWhiteSpace(_profile.Username) ? "Player" : _profile.Username;
        MaxRamSlider.Value = Math.Clamp(_profile.MaxRamMb / 1024.0, MaxRamSlider.Minimum, MaxRamSlider.Maximum);
        MaxRamValueText.Text = $"{(int)MaxRamSlider.Value} GB";
        PerformanceFlagsCheckBox.IsChecked = _profile.PerformanceFlags;
        ExitAfterLaunchCheckBox.IsChecked = _profile.ExitAfterLaunch;

        if (SelectedMicrosoftAccount() == null)
            _profile.SelectedMicrosoftId = null;
        ApplyAccountMode();
    }

    private MicrosoftAccount? SelectedMicrosoftAccount() =>
        _profile.SelectedMicrosoftId == null
            ? null
            : _profile.MicrosoftAccounts.FirstOrDefault(a => a.Id == _profile.SelectedMicrosoftId);

    /// <summary>Switches the username row between an editable offline name and a locked Microsoft account.</summary>
    private void ApplyAccountMode()
    {
        var msa = SelectedMicrosoftAccount();
        if (msa != null)
        {
            AccountLabel.Text = "MICROSOFT ACCOUNT";
            UsernameTextBox.Text = msa.Name;
            UsernameTextBox.IsReadOnly = true;
            AddAccountButton.IsEnabled = false;
        }
        else
        {
            AccountLabel.Text = "USERNAME (OFFLINE)";
            UsernameTextBox.IsReadOnly = false;
            AddAccountButton.IsEnabled = true;
        }
    }

    private List<AccountItem> BuildAccountItems()
    {
        var items = new List<AccountItem>();
        if (SelectedMicrosoftAccount() != null)
            items.Add(new AccountItem("Play offline…", "", null, CanRemove: false));
        items.AddRange(_profile.MicrosoftAccounts.Select(a => new AccountItem(a.Name, "Microsoft", a.Id)));
        items.AddRange(_profile.SavedAccounts.Select(n => new AccountItem(n, "Offline", null)));
        return items;
    }

    private void MaxRamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MaxRamValueText != null)
            MaxRamValueText.Text = $"{(int)e.NewValue} GB";
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _signInCts?.Cancel();
        SaveCurrentProfile();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_minecraftDir);
            Process.Start(new ProcessStartInfo { FileName = _minecraftDir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't open folder: {ex.Message}", isError: true);
        }
    }

    private void AccountsDropdownButton_Click(object sender, RoutedEventArgs e)
    {
        var items = BuildAccountItems();
        if (items.Count == 0)
        {
            SetStatus("No saved accounts yet - type a name and click + to save one, or sign in with Microsoft.", isError: false);
            return;
        }
        AccountsListBox.ItemsSource = items;
        AccountsPopup.IsOpen = true;
    }

    private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsListBox.SelectedItem is not AccountItem item)
            return;

        AccountsPopup.IsOpen = false;
        AccountsListBox.SelectedItem = null;

        if (item.MicrosoftId != null)
        {
            _profile.SelectedMicrosoftId = item.MicrosoftId;
        }
        else
        {
            _profile.SelectedMicrosoftId = null;
            UsernameTextBox.Text = item.CanRemove ? item.Name : _profile.Username;
        }
        ApplyAccountMode();
        SaveCurrentProfile();
    }

    private void RemoveAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not AccountItem item)
            return;

        if (item.MicrosoftId != null)
        {
            _profile.MicrosoftAccounts.RemoveAll(a => a.Id == item.MicrosoftId);
            if (_profile.SelectedMicrosoftId == item.MicrosoftId)
            {
                _profile.SelectedMicrosoftId = null;
                UsernameTextBox.Text = _profile.Username;
            }
        }
        else
        {
            var existing = _profile.SavedAccounts.FirstOrDefault(n => string.Equals(n, item.Name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
                _profile.SavedAccounts.Remove(existing);
        }

        ApplyAccountMode();
        SaveCurrentProfile();
        SetStatus($"Removed \"{item.Name}\".", isError: false);

        var items = BuildAccountItems();
        AccountsListBox.ItemsSource = items;
        if (items.Count == 0)
            AccountsPopup.IsOpen = false;
    }

    private void AddAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var name = UsernameTextBox.Text.Trim();
        if (name.Length == 0)
        {
            SetStatus("Enter a username before saving it.", isError: true);
            return;
        }

        if (!_profile.SavedAccounts.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            _profile.SavedAccounts.Add(name);
            SaveCurrentProfile();
            SetStatus($"Saved account \"{name}\".", isError: false);
        }
        else
        {
            SetStatus($"\"{name}\" is already saved.", isError: false);
        }
    }

    private async void MicrosoftSignInButton_Click(object sender, RoutedEventArgs e)
    {
        // Clicking again while waiting for the browser cancels the sign-in.
        if (_signInCts != null)
        {
            _signInCts.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(_profile.MsaClientId))
        {
            SaveCurrentProfile(); // makes sure the MsaClientId key exists in settings.json
            SetStatus("Microsoft sign-in needs an Azure app client ID. Set \"MsaClientId\" in " +
                      $"{SettingsStore.FilePath} and restart (see README).", isError: true);
            return;
        }

        _signInCts = new CancellationTokenSource();
        MicrosoftSignInButton.ToolTip = "Cancel sign-in";
        LaunchButton.IsEnabled = false;
        try
        {
            var auth = new MicrosoftAuth(_profile.MsaClientId.Trim());
            var account = await auth.SignInAsync(code =>
            {
                try { Clipboard.SetText(code.UserCode); } catch { /* clipboard busy - code is shown anyway */ }
                SetStatus($"Enter code {code.UserCode} at {code.VerificationUri} (copied to clipboard). " +
                          "Click the Microsoft button again to cancel.", isError: false);
                try { Process.Start(new ProcessStartInfo { FileName = code.VerificationUri, UseShellExecute = true }); }
                catch { /* user can open the URL manually */ }
            }, _signInCts.Token);

            UpsertMicrosoftAccount(account);
            _profile.SelectedMicrosoftId = account.Id;
            ApplyAccountMode();
            SaveCurrentProfile();
            SetStatus($"Signed in as {account.Name}.", isError: false);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Microsoft sign-in cancelled.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex is MicrosoftAuthException ? ex.Message : $"Microsoft sign-in failed: {ex.Message}", isError: true);
        }
        finally
        {
            _signInCts.Dispose();
            _signInCts = null;
            MicrosoftSignInButton.ToolTip = "Sign in with Microsoft";
            LaunchButton.IsEnabled = true;
        }
    }

    private void UpsertMicrosoftAccount(MicrosoftAccount account)
    {
        var index = _profile.MicrosoftAccounts.FindIndex(a => a.Id == account.Id);
        if (index >= 0)
            _profile.MicrosoftAccounts[index] = account;
        else
            _profile.MicrosoftAccounts.Add(account);
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (VersionComboBox.SelectedItem is not string versionId)
        {
            SetStatus("Select a version first.", isError: true);
            return;
        }

        var msa = SelectedMicrosoftAccount();
        var username = UsernameTextBox.Text.Trim();
        if (msa == null && username.Length == 0)
        {
            SetStatus("Enter a username.", isError: true);
            return;
        }

        SaveCurrentProfile(versionId);

        LaunchButton.IsEnabled = false;
        SetStatus("Starting...", isError: false);

        try
        {
            LaunchIdentity identity;
            if (msa != null)
            {
                if (msa.AccessTokenExpiresUtc <= DateTime.UtcNow.AddMinutes(5))
                {
                    SetStatus("Refreshing Microsoft login...", isError: false);
                    msa = await new MicrosoftAuth(_profile.MsaClientId.Trim()).RefreshAsync(msa, CancellationToken.None);
                    UpsertMicrosoftAccount(msa);
                    SaveCurrentProfile(versionId);
                }
                identity = new LaunchIdentity(msa.Name, msa.Id, MicrosoftAuth.GetAccessToken(msa), "msa", msa.Xuid, _profile.MsaClientId.Trim());
            }
            else
            {
                identity = new LaunchIdentity(username, OfflineAuth.OfflineUuid(username).ToString("N"), "0", "legacy");
            }

            var result = await _gameLauncher.LaunchAsync(_profile, identity, msg => Dispatcher.Invoke(() => SetStatus(msg, isError: false)));

            if (result.Success)
            {
                SetStatus("Minecraft is running.", isError: false);
                if (_profile.ExitAfterLaunch)
                {
                    Application.Current.Shutdown();
                    return;
                }
            }
            else
            {
                SetStatus(result.Message, isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex is MicrosoftAuthException ? ex.Message : $"Launch failed: {ex.Message}", isError: true);
        }

        LaunchButton.IsEnabled = true;
    }

    private void SaveCurrentProfile(string? versionId = null)
    {
        versionId ??= VersionComboBox.SelectedItem as string ?? _profile.VersionId;

        // The textbox shows the Microsoft gamertag while one is selected; don't overwrite the offline name with it.
        var username = UsernameTextBox.Text.Trim();
        if (SelectedMicrosoftAccount() == null && !string.IsNullOrWhiteSpace(username))
            _profile.Username = username;

        var maxRamMb = (int)MaxRamSlider.Value * 1024;
        _profile.VersionId = versionId;
        _profile.MinRamMb = Math.Min(512, maxRamMb);
        _profile.MaxRamMb = maxRamMb;
        _profile.PerformanceFlags = PerformanceFlagsCheckBox.IsChecked == true;
        _profile.ExitAfterLaunch = ExitAfterLaunchCheckBox.IsChecked == true;

        try
        {
            _settingsStore.Save(_profile);
        }
        catch (Exception ex)
        {
            SetStatus($"Couldn't save settings: {ex.Message}", isError: true);
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Brush)FindResource("ErrorBrush")
            : (Brush)FindResource("TextMutedBrush");
    }
}
