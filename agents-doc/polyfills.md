# Polyfills (platform folder pickers)

Reference for the per-OS `FolderPicker` polyfills and the `WindowsHelper` routing.
**Read this file when** touching folder pickers, `WindowsHelper`, `WindowsHelper.PickFolderAsync`
or `PickFolderResult`, any file under `UnoVibe/Polyfills/*`, or the csproj gating for them.
Desktop notifications have their own polyfills — see [`notifications.md`](notifications.md).

`UnoVibe/Polyfills/{Linux,MacOS,Windows}/` holds one-file-per-OS polyfills of the WinAppSDK
**`Microsoft.Windows.Storage.Pickers.FolderPicker`** so every platform's folder dialog can open at
an **exact path** (`WindowsHelper.PickFolderAsync`'s `startPath` = the window's folder). Uno's
built-in `Windows.Storage.Pickers.FolderPicker` has no exact-path control, which the WASDK 2.0
picker (`SuggestedStartFolder` = the path) does have. See
https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.windows.storage.pickers.folderpicker.
`WindowsHelper.PickFolderAsync` routes `#if WASDK` → WASDK picker, `#elif DESKTOP_LINUX ||
DESKTOP_MACOS` → the polyfill, else the classic fallback; callers pass the app `Window` and get a
`PickFolderResult?.Path`.

On the **WASDK** target the folder picker is the Windows App SDK picker (needs the `WindowId`
in its constructor, so no `InitializeWithWindow`; returns `.Path`, not a `StorageFolder`).
Only desktop-Windows Skia falls back to the classic picker + `InitializeWithWindow`, where the
start path is ignored (that API has no exact-path control).

## Conventions for every polyfill file

- Each file starts and ends with a single `#if DESKTOP_LINUX` / `#if DESKTOP_MACOS` /
  `#if DESKTOP_WINDOWS` guard (one guard per file, matching the file's folder). The OS-specific
  code is what makes the file exist; the constants are defined per-TFM in the csproj (see
  "Compile-time OS constants" in AGENTS.md), so the guard just keeps disabled targets from
  compiling it.
- The class registers itself app-wide as `FolderPicker` via a top-level
  `global using` alias (the app never `#if`s this — callers use the one polyfilled name).
  An alias can appear in only one file, so never duplicate it.
- **API shape mirrors the WASDK picker** with two deliberate deviations: the constructor takes the
  app `Window` instead of a `WindowId`, and shared props/methods cover the subset the app uses
  (`SuggestedStartFolder` exact path, `PickSingleFolderAsync()` → null on cancel; native failures
  throw). Getters/setters are plain .NET properties.
- **Keep the per-OS dependency footprint minimal** — a platform needs only what it already ships:
  - Desktop **Linux** talks to the XDG desktop portal over the session D-Bus, so it needs the
    Tmds.DBus packages (the same versions Uno's own X11 picker uses). C# interfaces are generated
    from the minimal XML files under `UnoVibe/Polyfills/Linux/dbus-interfaces/`, wired to the
    generator via csproj `AdditionalFiles` items.
  - Desktop **macOS** drives `NSOpenPanel` through the Objective-C runtime with
    `[LibraryImport("libobjc.A.dylib")]` stubs (libobjc ships with macOS, so **no new
    dependency**). Because the interop source generator emits `unsafe` blocks, macOS desktop
    builds set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in the csproj.
- **Gating in the csproj is per-OS**, matching the `DefineConstants` conditions (see
  "Compile-time OS constants" in AGENTS.md) — one ItemGroup each, not two.

## FolderPicker polyfill implementation notes

- Copy the Linux file's `#if` + `global using` + ctor/props/method shape for a future polyfill.
- **Linux D-Bus flow** (mirrors Uno's `X11FolderPicker`): session D-Bus → portal `Desktop` →
  check version → subscribe the `Response` signal to the expected request path **before** calling
  `OpenFile` (portal race warning) → `OpenFileAsync` with `directory=true` and `current_folder`
  = the start path → validate the returned request path, await the Response, take `uris[0]`.
  Response codes: 0 = success, 1 = user cancelled. Empty `parent_window` means "no parent".
- **macOS flow**: build `NSOpenPanel` via `objc_msgSend`; if the start folder exists, set it;
  run modal → OK/cancelled → read the path string.
  **Always enqueue onto the main UI dispatcher before presenting** — AppKit crashes on reentrant
  presentation from an in-flight pointer handler. When off the UI thread (no queue), run inline
  as a best effort.