namespace AVAArchon.Engine;

/// <summary>One side's champion in the arena.</summary>
public sealed class Fighter
{
    public const double Radius = 26;

    public required PieceType Type { get; init; }
    public required Side Side { get; init; }
    /// <summary>The board piece it fights for; null for a summoned elemental.</summary>
    public Piece? Piece { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Life { get; set; }
    public double MaxLife { get; init; }
    /// <summary>The square's strength in it: the extra life it started with.</summary>
    public int Bonus { get; init; }
    /// <summary>The way it faces (one of eight), which is where it strikes or shoots.</summary>
    public int Fx { get; set; }
    public int Fy { get; set; }
    public double Cooldown { get; set; }
    /// <summary>Time left in a blow (melee) or a burst (aura).</summary>
    public double Striking { get; set; }
    public bool StrikeLanded { get; set; }
    public double Hurt { get; set; }
    public bool Moving { get; set; }
    public bool Dead => Life <= 0;
    public bool Computer { get; set; }
}

public sealed class Missile
{
    public required Fighter Owner { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Vx { get; init; }
    public double Vy { get; init; }
    public double Size { get; init; }
    public int Damage { get; init; }
    public bool Gone { get; set; }
}

/// <summary>
/// A pillar of the arena. It breathes: most of the time it is solid, blocking walkers and missiles,
/// but for a moment in each breath it fades to nothing and anything can pass.
/// </summary>
public sealed class Pillar
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public double Radius { get; init; } = 26;
    public double Phase { get; init; }
    public double Rate { get; init; } = 0.9;
    public double Breath(double time) => Math.Sin(time * Rate + Phase);
    public bool Solid(double time) => Breath(time) > -0.55;
}

/// <summary>
/// The arena where two pieces settle who holds a square. Dawn's champion starts on the left and
/// Dusk's on the right; the floor is as light as the square they fight over. It runs in the engine's
/// fixed steps; whoever's life runs out first loses the square (and if both fall together, both are lost).
/// </summary>
public sealed class Fight
{
    public const double Width = 960, Height = 540;
    /// <summary>The moment before the fight when both stand still and the names are called.</summary>
    public const double Ready = 1.2;

    private readonly Action<Sfx> _play;

    public Fighter Dawn { get; }
    public Fighter Dusk { get; }
    public List<Missile> Missiles { get; } = [];
    public List<Pillar> Pillars { get; } = [];
    public int Shade { get; }
    public double Time { get; private set; }
    public bool Over => Dawn.Dead || Dusk.Dead;
    /// <summary>The square fought over.</summary>
    public (int X, int Y) Square { get; }

    public Fight(Fighter dawn, Fighter dusk, int shade, (int, int) square, Random rng, Action<Sfx> play)
    {
        Dawn = dawn;
        Dusk = dusk;
        Shade = shade;
        Square = square;
        _play = play;
        dawn.X = 110; dawn.Y = Height / 2; dawn.Fx = 1; dawn.Fy = 0;
        dusk.X = Width - 110; dusk.Y = Height / 2; dusk.Fx = -1; dusk.Fy = 0;
        // Pillars in mirrored pairs, so neither side has the better ground.
        int pairs = 3 + rng.Next(2);
        for (int i = 0; i < pairs; i++)
        {
            double x = 260 + rng.NextDouble() * 160, y = 70 + rng.NextDouble() * (Height - 140);
            double phase = rng.NextDouble() * Math.PI * 2, rate = 0.7 + rng.NextDouble() * 0.6;
            Pillars.Add(new Pillar { X = x, Y = y, Phase = phase, Rate = rate });
            Pillars.Add(new Pillar { X = Width - x, Y = Height - y, Phase = phase, Rate = rate });
        }
    }

    /// <summary>Makes a piece's champion: its life now, plus what the square gives its side.</summary>
    public static Fighter Champion(Piece p, int bonus) => new()
    {
        Type = p.Type, Side = p.Side, Piece = p, Life = p.Life + bonus, MaxLife = p.Type.Life + bonus, Bonus = bonus,
    };

    public static Fighter Summoned(Side side) => new()
    {
        Type = PieceType.All[Kind.Elemental], Side = side, Life = PieceType.All[Kind.Elemental].Life,
        MaxLife = PieceType.All[Kind.Elemental].Life, Computer = true,
    };

