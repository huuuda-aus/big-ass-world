namespace Realm.Gen.Terrain.Water;

/// <summary>
/// The water surface inside one square tile of the world, ready to become a mesh. Per vertex: position (3 floats, world metres),
/// texture coordinate (2 floats, metres: across the river from its centre line and along it from the source; world X and Z on lakes)
/// and a colour (4 floats: R flow speed share, G altitude share, B turbulence share, A closeness to the middle of the river).
/// </summary>
/// <param name="TileX">Tile column; tile 0 starts at the world's west edge.</param>
/// <param name="TileZ">Tile row; tile 0 starts at the world's north edge.</param>
/// <param name="Positions">Vertex positions, 3 floats each.</param>
/// <param name="Uvs">Texture coordinates, 2 floats per vertex.</param>
/// <param name="Colours">Vertex colours, 4 floats per vertex.</param>
/// <param name="Indices">Triangle list, counter-clockwise seen from above.</param>
public sealed record WaterTile(int TileX, int TileZ, float[] Positions, float[] Uvs, float[] Colours, int[] Indices)
{
    /// <summary>Number of vertices.</summary>
    public int VertexCount => Positions.Length / 3;
}
