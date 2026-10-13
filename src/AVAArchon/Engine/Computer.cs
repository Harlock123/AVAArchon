namespace AVAArchon.Engine;

/// <summary>What the computer decides to do with its turn.</summary>
public abstract record Plan;
public sealed record MovePlan(Piece Piece, int X, int Y) : Plan;
/// <summary>A spell, with its targets: pieces, a square, or a fallen piece to revive.</summary>
public sealed record SpellPlan(Spell Spell, Piece? Target = null, Piece? Second = null, int X = -1, int Y = -1) : Plan;

/// <summary>
/// The computer's general. It weighs every move it has: a fight it should win, a power point taken or
/// held, a piece pulled back out of danger, a step toward the enemy. Now and then a spell is worth
/// more than any move: a great piece healed, a fallen one brought back, a strong enemy imprisoned or
/// set upon by an elemental. The easier the setting, the more its judgement wanders.
/// </summary>
public sealed class Computer(Difficulty difficulty, Random rng)
{
    public Difficulty Difficulty => difficulty;

    /// <summary>A piece's worth: how often it beats the other side's pieces, and for a leader, its spells too.</summary>
    public static double Value(PieceType t) => Values.GetValueOrDefault(t.Kind, 1.0);

    private static readonly Dictionary<Kind, double> Values = PieceType.All.Values.Where(t => t.Kind != Kind.Elemental).ToDictionary(t => t.Kind, t =>
    {
        var foes = PieceType.All.Values.Where(o => o.Side != t.Side && o.Kind != Kind.Elemental);
        double wins = foes.Average(o => t.Side == Side.Dawn
            ? OddsTable.DawnWins.GetValueOrDefault((t.Kind, o.Kind), 0.5)
            : 1 - OddsTable.DawnWins.GetValueOrDefault((o.Kind, t.Kind), 0.5));
        return (0.5 + 2.5 * wins) * (t.Leader ? 1.8 : 1.0);
    });

    /// <summary>
    /// The chance that a (fighting for <paramref name="sideA"/>) beats b, with their lives now (squares'
    /// bonuses included): the measured odds at full life, shifted by how much life each has.
    /// </summary>
    public static double Odds(PieceType a, Side sideA, double lifeA, PieceType b, double lifeB)
    {
        double p = sideA == Side.Dawn
            ? OddsTable.DawnWins.GetValueOrDefault((a.Kind, b.Kind), 0.5)
            : 1 - OddsTable.DawnWins.GetValueOrDefault((b.Kind, a.Kind), 0.5);
        p = Math.Clamp(p, 0.02, 0.98);
        double logit = Math.Log(p / (1 - p)) + 2.5 * Math.Log(lifeA / a.Life / (lifeB / b.Life));
        return 1 / (1 + Math.Exp(-logit));
    }

    public static double Odds(Board board, Piece attacker, Piece defender, int x, int y) =>
        Odds(attacker.Type, attacker.Side, attacker.Life + board.Bonus(attacker.Side, x, y), defender.Type, defender.Life + board.Bonus(defender.Side, x, y));

    private double Noise => difficulty switch { Difficulty.Easy => 0.9, Difficulty.Normal => 0.3, _ => 0.08 };

    /// <param name="quietTurns">Turns since the last fight: the longer the quiet, the bolder the computer gets.</param>
    public Plan Choose(Board board, Side side, int quietTurns = 0)
    {
        double caution = Math.Max(0.4, 1 - quietTurns / 50.0);
        var enemies = board.Living(Board.Other(side)).ToList();
        double mine = board.Living(side).Sum(p => Value(p.Type)), theirs = enemies.Sum(p => Value(p.Type));
        // Closing in: worth more when ahead, or when nothing has happened for a while.
        double closing = (mine >= theirs * 0.9 ? 0.06 : 0.02) + quietTurns * 0.004;
        int Nearest(int x, int y) => enemies.Count == 0 ? 0 : enemies.Min(e => Math.Max(Math.Abs(e.X - x), Math.Abs(e.Y - y)));
        Plan? best = null;
        double bestScore = double.MinValue;
        var enemy = Board.Other(side);
        int heldBefore = Board.PowerPoints.Count(pp => board.At(pp.X, pp.Y)?.Side == side);
        int enemyHeld = Board.PowerPoints.Count(pp => board.At(pp.X, pp.Y)?.Side == enemy);
        var reach = Reach(board, enemy);

        foreach (var p in board.Living(side).ToList())
        {
            var moves = board.Moves(p);
            if (moves.Count == 0) continue;
            double here = Danger(board, p, p.X, p.Y, reach) * caution;
            int near = Nearest(p.X, p.Y);
            bool onPoint = Board.IsPowerPoint(p.X, p.Y);
            foreach (var (x, y) in moves)
            {
                double score;
                var target = board.At(x, y);
                bool point = Board.IsPowerPoint(x, y);
                if (target is not null)
                {
                    double win = Odds(board, p, target, x, y);
                    score = win * Value(target.Type) - (1 - win) * Value(p.Type) * caution;
                    if (point) score += win * PointWorth(heldBefore + (onPoint ? 0 : 1), enemyHeld, true);
                }
                else
                {
                    score = here - Danger(board, p, x, y, reach) * caution + closing * (near - Nearest(x, y));
                    if (point && !onPoint) score += PointWorth(heldBefore + 1, enemyHeld, false);
                    // Edge toward the middle, and toward the enemy.
                    double towards = side == Side.Dawn ? x - p.X : p.X - x;
                    score += 0.03 * towards + 0.02 * (Math.Abs(p.Y - 4) - Math.Abs(y - 4));
                }
                if (onPoint && !point) score -= PointWorth(heldBefore, enemyHeld, false) * 0.8;
                score += (rng.NextDouble() - 0.5) * Noise;
                if (score > bestScore) { bestScore = score; best = new MovePlan(p, x, y); }
            }
        }

        var spell = BestSpell(board, side, reach, out double spellScore);
        if (spell is not null && (best is null || spellScore > bestScore + 0.15)) return spell;
        return best ?? spell ?? new SpellPlan(Spell.ShiftTime);
    }

