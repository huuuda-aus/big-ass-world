using System.Collections.Generic;
using Realm.Gen.Settlements.Geometry;

namespace Realm.Gen.Settlements.Model;

/// <summary>What stands in the middle of the central square.</summary>
public enum CentrepieceKind
{
    /// <summary>Nothing; the square is left empty.</summary>
    None,
    /// <summary>A fountain.</summary>
    Fountain,
    /// <summary>A pyre.</summary>
    Pyre,
    /// <summary>An execution block.</summary>
    ExecutionBlock,
    /// <summary>A ring of market stalls.</summary>
    Market,
}
