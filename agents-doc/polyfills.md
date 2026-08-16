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

On the **WASDK** target the folder picker is the Windows App SDK's
`Microsoft.Windows.Storage.Pickers.FolderPicker` (relies on the `Microsoft.WindowsAppSDK` package's
`StoragePickersContract`, not Uno). It takes the `WindowId` (`window.AppWindow.Id`) in its
constructor, so it needs **no** `InitializeWithWindow`; its `PickSingleFolderAsync` returns a
`PickFolderResult` (`.Path`), not a `StorageFolder`. The `startPath` (set via `SuggestedStartFolder`)
is the current window path, i.e. `ChatStore.ServerDirectory`. Only `DESKTOP_WINDOWS` (a Skia build
running on Windows) falls back to the classic `Windows.Storage.Pickers.FolderPicker` +
`InitializeWithWindow`, where `startPath` is ignored (that API has no exact-path control). Both call
sites pass `Store.ServerDirectory` (`SessionSidebar`'s Open Folder / `ConnectPage`'s folder pick).

## Conventions for every polyfill file

- Each file starts and ends with a single `#if DESKTOP_LINUX` / `#if DESKTOP_MACOS` /
  `#if DESKTOP_WINDOWS` guard (one guard per file, matching the file's folder). The OS-specific
  code is what makes the file exist; the constants are defined per-TFM in the csproj (see
  "Compile-time OS constants" in AGENTS.md), so the guard just keeps disabled targets from
  compiling it.
- The class registers itself app-wide as `FolderPicker` via a top-level
  `global using FolderPicker = UnoVibe.Polyfills.<OS>.FolderPicker;` (the app never `#if`s this —
  callers use the one polyfilled name, and the WASDK/classic branches live inside
  `WindowsHelper`). `PickFolderResult` is a separate sibling file that owns its own
  `global using PickFolderResult = ...;` alias — an alias can appear in only one file, so never
  duplicate it.
- **API shape mirrors the WASDK picker** with two deliberate deviations: the constructor takes the
  app `Window` instead of a `WindowId`, and shared props are `SuggestedStartFolder` (exact path),
  `SuggestedFolder` (fallback), `SuggestedStartLocation` (a `PickerLocationId`), `CommitButtonText`,
  `Title`, `SettingsIdentifier` (macOS only). Methods: `PickSingleFolderAsync()` →
  `Task<PickFolderResult?>` (null = user cancelled; native failures throw). Getters/setters are
  plain .NET properties.
- **Keep the per-OS dependency footprint minimal** — the whole point of the polyfills is that a
  platform needs only what it already ships:
  - Desktop **Linux** talks to the XDG desktop portal over the session D-Bus, so it needs
    `Tmds.DBus.Protocol` + `Tmds.DBus.Generator` 0.92.0 — the same versions Uno's own X11 picker
    uses (`~/.nuget/packages/tmds.dbus.*`). C# interfaces are generated from the minimal XML files
    under `UnoVibe/Polyfills/Linux/dbus-interfaces/`
    (`org.freedesktop.portal.FileChooser.xml`, `org.freedesktop.portal.Request.xml`, plus
    `org.freedesktop.Notifications.xml` for toasts), wired to the `Tmds.DBus.Generator` source
    generator via csproj `AdditionalFiles` items (Namespace `UnoVibe.Polyfills.Linux.DBus`,
    `GenerateDBusTypes="true"`). The generated types land under
    `UnoVibe/obj/<tfm>/generated/Tmds.DBus.Generator/.../UnoVibe.Polyfills.Linux.DBus.g.cs`.
  - Desktop **macOS** drives `NSOpenPanel` through the Objective-C runtime with
    `[LibraryImport("libobjc.A.dylib")]` stubs in a `static partial` class (libobjc is part of
    macOS, so **no new dependency**). Because the interop source generator emits
    `unsafe` blocks, macOS desktop builds set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in
    the csproj (Uno.Sdk enables unsafe only for the WinAppSDK target).
- **Gating in the csproj is per-OS**, matching the `DefineConstants` conditions above: the Tmds
  packages + `AdditionalFiles` are referenced only for desktop-Linux builds, and `AllowUnsafeBlocks`
  only for desktop-macOS builds (both via the same `$(TargetFramework)`, `$(RuntimeIdentifier)`, and
  `[MSBuild]::IsOSPlatform(...)` combination used for the constants — one ItemGroup each, not two).

## FolderPicker polyfill implementation notes

- The canonical example is `UnoVibe/Polyfills/Linux/FolderPicker.cs` (the old
  `SampleFolderPicker.cs` was deleted). Copy its `#if` + `global using` + ctor/props/Method shape
  for a future polyfill.
- **Linux D-Bus flow** (from Uno's `X11FolderPicker` in `uno/src/Uno.UI.Runtime.Skia.X11/...`):
  session D-Bus → `org.freedesktop.portal.Desktop` `/org/freedesktop/portal/desktop` → check
  `version >= 3` → subscribe `org.freedesktop.portal.Request`'s `Response` signal to the expected
  request path **before** calling `OpenFile` (portal race warning) → `OpenFileAsync("",
  title, options)` with `handle_token`, `accept_label`, `multiple=false`, `directory=true`,
  and `current_folder` = the start path NUL-terminated in UTF-8 → validate the returned request path
  equals the expected `.../request/<unique-names-sans-colons>/<handle_token>`, await the Response,
  take `uris[0]` → `new Uri(...).LocalPath`. Response codes: 0 = success, 1 = user cancelled. Empty
  `parent_window` means "no parent" — `WindowNative.GetWindowHandle` here only exposes the fake
  `AppWindow.Id`, so always pass `""`.
- **macOS flow**: build `NSOpenPanel` via `objc_msgSend` (`openPanel`,
  `setCanChooseDirectories:`, `setCanChooseFiles:`, `setAllowsMultipleSelection:`); if the start
  folder exists, `setDirectoryURL:` (from a `NSURL` built via `fileURLWithPath:`); run `runModal`
  → 1 = OK, 0 = cancelled → `URL` → `path` → `UTF8String` → `Marshal.PtrToStringUTF8`.
  **Always enqueue onto the main UI dispatcher before `runModal`** — AppKit crashes on reentrant
  presentation from an in-flight pointer handler, and `DispatcherQueue.GetForCurrentThread()` wraps
  Uno's main dispatcher (native `AppWindow.DispatcherQueue` is unimplemented on Skia), so call it
  from the UI thread. When off the UI thread (no queue), run inline as a best effort.