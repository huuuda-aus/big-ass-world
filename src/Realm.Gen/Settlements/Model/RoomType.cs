using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>What a room is used for.</summary>
public enum RoomType
{
    /// <summary>An entrance hall.</summary>
    Hall,
    /// <summary>A living room.</summary>
    LivingRoom,
    /// <summary>A kitchen.</summary>
    Kitchen,
    /// <summary>A bedroom.</summary>
    Bedroom,
    /// <summary>A storeroom.</summary>
    Storage,
    /// <summary>A workshop.</summary>
    Workshop,
    /// <summary>A shop floor.</summary>
    Shop,
    /// <summary>The single hall of a cathedral.</summary>
    Nave,
}
