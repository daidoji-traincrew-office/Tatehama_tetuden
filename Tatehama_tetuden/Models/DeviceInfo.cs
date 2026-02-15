namespace Tatehama_tetuden.Models;

public class DeviceInfo
{
    public string Name { get; set; }
    public string ID   { get; set; }
    public override string ToString() => Name;
}