    public Fighter Foe(Fighter f) => f == Dawn ? Dusk : Dawn;

    public void Update(double dt, Controls dawn, Controls dusk)
    {
        Time += dt;
        if (Over) return;
        foreach (var f in new[] { Dawn, Dusk })
        {
            if (f.Cooldown > 0) f.Cooldown -= dt;
            if (f.Hurt > 0) f.Hurt -= dt;
        }
        if (Time < Ready) return;
        Act(Dawn, dawn, dt);
        Act(Dusk, dusk, dt);
        MoveMissiles(dt);
        // A pillar coming back may catch a walker inside it: it's pushed clear.
        foreach (var f in new[] { Dawn, Dusk })
            if (!f.Type.Flies)
                foreach (var p in Pillars)
                {
                    double dx = f.X - p.X, dy = f.Y - p.Y, d = Math.Sqrt(dx * dx + dy * dy), min = p.Radius + Fighter.Radius;
                    if (p.Solid(Time) && d < min)
                    {
                        if (d < 0.01) { dx = 1; d = 1; }
                        f.X = Math.Clamp(p.X + dx / d * min, Fighter.Radius, Width - Fighter.Radius);
                        f.Y = Math.Clamp(p.Y + dy / d * min, Fighter.Radius, Height - Fighter.Radius);
                    }
                }
        if (Over) _play(Sfx.Death);
    }

    private void Act(Fighter f, Controls c, double dt)
    {
        var foe = Foe(f);
        f.Moving = false;
        if (f.Striking > 0)
        {
            f.Striking -= dt;
            Strike(f, foe);
            // A burst roots its maker to the spot; a blow only slows it.
            if (f.Type.Attack == Attack.Aura) return;
        }
        if (c.Dx != 0 || c.Dy != 0)
        {
            f.Fx = c.Dx;
            f.Fy = c.Dy;
            double len = Math.Sqrt(c.Dx * c.Dx + c.Dy * c.Dy);
            double speed = f.Type.Speed * (f.Striking > 0 ? 0.4 : 1.0) * dt;
            double nx = f.X + c.Dx / len * speed, ny = f.Y + c.Dy / len * speed;
            if (Free(f, nx, ny, foe)) { f.X = nx; f.Y = ny; f.Moving = true; }
            else if (Free(f, nx, f.Y, foe)) { f.X = nx; f.Moving = true; }
            else if (Free(f, f.X, ny, foe)) { f.Y = ny; f.Moving = true; }
        }
        if (c.Fire && f.Cooldown <= 0) Unleash(f);
    }

    /// <summary>Whether a fighter may stand at (x, y): inside the walls, not on its foe, and (walking) not in a solid pillar.</summary>
    public bool Free(Fighter f, double x, double y, Fighter foe)
    {
        if (x < Fighter.Radius || y < Fighter.Radius || x > Width - Fighter.Radius || y > Height - Fighter.Radius) return false;
        double fx = x - foe.X, fy = y - foe.Y;
        if (fx * fx + fy * fy < 4 * Fighter.Radius * Fighter.Radius &&
            fx * fx + fy * fy < (f.X - foe.X) * (f.X - foe.X) + (f.Y - foe.Y) * (f.Y - foe.Y)) return false;
        if (f.Type.Flies) return true;
        foreach (var p in Pillars)
        {
            double dx = x - p.X, dy = y - p.Y, min = p.Radius + Fighter.Radius - 2;
            if (p.Solid(Time) && dx * dx + dy * dy < min * min) return false;
        }
        return true;
    }

    private void Unleash(Fighter f)
    {
        var t = f.Type;
        switch (t.Attack)
        {
            case Attack.Shot:
            {
                double len = Math.Sqrt(f.Fx * f.Fx + f.Fy * f.Fy);
                double angle = Math.Atan2(f.Fy / len, f.Fx / len);
                for (int i = 0; i < t.Shots; i++)
                {
                    double a = angle + (t.Shots == 1 ? 0 : (i - (t.Shots - 1) / 2.0) * 0.3);
                    double vx = Math.Cos(a), vy = Math.Sin(a);
                    Missiles.Add(new Missile
                    {
                        Owner = f, X = f.X + vx * (Fighter.Radius + t.ShotSize), Y = f.Y + vy * (Fighter.Radius + t.ShotSize),
                        Vx = vx * t.ShotSpeed, Vy = vy * t.ShotSpeed, Size = t.ShotSize, Damage = t.Damage,
                    });
                }
                f.Cooldown = t.Reload;
                _play(ShotSound(t.Kind));
                break;
            }
            case Attack.Melee:
                f.Striking = 0.22;
                f.StrikeLanded = false;
                f.Cooldown = t.Reload;
                _play(Sfx.Swing);
                break;
            case Attack.Aura:
                f.Striking = 0.7;
                f.StrikeLanded = false;
                f.Cooldown = t.Reload + 0.7;
                _play(t.Kind == Kind.Wailer ? Sfx.Wail : Sfx.Flame);
                break;
        }
    }

