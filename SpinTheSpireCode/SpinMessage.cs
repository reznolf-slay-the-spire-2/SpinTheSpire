using System.Collections.Generic;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace SpinTheSpire.SpinTheSpireCode;

public enum SpinMessageKind
{
    Hello = 0,
    Ready = 1,
    ReadyState = 2,
    Start = 3,
    Vote = 4,
    VoteState = 5,
    VoteResult = 6,
    ReturnToLobby = 7,
    ShowVanilla = 8,
    ShowMod = 9,
    Abort = 10,
    Advance = 11,
    ManualSpin = 12,
    PromptOpen = 13,
    PromptVote = 14,
    PromptState = 15,
    PromptResult = 16
}

/// <summary>
/// One compact message type for the lobby spin. The game's MessageTypes cache scans
/// mod assemblies for INetMessage implementations, so no BaseLib dependency is needed.
/// </summary>
public sealed class SpinMessage : INetMessage
{
    public SpinMessageKind Kind { get; set; }
    public ulong PlayerId { get; set; }
    public bool Flag { get; set; }
    public int Number { get; set; }
    public string Payload { get; set; } = "";

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => true;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt((int)Kind, 8);
        writer.WriteULong(PlayerId);
        writer.WriteBool(Flag);
        writer.WriteInt(Number);
        writer.WriteString(Payload ?? "");
    }

    public void Deserialize(PacketReader reader)
    {
        Kind = (SpinMessageKind)reader.ReadInt(8);
        PlayerId = reader.ReadULong();
        Flag = reader.ReadBool();
        Number = reader.ReadInt();
        Payload = reader.ReadString();
    }
}

public sealed class LobbyStatePayload
{
    public bool Unique { get; set; }
    public bool Started { get; set; }
    public bool Vanilla { get; set; }
    public List<ulong> Ready { get; set; } = new();
}

public sealed class VoteEntry
{
    public ulong PlayerId { get; set; }
    public bool Ride { get; set; }
}

public sealed class VoteStatePayload
{
    public List<VoteEntry> Votes { get; set; } = new();
}

public sealed class VoteResultPayload
{
    public bool Ride { get; set; }
    public ulong WinnerId { get; set; }
    public bool Split { get; set; }
    public ulong VisualSeed { get; set; }
}

internal static class SpinPayload
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, SpinJson.Options);

    public static T? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, SpinJson.Options);
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Warn($"Could not parse {typeof(T).Name}: {e.Message}");
            return default;
        }
    }
}
