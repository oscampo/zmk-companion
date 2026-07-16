namespace ZmkCompanion.Core;

// One page of the canvas: an independent set of widget placements shown as a
// whole 68×160 frame. When cycling is enabled, AppContext rotates through
// pages, showing each for DurationSeconds before advancing to the next.
sealed class CanvasPage
{
    public string Name            { get; set; } = "Page 1";
    public int    DurationSeconds { get; set; } = 10;

    // When true, this page is rendered via the cell-grid protocol (0x1527)
    // rather than a full-frame bitmap. The full-frame pipeline is paused
    // for the duration. Layout is fixed: clock (tier 4, large) + date (tier 0, small).
    public bool CellGrid { get; set; } = false;

    public List<WidgetPlacement> Widgets { get; set; } = [];

    public CanvasPage Clone() => new()
    {
        Name            = Name,
        DurationSeconds = DurationSeconds,
        CellGrid        = CellGrid,
        Widgets         = Widgets.Select(w => w.Clone()).ToList(),
    };
}
