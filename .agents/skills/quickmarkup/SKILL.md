---
name: quickmarkup
description: Write and edit QuickMarkup declarative UI markup in C# projects. Use when the project uses QuickMarkup, identifiable by `using QuickMarkup.Infra`, `using QuickMarkup.SourceGen`, or `[QuickMarkup(...)]` attribute in .cs files.
---

# QuickMarkup

QuickMarkup is a Vue-inspired declarative markup DSL embedded in C# that replaces XAML for UI declaration. It uses a **reactivity system** (not MVVM).

## How It Works

QuickMarkup code is placed inside a `[QuickMarkup("""...""")]` attribute on a `partial class`.

```csharp
[QuickMarkup("""
    int Counter = 0;
    <root>
        <StackPanel>
            <Button Text="Click Me" @Click+=`Counter++` />
            <TextBlock Text=`$"You clicked {Counter} time(s)"` />
        </StackPanel>
    </root>
    """)]
partial class CounterPage : Page;
```

A source generator processes the attribute and generates public constructors automatically. If you need custom initialization logic (e.g., setting up services, loading data), mark a method with `[QuickMarkupConstructor]`:

```csharp
[QuickMarkup("""
    int Counter = 0;
    <root>/* ... */</root>
    """)]
partial class MyPage : Page
{
    [QuickMarkupConstructor]
    private void MyInit()
    {
        // Custom setup before UI tree is built
        LoadSettings();
        Init(); // Must call Init() to build the UI tree
    }
}
```

The generated constructor:
- Without `[QuickMarkupConstructor]`: calls `Init()` automatically — no user code needed.
- With `[QuickMarkupConstructor]`: calls the user's method. The user **must call `Init()` explicitly** from their method to build the UI tree.
- The `Action<T>` callback (if used) runs **before** the `[QuickMarkupConstructor]` method, so upstream properties are already set when the user's method runs.

**Fallback (backward-compatible) mode:** If you declare an explicit C# constructor without `[QuickMarkupConstructor]`, the generator emits a `private void Init()` method that you must call manually from your constructor. This is supported for legacy code but not recommended for new code.

## Sections (in order)

1. **Usings** (optional) — namespace imports for the markup scope. Supports aliases (`using NavigationView = Microsoft.UI.Xaml.Controls.NavigationView;`) and `using static`. Global usings from C# files also apply.
2. **Reference/Computed/AsyncComputed declarations** (optional) — reactive variable declarations.
3. **`<setup>`** (optional) — C# code that runs before UI creation. Variables declared here are accessible in `<root>` but not exported outside.
4. **`<root>`** — the UI tree. This is where the markup goes.

## Reference Declarations

Declaring variables outside `<setup>` creates reactive references. The generated code wraps them in `Reference<T>` with a property getter/setter.

```
double Value = 0;          // creates Reference<double>, property Value, backing field ValueProp
double Output => `A + B`;  // creates Computed<double>, property Output, backing field OutputComp
User MyUser => async `Api.FetchUserAsync(Id)`; // creates AsyncComputed<User>, properties MyUser, MyUserStatus, MyUserFailure, backing field MyUserAsync
```

References auto-notify the UI on change. Computed variables cache and re-evaluate when dependencies change. Computed variables are lazily initialized — not evaluated until first accessed.

References get a `*Prop` backing field and computed get a `*Comp` backing field on the partial class, accessible directly if needed. Async computed gets `*Async` backing field (`AsyncComputed<T>`), plus `*Status` (`AsyncComputedState`) and `*Failure` (`Exception?`) properties. The value property throws if not yet loaded — check `*Status` first.

### Generic types

If you want to use generics inside reference declaration, wrap the type in `` `backticks` ``.

```
`MyGenericType<int>?` MyData;
`ObservableCollection<MyType>` Items = `new()`;
`List<MyType>` Items2 = `new()`;
```

Collection notes: do note that by putting collections inside here, would generate `Reference<ObservableCollection<MyType>> ItemsProp { get; }` property (with `ObservableCollection<MyType> Items { get; set; }`).

Depending on what you mean, you may either use:
- Declare inside QuickMarkup if you expect parents to replace the collection or provide their own collection.
- Declare inside C# as `ObservableCollection<MyType> Items { get; } = new()` if you want the class to own the collection.

