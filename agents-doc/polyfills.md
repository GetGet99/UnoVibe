# Polyfills (platform folder pickers)

Reference for the per-OS `FolderPicker` polyfills and the `WindowsHelper` routing.
**Read this file when** touching folder pickers, `WindowsHelper`, or any file under
`UnoVibe/Polyfills/*`.
Desktop notifications have their own polyfills — see [`notifications.md`](notifications.md).

## Why they exist

Uno's built-in folder picker has no exact-path control, which the WASDK 2.0 picker does have.
`UnoVibe/Polyfills/` holds one-file-per-OS polyfills of the WinAppSDK picker shape so every
platform's folder dialog can open at an exact path. `WindowsHelper.PickFolderAsync` routes
`#if WASDK` → WASDK picker, `#elif DESKTOP_LINUX || DESKTOP_MACOS` → the polyfill,
else the classic fallback (where the start path is ignored — that API has no exact-path
control). Callers pass the app `Window` and get a `PickFolderResult?.Path`.

## Conventions for every polyfill file

- Each file is guarded by a single per-OS `#if` matching the file's folder (the constants are
  defined per-TFM in the csproj — see "Compile-time OS constants" in AGENTS.md).
- The class registers itself app-wide as `FolderPicker` via a top-level `global using` alias
  (the app never `#if`s this — callers use the one polyfilled name).
  An alias can appear in only one file, so never duplicate it.
- **API shape mirrors the WASDK picker** with two deliberate deviations: the constructor takes the
  app `Window` instead of a `WindowId`, and shared props/methods cover the subset the app uses
  (exact-path start folder, `PickSingleFolderAsync()` → null on cancel; native failures throw).
- **Keep the per-OS dependency footprint minimal** — a platform needs only what it already ships:
  Linux talks to the XDG desktop portal over the session D-Bus (Tmds.DBus packages, same versions
  Uno's own picker uses; C# interfaces are generated from the minimal XML files via csproj
  `AdditionalFiles`). macOS drives `NSOpenPanel` through the Objective-C runtime with
  `[LibraryImport("libobjc.A.dylib")]` stubs (libobjc ships with macOS, so **no new
  dependency**); macOS desktop builds set `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` because
  the interop source generator emits `unsafe` blocks.
- **Gating in the csproj is per-OS**, matching the `DefineConstants` conditions — one ItemGroup
  each, not two.
