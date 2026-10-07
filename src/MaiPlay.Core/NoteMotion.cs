namespace MaiPlay.Core;

public readonly record struct RingPose(bool Visible, double Radius, double Scale, bool GuideVisible, double GuideRadius);
public readonly record struct HoldPose(bool Visible, double HeadRadius, double TailRadius, double Scale,
    bool GuideVisible, double GuideRadius, bool EndPointVisible);
public readonly record struct TouchPose(bool Visible, double FanAlpha, double FanOffset, bool Arrived);
public readonly record struct SlideStarPose(bool Visible, double Alpha, double Scale);

/// <summary>
/// MajdataPlay's default display mechanics, expressed as pure functions of music time.
/// Radius outputs are relative to the judgement ring. FanOffset uses upstream world units (ring = 4.8).
/// Sources and defaults are documented in docs/CHART-COMPATIBILITY.md; no Unity state machine is required.
/// </summary>
public static class NoteMotion
{
    public const double RingRadius = 4.8;
    public const double InnerRadius = 1.225;
    public const double AppearanceRate = .265;

    public static double TapVelocity(double speedSetting) =>
        107.25 / (71.4184491 * Math.Pow(speedSetting + .9975, -.985558604));

    public static double TapLeadIn(double speedSetting) =>
        Math.Max((RingRadius - InnerRadius + 1 / AppearanceRate) / TapVelocity(speedSetting), .15);

    public static double AppearTime(ChartNote note, double tapSpeed, double touchSpeed) =>
        note.Time - (note.Kind switch
        {
            NoteKind.Touch or NoteKind.TouchHold => TouchLeadIn(touchSpeed),
            NoteKind.Slide => SlideLeadIn(tapSpeed),
            _ => TapLeadIn(tapSpeed)
        });

    public static RingPose Tap(double chartTime, double hitTime, double speedSetting)
    {
        var distance = RingRadius + (chartTime - hitTime) * TapVelocity(speedSetting);
        var scale = 1 + AppearanceRate * (distance - InnerRadius);
        var radius = Math.Max(InnerRadius, distance) / RingRadius;
        return new(scale >= 0, radius, Math.Clamp(scale, 0, 1), scale > .3, radius);
    }

    public static HoldPose Hold(double chartTime, double hitTime, double duration, double speedSetting)
    {
        var tap = Tap(chartTime, hitTime, speedSetting);
        var tail = RingRadius + (chartTime - hitTime - duration) * TapVelocity(speedSetting);
        if (chartTime >= hitTime + duration)
            return new(tap.Visible, tail / RingRadius, tail / RingRadius, tap.Scale, tap.GuideVisible, tail / RingRadius, true);
        return new(tap.Visible, Math.Min(1, tap.Radius), Math.Max(InnerRadius, tail) / RingRadius,
            tap.Scale, tap.GuideVisible, Math.Min(1, tap.GuideRadius), tail >= InnerRadius);
    }

    public static double TouchLeadIn(double speedSetting) => 3.209385682 * Math.Pow(speedSetting, -.9549621752);

    public static TouchPose Touch(double chartTime, double hitTime, double speedSetting)
    {
        var whole = TouchLeadIn(speedSetting);
        var elapsed = chartTime - hitTime;
        var alpha = Math.Clamp((whole + elapsed) / (.2 * whole), 0, 1);
        var offset = elapsed < -.8 * whole ? .4 :
            Math.Clamp(.42 - Math.Exp(8 * (Math.Min(elapsed, 0) * .43 / (.8 * whole)) - .85), 0, .4);
        return new(elapsed >= -whole, alpha, offset, elapsed >= 0);
    }

    public static double SlideLeadIn(double speedSetting) => 3.926913 / TapVelocity(speedSetting);

    public static double SlideAlpha(double chartTime, double hitTime, double speedSetting, bool isWifi)
    {
        var start = hitTime - SlideLeadIn(speedSetting);
        if (chartTime < start) return 0;
        if (chartTime >= hitTime - .05) return 1;
        var duration = Math.Clamp(hitTime - .05 - start, 0, .2);
        return (isWifi ? 1 : .5) * (duration == 0 ? 1 : Math.Clamp((chartTime - start) / duration, 0, 1));
    }

    public static SlideStarPose SlideStar(double chartTime, double headTime, double startTime)
    {
        if (chartTime < headTime) return new(false, 0, 0);
        var alpha = startTime <= headTime ? 1 : Math.Clamp((chartTime - headTime) / (startTime - headTime), 0, 1);
        return new(true, alpha, .5 + alpha);
    }

    /// <summary>Unity degrees/second (convert sign for screen coordinates).</summary>
    public static double StarRotateSpeed(double duration, int timingUnits) =>
        Math.Max(-1080, -68.54838709677419 * (timingUnits * .47 / (duration * 1000)) / .0034803742562305);
}
