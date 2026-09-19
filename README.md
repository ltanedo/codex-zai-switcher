# Codex Z.AI Switcher & Mobile Remote Proxy 🔄

A native, ultra-lightweight Windows System Tray application for **OpenAI Codex Desktop** that provides 1-click switching between official **ChatGPT** models and **Z.AI (`glm-5.3-flash`)**, complete with an embedded proxy to fix iPhone/mobile remote control overrides and enable multimodal image input.

---

## 🚀 Key Features

* **⚡ Ultra-Lightweight & Fast**: Written in native C# (.NET Framework 4.8) and compiled directly via Windows built-in `csc.exe`. Single **22 KB** standalone executable using only **~15 MB of RAM** and **0.0% idle CPU**. No Node.js, Electron, or external runtimes required.
* **📱 iPhone Remote Control Fix**: When controlling Codex desktop from the ChatGPT iOS/mobile app, mobile clients force-send OpenAI model tags (`gpt-5.6-sol`). The embedded streaming proxy intercepts and rewrites mobile requests to `glm-5.3-flash` with `high` reasoning effort before forwarding to `https://api.z.ai`.
* **🖼️ Multimodal Vision Support**: Enables `glm-5.3-flash` to natively accept base64 screenshots and photos sent from your iPhone or pasted into the desktop app.
* **🎨 Dynamic System Tray Badges**:
  * 🟣 **Purple "Z" Icon**: Z.AI is active.
  * 🟢 **Green "C" Icon**: Official ChatGPT is active.
  * The currently active provider is automatically grayed out in the menu.
* **🔄 Seamless Process Management**: Cleanly closes and restarts Codex Desktop (`ChatGPT.exe`) on every provider switch so configuration changes take effect immediately.
* **🛡️ Credential Safety**: Reads your existing configuration in `%USERPROFILE%\.codex\config.toml` and preserves all plugins, marketplaces, and MCP servers without altering your tokens.

---

## 📦 What's Inside

```
codex-zai-switcher/
├── CodexSwitcherTray.cs    # Single-file source (Tray UI + Embedded HTTP Proxy)
├── zai.ico                 # 32x32 Purple "Z" icon asset
├── chatgpt.ico             # 32x32 Green "C" icon asset
├── build.bat               # 1-click build script using built-in csc.exe
├── config.example.toml     # Example config.toml configuration
├── models.example.json     # Example models.json with multimodal support
└── README.md               # Documentation
```

---

## 🛠️ Quick Start & Setup

### 1. Configure `models.json`
Codex requires declaring model capabilities in `%USERPROFILE%\.codex\models.json`. Ensure `glm-5.3-flash` and the mobile remote aliases declare image support:

```json
{
  "slug": "glm-5.3-flash",
  "display_name": "glm-5.3-flash",
  "default_reasoning_level": "high",
  "input_modalities": [ "text", "image" ],
  "context_window": 1048576
}
```
*(See [`models.example.json`](models.example.json) for the complete 4-model catalog).*

### 2. Configure `config.toml`
In `%USERPROFILE%\.codex\config.toml`, add the `[model_providers.ZAI]` block pointing to the local proxy:

```toml
model_provider = "ZAI"
model = "glm-5.3-flash"
model_reasoning_effort = "high"
model_catalog_json = "~/.codex/models.json"

[model_providers.ZAI]
name = "ZAI"
base_url = "http://127.0.0.1:8787/api/v1"
experimental_bearer_token = "<YOUR_ZAI_API_KEY>"
wire_api = "responses"
```

> **Tip:** Keep the `[model_providers.ZAI]` section permanently defined in `config.toml`. When you switch to ChatGPT mode, the switcher only modifies the top-level keys (`model = "gpt-6-astra"`). This ensures historical Z.AI chat threads never crash with *"Model provider ZAI not found"*.

### 3. Build & Run
To compile the standalone `.exe` using Windows' built-in C# compiler:
```cmd
build.bat
```
Or run directly from PowerShell:
```powershell
& "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /target:winexe /optimize+ /win32icon:zai.ico /out:CodexSwitcherTray.exe /r:System.dll,System.Core.dll,System.Drawing.dll,System.Windows.Forms.dll,Microsoft.CSharp.dll CodexSwitcherTray.cs
```

Double-click **`CodexSwitcherTray.exe`**. It will appear in your system tray immediately.

---

## 💡 Usage

* **Click or Right-Click the Tray Icon**:
  * `● Active: Z.AI (glm-5.3-flash)` — Shows current status.
  * `Switch to ChatGPT (Official)` — Toggles to OpenAI, restarts Codex.
  * `Switch to Z.AI (glm-5.3-flash)` — Toggles to Z.AI, restarts Codex.
  * `Reload Window (Fix Theme / Ctrl+R)` — Refreshes the Codex webview in place.
  * `Restart Codex (Full Reload)` — Cleanly restarts Codex without switching.
  * `Open config.toml` — Opens your config file in Notepad.
  * `Start with Windows` — Automatically adds/removes shortcut in `shell:startup`.

---

## ⚠️ Known Issues & Troubleshooting

### White Sidebar on Provider Switch
When switching providers, Codex Desktop occasionally renders the left sidebar with an un-composited white background due to an Electron/Chromium Acrylic theme initialization race condition.
* **Fix**: Press **`Ctrl + R`** inside Codex, or click **`Reload Window (Fix Theme / Ctrl+R)`** from the system tray context menu.

---

## 📄 License
MIT License.
