using System.Collections.Generic;

namespace Realm.Gen.Terrain.Erosion;

/// <summary>A lake: a depression that fills with water up to a flat surface.</summary>
/// <remarks>Thread-safety: immutable.</remarks>
public sealed class Lake
{
    internal Lake(int index, double surfaceHeight, double areaSquareKilometres, double centreX, double centreZ, IReadOnlyList<(double X, double Z)> cells)
    {
        Index = index;
        SurfaceHeight = surfaceHeight;
        AreaSquareKilometres = areaSquareKilometres;
        CentreX = centreX;
        CentreZ = centreZ;
        Cells = cells;
    }

    /// <summary>Position in <see cref="RiverNetwork.Lakes"/>; lakes are numbered in scan order, north-west first.</summary>
    public int Index { get; }

    /// <summary>Height of the flat water surface in metres above sea level.</summary>
    public double SurfaceHeight { get; }

    /// <summary>Surface area in square kilometres.</summary>
    public double AreaSquareKilometres { get; }

    /// <summary>East coordinate of the centre of the lake in metres from the world centre.</summary>
    public double CentreX { get; }

    /// <summary>South coordinate of the centre of the lake in metres from the world centre.</summary>
    public double CentreZ { get; }

    /// <summary>Centres of the working-grid cells the lake covers, in scan order.</summary>
    public IReadOnlyList<(double X, double Z)> Cells { get; }
}
