using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SpinTheSpire.SpinTheSpireCode;

/// <summary>A selectable wheel entry. For character-card modifiers the same ModifierId is
/// used for every character, distinguished by CharacterId.</summary>
public sealed class WheelEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "#FFFFFF";
    public int Weight { get; set; } = 1;
    public string? CharacterId { get; set; }
    public bool IsNothing { get; set; }

    public string Key => Id + "|" + (CharacterId ?? "");
}

public sealed class PlayerWheelEntry
{
    public ulong PlayerId { get; set; }
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "#5DADE2";
}

public sealed class PlayerAssignment
{
    public ulong PlayerId { get; set; }
    public string CharacterId { get; set; } = "";
    public int RevealIndex { get; set; }
}

public sealed class ModifierChoice
{
    public string Id { get; set; } = "";
    public string? CharacterId { get; set; }

    public string Key => Id + "|" + (CharacterId ?? "");

    public static ModifierChoice From(WheelEntry entry) =>
        new() { Id = entry.Id, CharacterId = entry.CharacterId };

    public override bool Equals(object? obj) =>
        obj is ModifierChoice other && other.Key == Key;

    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);
}

public sealed class ModifierRoll
{
    public ModifierChoice? Good { get; set; }
    public ModifierChoice? Bad { get; set; }
}

public sealed class SpinPlan
{
    public int Version { get; set; } = 1;
    public ulong VisualSeed { get; set; }
    public bool UniqueCharacters { get; set; }
    public bool ManualRolls { get; set; }
    public int RollCoefficient { get; set; } = 2;
    public int RevealsDone { get; set; }
    public int TargetRolls { get; set; }
    public bool AscensionChosen { get; set; } = true;
    public List<WheelEntry> CharacterPool { get; set; } = new();
    public List<WheelEntry> GoodPool { get; set; } = new();
    public List<WheelEntry> BadPool { get; set; } = new();
    public List<PlayerWheelEntry> Players { get; set; } = new();
    public List<PlayerAssignment> Assignments { get; set; } = new();
    public int Ascension { get; set; }
    public List<ModifierRoll> Rolls { get; set; } = new();
    public List<ModifierChoice> FinalModifiers { get; set; } = new();

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, SpinJson.Options);
    }

    /// <summary>
    /// Copies a freshly received plan into this instance without changing the object identity:
    /// the running sequence holds a reference to the original plan for the whole spin.
    /// </summary>
    public void CopyFrom(SpinPlan other)
    {
        Version = other.Version;
        VisualSeed = other.VisualSeed;
        UniqueCharacters = other.UniqueCharacters;
        ManualRolls = other.ManualRolls;
        RollCoefficient = other.RollCoefficient;
        RevealsDone = other.RevealsDone;
        TargetRolls = other.TargetRolls;
        AscensionChosen = other.AscensionChosen;
        CharacterPool = other.CharacterPool;
        GoodPool = other.GoodPool;
        BadPool = other.BadPool;
        Players = other.Players;
        Assignments = other.Assignments;
        Ascension = other.Ascension;
        Rolls = other.Rolls;
        FinalModifiers = other.FinalModifiers;
    }

    public static SpinPlan? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<SpinPlan>(json, SpinJson.Options);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not parse spin plan: {e.Message}");
            return null;
        }
    }
}

internal static class SpinJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        IncludeFields = true,
        PropertyNameCaseInsensitive = true
    };
}
