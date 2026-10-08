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

    internal static IReadOnlySet<int> InterferingSweeps(HandPlan plan, bool includeMaskedContacts = true)
    {
        var result = new HashSet<int>();
        foreach (var error in plan.Errors.Where(e => e.Note.Kind is NoteKind.Tap or NoteKind.Hold &&
                     e.DifferenceSeconds < 0))
        {
            var time = error.Note.Time + error.DifferenceSeconds!.Value;
            var tick = Tick(time);
            foreach (var input in plan.Replay.Events.Where(e => e.Channel == InputChannel.Button && e.IsDown &&
                         e.Area == error.Note.StartPosition.ToString() && Tick(e.Time) == tick))
                AddSweepsAt(plan, result, input);
        }
        if (!includeMaskedContacts) return result;
        foreach (var error in plan.Errors.Where(e => e.Note.Kind is NoteKind.Tap or NoteKind.Hold && e.Reason == "head-timeout"))
        {
            var attempts = plan.Strokes.Where(s => s.NoteId == error.Note.Id && s.Channel == InputChannel.Button).ToArray();
            if (attempts.Length == 0) continue;
            var samples = plan.Replay.Events.Where(e => e.Channel == InputChannel.Button &&
                e.Area == error.Note.StartPosition.ToString() && e.Time <= PlaybackSession.MissDeadline(error.Note))
                .GroupBy(e => Tick(e.Time));
            var inputs = new InputState();
            var origins = new Dictionary<string, InputEvent>();
            long previousTick = long.MinValue;
            foreach (var sample in samples)
            {
                // 同帧短按会锁存到下一采样；空采样也必须经过真实输入状态，不能仅合并浮点区间。
                if (previousTick != long.MinValue && sample.Key > previousTick + 1)
                {
                    inputs.Sample([]);
                    foreach (var source in origins.Keys.Where(s => !inputs.ButtonSources(error.Note.StartPosition).Contains(s)).ToArray())
                        origins.Remove(source);
                }
                var wasOn = inputs.IsButtonOn(error.Note.StartPosition);
                var blockers = origins.Values.ToArray();
                inputs.Sample(sample);
                if (wasOn && inputs.IsButtonOn(error.Note.StartPosition) && sample.Any(e => e.IsDown &&
                        attempts.Any(s => s.Hand == plan.Replay.HandForSource(e.Source) &&
                            sample.Key >= Tick(s.Start) - 1 && sample.Key <= Tick(s.End))))
                    foreach (var blocker in blockers) AddSweepsAt(plan, result, blocker);
                foreach (var input in sample.Where(e => e.IsDown)) origins.TryAdd(input.Source, input);
                foreach (var source in origins.Keys.Where(s => !inputs.ButtonSources(error.Note.StartPosition).Contains(s)).ToArray())
                    origins.Remove(source);
                previousTick = sample.Key;
            }
        }
        return result;
    }

    private static void AddSweepsAt(HandPlan plan, HashSet<int> result, InputEvent input)
    {
        var hand = plan.Replay.HandForSource(input.Source);
        var actions = plan.Strokes.Where(s => s.Hand == hand).OrderBy(s => s.Start).ToArray();
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            if (action.Technique != HandTechnique.Rotation) continue;
            if (action.Start <= input.Time && input.Time < action.End) result.Add(action.NoteId);
            // 连续扫键的接近段从前一键起沿弧保持接触，可能压住另一手尚未到时的键。
            if (i > 0 && actions[i - 1].Technique == HandTechnique.Rotation &&
                actions[i - 1].Start <= input.Time && input.Time <= action.Start)
                result.Add(action.NoteId);
        }
    }

    internal static IReadOnlyDictionary<int, double> EarlierContacts(HandPlan plan, bool includeHeadFeedback = true)
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
        if (!includeHeadFeedback) return starts;
        foreach (var error in plan.Errors.Where(e => e.Note.Kind is NoteKind.Tap or NoteKind.Hold && e.DifferenceSeconds < 0))
        {
            var tick = Tick(error.Note.Time + error.DifferenceSeconds!.Value);
            var area = "A" + error.Note.StartPosition;
            foreach (var span in TouchSpans(plan, new HashSet<string> { area }).Where(s => s.StartTick == tick))
            {
                // 同帧按下再松开仍会锁存；归因到释放前的动作，并给早窗前留出一个空采样。
                var time = Math.Min(tick / (double)PlaybackSession.LogicHz, span.End - Epsilon);
                foreach (var action in TouchActionsAt(plan, span, time))
                {
                    var note = plan.Chart.Notes.Single(n => n.Id == action.NoteId);
                    if (note.Kind != NoteKind.Slide || note.SlidePath!.IsWifi) continue;
                    var releaseBefore = error.Note.Time - HeadJudgements.EarlyInputWindow - PlaybackSession.LogicStep;
                    var start = Math.Clamp(action.Start - (span.End - releaseBefore),
                        note.SlideStartTime - .12, note.SlideStartTime + .12);
                    // 尾区提前碰到后续外键时，也尝试先离开；整条轨道延后可能只把错误转移到自身。
                    if (start < action.Start - Epsilon)
                        starts[note.Id] = Math.Min(starts.GetValueOrDefault(note.Id, double.PositiveInfinity), start);
                }
            }
        }
        foreach (var (noteId, start) in EarlierTouchClicks(plan))
            starts[noteId] = Math.Min(starts.GetValueOrDefault(noteId, double.PositiveInfinity), start);
        return starts;
    }

    private static IEnumerable<(int NoteId, double Start)> EarlierTouchClicks(HandPlan plan)
    {
        foreach (var error in plan.Errors.Where(e => e.Note.Kind == NoteKind.Touch && e.Reason == "head-timeout" &&
                     e.Note.TouchArea is ['A', >= '1' and <= '8']))
        {
            var note = error.Note;
            var button = note.TouchArea[1] - '0';
            var attempts = plan.Strokes.Where(s => s.NoteId == note.Id && s.Channel == InputChannel.Touch).ToArray();
            if (attempts.Length == 0) continue;
            var consumers = plan.Results.Where(e => e.Note.Kind is NoteKind.Tap or NoteKind.Hold &&
                    e.Note.StartPosition == button && e.DifferenceSeconds is not null && e.Reason != "head-timeout")
                .ToLookup(e => Tick(e.Note.Time + e.DifferenceSeconds!.Value));
            var samples = plan.Replay.Events.Where(e => e.Time <= PlaybackSession.MissDeadline(note) &&
                    (e.Channel == InputChannel.Button ? e.Area == button.ToString() : e.Area == note.TouchArea))
                .GroupBy(e => Tick(e.Time));
            var inputs = new InputState();
            long previousTick = long.MinValue;
            foreach (var sample in samples)
            {
                if (previousTick != long.MinValue && sample.Key > previousTick + 1) inputs.Sample([]);
                inputs.Sample(sample);
                var buttonClick = inputs.TryUseButtonClick(button);
                var touchClick = inputs.TryUseSensorClick(note.TouchArea);
                previousTick = sample.Key;
                // 外键优先吃 Button 边沿；只有它实际用了 Touch 边沿，才调整这次漏判的 Touch。
                if (buttonClick || !touchClick || !consumers.Contains(sample.Key)) continue;
                foreach (var down in sample.Where(e => e.Channel == InputChannel.Touch && e.IsDown))
                foreach (var action in attempts.Where(s => s.Hand == plan.Replay.HandForSource(down.Source) &&
                             down.Time >= s.Start - Epsilon && down.Time < s.End))
                foreach (var consumer in consumers[sample.Key])
                {
                    var beforeWindow = consumer.Note.Time - HeadJudgements.EarlyInputWindow - PlaybackSession.LogicStep;
                    var start = Math.Clamp(action.Start + beforeWindow - down.Time, note.Time - .125, note.Time + .125);
                    if (start < action.Start - Epsilon) yield return (note.Id, start);
                }
            }
        }
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
