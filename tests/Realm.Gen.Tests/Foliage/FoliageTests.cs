using System;
using System.Collections.Generic;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Foliage;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests.Foliage;

public class FoliageTests
{
    private const int World = 1024;
    private const int Spacing = 8;
    private const int Side = (World / Spacing) + 1;
    private const double Half = World / 2.0;

    private static readonly MacroMapDefinition Map = TerrainTestHelpers.LandAndSea();

    // Flat at 50 m everywhere except a 45 degree ramp east of x = -256.
    private static HeightField Field(bool ramp = false)
    {
        var h = new float[Side * Side];
        for (int row = 0; row < Side; row++)
        {
            for (int col = 0; col < Side; col++)
            {
                double x = -Half + (col * (double)Spacing);
                h[(row * Side) + col] = ramp && x > -256 ? (float)(50 + (x + 256)) : 50f;
            }
        }

        return new HeightField(World, Spacing, h);
    }

    private static FoliageDefinition Oak(double perHectare = 200, double maxSlope = 35) =>
        new(ContentId.Parse("foliage.oak"), "Oak", "tree", TagSet.From(new[] { "deciduous" }),
            new SortedDictionary<ContentId, double> { [ContentId.Parse("biome.land")] = perHectare },
            4, 0, 900, maxSlope, 0.8, 1.2, 100, 0.5, 0.4);

    private static FoliageDefinition Grass() =>
        new(ContentId.Parse("foliage.grass"), "Grass", "grass", TagSet.From(new[] { "grass" }),
            new SortedDictionary<ContentId, double> { [ContentId.Parse("biome.land")] = 2000 },
            0, 0, 900, 40, 0.7, 1.3, 0, 0, 0);

    private static IReadOnlyDictionary<ContentId, FoliageDefinition> Species(params FoliageDefinition[] defs) => defs.ToDictionary(d => d.Id);

    private static FoliageExclusion Clear(IReadOnlyList<Settlement>? settlements = null) =>
        new(World, null, Array.Empty<Realm.Gen.Settlements.Roads.Road>(), settlements ?? Array.Empty<Settlement>(), new FoliageSettings());

    private static FoliageData Run(HeightField field, IReadOnlyDictionary<ContentId, FoliageDefinition> species, FoliageExclusion exclusion, ulong seed = 1) =>
        FoliageGenerator.Generate(Map, TerrainTestHelpers.Rasterize(Map), species, field, exclusion, seed, new FoliageSettings { BlendMeters = 64 }, new ValidationReport());

