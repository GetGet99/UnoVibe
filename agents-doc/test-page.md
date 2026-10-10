# Test page (tool-view harness)

Dev-only harness for reproducing tool-card bugs without a live agent.
**Read this file when** editing `UnoVibe/Pages/Test/*` or adding fixtures for other tool views.

- Opening it: set `UNOVIBE_TEST_PAGE=1` (or `true`) before launch. The app routes to the test
  page instead of the connect flow, so no server is needed.
- The page owns one `ToolCallPartItem` rendered through the real tool card. Buttons walk it
  through pending → running → completed, and Reset swaps in a fresh pending part.
- Fixtures mirror the flattened fields the tool-part mapping sets, keeping `null` for absent
  (null-means-absent). To cover another tool view: add a fixture and render its card next to
  the edit one.
