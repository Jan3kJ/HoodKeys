using HoodKeys.Core.Keys;

namespace HoodKeys.Core.Actions;

/// <summary>Sends a key or combo to the foreground window.</summary>
public interface IKeySender
{
    void Send(KeyCombo combo);
}
