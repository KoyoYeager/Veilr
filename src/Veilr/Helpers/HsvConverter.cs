namespace Veilr.Helpers;

public record HsvColor(double H, double S, double V);

public static class HsvConverter
{
    public static HsvColor FromRgb(int r, int g, int b)
    {
        double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double h = 0;
        if (delta != 0)
        {
            if (max == rd) h = 60 * (((gd - bd) / delta) % 6);
            else if (max == gd) h = 60 * (((bd - rd) / delta) + 2);
            else h = 60 * (((rd - gd) / delta) + 4);
        }
        if (h < 0) h += 360;

        double s = max == 0 ? 0 : (delta / max) * 255;
        double v = max * 255;

        return new HsvColor(h / 2, s, v); // H: 0-180, S: 0-255, V: 0-255
    }
}
