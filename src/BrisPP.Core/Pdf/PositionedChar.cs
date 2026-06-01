namespace BrisPP.Core.Pdf;

/// <summary>
/// A single glyph with page coordinates in a top-down space (Y grows downward,
/// so smaller Top = higher on the page). Coordinates are in PDF points.
/// </summary>
public sealed record PositionedChar(
    string Text,
    double Left,
    double Right,
    double Top,
    double Bottom,
    double FontSize,
    string FontName)
{
    public double Width => Right - Left;
    public double Height => Bottom - Top;
    public double CenterX => (Left + Right) / 2.0;
    /// <summary>Baseline proxy (top-down). Letters on one line share this within a tolerance.</summary>
    public double Baseline => Bottom;
}
