using System.Numerics;
using Realm.Gen.Settlements.Model;

namespace Realm.Gen.Settlements.Meshing;

/// <summary>Colours for city meshes (sRGB, 0 to 1). All are placeholders until the retro art pass.</summary>
public static class SettlementPalette
{
    private static readonly float[] StuccoShades = { 0.78f, 0.83f, 0.87f, 0.91f, 0.94f, 0.97f, 1.0f };

    /// <summary>Inside faces of walls.</summary>
    public static Vector3 Inner { get; } = Hex(0xf1e7d0);

    /// <summary>Ceilings.</summary>
    public static Vector3 Ceiling { get; } = Hex(0xe6dcc4);

    /// <summary>Stair treads.</summary>
    public static Vector3 Stair { get; } = Hex(0x8a6a48);

    /// <summary>City wall and towers.</summary>
    public static Vector3 Wall { get; } = Hex(0x9a948a);

    /// <summary>Street surface.</summary>
    public static Vector3 Street { get; } = Hex(0xa89878);

    /// <summary>Timber beams, doors and rough wood.</summary>
    public static Vector3 Wood { get; } = Hex(0x5b3b22);

    /// <summary>Window and door frames.</summary>
    public static Vector3 Frame { get; } = Hex(0xcfc3aa);

    /// <summary>Window glass.</summary>
    public static Vector3 Glass { get; } = Hex(0x3a5568);

    /// <summary>Wall reveals (the thickness of the wall seen in an opening).</summary>
    public static Vector3 Reveal { get; } = Hex(0xc9bd9f);

    /// <summary>Door handles.</summary>
    public static Vector3 Handle { get; } = Hex(0xb08d3c);

    /// <summary>Beams of half-timbering.</summary>
    public static Vector3 Beam { get; } = Hex(0x4a3320);

    /// <summary>Outer wall plaster of a building; varies slightly from building to building.</summary>
    /// <param name="buildingId">Building identifier within its city.</param>
    /// <returns>The colour.</returns>
    public static Vector3 Stucco(int buildingId)
    {
        uint hash = unchecked((uint)(buildingId + 1) * 2654435761u);
        return Hex(0xece1c8) * StuccoShades[hash % (uint)StuccoShades.Length];
    }

    /// <summary>Ground colour of a ward.</summary>
    /// <param name="ward">The ward.</param>
    /// <returns>The colour.</returns>
    public static Vector3 WardGround(WardType ward) => ward switch
    {
        WardType.Market => Hex(0xcbbf9f),
        WardType.Cathedral => Hex(0xb7b0c4),
        WardType.Merchant => Hex(0xb9a98a),
        WardType.Craftsmen => Hex(0xaa9c80),
        WardType.Slum => Hex(0x9c8e74),
        WardType.Park => Hex(0x7fa86a),
        _ => Hex(0xb9a98a),
    };

    /// <summary>Roof colour of a ward.</summary>
    /// <param name="ward">The ward.</param>
    /// <returns>The colour.</returns>
    public static Vector3 WardRoof(WardType ward) => ward switch
    {
        WardType.Market => Hex(0x8a4a3a),
        WardType.Cathedral => Hex(0x5d5a78),
        WardType.Merchant => Hex(0x9a4b34),
        WardType.Craftsmen => Hex(0x8d5a3a),
        WardType.Slum => Hex(0x6f5a45),
        WardType.Park => Hex(0x7a4a3a),
        _ => Hex(0x8a4a3a),
    };

    private static readonly int[] SignColours =
    {
        0xb23a2e, 0x2e6fb2, 0x3a8a46, 0xc99a2e, 0x7a3a8a, 0xc9572e, 0x2e8a8a, 0x8a8a2e, 0xa04a6a,
    };

    /// <summary>Colour of the sign over a service building's door; the same service always gets the same colour.</summary>
    /// <param name="serviceId">Content ID of the service, for example <c>service.inn</c>.</param>
    /// <returns>The colour.</returns>
    public static Vector3 Sign(string serviceId)
    {
        uint hash = 17;
        foreach (char c in serviceId)
        {
            hash = unchecked((hash * 31) + c);
        }

        return Hex(SignColours[hash % (uint)SignColours.Length]);
    }

    /// <summary>Floor colour of a room type.</summary>
    /// <param name="type">The room type.</param>
    /// <returns>The colour.</returns>
    public static Vector3 RoomFloor(RoomType type) => type switch
    {
        RoomType.Hall => Hex(0x8b6b4a),
        RoomType.LivingRoom => Hex(0x9a7650),
        RoomType.Kitchen => Hex(0xa89a82),
        RoomType.Bedroom => Hex(0x8a5e55),
        RoomType.Storage => Hex(0x6c5a44),
        RoomType.Workshop => Hex(0x7a6a58),
        RoomType.Shop => Hex(0xa8844e),
        RoomType.Nave => Hex(0x8c8a96),
        _ => Hex(0x8b6b4a),
    };

    /// <summary>Converts a 0xRRGGBB value to a colour.</summary>
    /// <param name="rgb">Packed colour.</param>
    /// <returns>Red, green and blue in 0 to 1.</returns>
    public static Vector3 Hex(int rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
}
