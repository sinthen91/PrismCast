using Dalamud.Configuration;
using System.Numerics;

namespace PrismCast;

[Serializable]
internal sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // Playback
    public int Volume { get; set; } = 70;

    // Shared world-space screen state
    public bool CurvedScreen { get; set; } = true;
    public Vector3 ScreenPosition { get; set; } = new(0, 0, 0);
    public float ScreenYaw { get; set; }
    public float ScreenPitch { get; set; }
    public float ScreenRoll { get; set; }
    public float ScreenScale { get; set; } = 2.5f;
    public bool HasScreenPlacement { get; set; }

    // Screen-control preferences
    public float ScreenMovementStep { get; set; } = 0.025f;
    public float ScreenFineMovementStep { get; set; } = 0.005f;
    public float ScreenRotationStepDegrees { get; set; } = 2.5f;
    public float ScreenScaleStep { get; set; } = 0.05f;

    // Plex
    public string PlexBaseUrl { get; set; } = "http://127.0.0.1:32400";
    public string PlexToken { get; set; } = "";
    public string PlexServerName { get; set; } = "";
    public string PlexAccountToken { get; set; } = "";
    public string PlexClientIdentifier { get; set; } = "";

    // Local library
    public List<string> LocalMediaFolders { get; set; } = [];
    public bool ScanLocalMediaOnStartup { get; set; } = true;
    public bool IncludeSubfolders { get; set; } = true;

    // Internal PrismCast directory identity. RelayBaseUrl is retained only so older alpha configs
    // deserialize safely; normal users never configure the directory endpoint.
    public string RelayBaseUrl { get; set; } = "";
    public string RelaySecret { get; set; } = "";
    public string TrustedHostIdsText { get; set; } = ""; // legacy
    public bool AutoJoinTrustedHosts { get; set; } = false; // legacy

    // Persistent PrismCast groups. PrismRooms is retained as the serialized property name for
    // migration compatibility with the earlier room prototype.
    public string RoomClientId { get; set; } = "";
    public List<PrismRoomBookmark> PrismRooms { get; set; } = [];
    public string ActiveRoomId { get; set; } = "";
    public bool NearbyDiscoveryEnabled { get; set; } = false; // legacy, no Nearby UI

    // UI
    // 0 = Automatic, 1 = Tablet, 2 = Phone
    public int InterfaceMode { get; set; } = 0;
    public bool RememberLastPage { get; set; } = true;
    public int LastPage { get; set; }
}

[Serializable]
internal sealed class PrismRoomBookmark
{
    public string RoomId { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPrivate { get; set; }
    public bool IsHost { get; set; }
    public string InviteCode { get; set; } = "";
}