## Required Properties

Mark a reference declaration with the `required` keyword to make it a **required** for consumers to provide:

```csharp
[QuickMarkup("""
    required string Title;
    <TextBlock Text=`Title` />
    """)]
partial class MyLabel : IQuickMarkupComponent<TextBlock>;
```

In QuickMarkup code, the consumer must set `Title` on the markup.

```quickmarkup
// Markup consumer
<MyLabel Title="Welcome" />
```

In C# code, the value must be put as a constructor argument.

```csharp
// C# consumer
var label = new MyLabel("Welcome");
```

If a required property is not provided in markup, the binder reports error at compile time.

### Rules

- `required` only applies to non-computed, non-static reference declarations.
- You cannot use backward compatible constructor mode with required properties. That means, you cannot declare C# constructor. If you have an explicit constructor, migrate to a `[QuickMarkupConstructor]` method instead.
- The `[QuickMarkupRequiredProperty]` attribute is emitted on the generated C# property, enabling the binder to enforce requiredness on compiled library types.

### Constructor Arguments + Required Refs

When you also have a `[QuickMarkupConstructor]` method, required refs are appended as extra parameters:

```csharp
[QuickMarkup("""
    required string Title;
    <TextBlock Text=`Title` />
    """)]
partial class MyLabel : IQuickMarkupComponent<TextBlock>
{
    [QuickMarkupConstructor]
    private void Init(int fontSize)
    {
        // Custom setup with fontSize
    }
}

// Usage: [QuickMarkupConstructor] params first, then required refs
var label = new MyLabel(14, "Welcome");
```

## Provide/Inject

Context-based dependency injection for passing reactive references between parent and child components. A parent `provide`s a value, child components `inject` it by name and type. Changes propagate reactively.

### Syntax

```quickmarkup
provide string Theme = "dark";    // parent: provide
inject string Theme;              // child: required inject (throws if missing)
inject? string Theme;             // child: optional inject (returns default)
```

Default accessibility is `private`. Cannot be `static`, `required`, or use computed syntax (`=>`). Inject does not support default values. Requires `[assembly: QuickMarkupNewLifecycle]`.

### Renaming with `as`

Use `as` to decouple the local name from the context key:

```quickmarkup
provide string MyRef as MyCtx = "dark";   // local: MyRef, context key: MyCtx
inject string MyCtx as MyRef;             // context key: MyCtx, local: MyRef
inject? string MyCtx as MyRef;            // optional variant
```

Ordering differs: **`provide ref as ctx`**, **`inject ctx as ref`**.

### How It Works

1. Parent's `provide` stores its `Reference<T>` in a `QuickMarkupContext`.
2. Child components receive a cloned context.
3. Child's `inject` retrieves the same `Reference<T>` — bidirectional reactivity.

```quickmarkup
// Parent
provide string Label = "hello";
// Child (separate component)
inject string Label;
<TestText Text=`Label` />
```

### Constraints

- `provide`/`inject` cannot be `static` or `required`.
- `provide`/`inject` cannot use computed syntax (`=>`).
- `inject` does not support default values.

### Generated Code

Provide/inject initialization runs in the generated constructors, **before** the user's `[QuickMarkupConstructor]` method. This ensures injected values are available inside the constructor method.

`provide string Label = "hello"` generates `Context.Provide<string>("Label", LabelProp)` in the constructor. With `as`: `provide string MyRef as MyCtx` generates `Context.Provide<string>("MyCtx", MyRefProp)` — the context key uses the alias, the backing field uses the local name.

`inject string Label` generates `LabelProp = Context.Inject<string>("Label")` in the constructor. With `as`: `inject string MyCtx as MyRef` generates `MyRefProp = Context.Inject<string>("MyCtx")`.

`inject? string Label` generates `LabelProp = Context.TryInject<string>("Label")` in the constructor (nullable backing field, returns default when missing).

The primary constructor also accepts an optional `QuickMarkupContext?` parameter for context propagation from parent components.

### Context Hierarchy

Contexts form a hierarchy: grandparent → parent → child. A child can find providers from any ancestor via parent chain resolution.

## Markup Syntax

### Tags

