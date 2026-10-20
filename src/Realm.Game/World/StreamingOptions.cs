namespace Realm.Game.World;

/// <summary>How the region streamer loads and unloads regions around the camera.</summary>
/// <param name="LoadDistanceMeters">Regions whose squares come within this distance of the camera are wanted (about the fog distance plus a margin).</param>
/// <param name="UnloadDistanceMeters">Regions farther than this are removed; larger than <paramref name="LoadDistanceMeters"/> for hysteresis.</param>
/// <param name="MaxInFlight">Most regions being generated on worker threads at once.</param>
/// <param name="MaxCommitsPerFrame">Most regions added to the terrain in one frame.</param>
/// <param name="MaxRemovalsPerFrame">Most regions removed in one frame.</param>
/// <param name="CommitBudgetMs">No further region is added in a frame once this much main-thread time has gone on adds.</param>
/// <param name="UpdatePerAdd">True: Terrain3D rebuilds shared textures on every add. False: once per frame with adds.</param>
/// <param name="UpdatePerRemove">True: Terrain3D rebuilds on every remove.</param>
/// <param name="BuildOnWorker">True: the region object is built on the worker thread; false: on the main thread at commit time.</param>
/// <param name="MinRegion">Lowest region coordinate that exists on each axis (the edge of the world); defaults to Terrain3D's grid limit.</param>
/// <param name="MaxRegion">Highest region coordinate that exists on each axis; defaults to Terrain3D's grid limit.</param>
public sealed record StreamingOptions(
    double LoadDistanceMeters,
    double UnloadDistanceMeters,
    int MaxInFlight,
    int MaxCommitsPerFrame,
    int MaxRemovalsPerFrame,
    double CommitBudgetMs,
    bool UpdatePerAdd,
    bool UpdatePerRemove,
    bool BuildOnWorker,
    int MinRegion = -16,
    int MaxRegion = 15);
