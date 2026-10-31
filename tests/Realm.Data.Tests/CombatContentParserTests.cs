using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Random;
using Realm.Core.Validation;
using Realm.Data.Content;
using Xunit;

namespace Realm.Data.Tests;

public class CombatContentParserTests
{
    private static ValidationIssue SingleError(ValidationReport report) =>
        Assert.Single(report.Issues, i => i.Severity == Severity.Error);

    // ---- condition ----

    [Fact]
    public void Condition_FullRecord_Parses()
    {
        var report = new ValidationReport();
        ConditionDefinition? c = ConditionParser.Parse(
            "id: condition.poisoned\nname: Poisoned\ntags: [toxin]\nduration_s: 10\npulse: { every_s: 1, damage: 2, type: poison }\n",
            "conditions/poisoned.yaml",
            report);

        Assert.Empty(report.Issues);
        Assert.Equal(10, c!.DurationSeconds);
        Assert.False(c.PreventsActions);
        Assert.Equal(new ConditionPulse(1, 2, "poison"), c.Pulse);
    }

    [Fact]
    public void Condition_WithoutPulse_DefaultsFlagsOff()
    {
        var report = new ValidationReport();
        ConditionDefinition? c = ConditionParser.Parse("id: condition.asleep\nname: Asleep\nduration_s: 5\nprevents_actions: true\nbreaks_on_damage: true\n", "c.yaml", report);

        Assert.Empty(report.Issues);
        Assert.True(c!.PreventsActions);
        Assert.True(c.BreaksOnDamage);
        Assert.Null(c.Pulse);
    }

    [Theory]
    [InlineData("duration_s: 0\n", ValidatorCodes.InvalidConditionField)]
    [InlineData("duration_s: 4000\n", ValidatorCodes.InvalidConditionField)]
    [InlineData("\n", ValidatorCodes.MissingField)]
    [InlineData("duration_s: 5\npulse: { every_s: 6, damage: 1 }\n", ValidatorCodes.InvalidConditionField)]
    [InlineData("duration_s: 5\npulse: { every_s: 1, damage: -1 }\n", ValidatorCodes.InvalidConditionField)]
    [InlineData("duration_s: 5\npulse: { every_s: 1 }\n", ValidatorCodes.InvalidConditionField)]
    [InlineData("duration_s: 5\npulse: { every_s: 1, damage: 1, type: Poison }\n", ValidatorCodes.InvalidConditionField)]
    public void Condition_BadValues_AreReported(string body, string code)
    {
        var report = new ValidationReport();
        Assert.Null(ConditionParser.Parse("id: condition.x\nname: X\n" + body, "c.yaml", report));
        Assert.Equal(code, SingleError(report).Code);
    }

    // ---- attack ----

    private const string Bow =
        "id: attack.goblin_bow\nname: Goblin bow\ntags: [ranged]\nrecovery_s: 1.5\nrange_m: 25\ndamage: 1d6\nattack_bonus: 1\n" +
        "applies: { condition: condition.poisoned, chance: 20 }\n";

    [Fact]
    public void Attack_FullRecord_Parses_WithDefaults()
    {
        var report = new ValidationReport();
        AttackDefinition? a = AttackParser.Parse(Bow, "attacks/goblin_bow.yaml", report);

        Assert.Empty(report.Issues);
        Assert.Equal(1.5, a!.RecoverySeconds);
        Assert.Equal(25, a.RangeMeters);
        Assert.Equal(new Dice(1, 6), a.Damage);
        Assert.Equal("physical", a.DamageType);
        Assert.Equal(1, a.AttackBonus);
        Assert.Equal(0, a.AreaRadiusMeters);
        Assert.Equal(1, a.ActionPointCost);
        Assert.Equal(new AttackCondition(ContentId.Parse("condition.poisoned"), 20), a.Applies);
    }

    [Theory]
    [InlineData("recovery_s: 0\nrange_m: 2\ndamage: 1d6\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: -1\ndamage: 1d6\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: 2\ndamage: 2x6\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: 2\n", ValidatorCodes.MissingField)]
    [InlineData("range_m: 2\ndamage: 1d6\n", ValidatorCodes.MissingField)]
    [InlineData("recovery_s: 1\nrange_m: 2\ndamage: 1d6\ndamage_type: Fire\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: 2\ndamage: 1d6\narea_radius_m: -2\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: 2\ndamage: 1d6\nap_cost: 500\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: 2\ndamage: 1d6\napplies: { condition: condition.x, chance: 120 }\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: 2\ndamage: 1d6\napplies: { condition: condition.x }\n", ValidatorCodes.InvalidAttackField)]
    [InlineData("recovery_s: 1\nrange_m: 2\ndamage: 1d6\napplies: { condition: item.torch, chance: 5 }\n", ValidatorCodes.ReferenceTypeMismatch)]
    public void Attack_BadValues_AreReported(string body, string code)
    {
        var report = new ValidationReport();
        Assert.Null(AttackParser.Parse("id: attack.x\nname: X\n" + body, "a.yaml", report));
        Assert.Equal(code, SingleError(report).Code);
    }

    // ---- creature ----

    private const string Goblin =
        "id: creature.goblin\nname: Goblin\ntags: [humanoid]\nbehaviour: nearest_enemy\nhealth: 14\nattack_rating: 1\ndefence: 2\nspeed_m_s: 4\n" +
        "attacks: [attack.goblin_club]\nresistances: { poison: 10 }\n" +
        "tiers:\n" +
        "  - { name: Goblin Archer, tags: [ranged], health: 10, speed_m_s: 3.5, attacks: [attack.goblin_bow] }\n" +
        "  - { name: Goblin Chief, defence: 4, resistances: { poison: 50, fire: 20 } }\n";

