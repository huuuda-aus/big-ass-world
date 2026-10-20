using Realm.Gen.Terrain;
using Xunit;

namespace Realm.Gen.Tests;

public class TerrainColouringTests
{
    private const double Top = 1000;
    private const double Depth = 200;

    [Fact]
    public void GentleGround_KeepsItsHeightColour()
    {
        Assert.Equal(TerrainColouring.Albedo(100, Top, Depth), TerrainColouring.Albedo(100, Top, Depth, TerrainColouring.RockSlopeStart));
    }

    [Fact]
    public void SteepGround_IsBareRock_WhateverItsHeight()
    {
        var rock = (110.0, 105.0, 100.0);

        Assert.Equal(rock, TerrainColouring.Albedo(20, Top, Depth, TerrainColouring.RockSlopeFull));
        Assert.Equal(rock, TerrainColouring.Albedo(900, Top, Depth, 3.0));
    }

    [Fact]
    public void HalfwayThroughTheSlopeRange_IsHalfwayBetweenGroundAndRock()
    {
        double midSlope = (TerrainColouring.RockSlopeStart + TerrainColouring.RockSlopeFull) / 2;
        var ground = TerrainColouring.Albedo(100, Top, Depth);
        var mixed = TerrainColouring.Albedo(100, Top, Depth, midSlope);

        // smoothstep(0.5) = 0.5
        Assert.Equal((ground.R + 110.0) / 2, mixed.R, 6);
        Assert.Equal((ground.G + 105.0) / 2, mixed.G, 6);
        Assert.Equal((ground.B + 100.0) / 2, mixed.B, 6);
    }

    [Fact]
    public void UnderWater_SlopeDoesNotMatter() =>
        Assert.Equal(TerrainColouring.Albedo(-50, Top, Depth), TerrainColouring.Albedo(-50, Top, Depth, 5.0));
}
