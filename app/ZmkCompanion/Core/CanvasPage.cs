namespace ZmkCompanion.Core;

// One page of the canvas: an independent set of widget placements shown as a
// whole 68×160 frame. When cycling is enabled, AppContext rotates through
// pages, showing each for DurationSeconds before advancing to the next.
sealed class CanvasPage
{
    public string Name            { get; set; } = "Page 1";
    public int    DurationSeconds { get; set; } = 10;

    public List<WidgetPlacement> Widgets { get; set; } = [];

    public CanvasPage Clone() => new()
    {
        Name            = Name,
        DurationSeconds = DurationSeconds,
        Widgets         = Widgets.Select(w => w.Clone()).ToList(),
    };
}
