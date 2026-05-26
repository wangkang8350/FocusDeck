using System.Diagnostics;

namespace FocusDeck;

public static class WindowSwitcher
{
    public static string Toggle(AppShortcut app)
    {
        var target = FindTargetWindow(app.ProcessName);
        if (target.Handle != IntPtr.Zero)
        {
            if (PInvoke.GetForegroundWindow() == target.Handle)
            {
                PInvoke.ShowWindowAsync(target.Handle, 6);
                return $"已最小化 {app.Name}。";
            }

            PInvoke.ShowWindowAsync(target.Handle, 9);
            PInvoke.BringWindowToTop(target.Handle);
            PInvoke.SetForegroundWindow(target.Handle);
            return $"已唤起 {app.Name}。";
        }

        if (!app.StartIfNotRunning)
        {
            return $"{app.Name} 当前未显示。";
        }

        if (string.IsNullOrWhiteSpace(app.LaunchTarget))
        {
            return $"尚未为 {app.Name} 配置启动目标。";
        }

        if (app.LaunchType == "shellApp")
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{app.LaunchTarget}") { UseShellExecute = true });
        }
        else
        {
            Process.Start(new ProcessStartInfo(app.LaunchTarget) { UseShellExecute = true });
        }

        return $"已启动 {app.Name}。";
    }

    private static WindowCandidate FindTargetWindow(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return default;
        }

        var processIds = Process.GetProcessesByName(processName).Select(process => process.Id).ToHashSet();
        var best = new WindowCandidate();

        PInvoke.EnumWindows((hwnd, _) =>
        {
            if (!PInvoke.IsWindowVisible(hwnd))
            {
                return true;
            }

            PInvoke.GetWindowThreadProcessId(hwnd, out var ownerProcessId);
            if (!processIds.Contains((int)ownerProcessId))
            {
                return true;
            }

            PInvoke.GetWindowRect(hwnd, out var rect);
            var area = Math.Max(0, rect.Right - rect.Left) * Math.Max(0, rect.Bottom - rect.Top);
            if (area > best.Area)
            {
                best = new WindowCandidate(hwnd, area);
            }

            return true;
        }, IntPtr.Zero);

        return best;
    }

    private readonly record struct WindowCandidate(IntPtr Handle, int Area);
}
