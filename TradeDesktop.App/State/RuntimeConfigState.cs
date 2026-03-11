namespace TradeDesktop.App.State;

public sealed class RuntimeConfigState
{
    public string Code { get; private set; } = "DEMO-CODE";
    public string MapName1 { get; private set; } = "SANA_MAP";
    public string MapName2 { get; private set; } = "SANB_MAP";

    public event EventHandler? StateChanged;

    public void Update(string code, string mapName1, string mapName2)
    {
        Code = code.Trim();
        MapName1 = mapName1.Trim();
        MapName2 = mapName2.Trim();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
