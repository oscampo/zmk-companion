namespace ZmkCompanion.Core;

enum SplitHalf { None = 0, Top = 1, Bottom = 2 }

sealed class CellGridRow
{
    public byte      TierId       { get; set; } = 4; // large_impar (13×22)
    public string    Template     { get; set; } = "";
    public string    Align        { get; set; } = "center"; // left | center | right
    public SplitHalf SplitHalf    { get; set; } = SplitHalf.None;
    public bool      Bold         { get; set; } = false;
    public string    NumericStyle { get; set; } = "text"; // text | box | box_outline | box_multiple | plain | circle | circle_outline
    public string    AlphaStyle   { get; set; } = "text"; // text | plain | box | box_outline | circle | circle_outline
    public bool      AntiAlias    { get; set; } = false;  // AntiAliasGridFit → threshold bitmapping

    public CellGridRow Clone() => new()
    {
        TierId       = TierId,
        Template     = Template,
        Align        = Align,
        SplitHalf    = SplitHalf,
        Bold         = Bold,
        NumericStyle = NumericStyle,
        AlphaStyle   = AlphaStyle,
        AntiAlias    = AntiAlias,
    };
}

// One display page: a sequence of cell-grid rows rendered top-to-bottom.
// The sum of row heights must not exceed BitmapFrame.Height (160px).
sealed class CellGridPage
{
    public string Name            { get; set; } = "Page 1";
    public int    DurationSeconds { get; set; } = 10;
    public bool   LightMode       { get; set; } = false; // false=dark (bg black, glyphs white on display)
    public List<CellGridRow> Rows { get; set; } = [];

    public int TotalHeight => Rows.Sum(r =>
        r.TierId < CellGridProtocol.Tiers.Length ? CellGridProtocol.Tiers[r.TierId].H : 0);

    public CellGridPage Clone() => new()
    {
        Name            = Name,
        DurationSeconds = DurationSeconds,
        LightMode       = LightMode,
        Rows            = Rows.Select(r => r.Clone()).ToList(),
    };
}
