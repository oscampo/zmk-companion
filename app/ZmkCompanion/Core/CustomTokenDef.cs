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
}
