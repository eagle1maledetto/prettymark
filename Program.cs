using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PrettyMark;

// --- App Settings ---
class AppSettings
{
    public bool DarkMode { get; set; }
    public bool DrawerOpen { get; set; } = true;

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrettyMark", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(SettingsPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
    }
}

// --- Tab model ---
class TabInfo
{
    public string Id { get; set; }
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public FileSystemWatcher Watcher { get; set; }
}

// --- Entry point ---
static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string filePath = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
        if (filePath != null && !File.Exists(filePath))
        {
            MessageBox.Show($"File not found: {filePath}", "PrettyMark",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        Application.Run(new MainForm(filePath));
    }
}

class MainForm : Form
{
    private WebView2 webView;
    private System.Timers.Timer debounceTimer;
    private ToolStripMenuItem darkModeItem;
    private ToolStripMenuItem drawerItem;
    private AppSettings settings;

    // Fullscreen state
    private bool _isFullscreen;
    private FormBorderStyle _savedBorderStyle;
    private FormWindowState _savedWindowState;

    // Tab state
    private readonly List<TabInfo> tabs = new();
    private string activeTabId;

    public MainForm(string filePath)
    {
        settings = AppSettings.Load();
        Text = "PrettyMark";
        Size = new System.Drawing.Size(960, 800);
        MinimumSize = new System.Drawing.Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "favicon.ico");
        if (File.Exists(iconPath))
            Icon = new System.Drawing.Icon(iconPath);

        SetupMenu();
        InitializeWebView();

