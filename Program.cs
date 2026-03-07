using System;
using System.Collections.Generic;
using System.Globalization;
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
    public string Language { get; set; } = "";
    public List<string> RecentFiles { get; set; } = new();
    public List<string> SessionFiles { get; set; } = new();
    public string SessionActiveFile { get; set; } = "";

    public void AddRecentFile(string path)
    {
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 10) RecentFiles.RemoveRange(10, RecentFiles.Count - 10);
    }

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PrettyMark", "settings.json");

    private static readonly string[] ValidExtensions = { ".md", ".markdown", ".txt" };

    private static bool IsValidFilePath(string p) =>
        !string.IsNullOrEmpty(p) && Path.IsPathRooted(p) &&
        ValidExtensions.Contains(Path.GetExtension(p).ToLowerInvariant());

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                settings.RecentFiles = settings.RecentFiles?.Where(IsValidFilePath).Take(10).ToList() ?? new();
                settings.SessionFiles = settings.SessionFiles?.Where(IsValidFilePath).ToList() ?? new();
                if (!string.IsNullOrEmpty(settings.SessionActiveFile) && !IsValidFilePath(settings.SessionActiveFile))
                    settings.SessionActiveFile = "";
                return settings;
            }
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
            // Load minimal translations for error message
            var errorStrings = MainForm.LoadTranslationsStatic(
                MainForm.ResolveLanguageStatic(AppSettings.Load().Language));
            var msg = string.Format(
                errorStrings.GetValueOrDefault("error_file_not_found", "File not found: {0}"),
                filePath);
            MessageBox.Show(msg, errorStrings.GetValueOrDefault("app_name", "PrettyMark"),
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

    // i18n state
    private Dictionary<string, string> _strings = new();
    private string _currentLang = "en";

    // Menu item references for translation updates
    private ToolStripMenuItem fileMenu, openItem, closeTabItem, exitItem;
    private ToolStripMenuItem recentMenu, printItem;
    private ToolStripMenuItem editMenu, findItem;
    private ToolStripMenuItem viewMenu, zoomInItem, zoomOutItem, resetZoomItem, fullScreenItem;
    private ToolStripMenuItem langMenu, helpMenu, aboutItem;

    // Session restore
    private string _initialFilePath;

    public MainForm(string filePath)
    {
        settings = AppSettings.Load();
        _currentLang = ResolveLanguageStatic(settings.Language);
        _strings = LoadTranslationsStatic(_currentLang);

        Text = T("app_name");
        Size = new System.Drawing.Size(960, 800);
        MinimumSize = new System.Drawing.Size(400, 300);
        StartPosition = FormStartPosition.CenterScreen;

        var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "favicon.ico");
        if (File.Exists(iconPath))
            Icon = new System.Drawing.Icon(iconPath);

        SetupMenu();
        InitializeWebView();

        _initialFilePath = filePath;
    }

    private void SetupMenu()
    {
        var menuStrip = new MenuStrip();

        // File
        fileMenu = new ToolStripMenuItem();
        openItem = new ToolStripMenuItem("", null, (s, e) => OpenFile())
        { ShortcutKeys = Keys.Control | Keys.O };
        recentMenu = new ToolStripMenuItem();
        printItem = new ToolStripMenuItem("", null, (s, e) => PrintDocument())
        { ShortcutKeys = Keys.Control | Keys.P };
        closeTabItem = new ToolStripMenuItem("", null, (s, e) => { if (activeTabId != null) CloseTab(activeTabId); })
        { ShortcutKeys = Keys.Control | Keys.W };
        exitItem = new ToolStripMenuItem("", null, (s, e) => Close());
        fileMenu.DropDownItems.Add(openItem);
        fileMenu.DropDownItems.Add(recentMenu);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(printItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(closeTabItem);
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add(exitItem);

        // Edit
        editMenu = new ToolStripMenuItem();
        findItem = new ToolStripMenuItem("", null, (s, e) => ExecuteJs("toggleFind()"))
        { ShortcutKeys = Keys.Control | Keys.F };
        editMenu.DropDownItems.Add(findItem);

        // View
        viewMenu = new ToolStripMenuItem();
        darkModeItem = new ToolStripMenuItem("", null, (s, e) =>
        {
            SetDarkMode(!darkModeItem.Checked);
        }) { ShortcutKeys = Keys.Control | Keys.D };
        viewMenu.DropDownItems.Add(darkModeItem);

        drawerItem = new ToolStripMenuItem("", null, (s, e) =>
        {
            ToggleDrawer();
        }) { ShortcutKeys = Keys.Control | Keys.B };
        drawerItem.Checked = settings.DrawerOpen;
        viewMenu.DropDownItems.Add(drawerItem);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());

        // Language submenu
        langMenu = new ToolStripMenuItem();
        viewMenu.DropDownItems.Add(langMenu);

        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        zoomInItem = new ToolStripMenuItem("", null,
            (s, e) => ExecuteJs("zoomIn()")) { ShortcutKeys = Keys.Control | Keys.Oemplus };
        zoomOutItem = new ToolStripMenuItem("", null,
            (s, e) => ExecuteJs("zoomOut()")) { ShortcutKeys = Keys.Control | Keys.OemMinus };
        resetZoomItem = new ToolStripMenuItem("", null,
            (s, e) => ExecuteJs("zoomReset()")) { ShortcutKeys = Keys.Control | Keys.D0 };
        viewMenu.DropDownItems.Add(zoomInItem);
        viewMenu.DropDownItems.Add(zoomOutItem);
        viewMenu.DropDownItems.Add(resetZoomItem);
        viewMenu.DropDownItems.Add(new ToolStripSeparator());
        fullScreenItem = new ToolStripMenuItem("", null,
            (s, e) => ToggleFullscreen()) { ShortcutKeys = Keys.F11 };
        viewMenu.DropDownItems.Add(fullScreenItem);

        // ?
        helpMenu = new ToolStripMenuItem();
        aboutItem = new ToolStripMenuItem("", null, (s, e) => ExecuteJs("showAbout()"));
        helpMenu.DropDownItems.Add(aboutItem);

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, editMenu, viewMenu, helpMenu });
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);

        ApplyMenuTranslations();
    }

    private async void InitializeWebView()
    {
        webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = true };
        Controls.Add(webView);
        webView.BringToFront();

        var userDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PrettyMark", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
        await webView.EnsureCoreWebView2Async(env);

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
            System.Diagnostics.Debug.WriteLine($"Navigation blocked: {e.Uri}");

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

        // Send i18n strings to JS
        SendStringsToJs();

        // Apply dark mode from saved settings
        SetDarkMode(settings.DarkMode);

        // Apply drawer state
        ExecuteJs($"setDrawerOpen({(settings.DrawerOpen ? "true" : "false")})");

        await RestoreSession();
        if (_initialFilePath != null)
        {
            OpenTab(_initialFilePath);
            _initialFilePath = null;
        }
        if (tabs.Count == 0)
        {
            await webView.ExecuteScriptAsync("showWelcome()");
        }
    }

    private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!e.Source.StartsWith("https://app.local/")) return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var type = doc.RootElement.GetProperty("type").GetString();
            if (type == "open_url")
            {
                var url = doc.RootElement.GetProperty("url").GetString();
                if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "http" || uri.Scheme == "https"))
                {
                    System.Diagnostics.Process.Start(
                        new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
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
                    case "print": PrintDocument(); break;
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

    private static readonly string[] AllowedExtensions = { ".md", ".markdown", ".txt" };

    private async void OpenTab(string path)
    {
        path = Path.GetFullPath(path);

        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext)) return;
        if (!File.Exists(path)) return;

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

        settings.AddRecentFile(path);
        SaveSession();
        RebuildRecentMenu();

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
        SaveSession();
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
                Text = T("app_name");
                await webView.ExecuteScriptAsync("showWelcome()");
            }
        }
        SaveSession();
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
        var name = T("app_name");
        Text = tab != null ? $"{tab.FileName} \u2014 {name}" : name;
    }

    private static string ReadFile(string path)
    {
        try { return File.ReadAllText(path, Encoding.UTF8); }
        catch { return File.ReadAllText(path, Encoding.Latin1); }
    }

    private void OpenFile()
    {
        var mdLabel = T("dialog_filter_md");
        var allLabel = T("dialog_filter_all");
        using var dialog = new OpenFileDialog
        {
            Filter = $"{mdLabel} (*.md;*.markdown;*.txt)|*.md;*.markdown;*.txt|{allLabel} (*.*)|*.*",
            Title = T("dialog_open_title")
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

    private void RebuildRecentMenu()
    {
        recentMenu.DropDownItems.Clear();
        var recent = settings.RecentFiles.Where(File.Exists).Take(10).ToList();
        if (recent.Count == 0)
        {
            var emptyItem = new ToolStripMenuItem(T("menu_recent_empty")) { Enabled = false };
            recentMenu.DropDownItems.Add(emptyItem);
        }
        else
        {
            foreach (var path in recent)
            {
                var item = new ToolStripMenuItem(Path.GetFileName(path)) { ToolTipText = path, Tag = path };
                item.Click += (s, e) => OpenTab((string)((ToolStripMenuItem)s).Tag);
                recentMenu.DropDownItems.Add(item);
            }
            recentMenu.DropDownItems.Add(new ToolStripSeparator());
            var clearItem = new ToolStripMenuItem(T("menu_recent_clear"));
            clearItem.Click += (s, e) =>
            {
                settings.RecentFiles.Clear();
                settings.Save();
                RebuildRecentMenu();
            };
            recentMenu.DropDownItems.Add(clearItem);
        }
    }

    private void PrintDocument()
    {
        if (activeTabId == null) return;
        ExecuteJs("window.print()");
    }

    private void SaveSession()
    {
        settings.SessionFiles = tabs.Select(t => t.FilePath).ToList();
        var activeTab = tabs.FirstOrDefault(t => t.Id == activeTabId);
        settings.SessionActiveFile = activeTab?.FilePath ?? "";
        settings.Save();
    }

    private async Task RestoreSession()
    {
        var files = settings.SessionFiles.Where(File.Exists).ToList();
        if (files.Count == 0) return;

        string activeFilePath = settings.SessionActiveFile;
        string lastTabId = null;

        foreach (var path in files)
        {
            var tab = new TabInfo
            {
                Id = Guid.NewGuid().ToString("N"),
                FilePath = path,
                FileName = Path.GetFileName(path)
            };

            var dir = Path.GetDirectoryName(path);
            var file = Path.GetFileName(path);
            tab.Watcher = new FileSystemWatcher(dir, file) { NotifyFilter = NotifyFilters.LastWrite };
            tab.Watcher.Changed += (s, e) => OnFileChanged(tab.Id);
            tab.Watcher.EnableRaisingEvents = true;

            tabs.Add(tab);

            var tabJson = JsonSerializer.Serialize(new { id = tab.Id, name = tab.FileName, path = Path.GetDirectoryName(tab.FilePath) });
            await webView.ExecuteScriptAsync($"addTab({tabJson})");

            if (string.Equals(path, activeFilePath, StringComparison.OrdinalIgnoreCase))
                lastTabId = tab.Id;
            else if (lastTabId == null)
                lastTabId = tab.Id;
        }

        if (lastTabId != null)
        {
            activeTabId = lastTabId;
            await webView.ExecuteScriptAsync($"activateTab({JsonSerializer.Serialize(lastTabId)})");
            var activeTab = tabs.FirstOrDefault(t => t.Id == lastTabId);
            if (activeTab != null)
            {
                await RenderTab(activeTab);
                UpdateTitle(activeTab);
            }
        }
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

    // --- i18n ---

    private string T(string key)
    {
        return _strings.GetValueOrDefault(key, key);
    }

    public static Dictionary<string, string> LoadTranslationsStatic(string lang)
    {
        var langDir = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
        var path = Path.Combine(langDir, $"{lang}.json");
        if (!File.Exists(path))
            path = Path.Combine(langDir, "en.json");
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
        }
        catch { }
        return new Dictionary<string, string>();
    }

    public static string ResolveLanguageStatic(string settingsLang)
    {
        if (!string.IsNullOrEmpty(settingsLang))
            return settingsLang;

        var uiLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var langDir = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
        var path = Path.Combine(langDir, $"{uiLang}.json");
        return File.Exists(path) ? uiLang : "en";
    }

    private List<string> GetAvailableLanguages()
    {
        var langDir = Path.Combine(AppContext.BaseDirectory, "assets", "lang");
        if (!Directory.Exists(langDir)) return new List<string> { "en" };
        return Directory.GetFiles(langDir, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(l => l)
            .ToList();
    }

    private void ApplyMenuTranslations()
    {
        fileMenu.Text = T("menu_file");
        openItem.Text = T("menu_open");
        recentMenu.Text = T("menu_recent");
        RebuildRecentMenu();
        printItem.Text = T("menu_print");
        closeTabItem.Text = T("menu_close_tab");
        exitItem.Text = T("menu_exit");
        editMenu.Text = T("menu_edit");
        findItem.Text = T("menu_find");
        viewMenu.Text = T("menu_view");
        darkModeItem.Text = T("menu_dark_mode");
        drawerItem.Text = T("menu_sidebar");
        zoomInItem.Text = T("menu_zoom_in");
        zoomOutItem.Text = T("menu_zoom_out");
        resetZoomItem.Text = T("menu_reset_zoom");
        fullScreenItem.Text = T("menu_fullscreen");
        helpMenu.Text = T("menu_help");
        aboutItem.Text = T("menu_about");

        // Rebuild Language submenu
        langMenu.Text = T("menu_language");
        langMenu.DropDownItems.Clear();
        foreach (var lang in GetAvailableLanguages())
        {
            var item = new ToolStripMenuItem(lang.ToUpperInvariant())
            {
                Checked = lang == _currentLang,
                Tag = lang
            };
            item.Click += (s, e) =>
            {
                var l = (string)((ToolStripMenuItem)s).Tag;
                if (l != _currentLang) SwitchLanguage(l);
            };
            langMenu.DropDownItems.Add(item);
        }
    }

    private void SwitchLanguage(string lang)
    {
        _currentLang = lang;
        settings.Language = lang;
        settings.Save();
        _strings = LoadTranslationsStatic(lang);
        ApplyMenuTranslations();
        SendStringsToJs();

        // Update title
        var activeTab = tabs.FirstOrDefault(t => t.Id == activeTabId);
        UpdateTitle(activeTab);
    }

    private void SendStringsToJs()
    {
        var json = JsonSerializer.Serialize(_strings);
        ExecuteJs($"setStrings({json})");
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