```
<TypeName Property=Value SomeClass.AttachedPropertyName=Value>
    <Child />
</TypeName>
<SelfClosing Property=Value />
```

Comments use `//` or `/* */`. **Not** `<!-- -->`.

### Property Values

Values are **not** quoted (unlike XML/XAML). Use raw values directly.

| Kind | Syntax | Example |
|------|--------|---------|
| Integer | literal (dec/hex/binary) | `Width=100` / `Tag=0xDEADBEEF` / `Flags=0b101101` |
| Double | literal | `FontSize=14.5` |
| Boolean | literal | `IsChecked=true` |
| Boolean true shorthand | property name alone | `IsEnabled` |
| Boolean false shorthand | `!` prefix | `!IsHitTestVisible` |
| String | double quotes | `Text="Hello"` |
| Enum member | name alone | `HorizontalAlignment=Center` |
| null/default | keyword | `Tag=null` / `Target=default` |
| C# expression | backticks | `` Text=`$"Count: {Counter}"` `` |
| Alternate C# literal (backward compatability legacy syntax of above) | `/-...-/` | `Source=/-new Uri("ms-appx:///icon.png")-/` |

### Automatic `new` (single-argument constructors)

When you assign a **numeric or bool** literal to a property, the source generator may emit **`new PropertyType(literal)`** instead of the raw literal. That happens when the property type does not take the literal directly but exposes a **constructor with exactly one parameter** that does (`CodeTypeResolver.ShouldAutoNew`, `Binder.ValueOrAutoNew`).

**Examples:** `CornerRadius=16` → `new CornerRadius(16)`; `BorderThickness=1` → `new Thickness(1)` when the uniform constructor applies.

**Not covered:** multi-value `Thickness` / `CornerRadius` corners — use a backtick C# expression, e.g. `` Margin=`new(0,12,0,0)` ``. If assignment fails, use an explicit `` `new Thickness(...)` ``.

### C# Expressions (backtick syntax)

`` Property=`expression` `` — the expression re-evaluates automatically whenever any referenced reactive variable changes.

### Binding Directions

| Syntax | Direction | Example |
|--------|-----------|---------|
| `` =`expr` `` | One-way (source→UI) | `` Text=`Name` `` |
| `` =>`var` `` | Bindback (UI→source) | `` SelectedValue=>`Choice` `` |
| `` +=>`value => ...` `` | Bindback with `Action<T>` delegate | `` Text+=>`txt => Name = txt.Trim()` `` |
| `` <=>`var` `` | Two-way | `` Value<=>`Amount` `` |

You can combine one-way + bindback for preprocessing:

```
<NumberBox Value=`Math.Round(Val, 2)` Value=>`Val` />
```

### Events

```
Click+=`(sender, args) => DoSomething()`
@Click+=`Counter++`                        // @ auto-wraps in delegate { ... }
@Click+=`await DisplayDialog("Click")`     // @ with await auto-wraps in async delegate { ... }
```

### Variable Capture

Assign a tag to a variable accessible from C# code-behind:

```
myButton = <Button Content="Click" />
```

The variable `myButton` becomes a field on the partial class, usable in code-behind methods.

Note that referencing this element in `<setup>` tag would return `null`. The element is only defined from its point on in the markup. (ie. you cannot use the element above), see order of operations.

#### Tag Variable Capture as a reference

Use keyword `ref` to generate a reference pair.

```
<TextBox AutomationProperties.LabeledBy=`InputLabel`/>
ref InputLabel = <TextBlock AutomationProperties.AccessibilityView=Raw Text="subtitle" />
```

On the initial run `InputLabel` will be null, but after `InputLabel` is set, `` AutomationProperties.LabeledBy=`InputLabel` `` will be rerun.

`ref` variable capture is a bit more expensive but does not really provide much value other than use case above, so they are only useful in minimal case where you need to reference MyButton before the element is generated. Try to avoid using `ref` tag variable capture everywhere.

### Inline Tag as Property Value

```
<NumberBox NumberFormatter=<DecimalFormatter IntegerDigits=1 /> />
```

### Templates (DataTemplate)

Support: Uno Platform only. WASDK and UWP target project are not supported.

Assign a `template` value to a DataTemplate-typed property for controls that use the `ItemsSource` + `ItemTemplate` contract:

```quickmarkup
<ItemsControl ItemsSource=`Items` ItemTemplate=template (Person? person) { <TextBlock Text=`person?.Name` /> } />
// or without fragment works too
<ItemsControl ItemTemplate=template (Person? person) <TextBlock Text=`person?.Name` /> />
```

The direct body must resolve to **exactly one plain element** — nested fragments must still collapse to a single element. `if`, `foreach`, and `await` blocks are not allowed in the body, because a template must always return the same single element that the framework materializes once. The template parameter type must be **nullable or a value type**, and `template` is only accepted on template-like properties (name contains `Template`, or a `DataTemplate`/`FrameworkTemplate` type).

When the element is first created, the input value will be default value (ie. `null` for reference types or `default(YourValueType)` for value types) due to framework limitation. Ensure you guard null case properly.

The framework materializes the template per item and sets the element's `DataContext`. The parameter is bridged from `DataContext`, so the body must handle the value being `null` — the framework assigns `DataContext` **after** first materialization, so write null-safe expressions (`` `person?.Name` ``) or guard the initial render yourself.

### Inline Collection Children

Use `<>...</>` for collection-typed properties:

```
<Grid RowDefinitions=<>
        <RowDefinition Auto />
        <RowDefinition />
    </>
>
```

### Extension Method Callbacks

An identifier that isn't a recognized property is called as an extension method on the element:

```
<StackPanel CenterH CenterV>
```

This calls `element.CenterH()` and `element.CenterV()`. Define these as extension methods in C#.

### Lambda Callbacks

A standalone backtick expression that is `Action<T>` runs once with the created element:

```
<Grid `x => Grid.SetRow(x, 1)` />
```

### Fragment Children

A `{ ... }` block is a fragment. Contains any valid child nodes:

```
<StackPanel>
    {
        <TextBlock Text="A" />
        <TextBlock Text="B" />
    }
</StackPanel>
```

### Conditional Children

```
if (`condition`) { <TextBlock Text="Visible" /> }
else <TextBlock Text="Fallback" />
```

The `else` branch is required for single-child content positions (e.g., `Content`).

#### Notes about using it on ObservableCollection.

When using with `ObservableCollection<T>` it is worth knowing that `Count` property is NOT reactive. Use the `Reactive` extension property defined by QuickMarkup instead (you need to add `using QuickMarkup.Infra.Collections;` namespace).

```quickmarkup
// Avoid: won't update when myObservableCollection size changes
if (`myObservableCollection.Count == 0`) { <TextBlock Text="No items" /> }
// Use: will update when myObservableCollection size changes
if (`myObservableCollection.Reactive.Count == 0`) { <TextBlock Text="No items" /> }
```

Note: `ReactiveList<T>` does not have this limitation and does not need the `Reactive` extension property. You can use `reactiveList.Count`.

