using System;
using System.IO;
using System.Linq;
using Realm.Core.Ids;
using Realm.Core.Validation;
using Realm.Data.Content;
using Realm.Gen.Settlements.Generation;
using Realm.Gen.Settlements.Geometry;
using Realm.Gen.Settlements.Model;
using Realm.Gen.Settlements.Placement;
using Xunit;

namespace Realm.Gen.Tests.Settlements;

public class ServiceBuildingTests
{
    private static readonly Lazy<ContentDatabase> Db = new(() =>
        ContentLoader.Load(Path.Combine(RepoRoot(), "content"), new ValidationReport()));

    private static Settlement Make(SettlementTier tier, int seed, int buildings) =>
        new($"settlement.test_{seed}", "Testford", tier, 0, 0, 0, 100, 250, 280, seed, buildings, tier == SettlementTier.City, CentrepieceKind.Fountain,
            ServiceCatalog.For(Db.Value, tier));

    [Fact]
    public void Catalog_GivesCitiesEverythingAndTownsASubset()
    {
        var city = ServiceCatalog.For(Db.Value, SettlementTier.City).Select(s => s.Id).ToHashSet();
        var town = ServiceCatalog.For(Db.Value, SettlementTier.Town).Select(s => s.Id).ToHashSet();

        Assert.Contains("service.city_hall", city);
        Assert.Contains("service.jail", city);
        Assert.DoesNotContain("service.city_hall", town);
        Assert.Contains("service.inn", town);
        Assert.Contains("service.blacksmith", town);
        Assert.True(town.IsSubsetOf(city));
    }

    [Theory]
    [InlineData(SettlementTier.City, 1200, 1)]
    [InlineData(SettlementTier.City, 1200, 2)]
    [InlineData(SettlementTier.City, 1200, 3)]
    [InlineData(SettlementTier.Town, 400, 4)]
    [InlineData(SettlementTier.Town, 400, 5)]
    [InlineData(SettlementTier.Town, 400, 6)]
    [InlineData(SettlementTier.City, 250, 1)]
    [InlineData(SettlementTier.City, 250, 2)]
    [InlineData(SettlementTier.City, 250, 3)]
    [InlineData(SettlementTier.City, 250, 4)]
    [InlineData(SettlementTier.Town, 50, 1)]
    [InlineData(SettlementTier.Town, 50, 2)]
    [InlineData(SettlementTier.Town, 50, 3)]
    [InlineData(SettlementTier.Town, 50, 4)]
    [InlineData(SettlementTier.Town, 50, 5)]
    public void EveryRequiredService_IsPresent_InTheRightPlace_WithNoIssues(SettlementTier tier, int buildings, int seed)
    {
        Settlement s = Make(tier, seed, buildings);
        City city = CityGenerator.Generate(s.ToCityParams());
        var report = new ValidationReport();

        ServiceValidator.Validate(s, city, report);

        Assert.Empty(report.Issues);
        foreach (ServiceSpec spec in s.ServiceList)
        {
            Assert.Equal(spec.Count, city.AllBuildings.Count(b => b.Service?.Id == spec.Id));
        }
    }

    [Fact]
    public void ServiceBuildings_AreDistinctBuildings_AndOutsideOnesAreBeyondTheWall()
    {
        Settlement s = Make(SettlementTier.City, 7, 1200);
        City city = CityGenerator.Generate(s.ToCityParams());

        var services = city.AllBuildings.Where(b => b.Service is not null).ToList();
        Assert.Equal(s.ServiceList.Sum(x => x.Count), services.Count);

        double wallRadius = city.Wall.Max(w => w.Length);
        foreach (Building b in services.Where(b => b.Service!.OutsideWall))
        {
            Assert.Equal(WardType.Outskirts, b.Ward);
            Assert.True(b.Footprint.Centroid.Length > city.Wall.Min(w => w.Length) - 1, "farm building inside the wall");
            Assert.True(b.Footprint.Area > 0);
            Assert.True(b.Footprint.Centroid.Length < wallRadius + 200, "farm building too far from the wall");
        }

        // No two buildings share an ID.
        Assert.Equal(city.AllBuildings.Count(), city.AllBuildings.Select(b => b.Id).Distinct().Count());
    }

