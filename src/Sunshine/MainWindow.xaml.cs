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

public partial class MainWindow : Window
{
    private readonly string _minecraftDir;
    private readonly VersionResolver _versionResolver;
    private readonly GameLauncher _gameLauncher;
    private readonly SettingsStore _settingsStore = new();
    private LaunchProfile _profile = new();

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

        AccountsListBox.ItemsSource = _profile.SavedAccounts;

        UsernameTextBox.Text = string.IsNullOrWhiteSpace(_profile.Username) ? "Player" : _profile.Username;
        MaxRamSlider.Value = Math.Clamp(_profile.MaxRamMb / 1024.0, MaxRamSlider.Minimum, MaxRamSlider.Maximum);
        PerformanceFlagsCheckBox.IsChecked = _profile.PerformanceFlags;
        ExitAfterLaunchCheckBox.IsChecked = _profile.ExitAfterLaunch;
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
        if (_profile.SavedAccounts.Count == 0)
        {
            SetStatus("No saved accounts yet - type a name and click + to save one.", isError: false);
            return;
        }
        AccountsPopup.IsOpen = true;
    }

    private void AccountsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccountsListBox.SelectedItem is string name)
        {
            UsernameTextBox.Text = name;
            AccountsPopup.IsOpen = false;
            AccountsListBox.SelectedItem = null;
        }
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
            _profile.Username = name;
            _settingsStore.Save(_profile);
            SetStatus($"Saved account \"{name}\".", isError: false);
        }
        else
        {
            SetStatus($"\"{name}\" is already saved.", isError: false);
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (VersionComboBox.SelectedItem is not string versionId)
        {
            SetStatus("Select a version first.", isError: true);
            return;
        }

        var username = UsernameTextBox.Text.Trim();
        if (username.Length == 0)
        {
            SetStatus("Enter a username.", isError: true);
            return;
        }

        SaveCurrentProfile(username, versionId);

        LaunchButton.IsEnabled = false;
        SetStatus("Starting...", isError: false);

        var result = await _gameLauncher.LaunchAsync(_profile, msg => SetStatus(msg, isError: false));

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

        LaunchButton.IsEnabled = true;
    }

    private void SaveCurrentProfile(string? username = null, string? versionId = null)
    {
        username ??= UsernameTextBox.Text.Trim();
        versionId ??= VersionComboBox.SelectedItem as string ?? _profile.VersionId;

        if (string.IsNullOrWhiteSpace(username))
            username = _profile.Username;

        var maxRamMb = (int)MaxRamSlider.Value * 1024;
        _profile = new LaunchProfile
        {
            Username = username,
            VersionId = versionId,
            MinRamMb = Math.Min(512, maxRamMb),
            MaxRamMb = maxRamMb,
            PerformanceFlags = PerformanceFlagsCheckBox.IsChecked == true,
            ExitAfterLaunch = ExitAfterLaunchCheckBox.IsChecked == true,
            SavedAccounts = _profile.SavedAccounts,
        };
        _settingsStore.Save(_profile);
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError
            ? (Brush)FindResource("ErrorBrush")
            : (Brush)FindResource("TextMutedBrush");
    }
}
