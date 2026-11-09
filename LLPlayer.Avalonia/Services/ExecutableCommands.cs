using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FlyleafLib.MediaPlayer;

namespace LLPlayer.Avalonia.Services;

/// <summary>A current command owner and its effective keyboard metadata, shared by command viewers.</summary>
public sealed class ExecutableCommand : ObservableObject
{
    readonly Action? execute;
    readonly Func<bool>? available;

    public ExecutableCommand(string id, string actionName, string description, string group,
        IReadOnlyList<string> keys, Action? execute, Func<bool>? available = null)
    {
        Id = id;
        ActionName = actionName;
        Description = description;
        Group = group;
        Keys = keys.ToArray();
        this.execute = execute;
        this.available = available;
        Command = new RelayCommand(() => TryExecute(), () => CanExecute);
    }

    public string Id { get; }
    public string ActionName { get; }
    public string Description { get; }
    public string Group { get; }
    public IReadOnlyList<string> Keys { get; }
    public string KeysDisplay => string.Join(" / ", Keys);
    public string AvailabilityText => CanExecute ? "" : "Unavailable";
    public bool CanExecute => execute != null && (available?.Invoke() ?? true);
    public ICommand Command { get; }
    public void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanExecute));
        OnPropertyChanged(nameof(AvailabilityText));
        ((IRelayCommand)Command).NotifyCanExecuteChanged();
    }

    public bool TryExecute()
    {
        if (!CanExecute) return false;
        execute!();
        return true;
    }
}

public static class ExecutableCommandCatalog
{
    static readonly IReadOnlyDictionary<string, KeyBindingAction> unboundEngineAliases = new Dictionary<string, KeyBindingAction>(StringComparer.Ordinal)
    {
        [nameof(KeyBindingAction.ToggleRecording)] = KeyBindingAction.ToggleRecording,
        [nameof(KeyBindingAction.TakeSnapshot)] = KeyBindingAction.TakeSnapshot,
        [nameof(KeyBindingAction.ToggleVideoAcceleration)] = KeyBindingAction.ToggleVideoAcceleration,
    };

    public static IReadOnlyList<ExecutableCommand> Build(AppShortcutMap shortcuts,
        IEnumerable<KeyBinding> engineBindings, Func<KeyBindingAction, Action?> engineResolver,
        IReadOnlyDictionary<string, Action> appActions,
        Func<ShortcutActionDescriptor, bool>? availability = null,
        IEnumerable<ShortcutActionDescriptor>? appDescriptors = null)
    {
        KeyBinding[] engine = engineBindings.ToArray();
        List<ExecutableCommand> rows = [];
        foreach (ShortcutActionDescriptor descriptor in ShortcutActionCatalog.EngineActions)
        {
            KeyBindingAction action = descriptor.EngineAction!.Value;
            string[] keys = engine.Where(binding => binding.IsEnabled && binding.Action == action)
                .Select(binding => string.Join("+", KeyMapper.ChordParts(binding.Key, binding.Ctrl, binding.Alt, binding.Shift)))
                .Distinct(StringComparer.Ordinal).ToArray();
            if (keys.Length == 0) continue;
            rows.Add(new("engine:" + descriptor.Name, descriptor.Name, descriptor.Description, descriptor.Group,
                keys, engineResolver(action), () => availability?.Invoke(descriptor) ?? true));
        }

        IEnumerable<ShortcutActionDescriptor> aliases = ShortcutActionCatalog.EngineActions
            .Where(descriptor => unboundEngineAliases.ContainsKey(descriptor.Name))
            .Select(descriptor => descriptor with { Target = ShortcutActionTarget.Application, EngineAction = null });
        IEnumerable<ShortcutActionDescriptor> appMetadata = ShortcutActionCatalog.AppActions
            .Concat(aliases).Concat(appDescriptors ?? []).Where(row => row.Target == ShortcutActionTarget.Application)
            .DistinctBy(row => row.Name, StringComparer.Ordinal);
        foreach (ShortcutActionDescriptor descriptor in appMetadata)
        {
            // A disabled engine row is intentional. It must not acquire an unbound fallback owner.
            if (unboundEngineAliases.TryGetValue(descriptor.Name, out KeyBindingAction engineAction)
                && engine.Any(binding => binding.Action == engineAction)) continue;
            // Metadata is not an action owner. Only registered app functions become executable rows.
            if (!appActions.TryGetValue(descriptor.Name, out Action? action)) continue;
            string[] keys = shortcuts.Shortcuts.Where(binding => binding.IsEnabled && binding.ActionName == descriptor.Name)
                .Select(binding => string.Join("+", KeyMapper.ChordParts(binding.Key, binding.Ctrl, binding.Alt, binding.Shift)))
                .Distinct(StringComparer.Ordinal).ToArray();
            rows.Add(new("app:" + descriptor.Name, descriptor.Name, descriptor.Description, descriptor.Group,
                keys, action, () => availability?.Invoke(descriptor) ?? true));
        }

        string[] groups = ["Playback", "Subtitles", "Audio", "Video", "Window", "Application", "Other"];
        return rows.OrderBy(row => Array.IndexOf(groups, row.Group) is var index && index >= 0 ? index : groups.Length)
            .ThenBy(row => row.Description, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
