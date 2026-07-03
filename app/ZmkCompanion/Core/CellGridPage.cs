namespace ZmkCompanion.Core;

enum SplitHalf { None = 0, Top = 1, Bottom = 2 }

sealed class CellGridRow
{
    public byte      TierId    { get; set; } = 4; // large_impar (13×22)
    public string    Template  { get; set; } = "";
    public string    Align     { get; set; } = "center"; // left | center | right
    public SplitHalf SplitHalf { get; set; } = SplitHalf.None;

    public CellGridRow Clone() => new()
    {
        TierId    = TierId,
        Template  = Template,
        Align     = Align,
        SplitHalf = SplitHalf,
    };
}

// One display page: a sequence of cell-grid rows rendered top-to-bottom.
// The sum of row heights must not exceed BitmapFrame.Height (160px).
sealed class CellGridPage
{
    public string Name            { get; set; } = "Page 1";
    public int    DurationSeconds { get; set; } = 10;
    public List<CellGridRow> Rows { get; set; } = [];

    public int TotalHeight => Rows.Sum(r =>
        r.TierId < CellGridProtocol.Tiers.Length ? CellGridProtocol.Tiers[r.TierId].H : 0);

    public CellGridPage Clone() => new()
    {
        Name            = Name,
        DurationSeconds = DurationSeconds,
        Rows            = Rows.Select(r => r.Clone()).ToList(),
    };
}
