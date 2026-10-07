using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MaiPlay.Core;
using MaiPlay.Skins;

namespace MaiPlay.Rendering;

/// <summary>Draws the current audio-clock instant; never advances chart time itself.</summary>
public sealed class GameView : Control
{
    public Chart? Chart { get; set; }
    public PlaybackSession? Session { get; set; }
    public PlaybackSession? Engine { get => Session; set => Session = value; }
    public double Time { get; set; }
    public SkinPack? Skin { get; set; }
    public double TapSpeed { get; set; } = 7.5;
    public double TouchSpeed { get; set; } = 7.5;
    public bool ShowSensors { get; set; }
    public bool HighlightContacts { get; set; } = true;
    public bool ShowHandMarkers { get; set; } = true;
    public bool ShowHandTrails { get; set; }
    public HandReplay? HandReplay { get; set; }
    private double _unit;
    private Point _center;
    private static readonly IBrush BackgroundBrush = Brush.Parse("#0C1830");
    private static readonly IBrush MutedBrush = Brush.Parse("#7490B4");
    private static readonly Pen RingPen = new(Brush.Parse("#314663"), .025);
    private static readonly IBrush LeftHandBrush = Brush.Parse("#70BFFF");
    private static readonly IBrush RightHandBrush = Brush.Parse("#F4B06A");
    private static readonly IBrush LeftContactBrush = Brush.Parse("#3870BFFF");
    private static readonly IBrush RightContactBrush = Brush.Parse("#38F4B06A");
    private static readonly IBrush UnknownContactBrush = Brush.Parse("#3851E6AC");
    private HandReplay? ActiveHandReplay => Session?.Mode == PlayMode.Algorithm ? HandReplay : null;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        _unit = Math.Min(Bounds.Width, Bounds.Height) / 11.8;
        _center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (_unit <= 0) return;
        using var clip = context.PushClip(new Rect(Bounds.Size));
        using var transform = context.PushTransform(Matrix.CreateScale(_unit, _unit) * Matrix.CreateTranslation(_center.X, _center.Y));

        DrawArena(context);
        if (ShowSensors || HighlightContacts) DrawSensors(context);
        if (ShowHandTrails) DrawHandTrails(context);
        if (Chart is null)
        {
            Text(context, "MAIPLAY", new Point(0, -.25), .55, Brushes.White);
            Text(context, "打开歌曲目录，或载入演示", new Point(0, .42), .23, MutedBrush);
            return;
        }

