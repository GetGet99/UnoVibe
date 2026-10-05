# QuickMarkup

Details on QuickMarkup usage and gotchas, complementing the rules in AGENTS.md.
**Read this file before** writing or editing QuickMarkup markup, or when a QuickMarkup bind does
not compile or a reactive expression does not update.

**Always load the skill** when editing QuickMarkup UI:
`.agents/skills/quickmarkup/SKILL.md` (a copy of the one from the QuickMarkup repo).
The upstream source is `/mnt/Data/Codes/QuickMarkup/wt-master/` (see
[`referenced-projects.md`](referenced-projects.md)).

## Key gotchas

- A `[QuickMarkupConstructor]` method **must call `Init()`** (usually first) or the UI tree never builds.
  Non-UI refs-only classes use `[QuickRefs]` instead (see below) with normal C# constructors.
- **Refs-only models use `[QuickRefs]`:**
  session/chat state, providers, and all reactive models declare refs via `[QuickRefs]` and keep
  their own C# constructors. `[QuickMarkup]` never takes over their constructors, so there is no
  `[QuickMarkupConstructor]`/`Init()` to call.
  - `[QuickRefs]` accepts only ref/computed/async-computed declarations (plus `using` lines) —
    no `<root>`/`<setup>`/tags, and no `provide`/`inject` standalone.
  - `required` refs in `[QuickRefs]` do not generate constructor parameters (unlike
    `[QuickMarkup]`); keep an explicit C# constructor where call sites need one.
  - Do not put `[MemberNotNull]` on a C# constructor (CS0592) — plain constructors track
    assignments directly, so the attribute is unnecessary.
- Only `Reference<T>` fields declared in the `[QuickMarkup("""...""")]` header are reactive.
  Plain `ObservableCollection.Count` in an `if` condition is NOT reactive; with `&&` short-circuiting,
  at least one Reference must be read first to subscribe.
  `ReactiveList<T>` makes `Count`/LINQ natively reactive; the `.Reactive` extension is the
  ObservableCollection equivalent.
- **Keyed `foreach`**:
  - **Keyed**: the message list, the sidebar group/session/MCP loops, and the subagent strip are
    keyed so QuickMarkup reuses elements across wholesale collection rebuilds (Clear+re-Add).
  - **Deliberately unkeyed**: incrementally-mutated collections (single Add/Remove), where a
    keyless foreach is the O(1) fast path and a key adds reconcile overhead.
  - Add a key only for collections rebuilt via Clear+re-Add.
- Two-way binding is `` Property<=>`Var` ``.
  `CheckBox.IsChecked` is `bool?` and two-way binding it to a `bool` field will not compile —
  use `ToggleSwitch` (`IsOn` is `bool`) instead.
- Values in markup are not quoted; use backticks for C# expressions, `<>...</>` for collection-typed
  properties, `if (`expr`) { }` for conditional children.