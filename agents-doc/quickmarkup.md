# QuickMarkup

The skill is the source of truth for markup syntax and reactive rules.
**Read this file before** writing or editing QuickMarkup markup.

**Always load the skill** when editing QuickMarkup UI:
`.agents/skills/quickmarkup/SKILL.md` (a copy of the one from the QuickMarkup repo).
The upstream source is in [`referenced-projects.md`](referenced-projects.md).

## Gotchas the skill does not cover

- `CheckBox.IsChecked` is `bool?` and two-way binding it to a `bool` field will not compile —
  use `ToggleSwitch` (`IsOn` is `bool`) instead.
