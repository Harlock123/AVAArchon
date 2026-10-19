using Avalonia.Input;

namespace AVAArchon.Engine;

/// <summary>
/// The keyboard and up to two gamepads, boiled down to each player's controls. Player one (Dawn in a
/// two-player game) has WASD with Space or Left Ctrl to fire and Left Shift for the second button, and
/// the first gamepad; player two has the arrow keys with Enter or Right Ctrl to fire and Right Shift
/// (or Backspace), and the second gamepad. Playing alone, either set works.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly Dictionary<PhysicalKey, Key> _pressedAs = [];
    private readonly ControllerManager _pad1 = new(0);
    private readonly ControllerManager _pad2 = new(1);

    public void OnKeyDown(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        if (physical != PhysicalKey.None)
        {
            if (_pressedAs.TryGetValue(physical, out var was) && was != key) _held.Remove(was);
            _pressedAs[physical] = key;
        }
        _held.Remove(key);
        _held.Add(key);
    }

    /// <summary>A key comes up. The key it went down as is let go too: with the keyboard option
    /// shift:both_capslock_cancel, letting go of a Shift arrives as CapsLock, and the Shift would stay held.</summary>
    public void OnKeyUp(Key key, PhysicalKey physical = PhysicalKey.None)
    {
        _held.Remove(key);
        if (physical != PhysicalKey.None && _pressedAs.Remove(physical, out var was)) _held.Remove(was);
    }

    public bool Has(Key key) => _held.Contains(key);

    public void Update()
    {
        _pad1.Update();
        _pad2.Update();
    }

    private int Axis(Key[] minus, Key[] plus)
    {
        for (int i = _held.Count - 1; i >= 0; i--)
        {
            if (minus.Contains(_held[i])) return -1;
            if (plus.Contains(_held[i])) return 1;
        }
        return 0;
    }

    private static Controls Read(ControllerManager pad, int dx, int dy, bool fire, bool alt)
    {
        if (dx == 0) dx = pad.DPadLeft || pad.LeftStickX < -0.4f ? -1 : pad.DPadRight || pad.LeftStickX > 0.4f ? 1 : 0;
        if (dy == 0) dy = pad.DPadUp || pad.LeftStickY < -0.4f ? -1 : pad.DPadDown || pad.LeftStickY > 0.4f ? 1 : 0;
        return new Controls(dx, dy, fire || pad.ButtonA || pad.ButtonX, alt || pad.ButtonB || pad.ButtonY);
    }

    public Controls Player1 => Read(_pad1,
        Axis([Key.A], [Key.D]), Axis([Key.W], [Key.S]),
        Has(Key.Space) || Has(Key.LeftCtrl) || Has(Key.Z),
        Has(Key.LeftShift) || Has(Key.X) || Has(Key.Q));

    public Controls Player2 => Read(_pad2,
        Axis([Key.Left, Key.NumPad4], [Key.Right, Key.NumPad6]), Axis([Key.Up, Key.NumPad8], [Key.Down, Key.NumPad2]),
        Has(Key.Enter) || Has(Key.RightCtrl) || Has(Key.NumPad0),
        Has(Key.RightShift) || Has(Key.Back) || Has(Key.Decimal));

    public bool Start => Has(Key.Enter) || _pad1.ButtonStart || _pad2.ButtonStart;
    public bool PadPause => _pad1.ButtonStart || _pad2.ButtonStart;
    public bool PadBack => _pad1.ButtonBack || _pad2.ButtonBack;

    public void Dispose()
    {
        _pad1.Dispose();
        _pad2.Dispose();
    }
}
