using System.Windows.Media;

namespace Veilr.Models;

public class ColorTarget
{
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }

    public ColorTarget() { }

    public ColorTarget(byte r, byte g, byte b)
    {
        R = r; G = g; B = b;
    }

    public Color ToMediaColor() => Color.FromRgb(R, G, B);
    public int[] ToArray() => [R, G, B];

    public static ColorTarget FromArray(int[] rgb) =>
        new((byte)rgb[0], (byte)rgb[1], (byte)rgb[2]);

    public override bool Equals(object? obj) =>
        obj is ColorTarget other && R == other.R && G == other.G && B == other.B;

    public override int GetHashCode() => HashCode.Combine(R, G, B);
}
