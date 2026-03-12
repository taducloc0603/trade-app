namespace TradeDesktop.App.State;

public sealed class RuntimeConfigState
{
    public string LocalIp { get; private set; } = string.Empty;
    public string MapName1 { get; private set; } = "SANA_MAP";
    public string MapName2 { get; private set; } = "SANB_MAP";

    public event EventHandler? StateChanged;

    public void Update(string localIp, string mapName1, string mapName2)
    {
        LocalIp = localIp.Trim();
        MapName1 = mapName1.Trim();
        MapName2 = mapName2.Trim();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
