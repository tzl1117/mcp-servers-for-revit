using Newtonsoft.Json;

namespace RevitMCPCommandSet.Models.Common;

/// <summary>
/// </summary>
public class JZLine
{
    /// <summary>
    /// </summary>
    public JZLine()
    {
    }

    /// <summary>
    /// </summary>
    public JZLine(JZPoint p0, JZPoint p1)
    {
        P0 = p0;
        P1 = p1;
    }

    /// <summary>
    /// </summary>
    public JZLine(double x0, double y0, double z0, double x1, double y1, double z1)
    {
        P0 = new JZPoint(x0, y0, z0);
        P1 = new JZPoint(x1, y1, z1);
    }

    /// <summary>
    /// </summary>
    public JZLine(double x0, double y0, double x1, double y1)
    {
        P0 = new JZPoint(x0, y0, 0);
        P1 = new JZPoint(x1, y1, 0);
    }

    /// <summary>
    /// </summary>
    [JsonProperty("p0")]
    public JZPoint P0 { get; set; }

    /// <summary>
    /// </summary>
    [JsonProperty("p1")]
    public JZPoint P1 { get; set; }

    /// <summary>
    /// </summary>
    public double GetLength()
    {
        if (P0 == null || P1 == null)
            throw new InvalidOperationException("JZLine must have both P0 and P1 defined to calculate length.");

        var dx = P1.X - P0.X;
        var dy = P1.Y - P0.Y;
        var dz = P1.Z - P0.Z;

        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// </summary>
    public JZPoint GetDirection()
    {
        if (P0 == null || P1 == null)
            throw new InvalidOperationException("JZLine must have both P0 and P1 defined to calculate direction.");

        var dx = P1.X - P0.X;
        var dy = P1.Y - P0.Y;
        var dz = P1.Z - P0.Z;

        var length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        if (length == 0)
            throw new InvalidOperationException("Cannot determine direction for a line with zero length.");

        return new JZPoint(dx / length, dy / length, dz / length);
    }

    /// <summary>
    /// </summary>
    public static Line ToLine(JZLine jzLine)
    {
        if (jzLine.P0 == null || jzLine.P1 == null) return null;

        return Line.CreateBound(JZPoint.ToXYZ(jzLine.P0), JZPoint.ToXYZ(jzLine.P1));
    }
}
