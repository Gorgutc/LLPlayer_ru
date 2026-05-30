---
name: llplayer-wpf-xaml-review
description: Use when reviewing LLPlayer_ru WPF, XAML, dialogs, controls, resources, or view models.
---

# LLPlayer WPF/XAML Review

Review WPF changes as desktop UI, not web UI.

## Check

- Binding names match view model properties.
- Dialogs are registered in `App.xaml.cs` when required.
- Resources use existing MaterialDesign and local resource dictionaries.
- UI changes do not block the playback thread.
- Dispatcher usage is explicit when crossing background/UI boundaries.
- Text fits and keyboard/mouse shortcuts remain discoverable in app surfaces.

Build with `dotnet build --no-restore -warnaserror .\LLPlayer`.