    [Fact]
    public void Creature_Tiers_OverrideOnlyWhatTheyGive()
    {
        var report = new ValidationReport();
        CreatureDefinition? c = CreatureParser.Parse(Goblin, "creatures/goblin.yaml", report);

        Assert.Empty(report.Issues);
        Assert.Equal("nearest_enemy", c!.Behaviour);
        Assert.Equal(3, c.Tiers.Count);

        CreatureTier goblin = c.Tier(1);
        Assert.Equal(("Goblin", 14, 1, 2), (goblin.Name, goblin.Health, goblin.AttackRating, goblin.Defence));
        Assert.Equal(new[] { ContentId.Parse("attack.goblin_club") }, goblin.Attacks);

        CreatureTier archer = c.Tier(2);
        Assert.Equal(("Goblin Archer", 10, 1, 2), (archer.Name, archer.Health, archer.AttackRating, archer.Defence));
        Assert.Equal(new[] { ContentId.Parse("attack.goblin_bow") }, archer.Attacks);
        Assert.Equal(new[] { "humanoid", "ranged" }, archer.Tags.ToArray());
        Assert.Equal(10, archer.Resistances["poison"]);
        Assert.Equal(3.5, archer.SpeedMetersPerSecond);

        CreatureTier chief = c.Tier(3);
        Assert.Equal((14, 4, 4.0), (chief.Health, chief.Defence, chief.SpeedMetersPerSecond)); // from the family, not the archer
        Assert.Equal(new[] { ContentId.Parse("attack.goblin_club") }, chief.Attacks);
        Assert.Equal(new[] { "humanoid" }, chief.Tags.ToArray());
        Assert.Equal(new[] { "fire", "poison" }, chief.Resistances.Keys.ToArray());
        Assert.Equal(50, chief.Resistances["poison"]);
    }

    [Theory]
    [InlineData("behaviour: berserk\nhealth: 5\nattacks: [attack.a]\n", ValidatorCodes.InvalidCreatureField)]
    [InlineData("health: 5\nattacks: [attack.a]\n", ValidatorCodes.MissingField)]
    [InlineData("behaviour: nearest_enemy\nattacks: [attack.a]\n", ValidatorCodes.MissingField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 0\nattacks: [attack.a]\n", ValidatorCodes.InvalidCreatureField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\n", ValidatorCodes.MissingField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\nattacks: []\n", ValidatorCodes.MissingField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\nattacks: [material.iron]\n", ValidatorCodes.ReferenceTypeMismatch)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\nattacks: [attack.a]\nresistances: { fire: 120 }\n", ValidatorCodes.InvalidCreatureField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\nattacks: [attack.a]\ntiers: [{ health: 9 }]\n", ValidatorCodes.MissingField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\nattacks: [attack.a]\ntiers: [{ name: Big, health: -1 }]\n", ValidatorCodes.InvalidCreatureField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\nattacks: [attack.a]\ntiers: [{ name: Big, attacks: [] }]\n", ValidatorCodes.InvalidCreatureField)]
    [InlineData("behaviour: nearest_enemy\nhealth: 5\nattacks: [attack.a]\ntiers: [{ name: Big, speed_m_s: -2 }]\n", ValidatorCodes.InvalidCreatureField)]
    public void Creature_BadValues_AreReported(string body, string code)
    {
        var report = new ValidationReport();
        Assert.Null(CreatureParser.Parse("id: creature.x\nname: X\nspeed_m_s: 3\n" + body, "c.yaml", report));
        Assert.Equal(code, SingleError(report).Code);
    }

    [Theory]
    [InlineData("", ValidatorCodes.MissingField)]
    [InlineData("speed_m_s: -1\n", ValidatorCodes.InvalidCreatureField)]
    [InlineData("speed_m_s: 99\n", ValidatorCodes.InvalidCreatureField)]
    public void Creature_Speed_IsRequiredAndBounded(string speed, string code)
    {
        var report = new ValidationReport();
        Assert.Null(CreatureParser.Parse("id: creature.x\nname: X\nbehaviour: nearest_enemy\nhealth: 5\nattacks: [attack.a]\n" + speed, "c.yaml", report));
        Assert.Equal(code, SingleError(report).Code);
    }

    [Fact]
    public void Creature_SpeedZero_IsAStationaryCreature()
    {
        var report = new ValidationReport();
        CreatureDefinition? c = CreatureParser.Parse("id: creature.x\nname: X\nbehaviour: nearest_enemy\nhealth: 5\nspeed_m_s: 0\nattacks: [attack.a]\n", "c.yaml", report);

        Assert.Empty(report.Issues);
        Assert.Equal(0, c!.Tier(1).SpeedMetersPerSecond);
    }

    [Fact]
    public void Creature_UnknownField_IsReported()
    {
        var report = new ValidationReport();
        Assert.Null(CreatureParser.Parse("id: creature.x\nname: X\nbehaviour: nearest_enemy\nhealth: 5\nspeed_m_s: 3\nattacks: [attack.a]\nloot: [item.torch]\n", "c.yaml", report));
        Assert.Equal(ValidatorCodes.YamlInvalid, SingleError(report).Code);
    }
}