    /// <summary>How much a power point matters: more the more you hold, and everything if it's the fifth.</summary>
    private static double PointWorth(int heldAfter, int enemyHeld, bool takenFromEnemy)
    {
        if (heldAfter >= Board.PowerPoints.Length) return 50;
        double w = 0.6 + 0.25 * heldAfter;
        if (takenFromEnemy && enemyHeld >= 4) w += 8;
        return w;
    }

    /// <summary>Where each enemy piece could go next turn.</summary>
    public static Dictionary<Piece, HashSet<(int X, int Y)>> Reach(Board board, Side enemy) =>
        board.Living(enemy).ToDictionary(q => q, board.Moves);

    /// <summary>The worst an enemy that can reach (x, y) next turn might do to this piece there.</summary>
    public static double Danger(Board board, Piece p, int x, int y, Dictionary<Piece, HashSet<(int X, int Y)>> reach)
    {
        double worst = 0;
        foreach (var (q, moves) in reach)
        {
            if (!moves.Contains((x, y))) continue;
            double win = Odds(q.Type, q.Side, q.Life + board.Bonus(q.Side, x, y), p.Type, p.Life + board.Bonus(p.Side, x, y));
            worst = Math.Max(worst, win * Value(p.Type) - (1 - win) * Value(q.Type));
        }
        return Math.Max(0, worst) * 0.85;
    }

    private SpellPlan? BestSpell(Board board, Side side, Dictionary<Piece, HashSet<(int X, int Y)>> reach, out double score)
    {
        score = double.MinValue;
        var leader = board.Leader(side);
        if (leader is null || leader.Imprisoned > 0) return null;
        var used = board.SpellsUsed(side);
        SpellPlan? best = null;
        void Consider(SpellPlan plan, double s, ref double bestScore)
        {
            s += (rng.NextDouble() - 0.5) * Noise;
            if (s > bestScore) { bestScore = s; best = plan; }
        }
        var enemy = Board.Other(side);

        if (!used.Contains(Spell.Heal))
            foreach (var p in board.Living(side).Where(p => p.Wounded && !Board.IsPowerPoint(p.X, p.Y)))
            {
                double missing = 1 - (double)p.Life / p.Type.Life;
                if (missing > 0.45) Consider(new SpellPlan(Spell.Heal, p), Value(p.Type) * missing - 0.25, ref score);
            }

        if (!used.Contains(Spell.Revive))
        {
            var spot = Spells.ReviveSquares(board, side).FirstOrDefault((-1, -1));
            if (spot.Item1 >= 0)
                foreach (var p in board.Fallen(side).Where(p => !p.Type.Leader))
                    if (Value(p.Type) > 1.6) Consider(new SpellPlan(Spell.Revive, p, X: spot.Item1, Y: spot.Item2), Value(p.Type) - 0.9, ref score);
        }

        foreach (var e in board.Living(enemy).Where(e => !Board.IsPowerPoint(e.X, e.Y)))
        {
            if (!used.Contains(Spell.Elemental))
            {
                double win = Odds(PieceType.All[Kind.Elemental], side, PieceType.All[Kind.Elemental].Life, e.Type, e.Life + board.Bonus(enemy, e.X, e.Y));
                if (win > 0.55) Consider(new SpellPlan(Spell.Elemental, e), win * Value(e.Type) - 1.0, ref score);
            }
            if (!used.Contains(Spell.Imprison) && e.Imprisoned == 0 && Value(e.Type) > 2.0)
                Consider(new SpellPlan(Spell.Imprison, e), Value(e.Type) * 0.35 - 0.4, ref score);
        }

        // Teleport a piece onto an empty power point, when it's worth it.
        if (!used.Contains(Spell.Teleport))
        {
            int held = Board.PowerPoints.Count(pp => board.At(pp.X, pp.Y)?.Side == side);
            foreach (var (x, y) in Board.PowerPoints.Where(pp => board.At(pp.X, pp.Y) is null))
            {
                var piece = board.Living(side).Where(p => !Board.IsPowerPoint(p.X, p.Y) && p.Imprisoned == 0)
                    .OrderBy(p => Danger(board, p, x, y, reach)).ThenByDescending(p => p.Life).FirstOrDefault();
                if (piece is null) continue;
                double s = (held + 1 >= Board.PowerPoints.Length ? 50 : 0.4 + 0.2 * held) - Danger(board, piece, x, y, reach) - 0.5;
                Consider(new SpellPlan(Spell.Teleport, piece, X: x, Y: y), s, ref score);
            }
        }
        return best;
    }
}
