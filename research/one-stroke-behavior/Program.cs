using System.Text.Json;
using MaiPlay.Core;

if (args.ElementAtOrDefault(0) == "--path-samples")
{
    var realChart = SimaiParser.LoadSong(".reference/charts/game-extracted/21_BUDDiES/011634_青春コンプレックス")
        .Charts.Single(c => c.Difficulty == 5);
    object Samples(Chart source) => source.Notes.Where(n => n.Kind == NoteKind.Slide).Select(n => new
    {
        n.Id,
        points = Enumerable.Range(0, 65).Select(i => n.SlidePath!.PointAt(i / 64.0)).ToArray()
    }).ToArray();
    Chart Tail(int tail) => SimaiParser.ParseChart($"(150){{4}}4-7[4:1]/8h[#0.3],7<5[4:1],{{#0.43}},{tail},E", first: 1);
    var paths = new { realChart = Samples(realChart), tail2 = Samples(Tail(2)), tail7 = Samples(Tail(7)) };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllText(args[1], JsonSerializer.Serialize(paths));
    return;
}

if (args.ElementAtOrDefault(0) == "--visual-tracks")
{
    var song = SimaiParser.LoadSong(".reference/charts/game-extracted/21_BUDDiES/011634_青春コンプレックス");
    var realChart = song.Charts.Single(c => c.Difficulty == 5);
    var realPlan = HumanPlanner.Plan(realChart);
    var tails = new List<object>();
    if (args.ElementAtOrDefault(2) == "tails")
        foreach (var tail in new[] { 2, 7 })
        {
            var source = $"(150){{4}}4-7[4:1]/8h[#0.3],7<5[4:1],{{#0.43}},{tail},E";
            var tailChart = SimaiParser.ParseChart(source, first: 1);
            tails.Add(new { tail, source, data = ClipPlan(HumanPlanner.Plan(tailChart), .9, 2.5) });
        }
    var assembly = typeof(HumanPlanner).Assembly.Location;
    var coreSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(assembly)));
    var result = new { coreSha256, realChart = ClipPlan(realPlan, 134.1, 138.3), tailVariants = tails };
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllText(args[1], JsonSerializer.Serialize(result));
    return;
}

if (args.ElementAtOrDefault(0) == "--tail-variants")
{
    foreach (var tail in Enumerable.Range(1, 8))
    {
        var source = $"(150){{4}}4-7[4:1]/8h[#0.3],7<5[4:1],{{#0.43}},{tail},E";
        var variant = SimaiParser.ParseChart(source, first: 1);
        var plan = HumanPlanner.Plan(variant);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            source,
            plan.AllCriticalPerfect,
            plan.Diagnostics?.MovementCost,
            movement = new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes),
            diagnostics = plan.Diagnostics,
            strokes = plan.Strokes.OrderBy(s => s.Start).Select(s => new { note = variant.Notes.Single(n => n.Id == s.NoteId).Source, hand = s.Hand.ToString(), s.Start, s.End }),
            errors = plan.Errors.Select(e => new { e.Note.Source, grade = e.Grade.ToString() })
        }));
    }
    return;
}

if (args.ElementAtOrDefault(0) == "--timing-feedback")
{
    foreach (var (time, position) in new[] { (1.35, 1), (1.5, 2) })
    {
        var variant = SimaiParser.ParseChart("(190){4}1>5[4:1],5<1[4:1],1>5[4:1],E", first: 1);
        var tap = new ChartNote
        {
            Id = variant.Notes.Count, Kind = NoteKind.Tap, Time = time,
            StartPosition = position, EndPosition = position, Source = position.ToString()
        };
        variant = variant with { Notes = variant.Notes.Append(tap).OrderBy(n => n.Time).ThenBy(n => n.Id)
            .Select((n, id) => n with { Id = id }).ToArray() };
        var plan = HumanPlanner.Plan(variant);
        var feedbackSlides = plan.Strokes.Where(s => s.Technique == HandTechnique.Slide).OrderBy(s => s.Start).ToArray();
        var session = new PlaybackSession(variant, PlayMode.Algorithm, new HandReplay(plan.Tracks).Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        var oneHand = feedbackSlides.Select(s => s.Hand).Distinct().Count() == 1;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            tapTime = time,
            tapPosition = position,
            cp = results.Count(r => r.Grade == AchievementGrade.Perfect),
            total = results.Length,
            hands = string.Concat(feedbackSlides.Select(s => s.Hand == HandSide.Left ? "L" : "R")),
            chainContactGapSeconds = oneHand ? ContactGap(plan, feedbackSlides[0].Hand, feedbackSlides[0].Start, feedbackSlides[^1].End) : (double?)null,
            slides = feedbackSlides.Select(s => new { s.NoteId, s.Start, s.End, shift = s.Start - variant.Notes.Single(n => n.Id == s.NoteId).SlideStartTime }),
            diagnostics = plan.Diagnostics,
            errors = results.Where(e => e.Grade != AchievementGrade.Perfect).Select(e => new { e.Note.Source, grade = e.Grade.ToString() })
        }));
        if (args.Length > 1)
        {
            Directory.CreateDirectory(args[1]);
            File.WriteAllText(Path.Combine(args[1], $"timing-feedback-{position}-tracks.json"), JsonSerializer.Serialize(plan.Tracks));
        }
    }
    return;
}

