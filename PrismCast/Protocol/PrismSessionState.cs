using System.Numerics;

namespace PrismCast.Protocol;

internal sealed record PrismSessionState
{
    public string SessionId { get; init; } = "";
    public string MediaUrl { get; init; } = "";
    public bool Playing { get; init; }
    public double PositionSeconds { get; init; }
    public long HostUnixMilliseconds { get; init; }
    public Vector3 Position { get; init; }
    public float Yaw { get; init; }
    public float Pitch { get; init; }
    public float Roll { get; init; }
    public float Scale { get; init; } = 2.5f;
    public bool Curved { get; init; } = true;
    public string Title { get; init; } = "";
    public string HostName { get; init; } = "";
    public string[] Viewers { get; init; } = [];
}
