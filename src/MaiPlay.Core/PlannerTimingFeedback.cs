namespace MaiPlay.Core;

/// <summary>用整曲判定中的提前量调整接触时机，再交回同一搜索和判定流程。</summary>
internal static class PlannerTimingFeedback
{
    internal static IReadOnlyDictionary<int, double> DelayedContacts(HandPlan plan)
    {
        var starts = new Dictionary<int, double>();
        foreach (var error in plan.Errors.Where(e => e.DifferenceSeconds < 0 &&
                     e.Note.Kind is NoteKind.Slide or NoteKind.Tap or NoteKind.Hold))
        {
            // Hold 的最终事件发生在尾部，提前量记录的是头判；接触归因要回到头判时刻。
            var contactTime = error.Note.Kind == NoteKind.Slide ? error.Time : error.Note.Time + error.DifferenceSeconds!.Value;
            foreach (var action in plan.Strokes.Where(s => s.Channel == InputChannel.Touch &&
                         (error.Note.Kind == NoteKind.Slide ? s.NoteId == error.Note.Id :
                             s.Start <= contactTime && s.End >= contactTime - PlaybackSession.LogicStep)))
            {
                if (error.Note.Kind != NoteKind.Slide)
                {
                    var pose = plan.Replay.PoseAt(action.Hand, Math.Min(contactTime, action.End - 1e-8));
                    if (pose is null || !pose.Contacts.Any(c => c.Channel == InputChannel.Touch &&
                            ContactMapper.AreasAt(c.Channel, new(pose.Position.X + c.Offset.X, pose.Position.Y + c.Offset.Y),
                                c.Radius).Contains("A" + error.Note.StartPosition))) continue;
                }
                var delayed = action.Start - error.DifferenceSeconds!.Value;
                starts[action.NoteId] = Math.Max(starts.GetValueOrDefault(action.NoteId, double.NegativeInfinity), delayed);
            }
        }
        return starts;
    }
}
