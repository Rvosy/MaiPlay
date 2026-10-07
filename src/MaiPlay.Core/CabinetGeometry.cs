namespace MaiPlay.Core;

/// <summary>
/// An estimated physical scale for the existing chart plane. Contact geometry stays in chart coordinates.
/// The default assumes a 480-pixel judgement radius at 0.49005 mm/pixel; it is not hardware calibration.
/// The imported sensor canvas and approximate outer buttons have not been independently aligned to millimetres.
/// </summary>
public sealed class CabinetGeometry
{
    public static CabinetGeometry Approximate43Inch { get; } = new(235.224);

    public double JudgementRadiusMillimeters { get; }

    public CabinetGeometry(double judgementRadiusMillimeters)
    {
        if (!double.IsFinite(judgementRadiusMillimeters) || judgementRadiusMillimeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(judgementRadiusMillimeters));
        JudgementRadiusMillimeters = judgementRadiusMillimeters;
    }

    public ChartPoint ToMillimeters(ChartPoint position) =>
        new(position.X * JudgementRadiusMillimeters, position.Y * JudgementRadiusMillimeters);

    public double DistanceMillimeters(ChartPoint from, ChartPoint to) =>
        from.DistanceTo(to) * JudgementRadiusMillimeters;
}
