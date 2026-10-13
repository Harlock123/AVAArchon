namespace AVAArchon.Engine;

public enum Difficulty { Easy, Normal, Hard }

/// <summary>
/// The computer's hands in the arena. Every so often (quicker when harder) it looks at the fight:
/// it sidesteps missiles it sees coming, closes or keeps its distance as its weapon needs, lines up
/// along one of the eight directions it can strike in, and lets fly when the way is clear.
/// </summary>
public sealed class Brain(Difficulty difficulty, Random rng)
{
    private static readonly (int X, int Y)[] Ways = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];
    private double _think;
    private Controls _last;
    private readonly HashSet<Missile> _noticed = [];
    private readonly HashSet<Missile> _dodging = [];
    private int _wander = rng.Next(2) * 2 - 1;

    private double ThinkEvery => difficulty switch { Difficulty.Easy => 0.26, Difficulty.Normal => 0.13, _ => 0.06 };
    private double DodgeChance => difficulty switch { Difficulty.Easy => 0.25, Difficulty.Normal => 0.6, _ => 0.85 };
    /// <summary>How far off the line to the foe it will still shoot, in pixels at the foe.</summary>
    private double Slack => difficulty switch { Difficulty.Easy => 30, Difficulty.Normal => 18, _ => 11 };

    public Controls Think(Fight fight, Fighter me, double dt)
    {
        _think -= dt;
        if (_think > 0) return _last with { Fire = false };
        _think = ThinkEvery * (0.7 + rng.NextDouble() * 0.6);
        _last = Decide(fight, me);
        return _last;
    }

    private Controls Decide(Fight fight, Fighter me)
    {
        var foe = fight.Foe(me);
        var t = me.Type;
        double dist = Fight.Distance(me, foe);

        // Something coming? Step out of its path, at right angles.
        var threat = Threat(fight, me);
        if (threat is not null)
        {
            int px = Math.Sign(-threat.Vy), py = Math.Sign(threat.Vx);
            // Pick the side of the missile's path we're already on.
            double side = (me.X - threat.X) * -threat.Vy + (me.Y - threat.Y) * threat.Vx;
            if (side < 0) { px = -px; py = -py; }
            if (!fight.Free(me, me.X + px * 20, me.Y + py * 20, foe)) { px = -px; py = -py; }
            return new Controls(px, py, ReadyShot(fight, me, foe) is not null);
        }

        switch (t.Attack)
        {
            case Attack.Melee:
            {
                var (dx, dy) = Toward(me, foe);
                bool near = dist < Fighter.Radius * 2 + t.Reach + 6;
                return new Controls(dx, dy, near && me.Cooldown <= 0);
            }
            case Attack.Aura:
            {
                if (me.Cooldown > 0.4 && dist < t.Reach + 80)
                {
                    // Waiting for the next burst: keep away from its blows.
                    var (ax, ay) = Toward(foe, me);
                    return Steer(fight, me, foe, ax, ay);
                }
                var (dx, dy) = Toward(me, foe);
                return new Controls(dx, dy, dist < t.Reach + Fighter.Radius - 6 && me.Cooldown <= 0);
            }
            default:
            {
                if (ReadyShot(fight, me, foe) is { } way && me.Cooldown <= 0) return new Controls(way.X, way.Y, true);
                return Line(fight, me, foe);
            }
        }
    }

    /// <summary>The nearest of the eight ways from a to b.</summary>
    private static (int, int) Toward(Fighter a, Fighter b)
    {
        double angle = Math.Atan2(b.Y - a.Y, b.X - a.X);
        int i = ((int)Math.Round(angle / (Math.PI / 4)) + 8) % 8;
        return Ways[i];
    }

    /// <summary>A way to shoot that would hit the foe where it stands, if there's one with a clear line.</summary>
    private (int X, int Y)? ReadyShot(Fight fight, Fighter me, Fighter foe)
    {
        if (me.Type.Attack != Attack.Shot) return null;
        foreach (var (wx, wy) in Ways)
        {
            double len = Math.Sqrt(wx * wx + wy * wy), ux = wx / len, uy = wy / len;
            double along = (foe.X - me.X) * ux + (foe.Y - me.Y) * uy;
            if (along <= 0) continue;
            double off = Math.Abs((foe.X - me.X) * -uy + (foe.Y - me.Y) * ux);
            double slack = me.Type.Shots > 1 ? Slack + along * 0.25 : Slack;
            if (off < slack && fight.ClearLine(me.X, me.Y, foe.X, foe.Y, me.Type.ShotSize)) return (wx, wy);
        }
        return null;
    }

    /// <summary>Moves to line up a shot at a comfortable distance: tries each way, and picks the best place a moment on.</summary>
    private Controls Line(Fight fight, Fighter me, Fighter foe)
    {
        double want = me.Type.Speed < 100 ? 230 : 290;
        double bestScore = double.MaxValue;
        (int, int) best = (0, 0);
        foreach (var (wx, wy) in Ways.Append((0, 0)))
        {
            double len = wx == 0 && wy == 0 ? 1 : Math.Sqrt(wx * wx + wy * wy);
            double step = me.Type.Speed * 0.25;
            double x = me.X + wx / len * step, y = me.Y + wy / len * step;
            if ((wx, wy) != (0, 0) && !fight.Free(me, x, y, foe)) continue;
            double dx = foe.X - x, dy = foe.Y - y, d = Math.Sqrt(dx * dx + dy * dy);
            // How far off the nearest of the eight lines the foe would be.
            double off = double.MaxValue;
            foreach (var (ox, oy) in Ways)
            {
                double l = Math.Sqrt(ox * ox + oy * oy);
                if (dx * ox + dy * oy <= 0) continue;
                off = Math.Min(off, Math.Abs(dx * -oy / l + dy * ox / l));
            }
            double score = off + Math.Abs(d - want) * 0.35 + (fight.ClearLine(x, y, foe.X, foe.Y, me.Type.ShotSize) ? 0 : 60);
            // Keep off the walls, where there's no room to dodge.
            double edge = Math.Min(Math.Min(x, Fight.Width - x), Math.Min(y, Fight.Height - y));
            if (edge < 60) score += (60 - edge) * 0.8;
            score += rng.NextDouble() * 6;
            if (score < bestScore) { bestScore = score; best = (wx, wy); }
        }
        return new Controls(best.Item1, best.Item2);
    }

    /// <summary>Heads (roughly) in a direction, sliding round anything in the way.</summary>
    private Controls Steer(Fight fight, Fighter me, Fighter foe, int dx, int dy)
    {
        int i = Array.IndexOf(Ways, (dx, dy));
        for (int k = 0; k < 8; k++)
        {
            var (wx, wy) = Ways[((i + (k + 1) / 2 * (k % 2 == 0 ? 1 : -1) * _wander) % 8 + 8) % 8];
            if (fight.Free(me, me.X + wx * 24, me.Y + wy * 24, foe)) return new Controls(wx, wy);
        }
        _wander = -_wander;
        return new Controls(dx, dy);
    }

    /// <summary>A missile about to hit, that this brain noticed (and, by its skill, chose to dodge).</summary>
    private Missile? Threat(Fight fight, Fighter me)
    {
        foreach (var m in fight.Missiles)
        {
            if (m.Owner == me || m.Gone) continue;
            double rx = me.X - m.X, ry = me.Y - m.Y;
            double v2 = m.Vx * m.Vx + m.Vy * m.Vy;
            double tHit = (rx * m.Vx + ry * m.Vy) / v2;
            if (tHit < 0 || tHit > 0.7) continue;
            double cx = rx - m.Vx * tHit, cy = ry - m.Vy * tHit;
            if (cx * cx + cy * cy > (Fighter.Radius + m.Size + 10) * (Fighter.Radius + m.Size + 10)) continue;
            if (_noticed.Add(m) && rng.NextDouble() < DodgeChance) _dodging.Add(m);
            if (_dodging.Contains(m)) return m;
        }
        return null;
    }
}