if (args.ElementAtOrDefault(0) == "--reference-relay")
{
    foreach (var tail in new[] { 2, 7 })
    foreach (var relay in new[] { false, true })
    {
        var variant = SimaiParser.ParseChart($"(150){{4}}4-7[4:1]/8h[#0.3],7<5[4:1],{{#0.43}},{tail},E", first: 1);
        var tracks = ReferenceTracks(variant, relay);
        var replay = new HandReplay(tracks);
        var session = new PlaybackSession(variant, PlayMode.Algorithm, replay.Events);
        var results = session.Advance(session.FinishTime + .1).Where(e => e.IsFinal).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new { tail, relay, cp = results.Count(r => r.Grade == AchievementGrade.Perfect), total = results.Length,
            errors = results.Where(r => r.Grade != AchievementGrade.Perfect).Select(r => new { r.Note.Source, r.Grade, r.DifferenceSeconds }),
            movement = new HumanMotionModel().Analyze(tracks) }));
    }
    return;
}

var input = args.ElementAtOrDefault(0) ?? ".reference/charts/game-extracted/21_BUDDiES/011634_青春コンプレックス";
var output = args.ElementAtOrDefault(1);
var chart = Directory.Exists(input)
    ? SimaiParser.LoadSong(input).Charts.Single(c => c.Difficulty == 5)
    : SimaiParser.ParseChart(input, first: 1);
var rangeStart = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : Directory.Exists(input) ? 134.2 : 0;
var rangeEnd = args.Length > 3 ? double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : Directory.Exists(input) ? 138.1 : chart.Duration;
var slides = chart.Notes.Where(n => n.Kind == NoteKind.Slide && n.Time >= rangeStart && n.Time <= rangeEnd).OrderBy(n => n.SlideStartTime).ToArray();
var reports = new List<object>();
foreach (var name in new[] { "normal", "human" })
{
    var started = System.Diagnostics.Stopwatch.StartNew();
    var plan = name == "normal" ? NormalHandPlanner.Plan(chart) : HumanPlanner.Plan(chart);
    var strokes = slides.Select(n => plan.Strokes.SingleOrDefault(s => s.NoteId == n.Id)).ToArray();
    var seams = Enumerable.Range(1, Math.Max(0, slides.Length - 1)).Select(i =>
    {
        var before = slides[i - 1];
        var after = slides[i];
        var previous = strokes[i - 1];
        var current = strokes[i];
        var time = after.SlideStartTime;
        var gap = previous is null || current is null || previous.Hand != current.Hand
            ? (double?)null
            : ContactGap(plan, previous.Hand, time - .08, time + .08);
        return new { previousId = before.Id, nextId = after.Id, time, from = previous?.Hand.ToString(), to = current?.Hand.ToString(), contactGapSeconds = gap };
    }).ToArray();
    var report = new
    {
        planner = name,
        elapsedSeconds = started.Elapsed.TotalSeconds,
        plan.Achievement,
        cp = plan.Results.Count(r => r.Grade == AchievementGrade.Perfect),
        total = chart.Notes.Count,
        errors = plan.Errors.Select(e => new { e.Note.Id, e.Note.Source, e.Note.Time, grade = e.Grade.ToString() }),
        hands = string.Concat(strokes.Select(s => s is null ? "?" : s.Hand == HandSide.Left ? "L" : "R")),
        nearbyNotes = chart.Notes.Where(n => n.Time >= rangeStart && n.Time <= rangeEnd + .5)
            .Select(n => new { n.Id, n.Source, n.Time, kind = n.Kind.ToString() }),
        slides = slides.Select((n, i) => new { n.Id, n.Source, n.Time, n.SlideStartTime, n.EndTime, hand = strokes[i]?.Hand.ToString(), strokeStart = strokes[i]?.Start, strokeEnd = strokes[i]?.End }),
        seams,
        handSwitches = seams.Count(s => s.from is not null && s.to is not null && s.from != s.to),
        sameHandContactBreaks = seams.Count(s => s.contactGapSeconds > 1e-8),
        contactGapSeconds = seams.Sum(s => s.contactGapSeconds ?? 0),
        movement = new HumanMotionModel().Analyze(plan.Tracks, plan.Strokes)
    };
    reports.Add(report);
    Console.WriteLine($"{name}: {report.cp}/{report.total} CP, {report.hands}, {report.elapsedSeconds:F3}s");
}
var json = JsonSerializer.Serialize(new { chart.Title, chart.Level, rangeStart, rangeEnd, reports }, new JsonSerializerOptions { WriteIndented = true });
if (output is null) Console.WriteLine(json);
else
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
    File.WriteAllText(output, json);
}

