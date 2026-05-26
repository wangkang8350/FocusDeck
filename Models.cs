using System.Text.Json;
using System.IO;

namespace FocusDeck;

public sealed class Settings
{
    public bool LaunchAtStartup { get; set; }
    public List<AppShortcut> Applications { get; set; } = [];

    public static Settings Default() => new()
    {
        LaunchAtStartup = false,
        Applications =
        [
            new AppShortcut
            {
                Id = "codex",
                Name = "Codex",
                Shortcut = "Alt+Z",
                ProcessName = "Codex",
                LaunchType = "shellApp",
                LaunchTarget = "OpenAI.Codex_2p2nqsd0c76g0!App",
                StartIfNotRunning = true
            }
        ]
    };
}

public sealed class AppShortcut
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Shortcut { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string LaunchType { get; set; } = "executable";
    public string LaunchTarget { get; set; } = "";
    public bool StartIfNotRunning { get; set; } = true;
}

public sealed class ProcessCandidate
{
    public string DisplayName { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string LaunchType { get; init; } = "executable";
    public string LaunchTarget { get; init; } = "";
    public string WindowTitle { get; init; } = "";

    public override string ToString() => DisplayName;
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            var path = SettingsPath();
            return File.Exists(path)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? Settings.Default()
                : Settings.Default();
        }
        catch
        {
            return Settings.Default();
        }
    }

    public static void Save(Settings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath())!);
        File.WriteAllText(SettingsPath(), JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static string SettingsPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusDeck", "settings.json");
    }
}
