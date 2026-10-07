namespace MaiPlay.Core;

/// <summary>用整曲判定中的提前量调整接触时机，再交回同一搜索和判定流程。</summary>
internal static class PlannerTimingFeedback
{
    private const double Epsilon = 1e-8;
    private sealed record TouchSpan(string Area, string Source, double Start, double End)
    {
        public long StartTick => Tick(Start);
        public long EndTick => Math.Max(StartTick + 1, Tick(End));
        public bool ActiveAt(long tick) => StartTick <= tick && tick < EndTick;
    }

    internal static IReadOnlyDictionary<int, double> DelayedContacts(HandPlan plan)
    {
        var starts = new Dictionary<int, double>();
        foreach (var (action, delay, _) in EarlyContacts(plan))
        {
            var delayed = action.Start + delay;
            starts[action.NoteId] = Math.Max(starts.GetValueOrDefault(action.NoteId, double.NegativeInfinity), delayed);
        }
        return starts;
    }

    internal static IReadOnlySet<int> FingertipCandidates(HandPlan plan) =>
        EarlyContacts(plan).Where(contact => contact.Palm).Select(contact => contact.Action.NoteId)
            .Concat(MaskedTouchContacts(plan)).ToHashSet();

    internal static IReadOnlyDictionary<int, double> EarlierContacts(HandPlan plan)
    {
        var starts = new Dictionary<int, double>();
        foreach (var (action, _, _) in EarlyContacts(plan, slidesOnly: true))
        {
            var note = plan.Chart.Notes.Single(n => n.Id == action.NoteId);
            if (note.Kind != NoteKind.Slide || note.SlidePath!.IsWifi) continue;
            // 提前离开后轨的起判区也能防蹭；核验已有的提前候选，不能只把前轨越推越晚。
            var start = note.SlideStartTime - .12;
            if (start < action.Start - Epsilon) starts[note.Id] = start;
        }
        return starts;
    }

    private static IEnumerable<(PlannedStroke Action, double Delay, bool Palm)> EarlyContacts(HandPlan plan, bool slidesOnly = false)
    {
        foreach (var error in plan.Errors.Where(e => e.DifferenceSeconds < 0 && e.Reason != "touch-group" &&
                     (!slidesOnly || e.Note.Kind == NoteKind.Slide)))
        {
            if (error.Note.Kind == NoteKind.Slide)
            {
                var segment = error.Note.SlidePath!.Segments[^1];
                var (template, mirror) = SlidePath.GetTemplate(segment);
                var areas = SlideJudgeTables.Get(template).Queues.SelectMany(q => q[^1].Areas)
                    .Select(a => SlideJudgements.Transform(a, segment.Start, mirror).ToString()).ToHashSet();
                var tailTime = error.Note.Duration * error.Note.SlidePath.SegmentWeights[^1] * SlideJudgeTables.Get(template).TailConstant;
                var minimum = 0d;
                var maximum = -error.DifferenceSeconds!.Value;
                for (var i = 0; i < 24; i++)
                {
                    var delay = (minimum + maximum) / 2;
                    if (SlideJudgements.Grade(error.DifferenceSeconds.Value + delay, tailTime) == AchievementGrade.Perfect)
                        maximum = delay;
                    else minimum = delay;
                }
                // 末区在前一采样完成，当前采样才生成尾判。归因到当时真实压住末区的接触。
                var tick = (long)Math.Round(error.Time * PlaybackSession.LogicHz) - 1;
                foreach (var span in TouchSpans(plan, areas).Where(s => s.ActiveAt(tick)))
                {
                    var time = Math.Min(tick / (double)PlaybackSession.LogicHz, span.End - Epsilon);
                    foreach (var action in TouchActionsAt(plan, span, time))
                        yield return (action, maximum + PlaybackSession.LogicStep, IsPalm(plan, action, time));
                }
                continue;
            }
            // 长条的最终事件发生在尾部，提前量记录的是头判；接触归因要回到头判时刻。
            var contactTime = error.Note.Time + error.DifferenceSeconds!.Value;
            foreach (var action in plan.Strokes.Where(s => s.Channel == InputChannel.Touch &&
                         s.Start <= contactTime && s.End >= contactTime - PlaybackSession.LogicStep))
            {
                var pose = plan.Replay.PoseAt(action.Hand, Math.Min(contactTime, action.End - 1e-8));
                if (pose is null) continue;
                var area = error.Note.Kind is NoteKind.Touch or NoteKind.TouchHold
                    ? error.Note.TouchArea : "A" + error.Note.StartPosition;
                if (!pose.Contacts.Any(c => c.Channel == InputChannel.Touch &&
                        ContactMapper.AreasAt(c.Channel, new(pose.Position.X + c.Offset.X, pose.Position.Y + c.Offset.Y),
                            c.Radius).Contains(area))) continue;
                yield return (action, -error.DifferenceSeconds!.Value,
                    pose.Contacts.Any(c => c.Channel == InputChannel.Touch && c.Id == "palm"));
            }
        }
    }

