namespace ZmkCompanion.Core;

// A user-declared {custom.NAME} token. Declaring one here just makes it show
// up in the CellGridEditorForm binding picker under Category, grouped with
// any other declarations sharing that name, it does not set a value.
// Values only ever come from `zkc --set NAME value` / `--set NAME --watch` at
// runtime, via LiveState.UpdateCustom(); until the first SET arrives,
// {custom.NAME} resolves to "" (see LiveState.Resolve), not literal "{key}".
sealed class CustomTokenDef
{
    public string Name     { get; set; } = "";
    public string Category { get; set; } = "Personalizado";

    // Balloon warning threshold: if no `zkc --set` has landed for this name in
    // more than this many seconds, AppContext's stale-check timer shows a
    // ToolTipIcon.Error balloon. 0 = never check (default; not every value
    // needs a freshness guarantee - a battery temp update every few minutes
    // is fine, a stock price streamed every second going quiet for 5 minutes
    // is not, so this is per-token, not a global setting).
    public int StaleAfterSeconds { get; set; } = 0;
}
