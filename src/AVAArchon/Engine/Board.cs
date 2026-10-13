namespace AVAArchon.Engine;

/// <summary>What a square is: always light, always dark, or one that shifts with the lumen.</summary>
public enum Square { Light, Dark, Shifting }

/// <summary>The leaders' seven spells. Each leader may cast each one once a game.</summary>
public enum Spell { Teleport, Heal, ShiftTime, Exchange, Elemental, Revive, Imprison }

/// <summary>
/// The 9×9 board: its squares, the lumen that brightens and darkens the shifting squares turn by turn,
/// the five power points, and the pieces. Dawn starts on the left, Dusk on the right.
/// </summary>
public sealed class Board
{
    public const int Size = 9;
    public const int MaxLumen = 5;
    /// <summary>A piece on a power point heals this much a turn (others heal 1), and spells can't touch it.</summary>
    public const int PowerPointHealing = 5;

    public static readonly (int X, int Y)[] PowerPoints = [(4, 0), (0, 4), (4, 4), (8, 4), (4, 8)];

    public List<Piece> Pieces { get; } = [];
    /// <summary>How bright the shifting squares are: 0 (as dark as dark squares) to 5 (as light as light).</summary>
    public int Lumen { get; set; } = 3;
    /// <summary>Which way the lumen is going: +1 brightening, -1 darkening.</summary>
    public int LumenDirection { get; set; } = -1;
    public HashSet<Spell> DawnSpells { get; } = [];
    public HashSet<Spell> DuskSpells { get; } = [];

    public static Square SquareAt(int x, int y)
    {
        if ((x, y) == (0, 4)) return Square.Light;
        if ((x, y) == (8, 4)) return Square.Dark;
        if (x == 4 || y == 4) return Square.Shifting;
        return (x + y) % 2 == 0 ? Square.Light : Square.Dark;
    }

    public static bool IsPowerPoint(int x, int y) => Array.IndexOf(PowerPoints, (x, y)) >= 0;
    public static bool OnBoard(int x, int y) => x >= 0 && y >= 0 && x < Size && y < Size;

    /// <summary>How light a square is now, 0 to 5.</summary>
    public int Shade(int x, int y) => SquareAt(x, y) switch
    {
        Square.Light => MaxLumen,
        Square.Dark => 0,
        _ => Lumen,
    };

    /// <summary>The extra life a side's piece fights with on this square: Dawn draws strength from light, Dusk from dark.</summary>
    public int Bonus(Side side, int x, int y) => side == Side.Dawn ? Shade(x, y) : MaxLumen - Shade(x, y);

    public Piece? At(int x, int y) => Pieces.FirstOrDefault(p => p.Alive && p.X == x && p.Y == y);
    public IEnumerable<Piece> Living(Side side) => Pieces.Where(p => p.Alive && p.Side == side);
    public IEnumerable<Piece> Fallen(Side side) => Pieces.Where(p => !p.Alive && p.Side == side);
    public Piece? Leader(Side side) => Living(side).FirstOrDefault(p => p.Type.Leader);
    public HashSet<Spell> SpellsUsed(Side side) => side == Side.Dawn ? DawnSpells : DuskSpells;

    /// <summary>The armies drawn up for battle.</summary>
    public static Board Start()
    {
        var b = new Board();
        Kind[] dawnBack = [Kind.Archer, Kind.Stonewarden, Kind.Lancer, Kind.Stormcaller, Kind.Lightbringer, Kind.Sunbird, Kind.Lancer, Kind.Stonewarden, Kind.Archer];
        Kind[] duskBack = [Kind.Spitter, Kind.Ogre, Kind.Wraith, Kind.Wailer, Kind.Shadelord, Kind.Hydra, Kind.Wraith, Kind.Ogre, Kind.Spitter];
        for (int y = 0; y < Size; y++)
        {
            b.Add(dawnBack[y], 0, y);
            b.Add(y is 0 or 8 ? Kind.Gryphon : Kind.Sentinel, 1, y);
            b.Add(y is 0 or 8 ? Kind.Wyvern : Kind.Ghoul, 7, y);
            b.Add(duskBack[y], 8, y);
        }
        return b;
    }

    public Piece Add(Kind kind, int x, int y)
    {
        var t = PieceType.All[kind];
        var p = new Piece { Id = Pieces.Count, Type = t, Side = t.Side, X = x, Y = y, Life = t.Life };
        Pieces.Add(p);
        return p;
    }

    /// <summary>
    /// Where a piece can go this turn. Walkers step up to their move in any of eight directions through
    /// empty squares; fliers go anywhere within their move. Either may end on an enemy, to fight it.
    /// </summary>
    public HashSet<(int X, int Y)> Moves(Piece p)
    {
        var result = new HashSet<(int, int)>();
        if (!p.Alive || p.Imprisoned > 0) return result;
        int range = p.Type.Move;
        if (p.Type.Flies)
        {
            for (int y = p.Y - range; y <= p.Y + range; y++)
                for (int x = p.X - range; x <= p.X + range; x++)
                {
                    if (!OnBoard(x, y) || (x, y) == (p.X, p.Y)) continue;
                    var o = At(x, y);
                    if (o is null || o.Side != p.Side) result.Add((x, y));
                }
            return result;
        }
        var frontier = new List<(int, int)> { (p.X, p.Y) };
        var seen = new HashSet<(int, int)> { (p.X, p.Y) };
        for (int step = 0; step < range; step++)
        {
            var next = new List<(int, int)>();
            foreach (var (fx, fy) in frontier)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = fx + dx, y = fy + dy;
                        if ((dx, dy) == (0, 0) || !OnBoard(x, y) || !seen.Add((x, y))) continue;
                        var o = At(x, y);
                        if (o is null) { result.Add((x, y)); next.Add((x, y)); }
                        else if (o.Side != p.Side) result.Add((x, y));
                    }
            frontier = next;
        }
        return result;
    }

    /// <summary>The side holding all five power points, if any.</summary>
    public Side? HoldsAllPowerPoints()
    {
        Side? holder = null;
        foreach (var (x, y) in PowerPoints)
        {
            var p = At(x, y);
            if (p is null || (holder is { } h && h != p.Side)) return null;
            holder = p.Side;
        }
        return holder;
    }

    /// <summary>The end of a side's turn: its wounded heal, its prisoners count down.</summary>
    public void EndTurn(Side side)
    {
        foreach (var p in Living(side))
        {
            if (p.Wounded) p.Life = Math.Min(p.Type.Life, p.Life + (IsPowerPoint(p.X, p.Y) ? PowerPointHealing : 1));
            if (p.Imprisoned > 0) p.Imprisoned--;
        }
    }

    /// <summary>A full round has passed: the lumen moves one step, turning at either end.</summary>
    public void StepLumen()
    {
        Lumen += LumenDirection;
        if (Lumen >= MaxLumen) { Lumen = MaxLumen; LumenDirection = -1; }
        else if (Lumen <= 0) { Lumen = 0; LumenDirection = 1; }
    }

    public static Side Other(Side s) => s == Side.Dawn ? Side.Dusk : Side.Dawn;
}
