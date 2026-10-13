namespace AVAArchon.Engine;

/// <summary>
/// The leaders' spells: what each does and whom it may touch. No spell may touch a piece standing on a
/// power point, and each leader casts each spell only once a game.
/// </summary>
public static class Spells
{
    public const int ImprisonTurns = 3;

    public static readonly Spell[] All = Enum.GetValues<Spell>();

    public static string Name(Spell s) => s switch
    {
        Spell.ShiftTime => "Shift Time",
        Spell.Elemental => "Summon Elemental",
        _ => s.ToString(),
    };

    public static string About(Spell s) => s switch
    {
        Spell.Teleport => "Send one of your pieces to any square. Onto an enemy, it fights.",
        Spell.Heal => "Heal one of your pieces of every wound.",
        Spell.ShiftTime => "Turn the lumen back the other way.",
        Spell.Exchange => "Swap any two pieces on the board.",
        Spell.Elemental => "Call an elemental to fight one enemy piece. It vanishes after.",
        Spell.Revive => "Bring back one of your fallen, beside your leader.",
        Spell.Imprison => $"Hold an enemy piece where it stands for {ImprisonTurns} of its turns.",
        _ => "",
    };

    /// <summary>Whether a spell has anything to work on now.</summary>
    public static bool CanCast(Board b, Side side, Spell s) =>
        !b.SpellsUsed(side).Contains(s) && b.Leader(side) is { Imprisoned: 0 } && s switch
        {
            Spell.ShiftTime => true,
            Spell.Revive => b.Fallen(side).Any(p => !p.Type.Leader) && ReviveSquares(b, side).Any(),
            _ => FirstTargets(b, side, s).Count > 0,
        };

    public static bool CanCastAny(Board b, Side side) => All.Any(s => CanCast(b, side, s));

    private static bool Touchable(Piece p) => !Board.IsPowerPoint(p.X, p.Y);

    /// <summary>The squares the spell's first choice may be.</summary>
    public static HashSet<(int X, int Y)> FirstTargets(Board b, Side side, Spell s)
    {
        var enemy = Board.Other(side);
        IEnumerable<Piece> pieces = s switch
        {
            Spell.Teleport => b.Living(side).Where(p => Touchable(p) && p.Imprisoned == 0),
            Spell.Heal => b.Living(side).Where(p => Touchable(p) && p.Wounded),
            Spell.Exchange => b.Pieces.Where(p => p.Alive && Touchable(p)),
            Spell.Elemental or Spell.Imprison => b.Living(enemy).Where(Touchable),
            _ => [],
        };
        var set = pieces.Select(p => (p.X, p.Y)).ToHashSet();
        if (s == Spell.Exchange && set.Count < 2) set.Clear();
        return set;
    }

    /// <summary>The squares the second choice may be: where to teleport to, or what to swap with.</summary>
    public static HashSet<(int X, int Y)> SecondTargets(Board b, Side side, Spell s, Piece first)
    {
        var set = new HashSet<(int, int)>();
        for (int y = 0; y < Board.Size; y++)
            for (int x = 0; x < Board.Size; x++)
            {
                var o = b.At(x, y);
                bool ok = s switch
                {
                    Spell.Teleport => o is null || (o.Side != side && Touchable(o)),
                    Spell.Exchange => o is not null && o != first && Touchable(o),
                    _ => false,
                };
                if (ok) set.Add((x, y));
            }
        return set;
    }

    /// <summary>The empty squares around a side's leader, where the fallen may return.</summary>
    public static IEnumerable<(int X, int Y)> ReviveSquares(Board b, Side side)
    {
        if (b.Leader(side) is not { } l) yield break;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = l.X + dx, y = l.Y + dy;
                if ((dx, dy) != (0, 0) && Board.OnBoard(x, y) && b.At(x, y) is null) yield return (x, y);
            }
    }

    public static bool NeedsSecond(Spell s) => s is Spell.Teleport or Spell.Exchange;
}
