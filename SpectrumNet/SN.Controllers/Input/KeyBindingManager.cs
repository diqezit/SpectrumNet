#nullable enable

namespace SpectrumNet.SN.Controllers.Input;

public class KeyBindingManager : IKeyBindingManager
{
    private readonly ISettings _settings;
    private readonly Dictionary<string, Func<Key>> _bindingGetters;
    private readonly Dictionary<string, Action<Key>> _bindingSetters;
    private readonly Dictionary<string, string> _actionDescriptions;
    private readonly object _lockObject = new();

    public KeyBindingManager(ISettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _bindingGetters = CreateBindingGetters();
        _bindingSetters = CreateBindingSetters();
        _actionDescriptions = CreateActionDescriptions();
    }

    public Key GetKeyForAction(string actionName)
    {
        lock (_lockObject)
        {
            return _bindingGetters.TryGetValue(actionName, out var getter)
                ? getter()
                : Key.None;
        }
    }

    public bool SetKeyForAction(string actionName, Key key, bool force = false)
    {
        lock (_lockObject)
        {
            if (!_bindingSetters.TryGetValue(actionName, out Action<Key>? value))
                return false;

            if (key != Key.None && !force)
            {
                var existingAction = GetActionForKey(key);
                if (existingAction != null && existingAction != actionName)
                    return false;
            }

            if (key != Key.None && force)
            {
                ClearExistingKeyBinding(key, actionName);
            }

            value(key);
            return true;
        }
    }

    public string? GetActionForKey(Key key)
    {
        lock (_lockObject)
        {
            if (key == Key.None) return null;

            return _bindingGetters
                .Where(kvp => kvp.Value() == key)
                .Select(kvp => kvp.Key)
                .FirstOrDefault();
        }
    }

    public bool IsKeyBound(Key key) => GetActionForKey(key) != null;

    public IReadOnlyDictionary<string, Key> GetAllBindings()
    {
        lock (_lockObject)
        {
            return _bindingGetters.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value());
        }
    }

    public Dictionary<string, string> GetActionDescriptions() => new(_actionDescriptions);

    public void ClearKeyForAction(string actionName)
    {
        lock (_lockObject)
        {
            if (_bindingSetters.TryGetValue(actionName, out var setter))
                setter(Key.None);
        }
    }

    public void ValidateAndFixConflicts()
    {
        lock (_lockObject)
        {
            var keyToActions = BuildKeyToActionsMap();
            ResolveConflicts(keyToActions);
        }
    }

    private void ClearExistingKeyBinding(Key key, string excludeAction)
    {
        var existingAction = GetActionForKey(key);
        if (existingAction != null && existingAction != excludeAction)
        {
            _bindingSetters[existingAction](Key.None);
        }
    }

    private Dictionary<Key, List<string>> BuildKeyToActionsMap()
    {
        var keyToActions = new Dictionary<Key, List<string>>();

        foreach (var (actionName, getter) in _bindingGetters)
        {
            var key = getter();
            if (key != Key.None)
            {
                if (!keyToActions.ContainsKey(key))
                    keyToActions[key] = [];
                keyToActions[key].Add(actionName);
            }
        }

        return keyToActions;
    }

    private void ResolveConflicts(Dictionary<Key, List<string>> keyToActions)
    {
        foreach (var (key, actions) in keyToActions)
        {
            if (actions.Count > 1)
            {
                for (int i = 1; i < actions.Count; i++)
                {
                    if (_bindingSetters.TryGetValue(actions[i], out var setter))
                        setter(Key.None);
                }
            }
        }
    }

    private Dictionary<string, Func<Key>> CreateBindingGetters() => new()
    {
        ["NextRenderer"] = () => _settings.KeyBindings.NextRenderer,
        ["PreviousRenderer"] = () => _settings.KeyBindings.PreviousRenderer,
        ["QualityLow"] = () => _settings.KeyBindings.QualityLow,
        ["QualityMedium"] = () => _settings.KeyBindings.QualityMedium,
        ["QualityHigh"] = () => _settings.KeyBindings.QualityHigh,
        ["ToggleOverlay"] = () => _settings.KeyBindings.ToggleOverlay,
        ["ToggleControlPanel"] = () => _settings.KeyBindings.ToggleControlPanel,
        ["IncreaseBarCount"] = () => _settings.KeyBindings.IncreaseBarCount,
        ["DecreaseBarCount"] = () => _settings.KeyBindings.DecreaseBarCount,
        ["IncreaseBarSpacing"] = () => _settings.KeyBindings.IncreaseBarSpacing,
        ["DecreaseBarSpacing"] = () => _settings.KeyBindings.DecreaseBarSpacing,
        ["ToggleRecording"] = () => _settings.KeyBindings.ToggleRecording,
        ["ClosePopup"] = () => _settings.KeyBindings.ClosePopup
    };

    private Dictionary<string, Action<Key>> CreateBindingSetters() => new()
    {
        ["NextRenderer"] = key => _settings.KeyBindings.NextRenderer = key,
        ["PreviousRenderer"] = key => _settings.KeyBindings.PreviousRenderer = key,
        ["QualityLow"] = key => _settings.KeyBindings.QualityLow = key,
        ["QualityMedium"] = key => _settings.KeyBindings.QualityMedium = key,
        ["QualityHigh"] = key => _settings.KeyBindings.QualityHigh = key,
        ["ToggleOverlay"] = key => _settings.KeyBindings.ToggleOverlay = key,
        ["ToggleControlPanel"] = key => _settings.KeyBindings.ToggleControlPanel = key,
        ["IncreaseBarCount"] = key => _settings.KeyBindings.IncreaseBarCount = key,
        ["DecreaseBarCount"] = key => _settings.KeyBindings.DecreaseBarCount = key,
        ["IncreaseBarSpacing"] = key => _settings.KeyBindings.IncreaseBarSpacing = key,
        ["DecreaseBarSpacing"] = key => _settings.KeyBindings.DecreaseBarSpacing = key,
        ["ToggleRecording"] = key => _settings.KeyBindings.ToggleRecording = key,
        ["ClosePopup"] = key => _settings.KeyBindings.ClosePopup = key
    };

    private static Dictionary<string, string> CreateActionDescriptions() => new()
    {
        ["NextRenderer"] = "Next Renderer",
        ["PreviousRenderer"] = "Previous Renderer",
        ["QualityLow"] = "Low Quality",
        ["QualityMedium"] = "Medium Quality",
        ["QualityHigh"] = "High Quality",
        ["ToggleOverlay"] = "Toggle Overlay",
        ["ToggleControlPanel"] = "Toggle Control Panel",
        ["IncreaseBarCount"] = "Increase Bar Count",
        ["DecreaseBarCount"] = "Decrease Bar Count",
        ["IncreaseBarSpacing"] = "Increase Bar Spacing",
        ["DecreaseBarSpacing"] = "Decrease Bar Spacing",
        ["ToggleRecording"] = "Toggle Recording",
        ["ClosePopup"] = "Close Popup"
    };
}