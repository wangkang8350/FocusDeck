using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;

namespace FocusDeck;

public static class UpdateChecker
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/wangkang8350/FocusDeck/releases/latest";

    public static async Task CheckAsync(Window owner)
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FocusDeck");

            using var response = await http.GetAsync(LatestReleaseUrl);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            var root = document.RootElement;
            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersion = ParseVersion(tagName);
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

            if (latestVersion <= currentVersion)
            {
                return;
            }

            var downloadUrl = FindInstallerUrl(root);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                downloadUrl = root.GetProperty("html_url").GetString() ?? "";
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                return;
            }

            var result = System.Windows.MessageBox.Show(
                owner,
                $"发现新版本 {tagName}，当前版本 v{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Build}。\n\n是否现在打开安装包下载链接？",
                "FocusDeck 更新提示",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo(downloadUrl) { UseShellExecute = true });
            }
        }
        catch
        {
            // Update checks should never interrupt normal startup.
        }
    }

    private static Version ParseVersion(string tagName)
    {
        var normalized = tagName.Trim().TrimStart('v', 'V');
        return Version.TryParse(normalized, out var version) ? version : new Version(0, 0, 0);
    }

    private static string FindInstallerUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return asset.GetProperty("browser_download_url").GetString() ?? "";
            }
        }

        return "";
    }
}
