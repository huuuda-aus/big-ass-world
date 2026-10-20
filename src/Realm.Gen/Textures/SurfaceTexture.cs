using System;

namespace Realm.Gen.Textures;

/// <summary>
/// A small tileable surface texture in the layout Terrain3D expects: an albedo image whose alpha holds the height,
/// and a normal image whose alpha holds the roughness. Both are RGBA, 8 bits per channel, row by row.
/// </summary>
/// <remarks>Determinism: produced by <see cref="SurfaceTextureGenerator"/>, identical on every machine. Immutable.</remarks>
public sealed class SurfaceTexture
{
    /// <summary>Creates a texture from its two images.</summary>
    /// <param name="name">Short identifier such as <c>grass</c>.</param>
    /// <param name="size">Width and height in pixels.</param>
    /// <param name="albedoHeight">RGBA bytes: colour in RGB, height in A; <c>size * size * 4</c> bytes.</param>
    /// <param name="normalRoughness">RGBA bytes: tangent-space normal in RGB (0.5 = zero), roughness in A; <c>size * size * 4</c> bytes.</param>
    /// <exception cref="ArgumentException">An array has the wrong length.</exception>
    public SurfaceTexture(string name, int size, byte[] albedoHeight, byte[] normalRoughness)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(albedoHeight);
        ArgumentNullException.ThrowIfNull(normalRoughness);
        int expected = size * size * 4;
        if (size <= 0 || albedoHeight.Length != expected || normalRoughness.Length != expected)
        {
            throw new ArgumentException($"Both images must be {size} x {size} RGBA ({expected} bytes).");
        }

        Name = name;
        Size = size;
        AlbedoHeight = albedoHeight;
        NormalRoughness = normalRoughness;
    }

    /// <summary>Short identifier.</summary>
    public string Name { get; }

    /// <summary>Width and height in pixels.</summary>
    public int Size { get; }

    /// <summary>Albedo colour in RGB and height in A.</summary>
    public byte[] AlbedoHeight { get; }

    /// <summary>Normal in RGB and roughness in A.</summary>
    public byte[] NormalRoughness { get; }
}
