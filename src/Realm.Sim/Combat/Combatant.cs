using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Geometry;

namespace Realm.Sim.Combat;

/// <summary>
/// Anyone who takes part in a fight: the player, a creature or an NPC. Holds the stats the rules read and the state
/// the encounter changes (health, ready time, action points, conditions).
/// </summary>
/// <remarks>
/// Spike: a standalone object owned by one <see cref="CombatEncounter"/>; it becomes a set of components on a
/// <c>SimWorld</c> entity once combat joins the main simulation.
/// Thread-safety: not thread-safe; changed only by its encounter.
/// </remarks>
public sealed class Combatant
{
    private readonly Dictionary<string, int> _resistances;
    private readonly List<ActiveCondition> _conditions = new();

    /// <summary>Creates a combatant at full health, ready to act at tick 0, with full action points.</summary>
    /// <param name="id">Identifier, unique within the encounter; also the order in which combatants act.</param>
    /// <param name="name">Display name.</param>
    /// <param name="team">Side; combatants on different teams are enemies.</param>
    /// <param name="position">Where it stands.</param>
    /// <param name="maxHealth">Health when unhurt; must be positive.</param>
    /// <param name="attackRating">Added to an attack's bonus for hit rolls.</param>
    /// <param name="defence">Subtracted from an attacker's rating for hit rolls.</param>
    /// <param name="attacks">What it can do; at least one.</param>
    /// <param name="maxActionPoints">Action-point pool for the tactical pause; zero for anyone who can't pause.</param>
    /// <param name="resistances">Percent reduction per damage type, 0 to 100; missing types resist nothing.</param>
    /// <param name="speedMetersPerSecond">Walking speed; zero (the default) for anyone who never moves.</param>
    /// <exception cref="ArgumentException">Bad name, no attacks, or a resistance outside 0 to 100.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxHealth"/> is not positive, or <paramref name="maxActionPoints"/> or <paramref name="speedMetersPerSecond"/> is negative or not finite.</exception>
    public Combatant(
        int id,
        string name,
        int team,
        Point2D position,
        int maxHealth,
        int attackRating,
        int defence,
        IReadOnlyList<Attack> attacks,
        int maxActionPoints = 0,
        IReadOnlyDictionary<string, int>? resistances = null,
        double speedMetersPerSecond = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxHealth);
        ArgumentOutOfRangeException.ThrowIfNegative(maxActionPoints);
        if (!double.IsFinite(speedMetersPerSecond) || speedMetersPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(speedMetersPerSecond), speedMetersPerSecond, "Speed must be zero or more.");
        }
        ArgumentNullException.ThrowIfNull(attacks);
        if (attacks.Count == 0)
        {
            throw new ArgumentException("A combatant needs at least one attack.", nameof(attacks));
        }

        _resistances = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> pair in resistances ?? new Dictionary<string, int>())
        {
            if (pair.Value is < 0 or > CombatRules.PercentScale)
            {
                throw new ArgumentException($"Resistance to '{pair.Key}' must be 0 to 100, not {pair.Value}.", nameof(resistances));
            }

            _resistances[pair.Key] = pair.Value;
        }

        Id = id;
        Name = name;
        Team = team;
        Position = position;
        MaxHealth = maxHealth;
        Health = maxHealth;
        AttackRating = attackRating;
        Defence = defence;
        Attacks = attacks.ToArray();
        MaxActionPoints = maxActionPoints;
        ActionPoints = maxActionPoints;
        SpeedMetersPerSecond = speedMetersPerSecond;
    }

    /// <summary>Identifier, unique within the encounter.</summary>
    public int Id { get; }

    /// <summary>Display name.</summary>
    public string Name { get; }

    /// <summary>Side; combatants on different teams are enemies.</summary>
    public int Team { get; }

    /// <summary>Where it stands; changed by the encounter as it walks.</summary>
    public Point2D Position { get; internal set; }

    /// <summary>Walking speed in metres per second; zero for anyone who never moves.</summary>
    public double SpeedMetersPerSecond { get; }

    /// <summary>Health when unhurt.</summary>
    public int MaxHealth { get; }

    /// <summary>Current health; zero means dead.</summary>
    public int Health { get; internal set; }

    /// <summary>Added to an attack's bonus for hit rolls.</summary>
    public int AttackRating { get; }

    /// <summary>Subtracted from an attacker's rating for hit rolls.</summary>
    public int Defence { get; }

    /// <summary>What it can do, by index.</summary>
    public IReadOnlyList<Attack> Attacks { get; }

    /// <summary>Action-point pool for the tactical pause.</summary>
    public int MaxActionPoints { get; }

    /// <summary>Current action points.</summary>
    public int ActionPoints { get; internal set; }

    /// <summary>The tick from which it may act again.</summary>
    public long ReadyAt { get; internal set; }

    /// <summary>True while health is above zero.</summary>
    public bool IsAlive => Health > 0;

    /// <summary>Conditions currently on it, in the order they were applied.</summary>
    public IReadOnlyList<ActiveCondition> Conditions => _conditions;

    /// <summary>True when some active condition stops it from acting.</summary>
    public bool IsPrevented => _conditions.Exists(c => c.Definition.PreventsActions);

    /// <summary>Ticks accumulated towards the next action point.</summary>
    internal int ActionPointProgress { get; set; }

    /// <summary>Distance to another combatant in metres.</summary>
    /// <param name="other">The other combatant.</param>
    /// <returns>Straight-line distance on the ground plane.</returns>
    public double DistanceTo(Combatant other)
    {
        ArgumentNullException.ThrowIfNull(other);
        double dx = other.Position.X - Position.X;
        double dz = other.Position.Z - Position.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Percent of damage of one type that it ignores.</summary>
    /// <param name="damageType">The damage type.</param>
    /// <returns>0 to 100.</returns>
    public int ResistancePercent(string damageType) => _resistances.GetValueOrDefault(damageType);

    /// <summary>True when a condition with this identifier is active.</summary>
    /// <param name="conditionId">The condition identifier.</param>
    /// <returns>Whether it is active.</returns>
    public bool HasCondition(string conditionId) => _conditions.Exists(c => c.Definition.Id == conditionId);

    internal List<ActiveCondition> MutableConditions => _conditions;
}
