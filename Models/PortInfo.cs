namespace ActiveScanner.Models;

/// <summary>
/// Represents a port number with its common service name
/// </summary>
public class PortInfo
{
    public int Port { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName => $"{Name} ({Port})";

    public PortInfo() { }

    public PortInfo(int port, string name)
    {
        Port = port;
        Name = name;
    }
}