    private static IEnumerable<int> MaskedTouchContacts(HandPlan plan)
    {
        foreach (var error in plan.Errors.Where(e => e.Note.Kind is NoteKind.Touch or NoteKind.TouchHold &&
                     e.Reason == "head-timeout"))
        {
            var note = error.Note;
            var spans = TouchSpans(plan, new HashSet<string> { note.TouchArea }).ToArray();
            var earlyTick = Tick(note.Time - HeadJudgements.EarlyInputWindow);
            foreach (var intended in plan.Strokes.Where(s => s.NoteId == note.Id && s.Channel == InputChannel.Touch))
                foreach (var attempt in spans.Where(s => plan.Replay.HandForSource(s.Source) == intended.Hand &&
                             s.Start >= intended.Start - Epsilon && s.Start < intended.End &&
                             s.StartTick >= earlyTick && s.Start <= PlaybackSession.MissDeadline(note)))
                    foreach (var blocker in spans.Where(s => s.Source != attempt.Source && s.StartTick < earlyTick &&
                                 s.ActiveAt(attempt.StartTick - 1) && s.ActiveAt(attempt.StartTick)))
                    {
                        // 另一接触在早窗前已按住同一区，遮住了本次计划输入的全区上升沿。
                        foreach (var action in TouchActionsAt(plan, blocker, attempt.Start))
                            if (action.NoteId != note.Id && IsPalm(plan, action, attempt.Start)) yield return action.NoteId;
                    }
        }
    }

    private static IEnumerable<TouchSpan> TouchSpans(HandPlan plan, IReadOnlySet<string> areas)
    {
        var pressed = new Dictionary<(string Area, string Source), double>();
        foreach (var input in plan.Replay.Events.Where(e => e.Channel == InputChannel.Touch && areas.Contains(e.Area)))
        {
            var key = (input.Area, input.Source);
            if (input.IsDown) pressed[key] = input.Time;
            else if (pressed.Remove(key, out var start)) yield return new(input.Area, input.Source, start, input.Time);
        }
    }

    private static IEnumerable<PlannedStroke> TouchActionsAt(HandPlan plan, TouchSpan span, double time) =>
        plan.Strokes.Where(s => s.Channel == InputChannel.Touch && s.Hand == plan.Replay.HandForSource(span.Source) &&
            s.Start <= time + Epsilon && s.End > time);

    private static bool IsPalm(HandPlan plan, PlannedStroke action, double time) =>
        plan.Replay.PoseAt(action.Hand, time)?.Contacts.Any(c => c.Channel == InputChannel.Touch && c.Id == "palm") == true;

    private static long Tick(double time) => (long)Math.Ceiling(time * PlaybackSession.LogicHz - 1e-10);
}
