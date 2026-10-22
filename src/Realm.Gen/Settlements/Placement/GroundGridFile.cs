using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Realm.Core.Validation;

namespace Realm.Gen.Settlements.Placement;

/// <summary>Reads and writes the ground grids of all settlements (<c>settlements_ground.bin</c> next to the terrain tiles).</summary>
/// <remarks>
/// Layout (little endian): magic <c>SGRD</c>, format (int), count (int); then per grid: ID (length-prefixed UTF-8), origin X and Z (double),
/// spacing (double), side (int), side x side heights (float).
/// </remarks>
public static class GroundGridFile
{
    /// <summary>File name inside the baked directory.</summary>
    public const string FileName = "settlements_ground.bin";

    /// <summary>The format version written and accepted.</summary>
    public const int CurrentFormat = 1;

    private const uint Magic = 0x44524753; // "SGRD" read as a little-endian uint

    /// <summary>Serialises grids.</summary>
    /// <param name="grids">The grids, in the order to write them.</param>
    /// <returns>The file bytes.</returns>
    public static byte[] ToBytes(IReadOnlyList<GroundGrid> grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(CurrentFormat);
            writer.Write(grids.Count);
            foreach (GroundGrid g in grids)
            {
                writer.Write(g.SettlementId);
                writer.Write(g.OriginX);
                writer.Write(g.OriginZ);
                writer.Write(g.SpacingMeters);
                writer.Write(g.Side);
                foreach (float h in g.Heights)
                {
                    writer.Write(h);
                }
            }
        }

        return stream.ToArray();
    }

    /// <summary>Reads grids.</summary>
    /// <param name="bytes">The file bytes.</param>
    /// <param name="report">Receives <see cref="SettlementCodes.ManifestInvalid"/> when the file is unusable.</param>
    /// <returns>The grids by settlement ID, or null when the file is damaged or its format is unsupported.</returns>
    public static IReadOnlyDictionary<string, GroundGrid>? FromBytes(byte[] bytes, ValidationReport report)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(report);
        try
        {
            using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            if (reader.ReadUInt32() != Magic || reader.ReadInt32() != CurrentFormat)
            {
                report.Error(SettlementCodes.ManifestInvalid, FileName, $"Not a settlement ground file of format {CurrentFormat}.");
                return null;
            }

            int count = reader.ReadInt32();
            var result = new Dictionary<string, GroundGrid>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string id = reader.ReadString();
                double originX = reader.ReadDouble();
                double originZ = reader.ReadDouble();
                double spacing = reader.ReadDouble();
                int side = reader.ReadInt32();
                if (side < 2 || side > MaxSide)
                {
                    report.Error(SettlementCodes.ManifestInvalid, FileName, $"Ground grid '{id}' has an impossible size {side}.");
                    return null;
                }

                var heights = new float[side * side];
                for (int k = 0; k < heights.Length; k++)
                {
                    heights[k] = reader.ReadSingle();
                }

                result[id] = new GroundGrid(id, originX, originZ, spacing, side, heights);
            }

            return result;
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException)
        {
            report.Error(SettlementCodes.ManifestInvalid, FileName, $"The ground file is damaged: {e.Message}");
            return null;
        }
    }

    // A sanity limit so a damaged size cannot allocate gigabytes.
    private const int MaxSide = 2048;
}
