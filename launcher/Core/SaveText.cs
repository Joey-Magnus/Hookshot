namespace HookshotLauncher.Core;

/// A save folder summed up as the window writes it: "3 slots, saved yesterday 22:14" (Long) or "3 saves, last yesterday 22:14"
public static class SaveText
{
    public static DateTime? Last(IReadOnlyList<SaveSlot> slots) => slots.Select(s => s.Saved).Where(t => t != null).Max();

    public static string Long(IReadOnlyList<SaveSlot> slots, DateTime now)
    {
        if (slots.Count == 0) return "No saves yet";
        var text = slots.Count + (slots.Count == 1 ? " slot" : " slots");
        return Last(slots) is { } t ? text + ", saved " + Durations.When(t, now) : text;
    }

    public static string Short(IReadOnlyList<SaveSlot> slots, DateTime now)
    {
        if (slots.Count == 0) return "No saves yet";
        var text = slots.Count + (slots.Count == 1 ? " save" : " saves");
        return Last(slots) is { } t ? text + ", last " + Durations.When(t, now) : text;
    }

    /// "3 save slots", "1 save slot", "no saves" (a row of the home's list)
    public static string Count(int slots) => slots == 0 ? "no saves" : slots + (slots == 1 ? " save slot" : " save slots");

    /// A slot's number from its folder's name: slot_00 is 1
    public static int Number(string slotName) =>
        slotName.StartsWith("slot_", StringComparison.Ordinal) && int.TryParse(slotName[5..], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n + 1 : 0;
}