There are more important details about the `Reactive` extension property below in the [`ObservableCollection<T>` helpers](#observablecollectiont-helpers) section.

### Foreach Loops

```
// Range (lower inclusive, upper exclusive)
foreach (var i in ..3) { <TextBlock Text=/-$"Row {i}"-/ /> }
foreach (var i in 1..4) { <TextBlock Text=/-$"Item {i}"-/ /> }

// Iterable — any IEnumerable expression is accepted (materialized per reconcile). Reactive when the
// source implements INotifyCollectionChanged (e.g. ObservableCollection) or is a reference-tracked
// collection (e.g. ReactiveList<T>), so LINQ like `reactiveList.Take(10)` / `.Where(...)` stays reactive.
foreach (var item in `items`) { <TextBlock Text=/-item-/ /> }

// With key expression (for stable identity across collection changes), works for INotifyCollectionChanged
// or reference-tracked collections; uses the key as identity across resets/refreshes
foreach (var item in `animals`; `item.Id`) { <TextBlock Text=`item.Name` /> }

// With index variable
foreach (index; var item in `items`) { <TextBlock Text=`$"{index + 1}. {item}"` /> }

// With both
foreach (index; var item in `items`; `item.Id`) { <TextBlock Text=`$"{index + 1}. {item}"` /> }
```

### IMPORTANT

When using with `ReactiveList<T>` or any reactive expression to create list of items, you should almost always provide the key expression. Without key expression, all UI gets recreated and it will be *very* expensive. QuickMarkup is not VDOM framework so it will recreate actual UI elements and will reset all the UI states and have bad performance without the key.

### Await Blocks

Reactive async branching for `AsyncComputed<T>`, async expression values, or direct `Task<T>` values. Switch between loading, error, and success UI based on async state — useful whenever you're loading data from an async source:

```quickmarkup
User? User => async `Api.FetchCurrentUserAsync()`;

<StackPanel>
    await `UserAsync` with {
        <ProgressRing />
    } catch (ex) {
        <TextBlock Text=`$"Failed to load: {ex.Message}"` Foreground=Red />
    } then (user) {
        <TextBlock Text=`$"Welcome, {user.Name}!"` />
    }
</StackPanel>
```

The async expression is the `*Async` property (of type `AsyncComputed<T>`) auto-generated from an async computed declaration (`` Property => async `...` ``), or any `Task<T>` expression.

```quickmarkup
// Direct Task<T> usage — no async computed property needed (preferred if needed in one place)
<StackPanel>
    await `Api.FetchCurrentUserAsync()` with {
        <ProgressRing />
    } catch (ex) {
        <TextBlock Text=`$"Failed to load: {ex.Message}"` Foreground=Red />
    } then (user) {
        <TextBlock Text=`$"Welcome, {user.Name}!"` />
    }
</StackPanel>
```

- **`with`** — shown in loading state (no parameter).
- **`catch (ex)`** — shown on failure; `ex` captures the `Exception` (parameter is optional — `catch { ... }` is valid).
- **`then (val)`** — shown on success; `val` captures the resolved value (parameter is optional — `then { ... }` is valid).

All three branches are optional. If omitted, nothing renders in that state.

### Root Tag With Properties

`<root>` can carry properties that apply to the class itself (since it inherits from a UI type):

```
<root Background=`bgBrush.Value` CornerRadius=16 Margin=16 Padding=8 />
```

## Setup & Bootstrapping

For new project, for WinUI/UWP, when app is initialized, `ReactiveInitializer.InitReactiveScheduler()` must be called to setup reactivity, otherwise reactivity won't work. For other frameworks, you may refer to an example below and adapt for your framework.

```csharp
// UWP Example
ReactiveScheduler.AddTickCallbackForCurrentThread(delegate
{
    _ = Dispatcher.TryRunAsync(CoreDispatcherPriority.High, ReactiveScheduler.Tick);
});
```

## Order of Operations

### Recommended Pattern (`[QuickMarkupConstructor]` / no explicit constructor)

When using `[QuickMarkupConstructor]` or no explicit constructor:

1. Component is initialized.
2. If the component was created with QuickMarkup, parent consumers set properties via the `Action<T>` callback at this time.
3. Context is created or received from parent.
4. Provide/inject initialization runs (if the component has `provide`/`inject` declarations).
5. If a `[QuickMarkupConstructor]` method exists, it runs. (injected values are available from step 4)
6. The user's method must call `Init()` to run `<setup>` and build the UI tree.
Inside `Init()` call,
7. QuickMarkup goes through each element, one by one, running in order.
   - Properties are evaluated in order of declaration (left to right)
   - Children are evaluated in order of declaration (top to bottom)

Example:
```
<root> // 1.
    <StackPanel> // 2.
        <TextBlock /> // 3.
        <TextBox /> // 4.
    </StackPanel>
    stack = <StackPanel> // 5.
        <TextBlock /> // 6.
        <TextBox /> // 7.
    </StackPanel>
</root>
```

`stack` variable is assigned and is not null from point `5.` onwards only.

8. For each element, properties and extension methods are evaluated from left to right.

```
// 0. StackPanel is initialized, and `sp` is set to a new stack panel
sp = <StackPanel /* 1. */ First=1 /* 2. */ Second=2
    /* 3. */ ThirdExtension
    /* 4. */ `x => Debug.WriteLine($"This is run on the 4th order, and this should say true: '{sp == x}'")`
    /* 5. */ Fifth=5
/>
```

9. Reactivity changes: properties are rerun whenever values change. No explicit order defined.

*This behavior is only guaranteed from generated code. If user calls generated constructor themselves, just note that the evaluation step depends on user code.

### Backward Compatibility Mode (explicit `Init()` call)

When you declare a C# constructor **without** `[QuickMarkupConstructor]`, the generator falls back to backward-compatible mode:

1. User's constructor is called.
2. User must at some point call `Init()` method.
3. When `Init()` is called, steps 4-7 above apply.

```csharp
[QuickMarkup("""
    <root>
        <TextBlock Text="Hello" />
    </root>
    """)]
partial class MyPage : Page
{
    public MyPage()
    {
        // Custom logic before init
        Init(); // Must call this
    }
}
```

> **Warning:** In backward-compatible mode, `Init()` runs inside the constructor, before any upstream properties are set. This means reference properties will be `null` or `default` during `<setup>` and UI tree building. Parents set properties *after* the constructor returns. See the example below.

```csharp
record class Card(string Name, string Description);

[QuickMarkup("""
    Card Card;
    <root>
        // This will crash with NullReferenceException — Card is not yet set
        <TextBlock Text=`Card.Name` />
    </root>
    """)]
public partial class CardDisplay : StackPanel
{
    public CardDisplay() { Init(); }
}
```

To guard against this, use nullable types and conditional rendering:

```csharp
[QuickMarkup("""
    Card? Card;
    <root>
        if (`Card is not null`) {
            <TextBlock Text=`Card.Name` />
            <TextBlock Text=`Card.Description` />
        }
    </root>
    """)]
public partial class CardDisplay : StackPanel
{
    public CardDisplay() { Init(); }
}
```

> **Recommendation:** Migrate to `[QuickMarkupConstructor]` or remove explicit constructors entirely to avoid this pitfall. With the recommended pattern, upstream properties are always assigned before `<setup>` runs (when the `Action<T>` constructor is used), or required refs are guaranteed by the compiler.

## Generated Constructors

In new code (not backward compatible mode). The source generator emits two constructors:

**Primary constructor** — accepts required refs and any `[QuickMarkupConstructor]` parameters:

```csharp
// Example: required string Title + [QuickMarkupConstructor(int id)]
public Component(int id, string Title) { ... }
```

**Action constructor** — accepts any `[QuickMarkupConstructor]` parameters followed by an `Action<T>` to let consumers set properties. The action runs **before** the `[QuickMarkupConstructor]` method, so upstream properties are already set when the user's method runs:

```csharp
// Example: with [QuickMarkupConstructor(int id)] parameters
public Component(int id, Action<Component> quickMarkupInitializer) { ... }

// Example: without [QuickMarkupConstructor] parameters
public Component(Action<Component> quickMarkupInitializer) { ... }
```

**WARNING: Action constructor is only meant to be called by QuickMarkup. User code is not intended to call this and QuickMarkup reserve rights to make breaking change to how Action initializer behaves.**

If a `[QuickMarkupConstructor]` method declares parameters, those become parameters on both constructors (before required refs on the primary, before `Action<T>` on the action constructor). For example:

```csharp
[QuickMarkup("""
    string Label = "";
    <TextBlock Text=`Label` />
    """)]
partial class LabeledItem : IQuickMarkupComponent<TextBlock>
{
    [QuickMarkupConstructor]
    private void Init(string label)
    {
        Label = label;
        Init(); // Must call Init() to build the UI tree
    }
}

// Usage:
var item = new LabeledItem("My Label");                             // primary constructor
var item2 = new LabeledItem("My Label", x => { x.Label = "X"; });  // action constructor
```



## Components

Reusable QuickMarkup components implement one of two interfaces (from `QuickMarkup.WinUI` / `QuickMarkup.UWP`):

- **`IQuickMarkupComponent<T>`** — produces exactly one UI element (its `MarkupNode`). Properties set on the tag that don't exist on the component class are **forwarded** to `MarkupNode` on QuickMarkup, but not on C#. The child must be assignable to type `T`.
- **`IQuickMarkupFragmentComponent<T>`** — produces multiple UI elements; expands inline at the usage site. All children must be assignable to type `T`.

```csharp
[QuickMarkup("""
    string Text = "";
    <root>
        <TextBlock Text=`Text` FontSize=16 />
    </root>
    """)]
public partial class Label : IQuickMarkupComponent<UIElement>;

[QuickMarkup("""
    <root>
        <TextBlock Text="Item A" />
        <TextBlock Text="Item B" />
        <TextBlock Text="Item C" />
    </root>
    """)]
public partial class ItemList : IQuickMarkupFragmentComponent<TextBlock>;
```

You may also omit `<root>` tag if desired (recommended pattern), if you don't really need to put anything inside the `<root>` tag.

```csharp
[QuickMarkup("""
    string Text = "";
    
    <TextBlock Text=`Text` FontSize=16 />
    """)]
public partial class Label : IQuickMarkupComponent<UIElement>;

[QuickMarkup("""
    <TextBlock Text="Item A" />
    <TextBlock Text="Item B" />
    <TextBlock Text="Item C" />
    """)]
public partial class ItemList : IQuickMarkupFragmentComponent<TextBlock>;
```

Consuming a component:

```
// in QuickMarkup, HorizontalAlignment=Center is forwarded automatically to `MarkupNode`
<Label Text="Hello" HorizontalAlignment=Center />
```

```csharp
Label label = new Label();

label.Text = "Hello"; // this is defined on component, can access
label.MarkupNode.HorizontalAlignment = HorizontalAlignment.Center; // properties accessed in C# is not automatically forwareded. .MarkupNode is needed

Children.Add(label.MarkupNode); // need to manually specify markup node here, to get the underlying elmeent
```

For WinUI/UWP project with platform specific package installed, Non-generic versions (`IQuickMarkupComponent`, `IQuickMarkupFragmentComponent`) default `T` to `UIElement`.

Depending on your target UI framework, sometimes subclassing elements directly may make it easier to work with, ie. `partial class MyComponent : Grid`, but for case of sealed elements (ie. WinUI `Border`/`TextBlock` are sealed) or multiple children component/fragment, you may need these.

However, for WinUI/UWP, we usually prefer using `IQuickMarkupComponent<T>` instead of subclassing, since subclassing without XMAL can trigger multiple bugs, especially on top of styled or templated control.

A class may implement at most **one** of the two interfaces. Implementing both produces a compile-time error.

Note:
- `if`/`else`/`foreach` directly on top level without `<root>` tag is currently not supported due to a bug. If you need to use them, you can add `<root>` tag.
- subclassing regular UI still requires `<root>` tag if you have UI markup. Only QuickMarkup components may omit root tags and have non-root tag directly.

## Reactivity Infrastructure

For advanced use in C# code-behind (not inside markup):

```csharp
var r = Ref(0);                     // Reference<int>
var c = Computed(() => r.Value + 1); // Computed<int>
r.Watch(val => { ... });            // callback on change
r.Watch(val => { ... }, immediete: true); // also runs immediately
Effect(() => { ... }, ref1, ref2);  // runs when any listed ref changes
```

`ReferenceTracker.NoCapture(() => expr)` reads without tracking dependencies.

### `ReactiveList<T>`

`ReactiveList<T>` is a collection defined by QuickMarkup where mutating it will reevaluate all access.

`ReactiveList<T>` itself is a single reactive reference. Mutating anything in the list will revaluate everything.

```csharp
ReactiveList<int> integers = new();
var count = Computed(() => integers.Count);
var firstTwoItems = Computed(() => integers.Take(2)); // Take from LINQ

// ...

integers.Add(1); // will reevaluate `integers.Count` and `integers.Take(2)`
integers.Add(2); // will reevaluate `integers.Count` and `integers.Take(2)`
integers.Add(3); // will reevaluate `integers.Count` and including `integers.Take(2)`
integers[0] = 4; // yes, this will still reevaluate `integers.Count` and `integers.Take(2)`
```

Most of the time if you use the list's state with minimal computation like just checking `integers.Count > 0`, it will probably be fine even if `Count` does not change.

Reactive List is a very powerful tool especially when trimming the list of items down like `reactiveList.Take(10)` to render top 10 items, or filtering `reactiveList.Where(x => x.SomeData is not null)`. These LINQ expressions are `IEnumerable<T>` and can be used directly in a `foreach` — the foreach materializes the enumerable each reconcile, and because it re-reads the `ReactiveList` reference, it stays reactive:

```quickmarkup
foreach (var item in `reactiveList.Take(10)`) { <TextBlock Text=`item.Name` /> }
foreach (var item in `reactiveList.Where(x => x.SomeData is not null)`) { <TextBlock Text=`item.Name` /> }
```

### `ObservableCollection<T>` helpers

The `Reactive` extension property enables an `ObservableCollection<T>` to participate in the reactive chain. It returns the same `ObservableCollection<T>` instance (`ReferenceEquals(myCollection, myCollection.Reactive)`). The `.Reactive` getter must be evaluated inside the reactive tracking context (i.e. directly in a backtick expression) for the reference to be tracked. Reading it into a variable first loses reactivity:

```csharp
// Reactive: `.Reactive` getter invoked inside the tracking context
var sample1 = Computed(() => myCollection.Reactive.Count);

// NOT reactive: the getter is read outside the tracking context
var collection2 = myCollection.Reactive;
var sample2 = Computed(() => collection2);
```

There is also a `ReactiveProp` extension property that returns the same wrapper as an `IReference<ObservableCollection<T>>`. Like `Reactive`, its `.Value` getter must be invoked in the reactive tracking context to be tracked. `Reactive` is simply `ReactiveProp.Value`.

Like `ReactiveList<T>`, `myCollection.Reactive` works with LINQ too — LINQ expressions like `myCollection.Reactive.Where(...)` / `.Take(...)` stay reactive because the foreach re-reads the `Reactive` reference each reconcile:

```quickmarkup
foreach (var item in `myCollection.Reactive.Take(10)`) { <TextBlock Text=`item.Name` /> }
foreach (var item in `myCollection.Reactive.Where(x => x.SomeData is not null)`) { <TextBlock Text=`item.Name` /> }
```

## QuickMarkup.WinUI / QuickMarkup.UWP (NuGet Packages)

These packages provide WinUI 3 / UWP helpers that consume QuickMarkup's generated code.

**NuGet packages:** `QuickMarkup.WinUI` (WinUI 3) / `QuickMarkup.UWP` (UWP)

Namespace `QuickMarkup.WinUI`:

- **`ReactiveInitializer.InitReactiveScheduler()`** — call once on the UI thread (e.g., in `MainWindow` constructor) to initialize the reactive scheduler with the current dispatcher.
- **`ThemeResources.Get<T>(string resourceName)`** — returns a `Reference<T?>` that re-resolves on theme change. Also `Get<T>(string, FrameworkElement)` for per-element theme resolution.
- **`ThemeBrushes`** — static properties returning `Reference<Brush?>` for common WinUI theme brushes (`Accent`, `PrimaryText`, `SolidBackground`, `CardBackground`, `DividerStroke`, `SystemSuccess`, etc.).

### Initiative And Bootstrapping

The entry page must initialize the reactive scheduler. The simplest way is via `ReactiveInitializer.InitReactiveScheduler()`:

```csharp
// App.xaml.cs, for example of WinUI/UWP
QuickMarkup.WinUI.ReactiveInitializer.InitReactiveScheduler();
```

Only relevant if you start a new project from scratch.

### Using ThemeResources / ThemeBrushes in Markup

```csharp
[QuickMarkup("""
    using QuickMarkup.WinUI;
    <setup>
        var theme = UseThemeBrushes(this);
    </setup>
    <root Background=`theme.SolidBackground`>
        <TextBlock Foreground=`theme.PrimaryText` Text="Hello" />
    </root>
    """)]
partial class MyPage : Page;
```

## Best Practices

- **Prefer the recommended pattern** — omit explicit constructors or use `[QuickMarkupConstructor]` (with an `Init()` call at the end) instead of manually writing a C# constructor.
- Use `required` on reference declarations that the consumer must provide, to get compile-time safety and cleaner constructor APIs.
- Define **global usings** for common namespaces (`QuickMarkup.Infra`, `static QuickMarkup.Infra.QuickRefs`, etc.) so markup stays clean.
- Define **C# extension methods** like (`CenterH`, `CenterV`, `Center`, `Right`, `Bottom`, `StretchH`, `StretchV`) for layout shortcuts.
- The class must be `partial` (source generator emits the other part). The base class should be a UI element (`Page`, `Grid`, `StackPanel`, etc.) or QuickMarkup component interface mentioned above.
