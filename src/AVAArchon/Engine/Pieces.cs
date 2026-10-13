namespace AVAArchon.Engine;

/// <summary>The controls one player has: a stick (eight ways), fire, and a second button.</summary>
public record struct Controls(int Dx, int Dy, bool Fire = false, bool Alt = false);

/// <summary>The two orders at war. Dawn is strong on light squares, Dusk on dark ones.</summary>
public enum Side { Dawn, Dusk }

public enum Kind
{
    // Dawn
    Lightbringer, Sentinel, Archer, Gryphon, Stonewarden, Lancer, Stormcaller, Sunbird,
    // Dusk
    Shadelord, Ghoul, Spitter, Wyvern, Ogre, Wraith, Hydra, Wailer,
    // Summoned for one fight by a leader's spell
    Elemental,
}

/// <summary>How a piece fights in the arena.</summary>
public enum Attack
{
    /// <summary>A blow at close range, in the direction it faces.</summary>
    Melee,
    /// <summary>Missiles, in the direction it faces.</summary>
    Shot,
    /// <summary>A burst all around it; it can't move while the burst lasts.</summary>
    Aura,
}

/// <summary>
/// What a kind of piece is. <see cref="Move"/> is how far it goes on the board; fliers may pass over
/// other pieces, walkers may not. In the arena: <see cref="Life"/>, <see cref="Speed"/> in pixels a
/// second, and its weapon: <see cref="Damage"/> per hit, <see cref="Reload"/> seconds between attacks,
/// and for missiles their speed, size and how many fly at once (in a fan).
/// </summary>
public sealed record PieceType(
    Kind Kind, string Name, Side Side, int Move, bool Flies, int Life, double Speed,
    Attack Attack, int Damage, double Reload, double Reach = 0, double ShotSpeed = 0, double ShotSize = 0, int Shots = 1,
    string Weapon = "", string About = "")
{
    public bool Leader => Kind is Kind.Lightbringer or Kind.Shadelord;

    /// <summary>A rough worth in battle, for the computer's judgement: how long it lasts times how hard it hits.</summary>
    public double Power => Life * (Damage * Shots / Reload) * (Attack == Attack.Shot ? 1.15 : 1.0) * (0.6 + Speed / 400);

    public static readonly IReadOnlyDictionary<Kind, PieceType> All = new Dictionary<Kind, PieceType>
    {
        [Kind.Lightbringer] = new(Kind.Lightbringer, "Lightbringer", Side.Dawn, 3, true, 22, 160, Attack.Shot, 11, 0.9, ShotSpeed: 460, ShotSize: 7,
            Weapon: "sunbolts", About: "Leads Dawn. Flies, and casts the spells."),
        [Kind.Sentinel] = new(Kind.Sentinel, "Sentinel", Side.Dawn, 3, false, 12, 140, Attack.Melee, 6, 0.55, Reach: 42,
            Weapon: "sword", About: "Dawn's foot soldier: a shield and a sword."),
        [Kind.Archer] = new(Kind.Archer, "Archer", Side.Dawn, 3, false, 11, 130, Attack.Shot, 6, 0.85, ShotSpeed: 420, ShotSize: 4,
            Weapon: "arrows", About: "Quick arrows, little armour."),
        [Kind.Gryphon] = new(Kind.Gryphon, "Gryphon", Side.Dawn, 5, true, 16, 215, Attack.Melee, 8, 0.6, Reach: 44,
            Weapon: "talons", About: "Flies far, and strikes fast."),
        [Kind.Stonewarden] = new(Kind.Stonewarden, "Stonewarden", Side.Dawn, 3, false, 28, 85, Attack.Shot, 13, 1.6, ShotSpeed: 230, ShotSize: 11,
            Weapon: "hurled stones", About: "A walking statue: slow, tough, and heavy-handed."),
        [Kind.Lancer] = new(Kind.Lancer, "Lancer", Side.Dawn, 4, false, 17, 170, Attack.Melee, 10, 0.75, Reach: 62,
            Weapon: "lance", About: "A mounted knight with a long reach."),
        [Kind.Stormcaller] = new(Kind.Stormcaller, "Stormcaller", Side.Dawn, 4, true, 18, 130, Attack.Shot, 9, 1.2, ShotSpeed: 560, ShotSize: 6,
            Weapon: "lightning", About: "Rides the wind and throws lightning."),
        [Kind.Sunbird] = new(Kind.Sunbird, "Sunbird", Side.Dawn, 5, true, 22, 175, Attack.Aura, 14, 1.5, Reach: 130,
            Weapon: "a burst of flame", About: "Bursts into flame, burning all around it."),

        [Kind.Shadelord] = new(Kind.Shadelord, "Shadelord", Side.Dusk, 3, true, 22, 160, Attack.Shot, 11, 0.9, ShotSpeed: 460, ShotSize: 7,
            Weapon: "shadowbolts", About: "Leads Dusk. Flies, and casts the spells."),
        [Kind.Ghoul] = new(Kind.Ghoul, "Ghoul", Side.Dusk, 3, false, 12, 140, Attack.Melee, 6, 0.55, Reach: 42,
            Weapon: "claws", About: "Dusk's foot soldier: hungry and quick."),
        [Kind.Spitter] = new(Kind.Spitter, "Spitter", Side.Dusk, 3, false, 14, 120, Attack.Shot, 8, 1.05, ShotSpeed: 390, ShotSize: 6,
            Weapon: "acid", About: "A great lizard that spits acid."),
        [Kind.Wyvern] = new(Kind.Wyvern, "Wyvern", Side.Dusk, 5, true, 16, 210, Attack.Melee, 9, 0.6, Reach: 44,
            Weapon: "fangs", About: "Flies far, and bites hard."),
        [Kind.Ogre] = new(Kind.Ogre, "Ogre", Side.Dusk, 3, false, 27, 90, Attack.Shot, 14, 1.75, ShotSpeed: 220, ShotSize: 12,
            Weapon: "hurled rocks", About: "Huge, slow, and throws rocks."),
        [Kind.Wraith] = new(Kind.Wraith, "Wraith", Side.Dusk, 4, true, 14, 185, Attack.Melee, 10, 0.7, Reach: 50,
            Weapon: "a chilling touch", About: "A drifting spirit with a deadly touch."),
        [Kind.Hydra] = new(Kind.Hydra, "Hydra", Side.Dusk, 3, false, 27, 110, Attack.Shot, 5, 1.35, ShotSpeed: 360, ShotSize: 6, Shots: 3,
            Weapon: "three heads of fire", About: "Three heads, three flames at once."),
        [Kind.Wailer] = new(Kind.Wailer, "Wailer", Side.Dusk, 4, true, 21, 170, Attack.Aura, 13, 1.5, Reach: 135,
            Weapon: "a wail", About: "Its wail wounds everything near it."),

        [Kind.Elemental] = new(Kind.Elemental, "Elemental", Side.Dawn, 0, true, 24, 175, Attack.Shot, 10, 1.0, ShotSpeed: 480, ShotSize: 9,
            Weapon: "fire", About: "Summoned by a spell for one fight."),
    };
}

/// <summary>A piece on the board (or fallen).</summary>
public sealed class Piece
{
    public required int Id { get; init; }
    public required PieceType Type { get; init; }
    public required Side Side { get; init; }
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Its life now; wounds heal a little each turn.</summary>
    public int Life { get; set; }
    public bool Alive { get; set; } = true;
    /// <summary>Turns of its own side it still can't move, from the Imprison spell.</summary>
    public int Imprisoned { get; set; }

    public Kind Kind => Type.Kind;
    public string Name => Type.Name;
    public bool Wounded => Life < Type.Life;
    public override string ToString() => $"{Side} {Name} at {X},{Y} ({Life}/{Type.Life})";
}
