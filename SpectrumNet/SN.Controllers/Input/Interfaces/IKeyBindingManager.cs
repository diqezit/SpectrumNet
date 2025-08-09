#nullable enable

namespace SpectrumNet.SN.Controllers.Input.Interfaces;

public interface IKeyBindingManager
{
    Key GetKeyForAction(string actionName);
    bool SetKeyForAction(string actionName, Key key, bool force = false);
    bool IsKeyBound(Key key);
    string? GetActionForKey(Key key);
    IReadOnlyDictionary<string, Key> GetAllBindings();
    Dictionary<string, string> GetActionDescriptions();
    void ValidateAndFixConflicts();
    void ClearKeyForAction(string actionName);
}