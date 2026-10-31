namespace ChildLock.Core;

/// <summary>
/// Remembers which keys and mouse buttons were already down when the lock started,
/// so their release can pass through and Windows never thinks Ctrl or Alt is stuck.
/// </summary>
public sealed class HeldKeyTracker
{
    readonly HashSet<int> _held = [];

    public void Reset(IEnumerable<int> heldVks)
    {
        _held.Clear();
        _held.UnionWith(heldVks);
    }

    /// <summary>True (once) if this key-up belongs to a key that was held at lock time.</summary>
    public bool TryRelease(int vk) => _held.Remove(vk);

    /// <summary>True while a key held at lock time hasn't been released yet (its key-downs are auto-repeat).</summary>
    public bool IsHeld(int vk) => _held.Contains(vk);

    public int Count => _held.Count;
}