    [Fact]
    public void PlantsGrowOnLand_NotInTheSea_AndKeepTheirSpacing()
    {
        FoliageData data = Run(Field(), Species(Oak()), Clear());

        Assert.True(data.Instances.Count > 500, $"only {data.Instances.Count} trees");
        // The sea region is the east half; the biome blend lets density fade out a little before the border.
        Assert.All(data.Instances, i => Assert.True(i.X < 200, $"a tree stands far into the sea at {i.X:F0}"));
        Assert.Contains(data.Instances, i => i.X < -300);
        var report = new ValidationReport();
        FoliageValidator.Validate(data, Species(Oak()), Field(), Clear(), null, report);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public void SameInputs_SameFoliage_DifferentSeed_Different()
    {
        FoliageData a = Run(Field(), Species(Oak()), Clear());
        FoliageData b = Run(Field(), Species(Oak()), Clear());
        FoliageData c = Run(Field(), Species(Oak()), Clear(), seed: 2);

        Assert.Equal(a.Instances.ToArray(), b.Instances.ToArray());
        Assert.NotEqual(a.Instances.ToArray(), c.Instances.ToArray());
    }

    [Fact]
    public void AddingASpecies_DoesNotMoveTheOthers()
    {
        FoliageData alone = Run(Field(), Species(Oak()), Clear());
        FoliageDefinition shrub = Oak() with { Id = ContentId.Parse("foliage.shrub"), Kind = "bush" };

        FoliageData both = Run(Field(), Species(Oak(), shrub), Clear());

        int oak = both.Species.ToList().FindIndex(s => s.Id == "foliage.oak");
        Assert.Equal(alone.Instances.Select(i => (i.X, i.Z)).OrderBy(p => p).ToArray(), both.Instances.Where(i => i.Species == oak).Select(i => (i.X, i.Z)).OrderBy(p => p).ToArray());
    }

    [Fact]
    public void SettlementsKeepTheirPadAndMargin_Clear()
    {
        var town = new Settlement("settlement.test", "Test", SettlementTier.Town, -250, 0, 0, 10, 50, 60, 1, 30, false, CentrepieceKind.None);

        FoliageData data = Run(Field(), Species(Oak()), Clear(new[] { town }));

        double clear = town.PadRadiusMeters + new FoliageSettings().SettlementMarginMeters;
        Assert.All(data.Instances, i => Assert.True(Math.Sqrt(((i.X + 250) * (i.X + 250)) + (i.Z * i.Z)) >= clear - 5, $"a tree at ({i.X:F0}, {i.Z:F0}) is inside the settlement"));
        Assert.Contains(data.Instances, i => Math.Sqrt(((i.X + 250) * (i.X + 250)) + (i.Z * i.Z)) < clear + 80);
    }

    [Fact]
    public void SteepGround_GrowsNothing_WhenAboveTheSpeciesLimit()
    {
        FoliageData data = Run(Field(ramp: true), Species(Oak(maxSlope: 20)), Clear());

        Assert.NotEmpty(data.Instances);
        Assert.All(data.Instances, i => Assert.True(i.X < -250, $"a tree stands on the 45 degree ramp at {i.X:F0}"));
    }

    [Fact]
    public void Grass_GetsADensityMap_ThatIsEmptyInTheSea()
    {
        FoliageData data = Run(Field(), Species(Grass()), Clear());
        int cells = data.GrassSide;
        int seaColumn = (int)((Half + 400) / data.GrassCellMeters);
        int landColumn = (int)((Half - 200) / data.GrassCellMeters);

        Assert.Equal(0, data.GrassTufts[(cells / 2 * cells) + seaColumn]);
        Assert.True(data.GrassTufts[(cells / 2 * cells) + landColumn] > 20);
        Assert.Empty(data.Instances);
    }

    [Fact]
    public void TheFile_RoundTrips_WithinItsQuantisation()
    {
        FoliageData data = Run(Field(), Species(Oak(), Grass()), Clear());

        FoliageData back = FoliageFile.FromBytes(FoliageFile.ToBytes(data));

        Assert.Equal(data.Species.Select(sp => (sp.Id, sp.Kind, string.Join(',', sp.Tags), (float)sp.TrunkRadiusMeters)), back.Species.Select(sp => (sp.Id, sp.Kind, string.Join(',', sp.Tags), (float)sp.TrunkRadiusMeters)));
        Assert.Equal(data.Instances.Count, back.Instances.Count);
        Assert.Equal(data.GrassTufts, back.GrassTufts);
        Assert.Equal(data.GrassSpecies, back.GrassSpecies);
        foreach ((FoliageInstance a, FoliageInstance b) in data.Instances.Zip(back.Instances))
        {
            Assert.InRange(b.X, a.X - 0.1, a.X + 0.1);
            Assert.InRange(b.Z, a.Z - 0.1, a.Z + 0.1);
            Assert.InRange(b.Y, a.Y - 0.1, a.Y + 0.1);
            Assert.InRange(b.Scale, a.Scale - 0.03, a.Scale + 0.03);
        }

        Assert.Throws<System.IO.InvalidDataException>(() => FoliageFile.FromBytes(new byte[] { 1, 2, 3, 4 }));
    }

    [Fact]
    public void TheValidator_FlagsAPlantOnABlockedPlace()
    {
        FoliageData data = Run(Field(), Species(Oak()), Clear());
        var town = new Settlement("settlement.test", "Test", SettlementTier.Town, data.Instances[0].X, data.Instances[0].Z, 0, 10, 50, 60, 1, 30, false, CentrepieceKind.None);
        var report = new ValidationReport();

        FoliageValidator.Validate(data, Species(Oak()), Field(), Clear(new[] { town }), null, report);

        Assert.Contains(report.Issues, i => i.Code == FoliageCodes.PlacedWhereItCannotGrow);
    }

    [Fact]
    public void GrassScatter_IsDeterministic_AndStaysInsideTheCell()
    {
        IReadOnlyList<Tuft> a = GrassScatter.Cell(7, 40, 41, 32, 20480, 100);
        IReadOnlyList<Tuft> b = GrassScatter.Cell(7, 40, 41, 32, 20480, 100);

        Assert.Equal(a, b);
        Assert.Equal(100, a.Count);
        Assert.All(a, t =>
        {
            Assert.InRange(t.X, -10240 + (40 * 32), -10240 + (41 * 32));
            Assert.InRange(t.Z, -10240 + (41 * 32), -10240 + (42 * 32));
        });
        Assert.NotEqual(a, GrassScatter.Cell(7, 41, 41, 32, 20480, 100));
        Assert.Empty(GrassScatter.Cell(7, 0, 0, 32, 20480, 0));
    }

    [Fact]
    public void EveryStyle_BuildsANearAndASimplerFarModel()
    {
        foreach (string style in FoliageMeshes.Styles)
        {
            var near = FoliageMeshes.Build(style, far: false);
            var far = FoliageMeshes.Build(style, far: true);

            Assert.NotEmpty(near.Triangles);
            Assert.True(far.Triangles.Count <= near.Triangles.Count, style);
            Assert.True(near.HeightMeters > 0.3 && near.HeightMeters < 13, $"{style} is {near.HeightMeters} m tall");
        }

        Assert.Throws<ArgumentException>(() => FoliageMeshes.Build("nope", false));
        Assert.Equal("pine", FoliageMeshes.StyleFor("tree", new[] { "conifer" }));
        Assert.Equal("dead", FoliageMeshes.StyleFor("tree", new[] { "dead", "conifer" }));
        Assert.Equal("oak", FoliageMeshes.StyleFor("tree", Array.Empty<string>()));
        Assert.Equal("shrub", FoliageMeshes.StyleFor("bush", Array.Empty<string>()));
        Assert.Equal("tuft", FoliageMeshes.StyleFor("grass", Array.Empty<string>()));
    }

    [Fact]
    public void FixedInputs_GiveAStableFile_Golden()
    {
        byte[] bytes = FoliageFile.ToBytes(Run(Field(), Species(Oak(), Grass()), Clear()));

        ulong hash = 14695981039346656037UL;
        foreach (byte b in bytes)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        // Golden: a change means the foliage output changed. If intended, bump FoliageGenerator.GeneratorVersion and recapture.
        Assert.Equal(GoldenChecksum, hash);
    }

    private const ulong GoldenChecksum = 11299724178369465175UL;
}
