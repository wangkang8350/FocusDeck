using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using System.Text;
using Forms = System.Windows.Forms;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace FocusDeck;

public partial class MainWindow : Window
{
    private const int WmHotKey = 0x0312;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private readonly Dictionary<int, AppShortcut> hotkeys = [];
    private readonly List<ApplicationRow> rows = [];
    private readonly List<ProcessCandidate> processCandidates = [];
    private readonly Forms.NotifyIcon trayIcon;
    private Settings settings = Settings.Default();
    private HwndSource? hwndSource;
    private IntPtr hwnd;
    private bool isQuitting;
    private bool isLoading;

    public MainWindow()
    {
        InitializeComponent();
        trayIcon = CreateTrayIcon();
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        LoadSettings();
        Render();
    }

    public void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开设置", null, (_, _) => Dispatcher.Invoke(ShowAndActivate));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() =>
        {
            isQuitting = true;
            trayIcon.Visible = false;
            UnregisterHotkeys();
            Close();
        }));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        var icon = new Forms.NotifyIcon
        {
            Text = "FocusDeck",
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowAndActivate);
        return icon;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        hwnd = new WindowInteropHelper(this).Handle;
        hwndSource = HwndSource.FromHwnd(hwnd);
        hwndSource?.AddHook(WndProc);
        RegisterHotkeys();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (isQuitting)
        {
            trayIcon.Dispose();
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void LoadSettings()
    {
        isLoading = true;
        settings = SettingsStore.Load();
        StartupToggle.IsChecked = settings.LaunchAtStartup;
        UpdateStartupStateText();
        isLoading = false;
    }

    private void Render()
    {
        RefreshProcessCandidates();
        ApplicationsPanel.Children.Clear();
        rows.Clear();
        foreach (var app in settings.Applications)
        {
            var row = new ApplicationRow(app);
            rows.Add(row);
            ApplicationsPanel.Children.Add(CreateApplicationCard(row));
        }
        RegisterHotkeys();
    }

    private Border CreateApplicationCard(ApplicationRow row)
    {
        var card = new Border
        {
            Width = 1362,
            Height = 298,
            Background = Brush("#D9141A22"),
            BorderBrush = Brush("#2B3542"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(28, 26, 26, 22),
            Margin = new Thickness(0, 0, 0, 0)
        };

        var root = new StackPanel();
        card.Child = root;

        var header = new Grid { Height = 58, Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titlePanel = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titlePanel.Children.Add(new System.Windows.Controls.Image
        {
            Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("Assets/ui/codex-icon-card.png", UriKind.Relative)),
            Width = 50,
            Height = 50,
            Margin = new Thickness(0, 0, 24, 0)
        });
        row.NameBox = CreateTextBox(row.Model.Name, "程序名称", 24, true);
        row.NameBox.MinWidth = 180;
        titlePanel.Children.Add(row.NameBox);
        header.Children.Add(titlePanel);

        var statusPill = new Border
        {
            Background = Brush("#143B2B"),
            CornerRadius = new CornerRadius(15),
            Padding = new Thickness(12, 4, 12, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 20, 0)
        };
        row.StatusText = new TextBlock
        {
            Text = "等待保存",
            Foreground = Brush("#73E070"),
            FontSize = 17
        };
        statusPill.Child = row.StatusText;
        Grid.SetColumn(statusPill, 1);
        header.Children.Add(statusPill);

        var deleteButton = new WpfButton
        {
            Content = "⋮",
            Width = 32,
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(0),
            Background = WpfBrushes.Transparent,
            Foreground = Brush("#B8C0CC"),
            BorderBrush = WpfBrushes.Transparent,
            FontSize = 22
        };
        deleteButton.Click += (_, _) =>
        {
            settings.Applications.Remove(row.Model);
            Render();
        };
        Grid.SetColumn(deleteButton, 2);
        header.Children.Add(deleteButton);
        root.Children.Add(header);

        var form = new Grid { Height = 174 };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(364) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(158) });
        form.RowDefinitions.Add(new RowDefinition());
        form.RowDefinitions.Add(new RowDefinition());

        row.ShortcutBox = CreateTextBox(row.Model.Shortcut, "例如：Alt+Z");
        row.ShortcutBox.PreviewKeyDown += (_, e) => CaptureShortcut(row.ShortcutBox, e);
        row.ShortcutBox.PreviewTextInput += (_, e) => e.Handled = true;
        row.ShortcutBox.GotKeyboardFocus += (_, _) => row.ShortcutBox.SelectAll();
        AddField(form, "唤起快捷键", row.ShortcutBox, 0, 0);

        row.ProcessBox = CreateProcessComboBox(row);
        AddField(form, "进程名称", row.ProcessBox, 0, 1);

        row.TypeBox = new WpfComboBox
        {
            MinHeight = 52,
            Margin = new Thickness(0, 7, 0, 0),
            Background = Brush("#151922"),
            Foreground = Brush("#F2F2F4"),
            BorderBrush = Brush("#343B48"),
            Padding = new Thickness(18, 11, 40, 11),
            FontSize = 21
        };
        row.TypeBox.Items.Add("桌面程序");
        row.TypeBox.Items.Add("商店应用");
        row.TypeBox.SelectedIndex = row.Model.LaunchType == "shellApp" ? 1 : 0;
        row.TypeBox.SelectionChanged += (_, _) => row.TargetLabel.Text = row.TypeBox.SelectedIndex == 0 ? "程序路径" : "应用 ID";
        AddField(form, "程序类型", row.TypeBox, 0, 2);

        row.TargetLabel = new TextBlock { Text = row.Model.LaunchType == "shellApp" ? "应用 ID" : "程序路径", Foreground = Brush("#B8C0CC"), FontSize = 18 };
        row.TargetBox = CreateTextBox(row.Model.LaunchTarget, "");
        var targetPanel = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        targetPanel.Children.Add(row.TargetLabel);
        var targetGrid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition());
        targetGrid.Children.Add(row.TargetBox);
        targetPanel.Children.Add(targetGrid);
        Grid.SetRow(targetPanel, 1);
        Grid.SetColumnSpan(targetPanel, 3);
        form.Children.Add(targetPanel);

        var actionPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(16, 28, 0, 0)
        };
        var browseButton = new WpfButton { Content = "选择程序", Height = 52, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 20) };
        browseButton.Click += (_, _) => BrowseExecutable(row);
        actionPanel.Children.Add(browseButton);
        var testButton = new WpfButton { Content = "测试切换", Height = 52, Padding = new Thickness(12, 8, 12, 8) };
        testButton.Click += (_, _) =>
        {
            ApplyRow(row);
            ShowNotice(WindowSwitcher.Toggle(row.Model));
        };
        actionPanel.Children.Add(testButton);
        Grid.SetColumn(actionPanel, 4);
        Grid.SetRowSpan(actionPanel, 2);
        form.Children.Add(actionPanel);

        root.Children.Add(form);

        return card;
    }

    private static WpfTextBox CreateTextBox(string text, string placeholder, double fontSize = 15, bool nameStyle = false)
    {
        return new WpfTextBox
        {
            Text = text,
            ToolTip = placeholder,
            MinHeight = nameStyle ? 40 : 52,
            FontSize = fontSize,
            FontWeight = nameStyle ? FontWeights.SemiBold : FontWeights.Normal,
            Background = nameStyle ? WpfBrushes.Transparent : Brush("#151922"),
            BorderBrush = nameStyle ? WpfBrushes.Transparent : Brush("#343B48"),
            Foreground = Brush("#F2F2F4"),
            Padding = new Thickness(18, 11, 18, 11),
            Margin = new Thickness(0, 7, 0, 0)
        };
    }

    private WpfComboBox CreateProcessComboBox(ApplicationRow row)
    {
        var combo = new WpfComboBox
        {
            IsEditable = true,
            IsTextSearchEnabled = true,
            ItemsSource = processCandidates,
            Text = row.Model.ProcessName,
            ToolTip = "选择当前正在运行的窗口，或手动输入进程名",
            MinHeight = 52,
            FontSize = 21,
            Background = Brush("#151922"),
            Foreground = Brush("#F2F2F4"),
            BorderBrush = Brush("#343B48"),
            Padding = new Thickness(18, 11, 40, 11),
            Margin = new Thickness(0, 7, 24, 0)
        };

        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ProcessCandidate candidate)
            {
                ApplyProcessCandidate(row, candidate);
            }
        };

        combo.DropDownOpened += (_, _) =>
        {
            RefreshProcessCandidates();
            combo.ItemsSource = null;
            combo.ItemsSource = processCandidates;
        };

        return combo;
    }

    private void ApplyProcessCandidate(ApplicationRow row, ProcessCandidate candidate)
    {
        row.ProcessBox.Text = candidate.ProcessName;
        row.Model.ProcessName = candidate.ProcessName;

        if (!string.IsNullOrWhiteSpace(candidate.LaunchTarget))
        {
            row.TargetBox.Text = candidate.LaunchTarget;
            row.TypeBox.SelectedIndex = candidate.LaunchType == "shellApp" ? 1 : 0;
        }

        if (string.IsNullOrWhiteSpace(row.NameBox.Text) || row.NameBox.Text == "新程序")
        {
            row.NameBox.Text = string.IsNullOrWhiteSpace(candidate.WindowTitle) ? candidate.ProcessName : candidate.WindowTitle;
        }
    }

    private static void CaptureShortcut(WpfTextBox target, System.Windows.Input.KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.ImeProcessed)
        {
            key = e.ImeProcessedKey;
        }

        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        if (key == Key.Back || key == Key.Delete || key == Key.Escape)
        {
            target.Text = "";
            return;
        }

        var parts = new List<string>();
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            parts.Add("Ctrl");
        }
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0)
        {
            parts.Add("Alt");
        }
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            parts.Add("Shift");
        }
        if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0)
        {
            parts.Add("Win");
        }

        parts.Add(KeyToShortcutText(key));
        target.Text = string.Join("+", parts);
        target.CaretIndex = target.Text.Length;
    }

    private static string KeyToShortcutText(Key key)
    {
        return key switch
        {
            >= Key.A and <= Key.Z => key.ToString(),
            >= Key.D0 and <= Key.D9 => key.ToString()[1..],
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + key.ToString()[^1],
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Escape => "Esc",
            Key.OemPlus => "=",
            Key.OemMinus => "-",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            _ => key.ToString()
        };
    }

    private static void AddField(Grid form, string label, FrameworkElement control, int row, int column)
    {
        var panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 28, 0, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#B8C0CC"), FontSize = 18 });
        panel.Children.Add(control);
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        form.Children.Add(panel);
    }

    private void RefreshProcessCandidates()
    {
        var candidates = new Dictionary<string, ProcessCandidate>(StringComparer.OrdinalIgnoreCase);

        PInvoke.EnumWindows((windowHandle, _) =>
        {
            if (!PInvoke.IsWindowVisible(windowHandle) || PInvoke.GetWindowTextLength(windowHandle) == 0)
            {
                return true;
            }

            PInvoke.GetWindowThreadProcessId(windowHandle, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                var processName = process.ProcessName;
                var title = GetWindowTitle(windowHandle);
                var appId = GetApplicationUserModelId(processId);
                var executablePath = GetExecutablePath(process);
                var launchType = string.IsNullOrWhiteSpace(appId) ? "executable" : "shellApp";
                var launchTarget = launchType == "shellApp" ? appId : executablePath;
                var key = string.IsNullOrWhiteSpace(appId) ? processName : appId;

                if (!candidates.ContainsKey(key))
                {
                    candidates[key] = new ProcessCandidate
                    {
                        DisplayName = string.IsNullOrWhiteSpace(title) ? processName : $"{title}  ·  {processName}",
                        ProcessName = processName,
                        LaunchType = launchType,
                        LaunchTarget = launchTarget,
                        WindowTitle = title
                    };
                }
            }
            catch
            {
                // Some elevated/system windows cannot be queried from a normal user process.
            }

            return true;
        }, IntPtr.Zero);

        processCandidates.Clear();
        processCandidates.AddRange(candidates.Values.OrderBy(item => item.ProcessName).ThenBy(item => item.WindowTitle));
    }

    private static string GetWindowTitle(IntPtr windowHandle)
    {
        var length = PInvoke.GetWindowTextLength(windowHandle);
        if (length <= 0)
        {
            return "";
        }

        var builder = new StringBuilder(length + 1);
        PInvoke.GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string GetApplicationUserModelId(uint processId)
    {
        var handle = PInvoke.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            uint length = 0;
            _ = PInvoke.GetApplicationUserModelId(handle, ref length, null);
            if (length == 0)
            {
                return "";
            }

            var builder = new StringBuilder((int)length);
            return PInvoke.GetApplicationUserModelId(handle, ref length, builder) == 0 ? builder.ToString() : "";
        }
        finally
        {
            PInvoke.CloseHandle(handle);
        }
    }

    private static string GetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? "";
        }
        catch
        {
            return "";
        }
    }

    private void BrowseExecutable(ApplicationRow row)
    {
        using var dialog = new Forms.OpenFileDialog { Filter = "Windows 应用程序 (*.exe)|*.exe", CheckFileExists = true };
        if (dialog.ShowDialog() != Forms.DialogResult.OK)
        {
            return;
        }

        row.TargetBox.Text = dialog.FileName;
        row.ProcessBox.Text = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (row.NameBox.Text == "新程序")
        {
            row.NameBox.Text = row.ProcessBox.Text;
        }
    }

    private void ApplyRow(ApplicationRow row)
    {
        row.Model.Name = row.NameBox.Text.Trim();
        row.Model.Shortcut = row.ShortcutBox.Text.Trim();
        row.Model.ProcessName = row.ProcessBox.Text.Trim();
        row.Model.LaunchType = row.TypeBox.SelectedIndex == 1 ? "shellApp" : "executable";
        row.Model.LaunchTarget = row.TargetBox.Text.Trim();
        row.Model.StartIfNotRunning = true;
    }

    private void ApplyRows()
    {
        foreach (var row in rows)
        {
            ApplyRow(row);
        }
    }

    private void StartupToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (isLoading)
        {
            return;
        }

        settings.LaunchAtStartup = StartupToggle.IsChecked == true;
        UpdateStartupStateText();
    }

    private void UpdateStartupStateText()
    {
        StartupStateText.Text = settings.LaunchAtStartup ? "已开启" : "已关闭";
        StartupStateText.Foreground = Brush(settings.LaunchAtStartup ? "#67BBFA" : "#A8A8AD");
    }

    private void AddApplication_Click(object sender, RoutedEventArgs e)
    {
        settings.Applications.Add(new AppShortcut { Id = Guid.NewGuid().ToString("N"), Name = "新程序", LaunchType = "executable", StartIfNotRunning = true });
        Render();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        LoadSettings();
        Render();
        ShowNotice("已恢复上次保存的设置。");
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ApplyRows();
        SettingsStore.Save(settings);
        StartupManager.SetEnabled(settings.LaunchAtStartup);
        RegisterHotkeys();
        ShowNotice("设置已保存，快捷键已生效。");
    }

    private void RegisterHotkeys()
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        UnregisterHotkeys();
        var id = 100;
        foreach (var app in settings.Applications.Where(item => !string.IsNullOrWhiteSpace(item.Shortcut)))
        {
            var accelerator = HotkeyParser.Parse(app.Shortcut);
            if (accelerator is null || !PInvoke.RegisterHotKey(hwnd, id, accelerator.Value.Modifiers, accelerator.Value.VirtualKey))
            {
                UpdateStatus(app.Id, "快捷键冲突");
            }
            else
            {
                hotkeys[id] = app;
                UpdateStatus(app.Id, "已生效");
                id++;
            }
        }
    }

    private void UnregisterHotkeys()
    {
        foreach (var id in hotkeys.Keys.ToList())
        {
            PInvoke.UnregisterHotKey(hwnd, id);
        }
        hotkeys.Clear();
    }

    private void UpdateStatus(string appId, string status)
    {
        var row = rows.FirstOrDefault(item => item.Model.Id == appId);
        if (row?.StatusText is not null)
        {
            row.StatusText.Text = status;
            row.StatusText.Foreground = Brush(status == "已生效" ? "#48C774" : "#E75D5D");
        }
    }

    private IntPtr WndProc(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && hotkeys.TryGetValue(wParam.ToInt32(), out var app))
        {
            WindowSwitcher.Toggle(app);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void ShowNotice(string text)
    {
        NoticeText.Text = text;
    }

    private static SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#');
        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex.Substring(2, 2), 16);
        var b = Convert.ToByte(hex.Substring(4, 2), 16);
        return new SolidColorBrush(WpfColor.FromRgb(r, g, b));
    }

    private sealed class ApplicationRow(AppShortcut model)
    {
        public AppShortcut Model { get; } = model;
        public WpfTextBox NameBox { get; set; } = null!;
        public WpfTextBox ShortcutBox { get; set; } = null!;
        public WpfComboBox ProcessBox { get; set; } = null!;
        public WpfComboBox TypeBox { get; set; } = null!;
        public TextBlock TargetLabel { get; set; } = null!;
        public WpfTextBox TargetBox { get; set; } = null!;
        public TextBlock StatusText { get; set; } = null!;
    }
}
