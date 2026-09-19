using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CodexSwitcher
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "tray.log");
            try
            {
                File.AppendAllText(logPath, "[" + DateTime.Now + "] Starting CodexSwitcherTray (PID: " + Process.GetCurrentProcess().Id + ")...\n");

                // Kill any stale instance of CodexSwitcherTray so this launch takes over cleanly
                int currentPid = Process.GetCurrentProcess().Id;
                foreach (var p in Process.GetProcessesByName("CodexSwitcherTray"))
                {
                    if (p.Id != currentPid)
                    {
                        try { p.Kill(); p.WaitForExit(1000); } catch { }
                    }
                }

                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                    File.AppendAllText(logPath, "[" + DateTime.Now + "] AppDomain UnhandledException: " + e.ExceptionObject + "\n");
                };
                Application.ThreadException += (s, e) => {
                    File.AppendAllText(logPath, "[" + DateTime.Now + "] Application ThreadException: " + e.Exception + "\n");
                };

                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApplicationContext());
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, "[" + DateTime.Now + "] Fatal Main Exception: " + ex + "\n");
            }
        }
    }

    public class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _statusHeaderItem;
        private readonly ToolStripMenuItem _switchToChatGptItem;
        private readonly ToolStripMenuItem _switchToZaiItem;
        private readonly ToolStripMenuItem _reloadWindowItem;
        private readonly ToolStripMenuItem _restartCodexItem;
        private readonly ToolStripMenuItem _openConfigItem;
        private readonly ToolStripMenuItem _startWithWindowsItem;
        private readonly ToolStripMenuItem _exitItem;

        private readonly string _codexDir;
        private readonly string _configFile;
        private readonly string _startupShortcut;
        private FileSystemWatcher _configWatcher;

        private HttpListener _proxyListener;
        private CancellationTokenSource _proxyCts;

        private const int PROXY_PORT = 8787;
        private const string TARGET_HOST = "api.z.ai";
        private const string TARGET_MODEL = "glm-5.3-flash";
        private const string TARGET_EFFORT = "high";

        public TrayApplicationContext()
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "tray.log");
            try
            {
                File.AppendAllText(logPath, "[" + DateTime.Now + "] Entering TrayApplicationContext constructor\n");

                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                _codexDir = Path.Combine(userProfile, ".codex");
                _configFile = Path.Combine(_codexDir, "config.toml");

                string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                _startupShortcut = Path.Combine(startupFolder, "CodexSwitcherTray.lnk");

                File.AppendAllText(logPath, "[" + DateTime.Now + "] Creating menu items\n");
                _contextMenu = new ContextMenuStrip();
                _contextMenu.RenderMode = ToolStripRenderMode.System;

                _statusHeaderItem = new ToolStripMenuItem("● Status: Initializing...") { Enabled = false, Font = new Font(Control.DefaultFont, FontStyle.Bold) };
                _switchToChatGptItem = new ToolStripMenuItem("Switch to ChatGPT (Official)", null, OnSwitchToChatGpt);
                _switchToZaiItem = new ToolStripMenuItem("Switch to Z.AI (glm-5.3-flash)", null, OnSwitchToZai);
                _reloadWindowItem = new ToolStripMenuItem("Reload Window (Fix Theme / Ctrl+R)", null, OnReloadCodexWindow);
                _restartCodexItem = new ToolStripMenuItem("Restart Codex (Full Reload)", null, OnRestartCodexClicked);
                _openConfigItem = new ToolStripMenuItem("Open config.toml", null, OnOpenConfigClicked);
                _startWithWindowsItem = new ToolStripMenuItem("Start with Windows", null, OnToggleStartup);
                _exitItem = new ToolStripMenuItem("Exit", null, OnExitClicked);

                _contextMenu.Items.Add(_statusHeaderItem);
                _contextMenu.Items.Add(new ToolStripSeparator());
                _contextMenu.Items.Add(_switchToChatGptItem);
                _contextMenu.Items.Add(_switchToZaiItem);
                _contextMenu.Items.Add(new ToolStripSeparator());
                _contextMenu.Items.Add(_reloadWindowItem);
                _contextMenu.Items.Add(_restartCodexItem);
                _contextMenu.Items.Add(_openConfigItem);
                _contextMenu.Items.Add(_startWithWindowsItem);
                _contextMenu.Items.Add(new ToolStripSeparator());
                _contextMenu.Items.Add(_exitItem);

                File.AppendAllText(logPath, "[" + DateTime.Now + "] Creating NotifyIcon\n");
                _trayIcon = new NotifyIcon
                {
                    ContextMenuStrip = _contextMenu,
                    Visible = false
                };
                _trayIcon.MouseClick += (s, e) => {
                    if (e.Button == MouseButtons.Left) {
                        try {
                            System.Reflection.MethodInfo mi = typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (mi != null) mi.Invoke(_trayIcon, null);
                        } catch { }
                    }
                };
                _trayIcon.DoubleClick += (s, e) => ShowCurrentStatusNotification();

                File.AppendAllText(logPath, "[" + DateTime.Now + "] UpdateStartupCheckmark\n");
                UpdateStartupCheckmark();
                File.AppendAllText(logPath, "[" + DateTime.Now + "] RefreshState\n");
                RefreshState();
                File.AppendAllText(logPath, "[" + DateTime.Now + "] SetupConfigFileWatcher\n");
                SetupConfigFileWatcher();
                File.AppendAllText(logPath, "[" + DateTime.Now + "] StartProxyServer\n");
                StartProxyServer();

                string activeMsg = IsZaiActive() ? "Active: Z.AI (glm-5.3-flash)" : "Active: ChatGPT (Official)";
                _trayIcon.ShowBalloonTip(3500, "Codex Switcher Ready", activeMsg + " (Click tray icon to switch)", ToolTipIcon.Info);

                File.AppendAllText(logPath, "[" + DateTime.Now + "] Constructor completed successfully\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, "[" + DateTime.Now + "] Exception in constructor: " + ex + "\n");
                throw;
            }
        }

        private void SetupConfigFileWatcher()
        {
            try
            {
                if (Directory.Exists(_codexDir))
                {
                    _configWatcher = new FileSystemWatcher(_codexDir, "config.toml")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
                    };
                    _configWatcher.Changed += (s, e) => DebounceRefreshState();
                    _configWatcher.Created += (s, e) => DebounceRefreshState();
                    _configWatcher.EnableRaisingEvents = true;
                }
            }
            catch { }
        }

        private DateTime _lastRefreshTime = DateTime.MinValue;
        private void DebounceRefreshState()
        {
            if ((DateTime.Now - _lastRefreshTime).TotalMilliseconds < 500) return;
            _lastRefreshTime = DateTime.Now;

            // Wait a moment for write completion
            Thread.Sleep(200);
            try
            {
                if (_trayIcon != null && _trayIcon.ContextMenuStrip != null && _trayIcon.ContextMenuStrip.InvokeRequired)
                {
                    _trayIcon.ContextMenuStrip.BeginInvoke(new Action(RefreshState));
                }
                else
                {
                    RefreshState();
                }
            }
            catch { }
        }

        private bool IsZaiActive()
        {
            try
            {
                if (!File.Exists(_configFile)) return false;
                string[] lines = File.ReadAllLines(_configFile);
                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                    if (line.StartsWith("[") && line != "[model_providers.ZAI]") break; // past top-level keys
                    if (line.StartsWith("model_provider") && line.Contains("ZAI"))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void RefreshState()
        {
            bool isZai = IsZaiActive();

            string zaiIconPath = Path.Combine(_codexDir, "zai.ico");
            string chatgptIconPath = Path.Combine(_codexDir, "chatgpt.ico");

            Icon icon = null;
            try
            {
                if (isZai && File.Exists(zaiIconPath))
                {
                    icon = new Icon(zaiIconPath);
                }
                else if (!isZai && File.Exists(chatgptIconPath))
                {
                    icon = new Icon(chatgptIconPath);
                }
            }
            catch { }

            if (icon == null)
            {
                icon = SystemIcons.Application;
            }

            if (isZai)
            {
                _statusHeaderItem.Text = "● Active: Z.AI (glm-5.3-flash)";
                _switchToZaiItem.Enabled = false;
                _switchToZaiItem.Checked = true;
                _switchToChatGptItem.Enabled = true;
                _switchToChatGptItem.Checked = false;
                _trayIcon.Text = "Codex Switcher: Z.AI (glm-5.3-flash)";
            }
            else
            {
                _statusHeaderItem.Text = "● Active: ChatGPT (Official)";
                _switchToZaiItem.Enabled = true;
                _switchToZaiItem.Checked = false;
                _switchToChatGptItem.Enabled = false;
                _switchToChatGptItem.Checked = true;
                _trayIcon.Text = "Codex Switcher: ChatGPT (Official)";
            }

            _trayIcon.Visible = false;
            _trayIcon.Icon = icon;
            _trayIcon.Visible = true;
        }

        private void OnSwitchToChatGpt(object sender, EventArgs e)
        {
            if (!IsZaiActive())
            {
                MessageBox.Show("ChatGPT is already the active provider.", "Codex Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                CloseCodex();
                ApplyConfig(isZai: false);
                RefreshState();
                LaunchCodex(autoFixTheme: true);
                _trayIcon.ShowBalloonTip(3000, "Switched to ChatGPT", "Official OpenAI provider enabled. Codex restarted.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error switching to ChatGPT: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnSwitchToZai(object sender, EventArgs e)
        {
            if (IsZaiActive())
            {
                MessageBox.Show("Z.AI is already the active provider.", "Codex Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                CloseCodex();
                ApplyConfig(isZai: true);
                RefreshState();
                LaunchCodex(autoFixTheme: true);
                _trayIcon.ShowBalloonTip(3000, "Switched to Z.AI", "glm-5.3-flash (High Reasoning) enabled. Codex restarted.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error switching to Z.AI: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplyConfig(bool isZai)
        {
            if (!File.Exists(_configFile))
            {
                throw new FileNotFoundException("Cannot find config.toml at " + _configFile);
            }

            string content = File.ReadAllText(_configFile, Encoding.UTF8);

            // Extract existing notify line dynamically if present
            string notifyLine = "";
            Match notifyMatch = Regex.Match(content, @"notify\s*=\s*\[[^\]]+\]", RegexOptions.Singleline);
            if (notifyMatch.Success)
            {
                notifyLine = notifyMatch.Value;
            }

            // Find start of [model_providers.ZAI] or first section bracket
            int providerIndex = content.IndexOf("[model_providers.ZAI]", StringComparison.OrdinalIgnoreCase);
            string remainder;
            if (providerIndex >= 0)
            {
                remainder = content.Substring(providerIndex);
            }
            else
            {
                // Fallback: look for any [marketplaces or [plugins
                int bracketIndex = content.IndexOf('[');
                remainder = bracketIndex >= 0 ? content.Substring(bracketIndex) : "";
            }

            StringBuilder sb = new StringBuilder();
            if (isZai)
            {
                sb.AppendLine("model_provider = \"ZAI\"");
                sb.AppendLine("model = \"glm-5.3-flash\"");
                sb.AppendLine("model_reasoning_effort = \"high\"");
                sb.AppendLine("model_catalog_json = \"~/.codex/models.json\"");
                if (!string.IsNullOrEmpty(notifyLine))
                {
                    sb.AppendLine(notifyLine);
                }
                sb.AppendLine();
            }
            else
            {
                // In official ChatGPT mode, omit model_catalog_json so Codex Desktop
                // restores its native, official OpenAI model catalog and dropdown.
                sb.AppendLine("model = \"gpt-6-astra\"");
                sb.AppendLine("model_reasoning_effort = \"high\"");
                sb.AppendLine("service_tier = \"priority\"");
                if (!string.IsNullOrEmpty(notifyLine))
                {
                    sb.AppendLine(notifyLine);
                }
                sb.AppendLine();
            }

            sb.Append(remainder);
            File.WriteAllText(_configFile, sb.ToString(), Encoding.UTF8);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private const uint WM_CLOSE = 0x0010;

        private void CloseCodex()
        {
            try
            {
                IntPtr hWnd = FindWindow("Chrome_WidgetWin_1", "ChatGPT");
                if (hWnd != IntPtr.Zero)
                {
                    PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }

                foreach (var p in Process.GetProcessesByName("ChatGPT"))
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            PostMessage(p.MainWindowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                    catch { }
                }

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2500)
                {
                    if (Process.GetProcessesByName("ChatGPT").Length == 0) break;
                    Thread.Sleep(150);
                }

                foreach (var p in Process.GetProcessesByName("ChatGPT"))
                {
                    try { p.Kill(); p.WaitForExit(500); } catch { }
                }
                foreach (var p in Process.GetProcessesByName("codex"))
                {
                    try { p.Kill(); p.WaitForExit(500); } catch { }
                }
            }
            catch { }
        }

        private void LaunchCodex(bool autoFixTheme)
        {
            Thread.Sleep(1500);

            try
            {
                Process.Start("explorer.exe", @"shell:AppsFolder\OpenAI.Codex_2p2nqsd0c76g0!App");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not launch Codex: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (autoFixTheme)
            {
                // Give the window 2.5 seconds to open, then trigger a quick reload to ensure theme renders in dark mode
                Task.Factory.StartNew(() =>
                {
                    Thread.Sleep(2500);
                    if (_trayIcon != null && _trayIcon.ContextMenuStrip != null)
                    {
                        try
                        {
                            _trayIcon.ContextMenuStrip.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    IntPtr hWnd = FindWindow("Chrome_WidgetWin_1", "ChatGPT");
                                    if (hWnd != IntPtr.Zero)
                                    {
                                        SetForegroundWindow(hWnd);
                                        Thread.Sleep(50);
                                        SendKeys.SendWait("^r");
                                    }
                                }
                                catch { }
                            }));
                        }
                        catch { }
                    }
                });
            }
        }

        private void RestartCodex(bool autoFixTheme = false)
        {
            CloseCodex();
            LaunchCodex(autoFixTheme);
        }

        private void OnReloadCodexWindow(object sender, EventArgs e)
        {
            try
            {
                IntPtr hWnd = FindWindow("Chrome_WidgetWin_1", "ChatGPT");
                if (hWnd != IntPtr.Zero)
                {
                    SetForegroundWindow(hWnd);
                    Thread.Sleep(100);
                    SendKeys.SendWait("^r");
                    _trayIcon.ShowBalloonTip(1500, "Window Reloaded", "Sent Ctrl+R to reload Codex webview and re-evaluate theme.", ToolTipIcon.Info);
                }
                else
                {
                    MessageBox.Show("Codex window not found. Try 'Restart Codex' instead.", "Codex Switcher", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not reload window: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRestartCodexClicked(object sender, EventArgs e)
        {
            RestartCodex();
            _trayIcon.ShowBalloonTip(2000, "Codex Restarted", "Codex processes were gracefully closed and relaunched.", ToolTipIcon.Info);
        }

        private void OnOpenConfigClicked(object sender, EventArgs e)
        {
            try
            {
                Process.Start("notepad.exe", _configFile);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open config: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateStartupCheckmark()
        {
            _startWithWindowsItem.Checked = File.Exists(_startupShortcut);
        }

        private void OnToggleStartup(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(_startupShortcut))
                {
                    File.Delete(_startupShortcut);
                    _startWithWindowsItem.Checked = false;
                    _trayIcon.ShowBalloonTip(2000, "Startup Disabled", "Codex Switcher will not start automatically with Windows.", ToolTipIcon.Info);
                }
                else
                {
                    CreateStartupShortcut();
                    _startWithWindowsItem.Checked = true;
                    _trayIcon.ShowBalloonTip(2000, "Startup Enabled", "Codex Switcher will start automatically on login.", ToolTipIcon.Info);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to toggle startup: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateStartupShortcut()
        {
            string exePath = Application.ExecutablePath;
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(_startupShortcut);
            shortcut.TargetPath = exePath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
            shortcut.Description = "Codex Provider Switcher & Local Z.AI Proxy";
            shortcut.Save();
        }

        private void ShowCurrentStatusNotification()
        {
            bool isZai = IsZaiActive();
            string status = isZai ? "Z.AI (glm-5.3-flash @ High Reasoning)" : "ChatGPT (Official - gpt-6-astra)";
            _trayIcon.ShowBalloonTip(2500, "Active Provider", status, ToolTipIcon.Info);
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            StopProxyServer();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            Application.Exit();
        }

        #region Embedded High-Performance Proxy Server

        private void StartProxyServer()
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "tray.log");
            _proxyCts = new CancellationTokenSource();
            try
            {
                _proxyListener = new HttpListener();
                _proxyListener.Prefixes.Add("http://127.0.0.1:" + PROXY_PORT + "/");
                _proxyListener.Start();
                File.AppendAllText(logPath, "[" + DateTime.Now + "] Proxy listener started on port " + PROXY_PORT + "\n");
                Task.Factory.StartNew(() => ProxyListenLoop(_proxyCts.Token), TaskCreationOptions.LongRunning);
            }
            catch (Exception ex)
            {
                File.AppendAllText(logPath, "[" + DateTime.Now + "] Proxy listen error: " + ex + "\n");
            }
        }

        private void StopProxyServer()
        {
            try
            {
                if (_proxyCts != null) _proxyCts.Cancel();
                if (_proxyListener != null && _proxyListener.IsListening)
                {
                    _proxyListener.Stop();
                    _proxyListener.Close();
                }
            }
            catch { }
        }

        private void ProxyListenLoop(CancellationToken ct)
        {
            string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "tray.log");
            File.AppendAllText(logPath, "[" + DateTime.Now + "] ProxyListenLoop entered\n");
            while (!ct.IsCancellationRequested && _proxyListener != null && _proxyListener.IsListening)
            {
                try
                {
                    HttpListenerContext context = _proxyListener.GetContext();
                    ThreadPool.QueueUserWorkItem(s => ProcessProxyRequest(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    File.AppendAllText(logPath, "[" + DateTime.Now + "] ProxyListenLoop exception: " + ex + "\n");
                }
            }
            File.AppendAllText(logPath, "[" + DateTime.Now + "] ProxyListenLoop exited\n");
        }

        private void ProcessProxyRequest(HttpListenerContext context)
        {
            HttpListenerRequest req = context.Request;
            HttpListenerResponse res = context.Response;

            try
            {
                if (req.Url.AbsolutePath == "/health")
                {
                    byte[] healthMsg = Encoding.UTF8.GetBytes("{\"status\":\"ok\",\"engine\":\"unified-csharp\",\"targetModel\":\"" + TARGET_MODEL + "\",\"targetEffort\":\"" + TARGET_EFFORT + "\"}");
                    res.ContentType = "application/json";
                    res.StatusCode = 200;
                    res.OutputStream.Write(healthMsg, 0, healthMsg.Length);
                    res.Close();
                    return;
                }

                // Read request body
                byte[] bodyBuffer;
                using (MemoryStream ms = new MemoryStream())
                {
                    req.InputStream.CopyTo(ms);
                    bodyBuffer = ms.ToArray();
                }

                // Rewrite JSON payloads if needed (e.g. gpt-5.6-sol -> glm-5.3-flash)
                if (bodyBuffer.Length > 0 && req.ContentType != null && req.ContentType.IndexOf("application/json", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    try
                    {
                        string json = Encoding.UTF8.GetString(bodyBuffer);
                        // Rewrite model to glm-5.3-flash
                        json = Regex.Replace(json, @"""model""\s*:\s*""[^""]+""", "\"model\": \"" + TARGET_MODEL + "\"");
                        // Rewrite reasoning effort
                        json = Regex.Replace(json, @"""reasoning_effort""\s*:\s*""[^""]+""", "\"reasoning_effort\": \"" + TARGET_EFFORT + "\"");
                        json = Regex.Replace(json, @"""effort""\s*:\s*""[^""]+""", "\"effort\": \"" + TARGET_EFFORT + "\"");
                        bodyBuffer = Encoding.UTF8.GetBytes(json);
                    }
                    catch { }
                }

                // Forward to https://api.z.ai
                string targetUrl = "https://" + TARGET_HOST + req.RawUrl;
                HttpWebRequest webReq = (HttpWebRequest)WebRequest.Create(targetUrl);
                webReq.Method = req.HttpMethod;
                webReq.Host = TARGET_HOST;
                webReq.KeepAlive = true;

                // Copy headers
                foreach (string headerKey in req.Headers.AllKeys)
                {
                    if (headerKey.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                        headerKey.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                        headerKey.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                        headerKey.Equals("Expect", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (headerKey.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        webReq.ContentType = req.ContentType;
                        continue;
                    }
                    try
                    {
                        webReq.Headers.Add(headerKey, req.Headers[headerKey]);
                    }
                    catch { }
                }

                if (bodyBuffer.Length > 0)
                {
                    webReq.ContentLength = bodyBuffer.Length;
                    using (Stream requestStream = webReq.GetRequestStream())
                    {
                        requestStream.Write(bodyBuffer, 0, bodyBuffer.Length);
                    }
                }
                else
                {
                    webReq.ContentLength = 0;
                }

                // Stream response back to client
                try
                {
                    using (HttpWebResponse webRes = (HttpWebResponse)webReq.GetResponse())
                    {
                        CopyWebResponse(webRes, res);
                    }
                }
                catch (WebException wex)
                {
                    HttpWebResponse errRes = wex.Response as HttpWebResponse;
                    if (errRes != null)
                    {
                        CopyWebResponse(errRes, res);
                    }
                    else
                    {
                        res.StatusCode = 502;
                        byte[] errBytes = Encoding.UTF8.GetBytes("{\"error\":\"Proxy error: " + wex.Message.Replace("\"", "'") + "\"}");
                        res.ContentType = "application/json";
                        res.OutputStream.Write(errBytes, 0, errBytes.Length);
                        res.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    res.StatusCode = 500;
                    byte[] errBytes = Encoding.UTF8.GetBytes("{\"error\":\"Internal error: " + ex.Message.Replace("\"", "'") + "\"}");
                    res.ContentType = "application/json";
                    res.OutputStream.Write(errBytes, 0, errBytes.Length);
                    res.Close();
                }
                catch { }
            }
        }

        private static void CopyWebResponse(HttpWebResponse from, HttpListenerResponse to)
        {
            to.StatusCode = (int)from.StatusCode;
            to.ContentType = from.ContentType;

            foreach (string key in from.Headers.AllKeys)
            {
                if (key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("Server", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    to.Headers.Add(key, from.Headers[key]);
                }
                catch { }
            }

            using (Stream fromStream = from.GetResponseStream())
            {
                fromStream.CopyTo(to.OutputStream);
            }
            to.Close();
        }

        #endregion
    }
}