        // Tracks and guide rings sit behind note bodies; judgements sit above them.
        Text(context, "达成率", new Point(0, -.32), .20, MutedBrush);
        Text(context, Session?.Achievement.DisplayText ?? "—", new Point(0, .13), .48, Brushes.White);
        foreach (var note in Chart.Notes)
            if (Visible(note) && note.Kind == NoteKind.Slide) DrawSlide(context, note);
        DrawEachLines(context);
        if (ShowHandMarkers) DrawHandMarkers(context);
        foreach (var note in Chart.Notes)
            if (Visible(note) && note.Kind is NoteKind.Tap or NoteKind.Hold) DrawGuide(context, note);
        foreach (var note in Chart.Notes.Reverse())
        {
            if (!Visible(note)) continue;
            switch (note.Kind)
            {
                case NoteKind.Tap: DrawTap(context, note); break;
                case NoteKind.Hold: DrawHold(context, note); break;
                case NoteKind.Touch: case NoteKind.TouchHold: DrawTouch(context, note); break;
            }
        }
        DrawTouchBorders(context);
        DrawJudgements(context);
    }

    private void DrawArena(DrawingContext context)
    {
        context.DrawEllipse(Brush.Parse("#101F37"), null, default, 4.8, 4.8);
        context.DrawEllipse(null, RingPen, default, 4.8, 4.8);
        context.DrawEllipse(null, new Pen(Brush.Parse("#233650"), .015), default, 1.225, 1.225);
        for (var i = 1; i <= 8; i++)
        {
            var p = RingPosition(i, 5.34);
            Text(context, i.ToString(), p, .2, MutedBrush);
            var a = RingPosition(i, 4.67); var b = RingPosition(i, 4.91);
            context.DrawLine(new Pen(Brush.Parse("#7088AA"), .035), a, b);
        }
        Sprite(context, "outline.png", default);
    }

    private void DrawSensors(DrawingContext context)
    {
        var off = Brush.Parse("#164F6884");
        var edge = new Pen(Brush.Parse("#526C86"), .018);
        foreach (var polygon in ContactMapper.SensorPolygons)
        {
            var points = polygon.Points.Select(World).ToArray();
            if (ShowSensors) context.DrawGeometry(off, edge, Polygon(points));
            if (HighlightContacts && Session?.Inputs.IsSensorOn(polygon.Area) == true)
                DrawContactRegion(context, points, Session.Inputs.SensorSources(polygon.Area));
        }
        if (ShowSensors) foreach (var group in ContactMapper.SensorPolygons.GroupBy(p => p.Area))
        {
            var points = group.SelectMany(p => p.Points).ToArray();
            var center = group.Key == SensorArea.C ? new Point(0, -.8)
                : World(new ChartPoint(points.Average(p => p.X), points.Average(p => p.Y)));
            Text(context, group.Key.ToString(), center, .17, MutedBrush);
        }
        for (var i = 0; i < ContactMapper.ButtonPolygons.Count; i++)
        {
            var active = Session?.Inputs.IsButtonOn(i + 1) == true;
            var points = ContactMapper.ButtonPolygons[i].Select(World).ToArray();
            if (ShowSensors) context.DrawGeometry(off, edge, Polygon(points));
            if (HighlightContacts && active) DrawContactRegion(context, points, Session!.Inputs.ButtonSources(i + 1));
        }
    }

    private void DrawContactRegion(DrawingContext context, Point[] points, IReadOnlySet<string> sources)
    {
        var left = false; var right = false;
        if (ActiveHandReplay is { } replay)
            foreach (var source in sources)
            {
                left |= replay.HandForSource(source) == HandSide.Left;
                right |= replay.HandForSource(source) == HandSide.Right;
            }
        var geometry = Polygon(points);
        if (left && right)
        {
            var bounds = geometry.Bounds;
            using var clip = context.PushGeometryClip(geometry);
            context.FillRectangle(LeftContactBrush, new Rect(bounds.X, bounds.Y, bounds.Width / 2, bounds.Height));
            context.FillRectangle(RightContactBrush, new Rect(bounds.X + bounds.Width / 2, bounds.Y, bounds.Width / 2, bounds.Height));
        }
        else context.DrawGeometry(left ? LeftContactBrush : right ? RightContactBrush : UnknownContactBrush, null, geometry);
    }

    private void DrawHandMarkers(DrawingContext context)
    {
        if (ActiveHandReplay is not { } replay) return;
        foreach (var hand in replay.Hands)
        {
            if (replay.PoseAt(hand, Time) is not { } pose) continue;
            var p = World(pose.Position);
            var brush = hand == HandSide.Left ? LeftHandBrush : RightHandBrush;
            foreach (var contact in pose.Contacts.Where(c => c.Channel == InputChannel.Touch))
            {
                var centre = World(new(pose.Position.X + contact.Offset.X, pose.Position.Y + contact.Offset.Y));
                context.DrawEllipse(hand == HandSide.Left ? LeftContactBrush : RightContactBrush,
                    new Pen(brush, .025), centre, contact.Radius * 4.8, contact.Radius * 4.8);
            }
            var pressed = Session is { } session &&
                (Enumerable.Range(1, 8).Any(i => session.Inputs.ButtonSources(i).Any(s => replay.HandForSource(s) == hand)) ||
                 Enum.GetValues<SensorArea>().Any(a => session.Inputs.SensorSources(a).Any(s => replay.HandForSource(s) == hand)));
            // 标记随谱面缩放，仅表示手位，不代表接触面积。
            var scale = pressed ? .9 : 1;
            var r = .42 * scale;
            var pen = new Pen(brush, .085 * scale);
            var fill = pressed ? brush : null;
            if (hand == HandSide.Left) context.DrawEllipse(fill, pen, p, r, r);
            else context.DrawGeometry(fill, pen, Polygon(new(p.X, p.Y - r), new(p.X + r, p.Y), new(p.X, p.Y + r), new(p.X - r, p.Y)));
        }
    }

    private void DrawHandTrails(DrawingContext context)
    {
        if (ActiveHandReplay is not { } replay) return;
        const double duration = .18;
        const int steps = 18;
        foreach (var hand in replay.Hands)
        {
            for (var i = 0; i < steps; i++)
            {
                var time = Time - duration + duration * i / steps;
                var before = replay.PoseAt(hand, time);
                var after = replay.PoseAt(hand, time + duration / steps);
                if (before is null || after is null || before.Position == after.Position) continue;
                var touching = before.Contacts.Count != 0 && after.Contacts.Count != 0;
                if (!touching && i % 4 >= 2) continue;
                using var opacity = context.PushOpacity(.14 + .52 * i / steps);
                context.DrawLine(new Pen(hand == HandSide.Left ? LeftHandBrush : RightHandBrush, .06),
                    World(before.Position), World(after.Position));
            }
        }
    }

    private bool Visible(ChartNote n)
    {
        var appeared = n.Kind switch
        {
            NoteKind.Touch or NoteKind.TouchHold => NoteMotion.Touch(Time, n.Time, TouchSpeed).Visible,
            NoteKind.Slide => Time >= n.Time - NoteMotion.SlideLeadIn(TapSpeed),
            _ => NoteMotion.Tap(Time, n.Time, TapSpeed).Visible
        };
        if (!appeared) return false;
        if (Session?.GetResult(n) is not null) return false;
        return Session is not null || Time <= n.EndTime;
    }

    private void DrawGuide(DrawingContext context, ChartNote note)
    {
        if (note.Kind == NoteKind.Hold)
        {
            var pose = NoteMotion.Hold(Time, note.Time, note.Duration, TapSpeed);
            if (pose.GuideVisible) Sprite(context, SkinSprites.Guide(note), default, Angle(note.StartPosition), pose.GuideRadius);
        }
        else
        {
            var pose = NoteMotion.Tap(Time, note.Time, TapSpeed);
            if (pose.GuideVisible) Sprite(context, SkinSprites.Guide(note), default, Angle(note.StartPosition), pose.GuideRadius);
        }
    }

    private void DrawEachLines(DrawingContext context)
    {
        if (Skin is null || Chart is null) return;
        foreach (var group in Chart.Notes.Where(n => n.IsEach && n.Kind is NoteKind.Tap or NoteKind.Hold && Visible(n)).GroupBy(n => n.Time))
        {
            var pose = NoteMotion.Tap(Time, group.Key, TapSpeed);
            if (!pose.GuideVisible) continue;
            var notes = group.OrderBy(n => n.StartPosition).ToArray();
            for (var i = 0; i < notes.Length - 1; i++)
            {
                var a = notes[i].StartPosition; var b = notes[i + 1].StartPosition;
                var distance = b - a;
                if (distance > 4) { (a, b) = (b, a); distance = 8 - distance; }
                if (distance == 0) continue;
                Sprite(context, $"NoteGuideSkins/EachLine{distance}.png", default, (a - 1) * Math.PI / 4,
                    pose.GuideRadius);
            }
        }
    }

    private void DrawTap(DrawingContext context, ChartNote note)
    {
        var pose = NoteMotion.Tap(Time, note.Time, TapSpeed);
        var p = RingPosition(note.StartPosition, pose.Radius * 4.8);
        var scale = pose.Scale;
        var age = Math.Max(0, Time - (note.Time - NoteMotion.TapLeadIn(TapSpeed)));
        var angle = Angle(note.StartPosition) - note.StarRotateSpeed * Math.PI / 180 * age;
        if (Skin is not null)
        {
            Sprite(context, SkinSprites.Tap(note), p, angle, scale);
            if (note.IsEx) Sprite(context, SkinSprites.Ex(note), p, angle, scale, tint: ExTint(note));
        }
        else if (note.IsStar) Star(context, p, .62 * scale, Color(note), angle, note.IsDoubleStar);
        else
        {
            context.DrawEllipse(BackgroundBrush, new Pen(Color(note), .15 * scale), p, .47 * scale, .47 * scale);
            if (note.IsEx) context.DrawEllipse(null, new Pen(Brushes.White, .04), p, .61 * scale, .61 * scale);
        }
    }

    private void DrawHold(DrawingContext context, ChartNote note)
    {
        var pose = NoteMotion.Hold(Time, note.Time, note.Duration, TapSpeed);
        var head = pose.HeadRadius * 4.8;
        var tail = pose.TailRadius * 4.8;
        var length = head - tail + (pose.Scale < 1 ? 1.42 : 1.4);
        var midpoint = RingPosition(note.StartPosition, (head + tail) / 2);
        var scale = pose.Scale;
        var active = Time >= note.Time;
        var holding = Session?.IsHolding(note) == true;
        var released = active && !holding;
        using var transform = context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateRotation(Angle(note.StartPosition))
                                                   * Matrix.CreateTranslation(midpoint.X, midpoint.Y));
        if (Skin is not null)
        {
            NineSlice(context, SkinSprites.Hold(note, holding, released), 1.22, length);
            if (note.IsEx) NineSlice(context, SkinSprites.Ex(note), 1.22, length, ExTint(note));
            if (pose.EndPointVisible) Sprite(context, SkinSprites.HoldEnd(note), new Point(0, length / 2 - .6825));
        }
        else
        {
            var rect = new Rect(-.47, -length / 2, .94, length);
            context.DrawRectangle(BackgroundBrush, new Pen(released ? MutedBrush : Color(note), .14), rect, .46, .46);
            if (active && !released) context.DrawEllipse(new SolidColorBrush(Avalonia.Media.Color.FromArgb(80, 255, 240, 135)), null,
                new Point(0, -length / 2 + .7), .57, .57);
        }
    }

    private void DrawTouch(DrawingContext context, ChartNote note)
    {
        var p = TouchPosition(note.TouchArea, note.StartPosition);
        var hold = note.Kind == NoteKind.TouchHold;
        var pose = NoteMotion.Touch(Time, note.Time, TouchSpeed);
        var fanDistance = .226 + pose.FanOffset;
        if (Skin is null)
        {
            context.DrawEllipse(Color(note), null, p, .08, .08);
            if (!hold && pose.Arrived) context.DrawEllipse(null, new Pen(Color(note), .045), p, .64, .64);
            if (hold && pose.Arrived)
            {
                var progress = Math.Clamp((Time - note.Time) / Math.Max(note.Duration, .001), 0, 1);
                var points = Enumerable.Range(0, 61).Select(i =>
                {
                    var a = progress * Math.Tau * i / 60;
                    return new Point(p.X + Math.Sin(a) * .72, p.Y - Math.Cos(a) * .72);
                }).ToArray();
                context.DrawGeometry(null, new Pen(Session?.IsHolding(note) == true ? Brushes.LightYellow : MutedBrush, .055), Polyline(points));
            }
        }
        // TouchHold border has lower sorting order than the four fans.
        if (hold && Skin is not null && pose.Arrived)
        {
            var released = Session?.IsHolding(note) != true;
            var border = released ? "TouchHoldSkins/touchhold_off.png"
                : $"TouchHoldSkins/touchhold{(note.IsBreak ? "_break" : "")}_border.png";
            var progress = Math.Clamp((Time - note.Time) / Math.Max(note.Duration, .001), 0, 1);
            if (progress > 0) using (context.PushGeometryClip(Sector(p, 1.2, progress))) Sprite(context, border, p);
        }
        using (context.PushOpacity(pose.FanAlpha))
        for (var i = 3; i >= 0; i--)
        {
            var a = i * Math.PI / 2 + (hold ? Math.PI / 4 : 0);
            var fan = new Point(p.X + Math.Sin(a) * fanDistance, p.Y - Math.Cos(a) * fanDistance);
            if (Skin is not null)
                Sprite(context, hold ? $"TouchHoldSkins/touchhold{(note.IsBreak ? "_break" : "")}_{i}.png"
                    : $"TouchSkins/touch{SkinSprites.Variant(note)}.png", fan, Math.PI + a);
            else
            {
                using var fanTransform = context.PushTransform(Matrix.CreateRotation(a) * Matrix.CreateTranslation(fan.X, fan.Y));
                var fanColor = hold ? i switch { 0 => Brushes.Tomato, 1 => Brushes.Gold, 2 => Brushes.SpringGreen, _ => Brushes.DeepSkyBlue } : Color(note);
                context.DrawGeometry(fanColor, null, Polygon(new Point(-.3, -.18), new Point(.3, -.18), new Point(0, .2)));
            }
        }
        if (Skin is not null)
        {
            Sprite(context, SkinSprites.TouchPoint(note), p);
            if (!hold && pose.Arrived) Sprite(context, "TouchSkins/touch_just.png", p);
        }
    }

    private void DrawTouchBorders(DrawingContext context)
    {
        if (Chart is null || Skin is null) return;
        foreach (var group in Chart.Notes.Where(n => n.Kind is NoteKind.Touch or NoteKind.TouchHold && Visible(n))
                     .GroupBy(n => n.TouchArea))
        {
            var queued = group.OrderBy(n => n.Time).Take(3).ToArray();
            // Upstream TouchBorder uses the second and third queued note's colour.
            for (var i = 1; i < queued.Length; i++)
                Sprite(context, SkinSprites.TouchBorder(queued[i], i + 1), TouchPosition(queued[i].TouchArea, queued[i].StartPosition));
        }
    }

    private void DrawSlide(DrawingContext context, ChartNote note)
    {
        var path = note.SlidePath;
        if (path is null) return;
        var progress = Math.Clamp((Time - note.SlideStartTime) / note.Duration, 0, 1);
        double Consumed(int branch) => Session?.GetSlideProgress(note, branch) ?? 0;
        using (context.PushOpacity(NoteMotion.SlideAlpha(Time, note.Time, TapSpeed, path.IsWifi)))
        {
        if (path.IsWifi && Skin is not null)
        {
            DrawWifi(context, note, Enumerable.Range(0, path.Branches.Count).Min(Consumed));
        }
        else
        {
            foreach (var branch in Enumerable.Range(0, path.Branches.Count))
            {
                var points = path.Branches[branch];
                var distance = 0d;
                for (var i = 1; i < points.Count; i++) distance += points[i].DistanceTo(points[i - 1]);
                var count = Math.Max(2, (int)(distance * 4.8 / .48));
                for (var i = 0; i <= count; i++)
                {
                    var t = (i + .5) / (count + 1);
                    if (t < Consumed(branch)) continue;
                    var p = World(path.PointAtDistance(t, branch));
                    var before = path.PointAtDistance(Math.Max(0, t - .0001), branch);
                    var after = path.PointAtDistance(Math.Min(1, t + .0001), branch);
                    var a = Math.Atan2(after.Y - before.Y, after.X - before.X) + Math.PI;
                    if (Skin is not null) Sprite(context, $"SlideSkins/slide{SkinSprites.Variant(note)}.png", p, a);
                    else
                    {
                        using var tr = context.PushTransform(Matrix.CreateRotation(a) * Matrix.CreateTranslation(p.X, p.Y));
                        context.DrawGeometry(null, new Pen(Color(note), .11), Polyline(new Point(.14, -.27), new Point(-.14, 0), new Point(.14, .27)));
                    }
                }
            }
        }
        }
        var starPose = NoteMotion.SlideStar(Time, note.Time, note.SlideStartTime);
        if (!starPose.Visible) return;
        using var starOpacity = context.PushOpacity(starPose.Alpha);
        for (var branch = 0; branch < path.Branches.Count; branch++)
        {
            var p = World(path.PointAt(progress, branch));
            var tangent = path.TangentAt(progress, branch);
            var angle = path.IsWifi ? Math.PI + (branch == 0 ? 1 : branch == 1 ? 0 : 2) * Math.PI / 8 + (note.StartPosition - 1) * Math.PI / 4
                : Math.Atan2(tangent.Y, tangent.X) + Math.PI * .9;
            if (Skin is not null) Sprite(context, $"StarSkins/star{SkinSprites.Variant(note)}.png", p, angle, starPose.Scale);
            else Star(context, p, .62 * starPose.Scale, Color(note), angle);
        }
    }

    // Native prefab positions are for 1w5. The source sprites intentionally have
    // different widths; a single stretched fan image is not equivalent.
    private static readonly Point[] WifiPositions =
    [new(1.578,-3.894), new(1.389,-3.392), new(1.1561913,-2.811), new(.911,-2.204), new(.641,-1.597),
     new(.329,-.841), new(.017,-.113), new(-.28,.61), new(-.577,1.321), new(-.864,2.05), new(-1.225,2.868)];

    private void DrawWifi(DrawingContext context, ChartNote note, double consumed)
    {
        using var tr = context.PushTransform(Matrix.CreateRotation((note.StartPosition - 1) * Math.PI / 4));
        for (var i = 0; i < 11; i++)
            if ((i + 1d) / 12 >= consumed)
                Sprite(context, $"WifiSkins/wifi{SkinSprites.Variant(note)}_{i}.png", WifiPositions[i], mirrorX: true);
    }

    private void DrawJudgements(DrawingContext context)
    {
        if (Chart is null) return;
        foreach (var n in Chart.Notes)
        {
            var result = Session?.GetResult(n);
            if (result is null) continue;
            var miss = result.Kind == PlaybackEventKind.Miss;
            var elapsed = Time - result.Time;
            var lifetime = n.Kind == NoteKind.Slide ? .5 : .35;
            if (elapsed < 0 || elapsed > lifetime) continue;
            var p = n.Kind is NoteKind.Touch or NoteKind.TouchHold ? TouchPosition(n.TouchArea, n.StartPosition)
                : RingPosition(n.Kind == NoteKind.Slide ? n.EndPosition : n.StartPosition);
            using var opacity = context.PushOpacity(Math.Clamp(1 - elapsed / lifetime, 0, 1));
            if (!miss)
            {
                context.DrawEllipse(null, new Pen(Color(n), .05), p, .3 + elapsed * 2.4, .3 + elapsed * 2.4);
                if (n.IsFirework)
                    for (var i = 0; i < 12; i++)
                    {
                        var a = i * Math.PI / 6;
                        var r = .45 + elapsed * 3;
                        context.DrawLine(new Pen(Color(n), .045), new Point(p.X + Math.Sin(a) * r, p.Y + Math.Cos(a) * r),
                            new Point(p.X + Math.Sin(a) * (r + .2), p.Y + Math.Cos(a) * (r + .2)));
                    }
            }
            var timing = JudgementDisplay.Timing(result);
            if (Skin is null)
            {
                var color = result.Kind switch
                {
                    PlaybackEventKind.Great => Brushes.Orchid,
                    PlaybackEventKind.Good => Brushes.LightGreen,
                    PlaybackEventKind.Miss => MutedBrush,
                    _ => Brushes.LightYellow
                };
                Text(context, JudgementDisplay.Label(result.Grade), p, .22, color);
                if (timing.Length > 0) Text(context, timing, p + new Vector(0, .3), .17, timing == "FAST" ? Brushes.SkyBlue : Brushes.LightPink);
            }
            else if (n.Kind == NoteKind.Slide)
            {
                var layout = SlideJudgementLayout.Get(n.SlidePath!);
                Sprite(context, JudgementDisplay.SlideSprite(layout.Shape, result.Grade), layout.Position, layout.Angle);
            }
            else
            {
                var touch = n.Kind is NoteKind.Touch or NoteKind.TouchHold;
                var radius = Math.Sqrt(p.X * p.X + p.Y * p.Y);
                var rotation = touch ? radius > 0 ? Math.Atan2(p.X, -p.Y) : 0 : Angle(n.StartPosition);
                var inward = touch ? .66 : 1;
                var textPoint = radius > 0 ? new Point(p.X * (radius - inward) / radius, p.Y * (radius - inward) / radius)
                    : new Point(0, inward);
                if (n.IsBreak && result.Grade == AchievementGrade.Perfect)
                    Sprite(context, "JudgeTextSkins/judge_text_break_2600_shine.png", textPoint, rotation, 1 + elapsed * .35);
                Sprite(context, JudgementDisplay.JudgeSprite(n, result.Grade), textPoint, rotation);
                if (timing.Length > 0)
                    Sprite(context, $"JudgeTextSkins/{timing.ToLowerInvariant()}.png", textPoint + new Vector(Math.Sin(rotation) * -.38, Math.Cos(rotation) * .38), rotation, .8);
            }
        }
    }

    private bool Sprite(DrawingContext context, string path, Point position, double angle = 0, double scale = 1, bool mirrorX = false, uint? tint = null)
    {
        var bitmap = tint is uint color ? Skin?.GetTinted(path, color) : Skin?.Get(path);
        if (bitmap is null) return false;
        using var tr = context.PushTransform(Matrix.CreateScale(mirrorX ? -scale : scale, scale) * Matrix.CreateRotation(angle)
                                             * Matrix.CreateTranslation(position.X, position.Y));
        var w = bitmap.PixelSize.Width / 100d; var h = bitmap.PixelSize.Height / 100d;
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height), new Rect(-w / 2, -h / 2, w, h));
        return true;
    }

    private void NineSlice(DrawingContext context, string path, double width, double height, uint? tint = null)
    {
        var bitmap = tint is uint color ? Skin?.GetTinted(path, color) : Skin?.Get(path);
        if (bitmap is null) return;
        var sourceHeight = bitmap.Size.Height;
        var sourceCap = Math.Min(58d * bitmap.Size.Height / bitmap.PixelSize.Height, sourceHeight / 2);
        var cap = Math.Min(.58, height / 2);
        context.DrawImage(bitmap, new Rect(0, 0, bitmap.Size.Width, sourceCap), new Rect(-width / 2, -height / 2, width, cap));
        if (sourceHeight > sourceCap * 2 && height > cap * 2)
            context.DrawImage(bitmap, new Rect(0, sourceCap, bitmap.Size.Width, sourceHeight - sourceCap * 2),
                new Rect(-width / 2, -height / 2 + cap, width, height - cap * 2));
        context.DrawImage(bitmap, new Rect(0, sourceHeight - sourceCap, bitmap.Size.Width, sourceCap),
            new Rect(-width / 2, height / 2 - cap, width, cap));
    }

    public static Point RingPosition(int button, double radius = 4.8)
    {
        var angle = Angle(button);
        return new Point(Math.Sin(angle) * radius, -Math.Cos(angle) * radius);
    }
    private static double Angle(int button) => Math.PI / 8 + (button - 1) * Math.PI / 4;
    private static uint ExTint(ChartNote n) => n.IsEach || n.IsBreak ? 0xFFFFFF77u : n.IsStar ? 0xFFFFFFFFu : 0xFFFFACE1u;
    private static Point World(ChartPoint p) => new(p.X * 4.8, p.Y * 4.8);
    private static Point TouchPosition(string area, int button)
    {
        var family = area.Length > 0 ? char.ToUpperInvariant(area[0]) : 'C';
        if (family == 'C') return default;
        var radius = family switch { 'A' => 4.0, 'B' => 2.2, 'D' => 4.1, 'E' => 3.1, _ => 0 };
        var a = Angle(button) - (family is 'D' or 'E' ? Math.PI / 8 : 0);
        return new Point(Math.Sin(a) * radius, -Math.Cos(a) * radius);
    }
    private static IBrush Color(ChartNote note) => note.IsBreak ? Brushes.Orange : note.IsEach ? Brushes.Gold
        : note.Kind is NoteKind.Slide or NoteKind.Touch or NoteKind.TouchHold || note.IsStar ? Brushes.Turquoise : Brushes.HotPink;
    private static void Text(DrawingContext context, string value, Point center, double size, IBrush brush)
    {
        var text = new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), size, brush);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
    private static void Star(DrawingContext context, Point center, double radius, IBrush color, double angle, bool doubled = false)
    {
        var points = Enumerable.Range(0, doubled ? 20 : 10).Select(i =>
        {
            var a = angle + i * Math.PI / (doubled ? 10 : 5);
            var r = i % 2 == 0 ? radius : radius * .42;
            return new Point(center.X + Math.Sin(a) * r, center.Y - Math.Cos(a) * r);
        }).ToArray();
        context.DrawGeometry(color, new Pen(Brushes.White, .035), Polygon(points));
    }
    private static StreamGeometry Polygon(params Point[] points) => Geometry(points, true);
    private static StreamGeometry Polyline(params Point[] points) => Geometry(points, false);
    private static StreamGeometry Geometry(Point[] points, bool closed)
    {
        var geometry = new StreamGeometry();
        using var drawing = geometry.Open();
        drawing.BeginFigure(points[0], closed);
        foreach (var p in points.Skip(1)) drawing.LineTo(p);
        drawing.EndFigure(closed);
        return geometry;
    }
    private static StreamGeometry Sector(Point p, double radius, double progress)
    {
        var points = new List<Point> { p };
        var count = Math.Max(2, (int)(progress * 60));
        for (var i = 0; i <= count; i++)
        {
            var a = progress * Math.Tau * i / count;
            points.Add(new Point(p.X + Math.Sin(a) * radius, p.Y - Math.Cos(a) * radius));
        }
        return Polygon(points.ToArray());
    }
}
