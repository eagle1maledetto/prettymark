using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace PrettyMark;

// --- P/Invoke for OLE drag-drop ---
static class NativeMethods
{
    [DllImport("ole32.dll")] public static extern int RevokeDragDrop(IntPtr hwnd);
    [DllImport("ole32.dll")] public static extern int RegisterDragDrop(IntPtr hwnd, IDropTargetNative pDropTarget);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int maxCount);
    [DllImport("shell32.dll", CharSet = CharSet.Auto)] public static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder sb, uint cch);
    [DllImport("ole32.dll")] public static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
}

[StructLayout(LayoutKind.Sequential)]
struct POINTL { public int x, y; }

[StructLayout(LayoutKind.Sequential)]
struct FORMATETC
{
    public ushort cfFormat;
    public IntPtr ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
}

[StructLayout(LayoutKind.Sequential)]
struct STGMEDIUM
{
    public uint tymed;
    public IntPtr unionmember;
    public IntPtr pUnkForRelease;
}

[ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDropTargetNative
{
    void DragEnter([In, MarshalAs(UnmanagedType.Interface)] object pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    void DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
    void DragLeave();
    void Drop([In, MarshalAs(UnmanagedType.Interface)] object pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
}

class FileDropTarget : IDropTargetNative
{
    private const short CF_HDROP = 15;
    private const uint TYMED_HGLOBAL = 1;
    private const uint DVASPECT_CONTENT = 1;
    private const uint DROPEFFECT_COPY = 1;
    private const uint DROPEFFECT_NONE = 0;
    private static readonly string[] AllowedExtensions = { ".md", ".markdown", ".txt" };

    private readonly Action<string> _onDrop;

    public FileDropTarget(Action<string> onDrop) => _onDrop = onDrop;

    public void DragEnter(object pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        pdwEffect = HasHDrop(pDataObj) ? DROPEFFECT_COPY : DROPEFFECT_NONE;
    }

    public void DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        pdwEffect = DROPEFFECT_COPY;
    }

    public void DragLeave() { }

    public void Drop(object pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect)
    {
        pdwEffect = DROPEFFECT_NONE;
        try
        {
            var dataObj = pDataObj as System.Runtime.InteropServices.ComTypes.IDataObject;
            if (dataObj == null) return;

            var fmt = new System.Runtime.InteropServices.ComTypes.FORMATETC
            {
                cfFormat = CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
            };

            dataObj.GetData(ref fmt, out var medium);
            var hDrop = (IntPtr)medium.unionmember;
            try
            {
                uint count = NativeMethods.DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
                for (uint i = 0; i < count; i++)
                {
                    var sb = new StringBuilder(260);
                    NativeMethods.DragQueryFile(hDrop, i, sb, 260);
                    var path = sb.ToString();
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (AllowedExtensions.Contains(ext))
                    {
                        _onDrop(path);
                        pdwEffect = DROPEFFECT_COPY;
                    }
                }
            }
            finally
            {
                var stg = new STGMEDIUM
                {
                    tymed = (uint)medium.tymed,
                    unionmember = hDrop,
                    pUnkForRelease = medium.pUnkForRelease != null ? (IntPtr)medium.pUnkForRelease : IntPtr.Zero
                };
                NativeMethods.ReleaseStgMedium(ref stg);
            }
        }
        catch { }
    }

    private static bool HasHDrop(object pDataObj)
    {
        try
        {
            var dataObj = pDataObj as System.Runtime.InteropServices.ComTypes.IDataObject;
            if (dataObj == null) return false;
            var fmt = new System.Runtime.InteropServices.ComTypes.FORMATETC
            {
                cfFormat = CF_HDROP,
                ptd = IntPtr.Zero,
                dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
            };
            return dataObj.QueryGetData(ref fmt) == 0;
        }
        catch { return false; }
    }
}

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

class PreferencesForm : Form
{
    public AppSettings Settings { get; }

    public PreferencesForm(AppSettings settings)
    {
        Settings = settings;
        Text = "Preferences";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new System.Drawing.Size(340, 180);

        var themeLabel = new Label
        {
            Text = "Theme:",
            Location = new System.Drawing.Point(20, 24),
            AutoSize = true
        };

        var themeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new System.Drawing.Point(120, 20),
            Width = 160
        };
        themeCombo.Items.AddRange(new[] { "Light", "Dark" });
        themeCombo.SelectedIndex = settings.DarkMode ? 1 : 0;

        var okBtn = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new System.Drawing.Point(110, 90),
            Width = 80
        };

        var cancelBtn = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new System.Drawing.Point(200, 90),
            Width = 80
        };

        AcceptButton = okBtn;
        CancelButton = cancelBtn;
        Controls.AddRange(new Control[] { themeLabel, themeCombo, okBtn, cancelBtn });

        okBtn.Click += (s, e) =>
        {
            Settings.DarkMode = themeCombo.SelectedIndex == 1;
        };
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
        fileMenu.DropDownItems.Add("&Preferences...", null, (s, e) => ShowPreferences());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("E&xit", null, (s, e) => Close());

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

        // ?
        var helpMenu = new ToolStripMenuItem("?");
        helpMenu.DropDownItems.Add("&About PrettyMark", null, (s, e) => ExecuteJs("showAbout()"));

        menuStrip.Items.AddRange(new ToolStripItem[] { fileMenu, viewMenu, helpMenu });
        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private async void InitializeWebView()
    {
        webView = new WebView2 { Dock = DockStyle.Fill, AllowExternalDrop = false };
        Controls.Add(webView);
        webView.BringToFront();

        await webView.EnsureCoreWebView2Async();

        // Serve assets from local folder via virtual host
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "app.local", assetsDir, CoreWebView2HostResourceAccessKind.Allow);

        // Handle JS messages
        webView.CoreWebView2.WebMessageReceived += OnWebMessage;

        // Block navigation away from our app
        webView.CoreWebView2.NavigationStarting += (s, e) =>
        {
            if (!e.Uri.StartsWith("https://app.local/"))
                e.Cancel = true;
        };

        // Load content once page is ready
        webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;

        // Clean up WebView chrome
        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

        webView.CoreWebView2.Navigate("https://app.local/index.html");
    }

    private void RegisterDropTarget()
    {
        var hwnd = FindChromeWidgetHwnd(webView.Handle);
        if (hwnd != IntPtr.Zero)
        {
            NativeMethods.RevokeDragDrop(hwnd);
            NativeMethods.RegisterDragDrop(hwnd, new FileDropTarget(path =>
                BeginInvoke(() => OpenTab(path))));
        }
    }

    private static IntPtr FindChromeWidgetHwnd(IntPtr parentHwnd)
    {
        IntPtr result = IntPtr.Zero;
        NativeMethods.EnumChildWindows(parentHwnd, (hWnd, lParam) =>
        {
            var sb = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, sb, 256);
            if (sb.ToString() == "Chrome_WidgetWin_0")
            {
                result = hWnd;
                return false; // stop enumeration
            }
            // recurse into children
            var child = FindChromeWidgetHwnd(hWnd);
            if (child != IntPtr.Zero)
            {
                result = child;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private async void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;

        // Register COM drop target
        RegisterDropTarget();

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

    private void ShowPreferences()
    {
        using var form = new PreferencesForm(settings);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            settings.Save();
            SetDarkMode(settings.DarkMode);
        }
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
