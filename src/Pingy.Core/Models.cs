namespace Pingy.Core;

public sealed class HostEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Group { get; set; } = "Без группы";
    public string Description { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public HostEntry Clone() => new()
    {
        Id = Id, Name = Name, Address = Address, Group = Group,
        Description = Description, Enabled = Enabled
    };
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public Guid ProfileId { get; set; } = Guid.NewGuid();
    public int IntervalMs { get; set; } = 1000;
    public int TimeoutMs { get; set; } = 1000;
    public List<HostEntry> Hosts { get; set; } = [];

    public AppConfig Clone() => new()
    {
        Version = Version, ProfileId = ProfileId, IntervalMs = IntervalMs,
        TimeoutMs = TimeoutMs, Hosts = Hosts.Select(host => host.Clone()).ToList()
    };
}
