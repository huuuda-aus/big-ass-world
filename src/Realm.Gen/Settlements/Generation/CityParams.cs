using System.Collections.Generic;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Generation;

/// <summary>Options for <see cref="CityGenerator"/>. Use <see cref="MaxBuildings"/> to size a city by house count.</summary>
public sealed class CityParams
{
    /// <summary>Seed of this city; the same seed and options give the same city.</summary>
    public int Seed { get; set; } = 1;

    /// <summary>Voronoi sites generated around the centre; most end up as farmland.</summary>
    public int SiteCount { get; set; } = 220;

    /// <summary>Wards used when neither CityPatchCount nor MaxBuildings says otherwise.</summary>
    public const int DefaultWardCount = 24;

    /// <summary>
    /// Number of patches (wards) enclosed by the wall. When null, it is derived from
    /// MaxBuildings if that is set, otherwise DefaultWardCount.
    /// </summary>
    public int? CityPatchCount { get; set; }

    /// <summary>
    /// Upper limit on the number of houses. Unless CityPatchCount is given, the city is grown to
    /// roughly this size first; the excess is then removed, outermost wards first. A limit the
    /// site count cannot reach simply gives the biggest city possible.
    /// </summary>
    public int? MaxBuildings { get; set; }

    /// <summary>Spacing scale of the site spiral, in metres.</summary>
    public double SiteSpacing { get; set; } = 32;

    /// <summary>Number of gates in the wall (at most the number of wall corners).</summary>
    public int GateCount { get; set; } = 4;

    /// <summary>Whether the ward next to the central square holds a cathedral; if not it is ordinary housing.</summary>
    public bool Cathedral { get; set; } = true;

    /// <summary>What stands in the central square. None leaves it empty.</summary>
    public CentrepieceKind Centre { get; set; } = CentrepieceKind.None;

    /// <summary>Service buildings the city must contain; empty leaves every building an ordinary house.</summary>
    public IReadOnlyList<ServiceSpec> Services { get; set; } = System.Array.Empty<ServiceSpec>();
}
