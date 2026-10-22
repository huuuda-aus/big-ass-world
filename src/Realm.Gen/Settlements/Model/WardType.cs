using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>The character of a ward.</summary>
public enum WardType
{
    /// <summary>A market ward next to the central square.</summary>
    Market,
    /// <summary>The cathedral ward.</summary>
    Cathedral,
    /// <summary>A merchant quarter.</summary>
    Merchant,
    /// <summary>A craftsmen quarter.</summary>
    Craftsmen,
    /// <summary>A slum.</summary>
    Slum,
    /// <summary>An open park without buildings.</summary>
    Park,
    /// <summary>Farmland outside the wall; only service buildings such as a farm stand there.</summary>
    Outskirts,
}