static double ContactGap(HandPlan plan, HandSide hand, double start, double end)
{
    var frames = plan.Tracks.Single(t => t.Hand == hand).Frames;
    double gap = 0;
    for (var i = 0; i < frames.Count - 1; i++)
        if (!frames[i].Contacts.Any(c => c.Channel == InputChannel.Touch))
            gap += Math.Max(0, Math.Min(end, frames[i + 1].Time) - Math.Max(start, frames[i].Time));
    return gap;
}

static object ClipPlan(HandPlan plan, double start, double end) => new
{
    plan.Chart.Title,
    start,
    end,
    cp = plan.Results.Count(r => r.Grade == AchievementGrade.Perfect),
    total = plan.Results.Count,
    tracks = plan.Tracks.Select(track =>
    {
        var frames = track.Frames.ToArray();
        var first = Math.Max(0, Array.FindLastIndex(frames, f => f.Time <= start));
        var last = Array.FindIndex(frames, f => f.Time >= end);
        if (last < 0) last = frames.Length - 1;
        return new { hand = track.Hand.ToString(), frames = frames.Skip(first).Take(last - first + 1) };
    }),
    notes = plan.Chart.Notes.Where(n => n.EndTime >= start && n.Time <= end).Select(n => new
    {
        n.Id, kind = n.Kind.ToString(), n.Source, n.Time, n.SlideStartTime, n.EndTime, n.StartPosition, n.EndPosition,
        points = n.Kind == NoteKind.Slide ? Enumerable.Range(0, 65).Select(i => n.SlidePath!.PointAt(i / 64.0)).ToArray() : null
    }),
    strokes = plan.Strokes.Where(s => s.End >= start && s.Start <= end).Select(s => new
    {
        s.NoteId, hand = s.Hand.ToString(), s.Start, s.End, technique = s.Technique.ToString()
    })
};

static HandTrack[] ReferenceTracks(Chart chart, bool relay)
{
    var left = new List<HandFrame> { new(0, SlidePath.ButtonPosition(8, 1.12), []) };
    var right = new List<HandFrame> { new(0, SlidePath.ButtonPosition(4, 1.12), []) };
    var slides = chart.Notes.Where(n => n.Kind == NoteKind.Slide).OrderBy(n => n.Time).ToArray();
    Button(left, 1, 1.3, 8);
    Button(right, 1, 1.025, 4);
    Button(left, 1.4, 1.425, 7);
    Slide(right, slides[0], relay ? .9 : 1, keepContact: !relay);
    if (relay)
    {
        var endpoint = slides[0].SlidePath!.PointAt(.9);
        right.Add(new(1.8, new(endpoint.X + .6, endpoint.Y), []));
    }
    Slide(relay ? left : right, slides[1], .9, keepContact: false);
    var tail = chart.Notes.Last();
    var tailHand = tail.StartPosition == 7 ? left : right;
    Button(tailHand, tail.Time, tail.Time + .025, tail.StartPosition);
    return [new(HandSide.Left, left.OrderBy(f => f.Time).ToArray()), new(HandSide.Right, right.OrderBy(f => f.Time).ToArray())];

    static void Button(List<HandFrame> frames, double start, double end, int button)
    {
        var p = SlidePath.ButtonPosition(button, 1.12);
        frames.Add(new(start, p, [new("finger", InputChannel.Button, default, .04)]));
        frames.Add(new(end, p, []));
    }
    static void Slide(List<HandFrame> frames, ChartNote note, double progress, bool keepContact)
    {
        var count = (int)Math.Ceiling(note.Duration * PlaybackSession.LogicHz * progress);
        for (var i = 0; i <= count; i++)
        {
            var t = progress * i / count;
            var time = note.SlideStartTime + note.Duration * t;
            if (frames.Count > 0 && Math.Abs(frames[^1].Time - time) < 1e-8) frames.RemoveAt(frames.Count - 1);
            frames.Add(new(time, note.SlidePath!.PointAt(t), i == count && !keepContact ? [] : [new("palm", InputChannel.Touch, default, .19)]));
        }
    }
}