    public static Sfx ShotSound(Kind k) => k switch
    {
        Kind.Archer => Sfx.Arrow,
        Kind.Stonewarden or Kind.Ogre => Sfx.Boulder,
        Kind.Stormcaller => Sfx.Lightning,
        Kind.Spitter => Sfx.Acid,
        Kind.Hydra or Kind.Elemental => Sfx.Fire,
        _ => Sfx.Bolt,
    };

    /// <summary>Where a blow lands: a little way in front, as far as the weapon reaches.</summary>
    public static (double X, double Y, double R) StrikeZone(Fighter f)
    {
        double len = Math.Sqrt(f.Fx * f.Fx + f.Fy * f.Fy);
        double reach = f.Type.Reach;
        return (f.X + f.Fx / len * (Fighter.Radius + reach * 0.5), f.Y + f.Fy / len * (Fighter.Radius + reach * 0.5), reach * 0.55);
    }

    private void Strike(Fighter f, Fighter foe)
    {
        if (f.StrikeLanded) return;
        bool hit;
        if (f.Type.Attack == Attack.Aura)
            hit = Distance(f, foe) < f.Type.Reach + Fighter.Radius;
        else
        {
            var (x, y, r) = StrikeZone(f);
            double dx = foe.X - x, dy = foe.Y - y;
            hit = dx * dx + dy * dy < (r + Fighter.Radius) * (r + Fighter.Radius);
        }
        if (!hit) return;
        f.StrikeLanded = true;
        Wound(foe, f.Type.Damage);
    }

    private void Wound(Fighter f, int damage)
    {
        f.Life = Math.Max(0, f.Life - damage);
        f.Hurt = 0.2;
        _play(Sfx.Hit);
    }

    private void MoveMissiles(double dt)
    {
        foreach (var m in Missiles)
        {
            if (m.Gone) continue;
            m.X += m.Vx * dt;
            m.Y += m.Vy * dt;
            if (m.X < -20 || m.Y < -20 || m.X > Width + 20 || m.Y > Height + 20) { m.Gone = true; continue; }
            foreach (var p in Pillars)
            {
                double dx = m.X - p.X, dy = m.Y - p.Y;
                if (p.Solid(Time) && dx * dx + dy * dy < (p.Radius + m.Size) * (p.Radius + m.Size)) { m.Gone = true; break; }
            }
            if (m.Gone) continue;
            var foe = Foe(m.Owner);
            double fx = m.X - foe.X, fy = m.Y - foe.Y;
            if (fx * fx + fy * fy < (Fighter.Radius + m.Size) * (Fighter.Radius + m.Size))
            {
                m.Gone = true;
                Wound(foe, m.Damage);
            }
        }
        Missiles.RemoveAll(m => m.Gone);
    }

    public static double Distance(Fighter a, Fighter b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>Whether a missile from (x0, y0) to (x1, y1) would get past the pillars (as they stand now).</summary>
    public bool ClearLine(double x0, double y0, double x1, double y1, double size)
    {
        double dx = x1 - x0, dy = y1 - y0, len2 = dx * dx + dy * dy;
        foreach (var p in Pillars)
        {
            if (!p.Solid(Time)) continue;
            double t = len2 == 0 ? 0 : Math.Clamp(((p.X - x0) * dx + (p.Y - y0) * dy) / len2, 0, 1);
            double cx = x0 + dx * t - p.X, cy = y0 + dy * t - p.Y;
            if (cx * cx + cy * cy < (p.Radius + size) * (p.Radius + size)) return false;
        }
        return true;
    }
}
