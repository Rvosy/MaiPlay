using MaiPlay.Core;

namespace MaiPlay.Skins;

public static class SkinSprites
{
    public static string Variant(ChartNote note) => note.IsBreak ? "_break" : note.IsEach ? "_each" : "";
    public static string Tap(ChartNote note) => note.IsStar
        ? $"StarSkins/star{Variant(note)}{(note.IsDoubleStar ? "_double" : "")}.png"
        : $"TapSkins/tap{Variant(note)}.png";
    public static string Ex(ChartNote note) => note.Kind == NoteKind.Hold ? "HoldSkins/hold_ex.png"
        : note.IsStar ? $"StarSkins/star_ex{(note.IsDoubleStar ? "_double" : "")}.png" : "TapSkins/tap_ex.png";
    public static string Hold(ChartNote note, bool active, bool released) => released ? "HoldSkins/hold_off.png"
        : $"HoldSkins/hold{Variant(note)}{(active ? "_on" : "")}.png";
    public static string Guide(ChartNote note) => $"NoteGuideSkins/{(note.IsBreak ? "Break" : note.IsEach ? "Each" : note.IsStar ? "Slide" : "Normal")}.png";
    public static string HoldEnd(ChartNote note) => $"NoteGuideSkins/Hold{(note.IsBreak ? "_Break" : note.IsEach ? "_Each" : "")}_End.png";
    public static string TouchPoint(ChartNote note) => note.IsBreak ? "TouchSkins/touch_break_point.png"
        : $"TouchSkins/touch_point{(note.IsEach ? "_each" : "")}.png";
    public static string TouchBorder(ChartNote note, int count) => note.IsBreak ? $"TouchSkins/touch_break_border_{count}.png"
        : $"TouchSkins/touch_border_{count}{(note.IsEach ? "_each" : "")}.png";
}
