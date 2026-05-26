using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.IO;
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
    private readonly Dictionary<int, AppShortcut> hotkeys = [];
    private readonly List<ApplicationRow> rows = [];
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
            Background = Brush("#2B2B2B"),
            BorderBrush = Brush("#353535"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(20),
            Margin = new Thickness(0, 0, 0, 16)
        };

        var root = new StackPanel();
        card.Child = root;

        var header = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        row.NameBox = CreateTextBox(row.Model.Name, "程序名称", 20, true);
        header.Children.Add(row.NameBox);

        row.StatusText = new TextBlock
        {
            Text = "等待保存",
            Foreground = Brush("#BBBBBB"),
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 16, 0)
        };
        Grid.SetColumn(row.StatusText, 1);
        header.Children.Add(row.StatusText);

        var deleteButton = new WpfButton { Content = "删除", Background = WpfBrushes.Transparent, Foreground = Brush("#BABAC0"), BorderBrush = WpfBrushes.Transparent, Padding = new Thickness(10, 6, 10, 6), MinHeight = 32 };
        deleteButton.Click += (_, _) =>
        {
            settings.Applications.Remove(row.Model);
            Render();
        };
        Grid.SetColumn(deleteButton, 2);
        header.Children.Add(deleteButton);
        root.Children.Add(header);

        var form = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition());
        form.RowDefinitions.Add(new RowDefinition());

        row.ShortcutBox = CreateTextBox(row.Model.Shortcut, "例如：Alt+Z");
        AddField(form, "唤起快捷键", row.ShortcutBox, 0, 0);

        row.ProcessBox = CreateTextBox(row.Model.ProcessName, "例如：Codex");
        AddField(form, "进程名称", row.ProcessBox, 0, 1);

        row.TypeBox = new WpfComboBox { MinHeight = 44, Margin = new Thickness(8, 7, 0, 0), Background = Brush("#383838"), Foreground = Brush("#F2F2F4"), BorderBrush = Brush("#424242"), Padding = new Thickness(12, 8, 12, 8) };
        row.TypeBox.Items.Add("桌面程序（exe）");
        row.TypeBox.Items.Add("商店应用（应用 ID）");
        row.TypeBox.SelectedIndex = row.Model.LaunchType == "shellApp" ? 1 : 0;
        row.TypeBox.SelectionChanged += (_, _) => row.TargetLabel.Text = row.TypeBox.SelectedIndex == 0 ? "程序路径" : "应用 ID";
        AddField(form, "程序类型", row.TypeBox, 0, 2);

        row.TargetLabel = new TextBlock { Text = row.Model.LaunchType == "shellApp" ? "应用 ID" : "程序路径", Foreground = Brush("#C2C2C6"), FontSize = 14 };
        row.TargetBox = CreateTextBox(row.Model.LaunchTarget, "");
        var targetPanel = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        targetPanel.Children.Add(row.TargetLabel);
        var targetGrid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition());
        targetGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        targetGrid.Children.Add(row.TargetBox);
        var browseButton = new WpfButton { Content = "选择程序", Padding = new Thickness(18, 10, 18, 10), Margin = new Thickness(10, 0, 0, 0) };
        browseButton.Click += (_, _) => BrowseExecutable(row);
        Grid.SetColumn(browseButton, 1);
        targetGrid.Children.Add(browseButton);
        targetPanel.Children.Add(targetGrid);
        Grid.SetRow(targetPanel, 1);
        Grid.SetColumnSpan(targetPanel, 3);
        form.Children.Add(targetPanel);
        root.Children.Add(form);

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Children.Add(new TextBlock { Text = "快捷键只会占用这里填写的组合键。", Foreground = Brush("#9E9EA3"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center });
        var testButton = new WpfButton { Content = "测试切换", Padding = new Thickness(18, 10, 18, 10) };
        testButton.Click += (_, _) =>
        {
            ApplyRow(row);
            ShowNotice(WindowSwitcher.Toggle(row.Model));
        };
        Grid.SetColumn(testButton, 1);
        footer.Children.Add(testButton);
        root.Children.Add(footer);

        return card;
    }

    private static WpfTextBox CreateTextBox(string text, string placeholder, double fontSize = 15, bool nameStyle = false)
    {
        return new WpfTextBox
        {
            Text = text,
            ToolTip = placeholder,
            MinHeight = nameStyle ? 36 : 44,
            FontSize = fontSize,
            FontWeight = nameStyle ? FontWeights.SemiBold : FontWeights.Normal,
            Background = nameStyle ? WpfBrushes.Transparent : Brush("#383838"),
            BorderBrush = nameStyle ? WpfBrushes.Transparent : Brush("#424242"),
            Foreground = Brush("#F2F2F4"),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 7, 8, 0)
        };
    }

    private static void AddField(Grid form, string label, FrameworkElement control, int row, int column)
    {
        var panel = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 8, 0, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Foreground = Brush("#C2C2C6"), FontSize = 14 });
        panel.Children.Add(control);
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        form.Children.Add(panel);
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
        public WpfTextBox ProcessBox { get; set; } = null!;
        public WpfComboBox TypeBox { get; set; } = null!;
        public TextBlock TargetLabel { get; set; } = null!;
        public WpfTextBox TargetBox { get; set; } = null!;
        public TextBlock StatusText { get; set; } = null!;
    }
}
