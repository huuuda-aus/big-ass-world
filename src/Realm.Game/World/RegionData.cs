namespace Realm.Game.World;

/// <summary>The data for one terrain region on its way into Terrain3D.</summary>
/// <param name="Heights">Heights in metres, row by row; region size squared values.</param>
/// <param name="Colours">Colour per vertex packed 0xAABBGGRR, or null to let Terrain3D use its default colour.</param>
public sealed record RegionData(float[] Heights, uint[]? Colours);
