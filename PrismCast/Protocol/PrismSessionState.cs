using System.Numerics;

namespace PrismCast.Protocol;

internal sealed record PrismSessionState
{
    public string SessionId { get; init; } = "";
    public string PlaybackKind { get; init; } = "native";
    public string MediaUrl { get; init; } = "";
    public bool Playing { get; init; }
    public bool IsLiveStream { get; init; }
    public int VideoWidth { get; init; } = 1280;
    public int VideoHeight { get; init; } = 720;
    public double PositionSeconds { get; init; }
    public double DurationSeconds { get; init; }
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
