using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Validation;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class SettlementTerrainTests
{
    private const int World = 4096;
    private const int Spacing = 16;
    private const int Side = (World / Spacing) + 1;
    private const double Half = World / 2.0;

    private static HeightField Rolling()
    {
        var heights = new float[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                double x = -Half + (col * (double)Spacing);
                double z = -Half + (row * (double)Spacing);
                heights[(row * Side) + col] = (float)(100 + (8 * Math.Sin(x / 300) * Math.Cos(z / 260)));
            }
        }

        return new HeightField(World, Spacing, heights);
    }

    private static Settlement At(double x, double z, double ground = 100, double pad = 300) =>
        new("settlement.test", "Test", SettlementTier.Town, x, z, 0, ground, pad - 30, pad, 1, 40, false, CentrepieceKind.None);

    private static readonly SettlementSettings Full = new() { FlattenStrength = 1 };

    [Fact]
    public void Flatten_DoesNothingByDefault_SoCitiesStandOnTheNaturalGround()
    {
        HeightField field = Rolling();

        HeightField result = SettlementTerrain.Flatten(field, new[] { At(0, 0) });

        Assert.Same(field, result);
    }

    [Fact]
    public void Flatten_HalfStrength_MovesTheGroundHalfwayToThePad()
    {
        HeightField field = Rolling();
        float natural = field.At(Side / 2, Side / 2);

        HeightField half = SettlementTerrain.Flatten(field, new[] { At(0, 0) }, new SettlementSettings { FlattenStrength = 0.5 });

        Assert.Equal(natural + (0.5 * (100 - natural)), half.At(Side / 2, Side / 2), 3);
    }

    [Fact]
    public void Weight_IsOneInsidePad_HalfwayAtHalfBlend_ZeroBeyond()
    {
        Assert.Equal(1, SettlementTerrain.Weight(0, 300, 100));
        Assert.Equal(1, SettlementTerrain.Weight(300, 300, 100));
        Assert.Equal(0.5, SettlementTerrain.Weight(350, 300, 100), 12); // smoothstep(0.5) = 0.5
        Assert.Equal(0, SettlementTerrain.Weight(400, 300, 100));
        Assert.Equal(0, SettlementTerrain.Weight(900, 300, 100));
    }

    [Fact]
    public void Weight_FallsMonotonically_AcrossTheBlend()
    {
        double previous = 1;
        for (double d = 300; d <= 400; d += 5)
        {
            double w = SettlementTerrain.Weight(d, 300, 100);
            Assert.True(w <= previous + 1e-12, $"weight rose at {d}");
            previous = w;
        }
    }

    [Fact]
    public void Flatten_LevelsThePad_LeavesFarGroundAlone_AndKeepsTheInput()
    {
        HeightField field = Rolling();
        float[] before = field.Heights.ToArray();
        Settlement s = At(0, 0);

        HeightField flat = SettlementTerrain.Flatten(field, new[] { s }, Full);

        Assert.Equal(before, field.Heights.ToArray()); // the input is untouched
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                double x = -Half + (col * (double)Spacing);
                double z = -Half + (row * (double)Spacing);
                double d = Math.Sqrt((x * x) + (z * z));
                float h = flat.At(col, row);
                if (d <= 300)
                {
                    Assert.Equal(100.0, h, 3);
                }
                else if (d >= 400)
                {
                    Assert.Equal(before[(row * Side) + col], h);
                }
                else
                {
                    // In the blend zone the height lies between the natural height and the pad level.
                    float natural = before[(row * Side) + col];
                    Assert.InRange(h, Math.Min(natural, 100f) - 1e-3, Math.Max(natural, 100f) + 1e-3);
                }
            }
        }
    }

    [Fact]
    public void Flatten_TwoSettlements_EachGetTheirOwnLevel()
    {
        HeightField flat = SettlementTerrain.Flatten(Rolling(), new[] { At(-1200, 0, ground: 90), At(1200, 0, ground: 120) }, Full);

        Assert.Equal(90.0, flat.Sample(-1200, 0), 3);
        Assert.Equal(120.0, flat.Sample(1200, 0), 3);
    }

    [Fact]
    public void Validator_AcceptsGentleGround_AndFlagsAPadThatIsTooSteepOrOutOfRange()
    {
        HeightField gentle = Rolling();
        Settlement s = At(0, 0);
        var steep = new float[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                steep[(row * Side) + col] = 100 + (0.5f * ((col * Spacing) - (Side * Spacing / 2f))); // a 27 degree ramp
            }
        }

        var good = new ValidationReport();
        SettlementValidator.Validate(new[] { s }, gentle, null, null, good);
        var tooSteep = new ValidationReport();
        SettlementValidator.Validate(new[] { s }, new HeightField(World, Spacing, steep), null, null, tooSteep);
        var tooLow = new ValidationReport();
        SettlementValidator.Validate(new[] { s with { Highland = true } }, gentle, null, null, tooLow);

        Assert.Empty(good.Issues);
        Assert.Equal(SettlementCodes.PadUnsuitable, Assert.Single(tooSteep.Issues).Code);
        Assert.Equal(SettlementCodes.PadUnsuitable, Assert.Single(tooLow.Issues).Code); // 100 m is below the mountain minimum
    }

    [Fact]
    public void Validator_FlagsCloseSettlements_AndWaterOnThePad()
    {
        HeightField field = Rolling();
        Settlement a = At(0, 0);
        Settlement b = a with { Id = "settlement.other", X = 1000 };
        var points = new List<Realm.Gen.Terrain.Erosion.RiverPoint> { new(-100, 0, 100, 5), new(100, 0, 100, 5) };
        var network = new Realm.Gen.Terrain.Erosion.RiverNetwork(
            new List<Realm.Gen.Terrain.Erosion.River> { new(0, points, Realm.Gen.Terrain.Erosion.RiverMouth.Sea, null, 200) },
            new List<Realm.Gen.Terrain.Erosion.Lake>(), 32);
        var report = new ValidationReport();

        SettlementValidator.Validate(new[] { a, b }, field, network, null, report);

        Assert.Contains(report.Issues, i => i.Code == SettlementCodes.TooClose);
        Assert.Contains(report.Issues, i => i.Code == SettlementCodes.WaterOnPad);
    }

    [Fact]
    public void Manifest_RoundTrips_AndRejectsBadInput()
    {
        var manifest = new SettlementManifest
        {
            World = "world.test",
            Seed = 7,
            Generators = new[] { "settlement.placement v1" },
            Settlements = new[] { At(10.5, -20.25), At(500, 600) with { Id = "settlement.two", Tier = SettlementTier.City, Cathedral = true, Centre = CentrepieceKind.Fountain } },
        };

        var report = new ValidationReport();
        SettlementManifest? back = SettlementManifest.FromJson(manifest.ToJson(), report);

        Assert.Empty(report.Issues);
        Assert.NotNull(back);
        Assert.Equal(manifest.Settlements, back!.Settlements);
        Assert.Equal("world.test", back.World);
        Assert.Equal(7UL, back.Seed);
        Assert.Equal(manifest.ToJson(), back.ToJson());

        foreach (string json in new[] { "not json", "{\"format\": 99}", manifest.ToJson().Replace("\"Town\"", "\"Village\"") })
        {
            var bad = new ValidationReport();
            Assert.Null(SettlementManifest.FromJson(json, bad));
            Assert.Equal(SettlementCodes.ManifestInvalid, Assert.Single(bad.Issues).Code);
        }
    }
}
