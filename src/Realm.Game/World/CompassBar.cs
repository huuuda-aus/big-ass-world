using System;
using Godot;
using Realm.Core.Geometry;

namespace Realm.Game.World;

/// <summary>
/// A compass strip across the top of the screen: the bearing the view looks along under a fixed marker, with the cardinal points and a
/// tick every 5 degrees sliding past as you turn. Drawn at window resolution so it stays sharp.
/// </summary>
/// <remarks>Main thread only.</remarks>
public sealed partial class CompassBar : Control
{
    /// <summary>Width of the strip in pixels.</summary>
    public const float StripWidth = 560;

    /// <summary>Height of the strip in pixels.</summary>
    public const float StripHeight = 40;

    private const double VisibleDegrees = 180;
    private const int MinorTickDegrees = 5;
    private const int MajorTickDegrees = 15;
    private const float MarkerSize = 7;
    private const int LabelFontSize = 16;
    private const int BearingFontSize = 15;
    private const double RedrawThresholdDegrees = 0.05;

    private static readonly Color Background = new(0f, 0f, 0f, 0.38f);
    private static readonly Color Tick = new(1f, 1f, 1f, 0.8f);
    private static readonly Color North = new(1f, 0.35f, 0.3f);
    private static readonly Color Marker = new(1f, 0.9f, 0.4f);

    private double _heading;
    private double _drawnHeading = double.NaN;

    /// <summary>Creates the strip, anchored to the top centre of its parent.</summary>
    public CompassBar()
    {
        Name = "CompassBar";
        MouseFilter = MouseFilterEnum.Ignore;
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        OffsetLeft = -StripWidth / 2;
        OffsetRight = StripWidth / 2;
        OffsetTop = 6;
        OffsetBottom = 6 + StripHeight;
    }

    /// <summary>Sets the bearing the view looks along.</summary>
    /// <param name="headingDegrees">Degrees clockwise from north.</param>
    public void SetHeading(double headingDegrees)
    {
        _heading = CompassMath.Normalize(headingDegrees);
        if (double.IsNaN(_drawnHeading) || Math.Abs(CompassMath.Offset(_drawnHeading, _heading)) > RedrawThresholdDegrees)
        {
            QueueRedraw();
        }
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        _drawnHeading = _heading;
        Font font = ThemeDB.FallbackFont;
        float centre = StripWidth / 2;
        float pxPerDegree = StripWidth / (float)VisibleDegrees;
        DrawRect(new Rect2(0, 0, StripWidth, StripHeight), Background);

        int first = (int)Math.Floor((_heading - (VisibleDegrees / 2)) / MinorTickDegrees) * MinorTickDegrees;
        for (int bearing = first; bearing <= _heading + (VisibleDegrees / 2); bearing += MinorTickDegrees)
        {
            float x = centre + ((float)CompassMath.Offset(_heading, bearing) * pxPerDegree);
            if (x < 2 || x > StripWidth - 2)
            {
                continue;
            }

            int wrapped = (int)CompassMath.Normalize(bearing);
            bool cardinal = wrapped % 45 == 0;
            bool major = wrapped % MajorTickDegrees == 0;
            float length = cardinal ? 16 : major ? 11 : 6;
            Color colour = wrapped == 0 ? North : Tick;
            DrawLine(new Vector2(x, StripHeight - 2), new Vector2(x, StripHeight - 2 - length), colour, cardinal ? 2f : 1f);

            if (cardinal)
            {
                string text = CompassMath.PointName(wrapped);
                Color textColour = wrapped == 0 ? North : Colors.White;
                DrawString(font, new Vector2(x - 20, 17), text, HorizontalAlignment.Center, 40, LabelFontSize, textColour);
            }
            else if (wrapped % 30 == 0)
            {
                DrawString(font, new Vector2(x - 20, 17), wrapped.ToString(System.Globalization.CultureInfo.InvariantCulture), HorizontalAlignment.Center, 40, BearingFontSize - 2, new Color(1, 1, 1, 0.7f));
            }
        }

        // The fixed marker under the bearing and the current bearing beside it.
        DrawColoredPolygon(
            new[] { new Vector2(centre - MarkerSize, StripHeight), new Vector2(centre + MarkerSize, StripHeight), new Vector2(centre, StripHeight - MarkerSize - 1) },
            Marker);
        string bearingText = $"{(int)Math.Round(_heading) % 360:000}° {CompassMath.PointName(_heading)}";
        DrawString(font, new Vector2(centre - 40, StripHeight + 16), bearingText, HorizontalAlignment.Center, 80, BearingFontSize, Marker);
    }
}
