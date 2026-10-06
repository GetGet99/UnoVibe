# Test page (tool-view harness)

Dev-only harness for reproducing tool-card bugs without a live agent.
**Read this file when** editing `UnoVibe/Pages/Test/*` or adding fixtures for other tool views.

- Opening it: set `UNOVIBE_TEST_PAGE=1` (or `true`) before launch. `App.CreateWindow`
  routes to `WindowController.ShowTest()` instead of the connect flow, so no server is needed.
- VS Code: run the `Uno Platform Desktop Debug (Test Page)` config in `.vscode/launch.json`
  (mirrors `Uno Platform Desktop Debug`, adds the env flag, passes no server arg).
- `TestPage` owns one `ToolCallPartItem` (`Part`) rendered through the real `ToolViewEdit`.
  Buttons walk it through `pending` (grey spinner) → `running` (yellow spinner) →
  `completed` (diff present, click the header to expand), and Reset swaps in a fresh pending part.
- Fixtures live in `TestEditParts` (`CreatePending`/`MarkRunning`/`MarkCompleted`). They mirror
  the flattened fields `MessageJsonHelper.ApplyToolPart` sets (`ToolStatus`, `ToolFilePath`,
  `ToolTitle`, `Diff`, `ToolOutput`) plus `State`, keeping `null` for absent (null-means-absent).
- To cover another tool view: add a `Test<Type>Parts` fixture and render its card next to the edit one.