    [Fact]
    public void SameSeed_GivesTheSameServiceLots()
    {
        Settlement s = Make(SettlementTier.City, 11, 600);
        string Lots(City c) => string.Join(";", c.AllBuildings.Where(b => b.Service is not null).Select(b => $"{b.Id}:{b.Service!.Id}"));

        Assert.Equal(Lots(CityGenerator.Generate(s.ToCityParams())), Lots(CityGenerator.Generate(s.ToCityParams())));
    }

    [Fact]
    public void AddingServices_DoesNotMoveAnyHouse()
    {
        Settlement s = Make(SettlementTier.City, 3, 600);
        City withServices = CityGenerator.Generate(s.ToCityParams());
        City without = CityGenerator.Generate(s.ToCityParams().WithoutServices());

        Assert.Equal(
            without.CityPatches.SelectMany(p => p.Buildings).Select(b => (b.Id, b.Footprint.Centroid)),
            withServices.CityPatches.SelectMany(p => p.Buildings).Select(b => (b.Id, b.Footprint.Centroid)));
    }

    [Fact]
    public void ServiceBuilding_GetsItsMainRoom()
    {
        Settlement s = Make(SettlementTier.City, 2, 600);
        City city = CityGenerator.Generate(s.ToCityParams());

        foreach (Building b in city.AllBuildings.Where(b => b.Service is not null))
        {
            Interior interior = InteriorGenerator.Generate(b, city.Seed);
            Assert.Contains(b.Service!.MainRoom, interior.Floors[0].RoomTypes);
        }
    }

    [Fact]
    public void Manifest_RoundTripsTheServices()
    {
        Settlement s = Make(SettlementTier.Town, 5, 400);
        var manifest = new SettlementManifest { World = "world.test", Seed = 1, Settlements = new[] { s } };

        SettlementManifest? back = SettlementManifest.FromJson(manifest.ToJson(), new ValidationReport());

        Assert.NotNull(back);
        Assert.Equal(s.ServiceList, back!.Settlements[0].ServiceList, ServiceSpecComparer.Instance);
        Assert.NotEmpty(back.Settlements[0].ServiceList);
    }

    [Fact]
    public void Validator_ReportsAMissingService()
    {
        Settlement s = Make(SettlementTier.Town, 5, 400);
        City city = CityGenerator.Generate(s.ToCityParams().WithoutServices());
        var report = new ValidationReport();

        ServiceValidator.Validate(s, city, report);

        Assert.Contains(report.Issues, i => i.Code == SettlementCodes.ServiceMissing);
    }

    [Fact]
    public void VocabularyWords_AllMapToGeneratorEnums()
    {
        foreach (string ward in ServiceVocabulary.Wards)
        {
            Assert.True(Enum.TryParse<WardType>(string.Concat(ward.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..])), out _), ward);
        }

        foreach (string near in ServiceVocabulary.Near)
        {
            Assert.True(Enum.TryParse<ServiceNear>(char.ToUpperInvariant(near[0]) + near[1..], out _), near);
        }

        foreach (string foundation in ServiceVocabulary.Foundations)
        {
            Assert.True(Enum.TryParse<FoundationStyle>(char.ToUpperInvariant(foundation[0]) + foundation[1..], out _), foundation);
        }

        foreach (string room in ServiceVocabulary.Rooms)
        {
            Assert.True(Enum.TryParse<RoomType>(string.Concat(room.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..])), out _), room);
        }
    }

    private sealed class ServiceSpecComparer : System.Collections.Generic.IEqualityComparer<ServiceSpec>
    {
        public static readonly ServiceSpecComparer Instance = new();

        public bool Equals(ServiceSpec? a, ServiceSpec? b) =>
            a is not null && b is not null && a.Id == b.Id && a.Count == b.Count && a.Near == b.Near && a.OutsideWall == b.OutsideWall
            && a.MinAreaMeters2 == b.MinAreaMeters2 && a.Foundation == b.Foundation && a.MainRoom == b.MainRoom && a.Wards.SequenceEqual(b.Wards);

        public int GetHashCode(ServiceSpec s) => s.Id.GetHashCode();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Realm.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Realm.sln not found above the test binary.");
    }
}

internal static class CityParamsExtensions
{
    public static CityParams WithoutServices(this CityParams p)
    {
        p.Services = Array.Empty<ServiceSpec>();
        return p;
    }
}
