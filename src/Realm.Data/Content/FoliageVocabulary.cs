using System;
using System.Collections.Generic;

namespace Realm.Data.Content;

/// <summary>The words and limits a <c>foliage</c> record may use.</summary>
public static class FoliageVocabulary
{
    /// <summary>A tree: baked as individual instances, drawn as a 3D model near and a simpler one far.</summary>
    public const string Tree = "tree";

    /// <summary>A bush: like a tree, smaller.</summary>
    public const string Bush = "bush";

    /// <summary>Grass: baked as a density map only; tufts are scattered around the player at run time.</summary>
    public const string Grass = "grass";

    /// <summary>The kinds, in the order the generator processes them.</summary>
    public static readonly IReadOnlyList<string> Kinds = Array.AsReadOnly(new[] { Tree, Bush, Grass });

    /// <summary>Most plants of a tree or bush per hectare (10,000 square metres) a record may ask for.</summary>
    public const double MaxPlantsPerHectare = 600;

    /// <summary>Most tufts of grass per hectare a record may ask for.</summary>
    public const double MaxTuftsPerHectare = 20000;

    /// <summary>Default smallest distance between two plants of the same species, in metres.</summary>
    public const double DefaultMinSpacingMeters = 2;

    /// <summary>Default slope limit in degrees.</summary>
    public const double DefaultMaxSlopeDegrees = 35;

    /// <summary>Default highest ground a species grows on, in metres above sea level.</summary>
    public const double DefaultMaxHeightMeters = 900;
}
