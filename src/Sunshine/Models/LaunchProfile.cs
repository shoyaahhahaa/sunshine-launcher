using System.Collections.ObjectModel;

namespace Sunshine.Models;

public sealed class LaunchProfile
{
    public string Username { get; set; } = "Player";
    public string VersionId { get; set; } = "";
    public int MinRamMb { get; set; } = 2048;
    public int MaxRamMb { get; set; } = 4096;
    public bool PerformanceFlags { get; set; } = true;
    public bool ExitAfterLaunch { get; set; } = true;

    // ObservableCollection so the accounts dropdown stays in sync automatically when
    // an account is added, instead of needing to be manually rebound (which previously
    // desynced WPF's collection view and crashed the app).
    public ObservableCollection<string> SavedAccounts { get; set; } = new();

    public List<MicrosoftAccount> MicrosoftAccounts { get; set; } = new();

    /// <summary>Profile id of the Microsoft account to launch with; null means offline.</summary>
    public string? SelectedMicrosoftId { get; set; }

    /// <summary>
    /// Azure app (client) ID used for Microsoft sign-in. Must be a public-client app
    /// approved by Mojang for the Minecraft API.
    /// </summary>
    public string MsaClientId { get; set; } = "";
}
