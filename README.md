# FURSOY Browser

A personal hobby and exploration project: building a custom Windows desktop browser using **C#**, **.NET 8**, **WPF**, and **CefSharp** (embedded Chromium).

> **Note & Scope:**  
> This is a personal sandbox / fun build exploring browser mechanics, custom UI vector geometry, and native Chromium embedding. It is not intended as a commercial browser replacement for daily drivers like Chrome or Firefox, but rather a hands-on exploration of how browser tabs, omnibox systems, session persistence, and Chromium handlers fit together.

![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)
![Framework](https://img.shields.io/badge/.NET-8.0-purple)
![Engine](https://img.shields.io/badge/engine-CefSharp%20%2F%20Chromium-green)
![Type](https://img.shields.io/badge/project-personal%20%2F%20hobby-orange)
![Tests](https://img.shields.io/badge/smoke%20tests-273%20passed-brightgreen)

---

## What's Inside

### 1. Chromium-Accurate Tab Silhouette
- Custom tab path rendering (`TabShape.cs`) mathematically modeled after Chromium's `tab_style.cc` and `layout_constants.cc`.
- Convex top corners, concave arcs flaring into the toolbar, and exact 18px tab overlap geometry.
- Responsive tab sizing, smooth hover/active close buttons, and pinned tab support.

### 2. Intelligent Omnibox
- **URL Resolution:** Automatically distinguishes local addresses, IP addresses, full URLs, and search engine queries (`NavigationResolver`).
- **Search Engines:** Support for Google, Bing, and DuckDuckGo.
- **Inline Autocomplete:** Auto-completes domain matches directly in the input box.
- **Suggestions Dropdown:** Real-time dropdown providing history and web search suggestions.

### 3. Integrated Downloads System
- Built-in internal downloads page at `fursoy://downloads`.
- Persistent download tracking via `DownloadHistoryStore`.
- Animated downloads toolbar popup with DPI-aware shadow positioning (`ToolbarPopupAnchor`).

### 4. Native Browser Handlers & Controls
- **In-Page Search (Ctrl+F):** Find bar with match count, previous/next navigation, and term highlighting.
- **Custom Context Menu:** Native-styled Chromium context menu (Inspect Element, Open Link in New Tab, Save Image, View Source).
- **Keyboard Shortcuts:** Standard browser bindings (Ctrl+T, Ctrl+W, Ctrl+R, F5, Ctrl+Tab, Ctrl+Shift+T, Ctrl+J, Ctrl+H, Ctrl+Plus/Minus/0 zoom).
- **Session Persistence:** Saves and restores open tabs, active index, and pinned states across launches (`BrowserSessionStore`).
- **Dynamic Theming:** Light and Dark themes referencing actual Google Chrome color tokens (`LightColors.xaml`, `Colors.xaml`).
- **Favicon Caching:** In-memory and persistent icon caching for tabs and bookmarks.

---

## Stability & Verification

The project includes an automated smoke test suite (`Fursoy.Browser.SmokeTests`) covering 273 regression and stability assertions:
- Toolbar popup anchor calculations and multi-monitor DPI scaling assertions.
- Navigation resolver tests (strict IPv4, loopback, relative paths, search engines).
- Session store normalization, tab count limits, and pinned tab clamping.
- Download entry state machines and URL sanitizer rules.

---

## Building and Running

### Prerequisites
- Windows 10 or 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Visual Studio 2022 (recommended) or VS Code with C# Dev Kit

### Steps

1. Clone the repository:
   ```bash
   git clone https://github.com/FURSOY/fursoy-browser.git
   cd fursoy-browser
   ```

2. Build the solution:
   ```bash
   dotnet build FursoyBrowser.sln
   ```

3. Launch the browser:
   ```bash
   dotnet run --project src/Fursoy.Browser.App/FursoyBrowser.csproj
   ```

4. Run the automated smoke tests:
   ```bash
   dotnet run --project tests/Fursoy.Browser.SmokeTests/Fursoy.Browser.SmokeTests.csproj
   ```

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
