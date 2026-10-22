using System.Collections.Generic;

namespace Realm.Gen.Foliage;

/// <summary>What the game needs to know about a species: its ID, kind, tags (the renderer picks a model from them) and trunk collision.</summary>
/// <param name="Id">Content ID, for example <c>foliage.oak</c>.</param>
/// <param name="Kind"><c>tree</c>, <c>bush</c> or <c>grass</c>.</param>
/// <param name="Tags">The record's tags, sorted.</param>
/// <param name="TrunkRadiusMeters">Collision radius a walking player bumps into; 0 for none.</param>
public sealed record FoliageSpecies(string Id, string Kind, IReadOnlyList<string> Tags, double TrunkRadiusMeters);