        if (filePath != null)
            BeginInvoke(() => OpenTab(filePath));
    }

    private void SetupMenu()
    {
        var menuStrip = new MenuStrip();

        // File
        var fileMenu = new ToolStripMenuItem("&File");
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Open", null, (s, e) => OpenFile())
        {
            ShortcutKeys = Keys.Control | Keys.O
        });
        fileMenu.DropDownItems.Add(new ToolStripMenuItem("&Close Tab", null, (s, e) => { if (activeTabId != null) CloseTab(activeTabId); })
        {
            ShortcutKeys = Keys.Control | Keys.W
        });
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("E&xit", null, (s, e) => Close());

        // Edit
        var editMenu = new ToolStripMenuItem("&Edit");
        editMenu.DropDownItems.Add(new ToolStripMenuItem("&Find", null, (s, e) => ExecuteJs("toggleFind()"))
        {
            ShortcutKeys = Keys.Control | Keys.F
        });

        // View
        var viewMenu = new ToolStripMenuItem("&View");
        darkModeItem = new ToolStripMenuItem("&Dark Mode", null, (s, e) =>
        {
            SetDarkMode(!darkModeItem.Checked);
        }) { ShortcutKeys = Keys.Control | Keys.D };
        viewMenu.DropDownItems.Add(darkModeItem);

        drawerItem = new ToolStripMenuItem("Side&bar", null, (s, e) =>
        {
            ToggleDrawer();
        }) { ShortcutKeys = Keys.Control | Keys.B };
        drawerItem.Checked = settings.DrawerOpen;
        viewMenu.DropDownItems.Add(drawerItem);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Zoom &In", null,
            (s, e) => ExecuteJs("zoomIn()")) { ShortcutKeys = Keys.Control | Keys.Oemplus });
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("Zoom &Out", null,
            (s, e) => ExecuteJs("zoomOut()")) { ShortcutKeys = Keys.Control | Keys.OemMinus });
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Reset Zoom", null,
            (s, e) => ExecuteJs("zoomReset()")) { ShortcutKeys = Keys.Control | Keys.D0 });
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        viewMenu.DropDownItems.Add(new ToolStripMenuItem("&Full Screen", null,
            (s, e) => ToggleFullscreen()) { ShortcutKeys = Keys.F11 });

        // ?
        var helpMenu = new ToolStripMenuItem("?");
        helpMenu.DropDownItems.Add("&About PrettyMark", null, (s, e) => ExecuteJs("showAbout()"));

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, viewMenu, helpMenu });
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private async void InitializeWebView()
    {
        webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = true };
        Controls.Add(webView);
        webView.BringToFront();

        await webView.EnsureCoreWebView2Async();

        // Serve assets from local folder via virtual host
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", assetsDir, CoreWebView2HostResourceAccessKind.Allow);

        // Handle JS messages
        webView.CoreWebView2.WebMessageReceived += OnWebMessage;

        // Intercept navigation: only allow initial page load, block everything else
        webView.CoreWebView2.NavigationStarting += (s, e) =>
        {
            if (e.Uri == "https://app.local/index.html") return;

            e.Cancel = true;

            if (e.Uri.StartsWith("file:///"))
            {
                var path = new Uri(e.Uri).LocalPath;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (new[] { ".md", ".markdown", ".txt" }.Contains(ext))
                    BeginInvoke(() => OpenTab(path));
            }
        };

        // Intercept new window requests (triggered by file drag & drop)
        webView.CoreWebView2.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            if (e.Uri.StartsWith("file:///"))
            {
                var path = new Uri(e.Uri).LocalPath;
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (new[] { ".md", ".markdown", ".txt" }.Contains(ext))
                    BeginInvoke(() => OpenTab(path));
            }
        };

        // Load content once page is ready
        webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

        // Clean up WebView chrome
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false;

        webView.CoreWebView2.Navigate("https://app.local/index.html");
    }

    private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;

        // Apply dark mode from saved settings
        SetDarkMode(settings.DarkMode);

        // Apply drawer state
        ExecuteJs($"setDrawerOpen({(settings.DrawerOpen ? "true" : "false")})");

        if (tabs.Count == 0)
        {
            await webView.ExecuteScriptAsync("showWelcome()");
        }
    }

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.GetProperty("type").GetString();
            if (type == "open_url")
            {
                var url = doc.RootElement.GetProperty("url").GetString();
                if (url.StartsWith("http://") || url.StartsWith("https://"))
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            else if (type == "switch_tab")
            {
                var id = doc.RootElement.GetProperty("id").GetString();
                SwitchTab(id);
            }
            else if (type == "close_tab")
            {
                var id = doc.RootElement.GetProperty("id").GetString();
                CloseTab(id);
            }
            else if (type == "shortcut")
            {
                var action = doc.RootElement.GetProperty("action").GetString();
                switch (action)
                {
                    case "open": OpenFile(); break;
                    case "close_tab": if (activeTabId != null) CloseTab(activeTabId); break;
                    case "fullscreen": ToggleFullscreen(); break;
                    case "dark_mode": SetDarkMode(!darkModeItem.Checked); break;
                    case "sidebar": ToggleDrawer(); break;
                }
            }
            else if (type == "click")
            {
                foreach (ToolStripItem item in MainMenuStrip.Items)
                    if (item is ToolStripMenuItem mi && mi.DropDown.Visible)
                        mi.HideDropDown();
            }
            else if (type == "drawer_toggled")
            {
                var open = doc.RootElement.GetProperty("open").GetBoolean();
                settings.DrawerOpen = open;
                drawerItem.Checked = open;
                settings.Save();
            }
        }
        catch { }
    }

    // --- Tab operations ---

    private async void OpenTab(string path)
    {
        path = Path.GetFullPath(path);

        // If file already open, just switch to it
        var existing = tabs.FirstOrDefault(t => string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SwitchTab(existing.Id);
            return;
        }

        var tab = new TabInfo
        {
            Id = Guid.NewGuid().ToString("N"),
            FilePath = path,
            FileName = Path.GetFileName(path)
        };

        // Create watcher
        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        tab.Watcher = new FileSystemWatcher(dir, file) { NotifyFilter = NotifyFilters.LastWrite };
        tab.Watcher.Changed += (s, e) => OnFileChanged(tab.Id);
        tab.Watcher.EnableRaisingEvents = true;

        tabs.Add(tab);
        activeTabId = tab.Id;

        // Send tab info to JS
        var tabJson = JsonSerializer.Serialize(new { id = tab.Id, name = tab.FileName, path = Path.GetDirectoryName(tab.FilePath) });
        await webView.ExecuteScriptAsync($"addTab({tabJson})");

        // Render content
        await RenderTab(tab);
        UpdateTitle(tab);
    }

    private async void SwitchTab(string tabId)
    {
        var tab = tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null || tab.Id == activeTabId) return;

        activeTabId = tabId;
        await webView.ExecuteScriptAsync($"activateTab({JsonSerializer.Serialize(tabId)})");
        await RenderTab(tab);
        UpdateTitle(tab);
    }

    private async void CloseTab(string tabId)
    {
        var tab = tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        // Dispose watcher
        tab.Watcher.EnableRaisingEvents = false;
        tab.Watcher.Dispose();
        tab.Watcher = null;

        tabs.Remove(tab);
        await webView.ExecuteScriptAsync($"removeTab({JsonSerializer.Serialize(tabId)})");

        if (activeTabId == tabId)
        {
            if (tabs.Count > 0)
            {
                // Activate the last tab
                var next = tabs.Last();
                activeTabId = next.Id;
                await webView.ExecuteScriptAsync($"activateTab({JsonSerializer.Serialize(next.Id)})");
                await RenderTab(next);
                UpdateTitle(next);
            }
            else
            {
                activeTabId = null;
                Text = "PrettyMark";
                await webView.ExecuteScriptAsync("showWelcome()");
            }
        }
    }

    private async Task RenderTab(TabInfo tab)
    {
        if (tab == null || !File.Exists(tab.FilePath)) return;
        var content = ReadFile(tab.FilePath);
        var json = JsonSerializer.Serialize(content);
        await webView.ExecuteScriptAsync($"render({json})");
    }

    private void UpdateTitle(TabInfo tab)
    {
        Text = tab != null ? $"{tab.FileName} \u2014 PrettyMark" : "PrettyMark";
    }

    private static string ReadFile(string path)
    {
        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch { return File.ReadAllText(path, Encoding.Latin1); }
    }

    private void OpenFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Markdown Files (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|All Files (*.*)|*.*",
            Title = "Open Markdown File"
        };
        if (dialog.ShowDialog() == DialogResult.OK)
            OpenTab(dialog.FileName);
    }

    private void OnFileChanged(string tabId)
    {
        debounceTimer?.Stop();
        debounceTimer?.Dispose();
        debounceTimer = new System.Timers.Timer(300) { AutoReset = false };
        debounceTimer.Elapsed += (s, ev) =>
        {
            BeginInvoke(() =>
            {
                // Only re-render if this is the active tab
                if (tabId == activeTabId)
                {
                    var tab = tabs.FirstOrDefault(t => t.Id == tabId);
                    if (tab != null) _ = RenderTab(tab);
                }
            });
        };
        debounceTimer.Start();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            FormBorderStyle = _savedBorderStyle;
            WindowState = _savedWindowState;
            MainMenuStrip.Visible = true;
        }
        else
        {
            _savedBorderStyle = FormBorderStyle;
            _savedWindowState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            MainMenuStrip.Visible = false;
        }
        _isFullscreen = !_isFullscreen;
    }

    private void ToggleDrawer()
    {
        settings.DrawerOpen = !settings.DrawerOpen;
        drawerItem.Checked = settings.DrawerOpen;
        settings.Save();
        ExecuteJs($"setDrawerOpen({(settings.DrawerOpen ? "true" : "false")})");
    }

    private void SetDarkMode(bool on)
    {
        darkModeItem.Checked = on;
        settings.DarkMode = on;
        settings.Save();
        ExecuteJs($"setDarkMode({(on ? "true" : "false")})");
    }

    private async void ExecuteJs(string script)
    {
        if (webView?.CoreWebView2 != null)
            await webView.ExecuteScriptAsync(script);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var tab in tabs)
            {
                if (tab.Watcher != null)
                {
                    tab.Watcher.EnableRaisingEvents = false;
                    tab.Watcher.Dispose();
                }
            }
            tabs.Clear();
            debounceTimer?.Dispose();
            webView?.Dispose();
        }
        base.Dispose(disposing);
    }
}
