using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Random;

namespace SpinTheSpire.SpinTheSpireCode;

public enum ModifierChange
{
    Added,
    Removed,
    Replaced
}

public readonly record struct ModifierEvent(
    ModifierChange Change,
    ModifierChoice Choice,
    IReadOnlyList<ModifierChoice> Evicted);

/// <summary>
/// Tracks the active modifier set using the rules agreed for Spin the Spire:
/// a duplicate toggles a modifier off, and a newly added member of a mutually
/// exclusive group evicts the others. Evicted modifiers do not come back when the
/// new one is later toggled off.
/// </summary>
public sealed class ModifierTrack
{
    private readonly List<ModifierChoice> _active = new();
    private readonly Dictionary<string, int> _exclusiveGroups;

    public ModifierTrack()
    {
        _exclusiveGroups = SpinRules.GetExclusiveGroupMap();
    }

    public IReadOnlyList<ModifierChoice> Active => _active;

    public ModifierEvent Apply(ModifierChoice choice)
    {
        int existing = _active.FindIndex(c => c.Key == choice.Key);
        if (existing >= 0)
        {
            _active.RemoveAt(existing);
            return new ModifierEvent(ModifierChange.Removed, choice, Array.Empty<ModifierChoice>());
        }

        var evicted = new List<ModifierChoice>();
        if (_exclusiveGroups.TryGetValue(choice.Id, out int group))
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                ModifierChoice other = _active[i];
                if (_exclusiveGroups.TryGetValue(other.Id, out int otherGroup)
                    && otherGroup == group)
                {
                    evicted.Add(other);
                    _active.RemoveAt(i);
                }
            }
        }

        _active.Add(choice);
        return evicted.Count > 0
            ? new ModifierEvent(ModifierChange.Replaced, choice, evicted)
            : new ModifierEvent(ModifierChange.Added, choice, Array.Empty<ModifierChoice>());
    }
}

public static class SpinRules
{
    private static List<CharacterModel>? _playableCharacters;
    private static Dictionary<string, int>? _exclusiveGroups;
    private static Dictionary<string, ModifierModel>? _canonicalModifiers;

    private static readonly string[] VanillaGoodOrder =
    {
        "Draft", "SealedDeck", "Hoarder", "Specialized", "Insanity", "AllStar", "Flight", "Vintage"
    };

    private static readonly string[] VanillaGoodColors =
    {
        "#FFFFFF", "#000000", "#FFFFFF", "#000000", "#FFFFFF", "#000000", "#FFFFFF", "#000000"
    };

    private static readonly string[] VanillaBadOrder =
    {
        "DeadlyEvents", "CursedRun", "BigGameHunter", "Midas", "Murderous", "NightTerrors", "Terminal"
    };

    private static readonly string[] VanillaBadColors =
    {
        "#FF6666", "#F50000", "#FF6666", "#FF3333", "#F50000", "#A30000", "#5C0000"
    };

    private static readonly string[] PlayerColors =
    {
        "#5DADE2", "#82E0AA", "#F7DC6F", "#AF7AC5", "#E59866", "#85C1E9", "#F1948A", "#A3E4D7"
    };

    private static readonly string[] ModdedGoodColors =
    {
        "#7FB3D5", "#A9DFBF", "#F9E79F", "#D7BDE2", "#F5CBA7", "#AED6F1"
    };

    private static readonly string[] ModdedBadColors =
    {
        "#FF7F7F", "#E74C3C", "#C0392B", "#922B21", "#641E16", "#FFB3B3"
    };

    public static readonly (int Value, string Label, string Color)[] AscensionEntries =
    {
        (0, "0 - Baby Mode", "#FFD9D9"),
        (1, "1 - Swarming Elites", "#FFB8B8"),
        (2, "2 - Weary Traveler", "#FF9797"),
        (3, "3 - Poverty", "#F57676"),
        (4, "4 - Tight Belt", "#E55959"),
        (5, "5 - Ascender's Bane", "#CF4040"),
        (6, "6 - Inflation", "#B52C2C"),
        (7, "7 - Scarcity", "#991D1D"),
        (8, "8 - Tough Enemies", "#7C1212"),
        (9, "9 - Deadly Enemies", "#5E0A0A"),
        (10, "10 - Double Boss", "#420404")
    };

    public static List<CharacterModel> GetPlayableCharacters()
    {
        if (_playableCharacters != null)
        {
            return _playableCharacters;
        }

        var result = new List<CharacterModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (CharacterModel character in ModelDb.AllCharacters)
        {
            if (character.IsPlayable && seen.Add(character.Id.ToString()))
            {
                result.Add(character);
            }
        }

        IEnumerable<CharacterModel> custom = ModelDb.AllAbstractModelSubtypes
            .Where(t => !t.IsAbstract && typeof(CharacterModel).IsAssignableFrom(t))
            .Select(t => ModelDb.GetById<CharacterModel>(ModelDb.GetId(t)))
            .Where(c => c.IsPlayable && seen.Add(c.Id.ToString()))
            .OrderBy(c => c.Id.ToString(), StringComparer.Ordinal);

        result.AddRange(custom);
        _playableCharacters = result;
        return result;
    }

    public static List<WheelEntry> BuildCharacterPool()
    {
        var entries = new List<WheelEntry>();
        foreach (CharacterModel character in GetPlayableCharacters())
        {
            string label;
            try
            {
                label = character.Title.GetFormattedText();
            }
            catch
            {
                label = character.Id.Entry;
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                label = character.Id.Entry;
            }

            entries.Add(new WheelEntry
            {
                Id = character.Id.ToString(),
                Label = label.ToUpperInvariant(),
                ColorHex = CharacterColorHex(character)
            });
        }

        return entries;
    }

    /// <summary>
    /// Uses the same identity colors the game assigns to each character (StsColors),
    /// which is also what their name label uses in-run.
    /// </summary>
    private static string CharacterColorHex(CharacterModel character)
    {
        return character switch
        {
            Ironclad => "#FF5555",
            Silent => "#7FFF00",
            Defect => "#87CEEB",
            Regent => "#FFA518",
            Necrobinder => "#EE82EE",
            _ => ToHex(character.NameColor)
        };
    }

    public static List<WheelEntry> BuildGoodPool()
    {
        var entries = new List<WheelEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<ModifierModel> good = ModelDb.GoodModifiers.ToList();

        for (int i = 0; i < VanillaGoodOrder.Length; i++)
        {
            ModifierModel? model = good.FirstOrDefault(m => m.GetType().Name == VanillaGoodOrder[i]);
            if (model == null)
            {
                continue;
            }

            AddModifierEntry(entries, seen, model, VanillaGoodColors[i]);
        }

        foreach (CharacterModel character in GetPlayableCharacters())
        {
            var entry = new WheelEntry
            {
                Id = ModelDb.GetId<CharacterCards>().ToString(),
                CharacterId = character.Id.ToString(),
                Label = SafeLoc(() => character.CardsModifierTitle.GetFormattedText(), $"{character.Id.Entry} Cards"),
                ColorHex = ToHex(character.NameColor)
            };
            if (seen.Add(entry.Key))
            {
                entries.Add(entry);
            }
        }

        int moddedIndex = 0;
        foreach (ModifierModel model in good)
        {
            if (model is CharacterCards)
            {
                continue;
            }

            if (VanillaGoodOrder.Contains(model.GetType().Name, StringComparer.Ordinal))
            {
                continue;
            }

            AddModifierEntry(entries, seen, model, ModdedGoodColors[moddedIndex % ModdedGoodColors.Length]);
            moddedIndex++;
        }

        AddAlignmentModifiers(entries, seen, "Good", ModdedGoodColors);
        return entries;
    }

    public static List<WheelEntry> BuildBadPool()
    {
        var entries = new List<WheelEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<ModifierModel> bad = ModelDb.BadModifiers.ToList();

        for (int i = 0; i < VanillaBadOrder.Length; i++)
        {
            ModifierModel? model = bad.FirstOrDefault(m => m.GetType().Name == VanillaBadOrder[i]);
            if (model == null)
            {
                continue;
            }

            AddModifierEntry(entries, seen, model, VanillaBadColors[i]);
        }

        int moddedIndex = 0;
        foreach (ModifierModel model in bad)
        {
            if (VanillaBadOrder.Contains(model.GetType().Name, StringComparer.Ordinal))
            {
                continue;
            }

            AddModifierEntry(entries, seen, model, ModdedBadColors[moddedIndex % ModdedBadColors.Length]);
            moddedIndex++;
        }

        AddAlignmentModifiers(entries, seen, "Bad", ModdedBadColors);
        int badCount = entries.Count;
        entries.Add(new WheelEntry
        {
            Id = "NOTHING",
            Label = "Safe",
            ColorHex = "#FFFFFF",
            Weight = Math.Max(1, badCount),
            IsNothing = true
        });

        return entries;
    }

    public static List<WheelEntry> BuildAscensionPool()
    {
        return AscensionEntries
            .Select(e => new WheelEntry
            {
                Id = "ASC_" + e.Value,
                Label = StripAscensionNumber(e.Label),
                ColorHex = e.Color
            })
            .ToList();
    }

    /// <summary>Wheel slices only show the flavour name, e.g. "Deadly Enemies" without "9 - ".</summary>
    private static string StripAscensionNumber(string label)
    {
        int separator = label.IndexOf(" - ", StringComparison.Ordinal);
        return separator >= 0 ? label[(separator + 3)..] : label;
    }

    public static string PlayerColor(int index) => PlayerColors[index % PlayerColors.Length];

    public static SpinPlan BuildPlan(
        StartRunLobby lobby,
        bool uniqueCharacters,
        int rollCoefficient,
        bool manualRolls)
    {
        var rng = new Rng(Rng.Chaotic.NextUnsignedLong());
        var plan = new SpinPlan
        {
            VisualSeed = rng.NextUnsignedLong(),
            UniqueCharacters = uniqueCharacters,
            CharacterPool = BuildCharacterPool(),
            GoodPool = BuildGoodPool(),
            BadPool = BuildBadPool(),
            ManualRolls = manualRolls,
            RollCoefficient = Math.Clamp(rollCoefficient, 0, 5)
        };

        List<StartRunLobbyPlayer> players = lobby.Players
            .OrderBy(p => p.slotId)
            .ToList();

        for (int i = 0; i < players.Count; i++)
        {
            StartRunLobbyPlayer player = players[i];
            plan.Players.Add(new PlayerWheelEntry
            {
                PlayerId = player.id,
                Name = PlayerName(lobby, player),
                ColorHex = PlayerColor(i)
            });
        }

        FillCharacterAssignments(plan, rng);

        if (manualRolls)
        {
            plan.Ascension = -1;
            plan.AscensionChosen = false;
            plan.TargetRolls = 0;
            return plan;
        }

        plan.Ascension = rng.NextInt(0, 11);
        plan.AscensionChosen = true;
        plan.TargetRolls = TargetRolls(plan);

        for (int i = 0; i < plan.TargetRolls; i++)
        {
            AddRandomModifierRoll(plan, rng);
        }

        RefreshFinalModifiers(plan);
        return plan;
    }

    /// <summary>Number of modifier rolls for the current ascension and coefficient.</summary>
    public static int TargetRolls(SpinPlan plan)
    {
        int ascension = Math.Max(0, plan.Ascension);
        int coefficient = Math.Clamp(plan.RollCoefficient, 0, 5);
        return (ascension + 1) * coefficient;
    }

    /// <summary>Re-rolls the whole player/character pairing, e.g. after a manual reset.</summary>
    public static void RebuildCharacterAssignments(SpinPlan plan)
    {
        FillCharacterAssignments(plan, new Rng(Rng.Chaotic.NextUnsignedLong()));
        plan.RevealsDone = 0;
    }

    private static void FillCharacterAssignments(SpinPlan plan, Rng rng)
    {
        plan.Assignments.Clear();

        var availablePlayers = plan.Players.ToList();
        var availableCharacters = plan.CharacterPool.ToList();
        bool unique = plan.UniqueCharacters && availableCharacters.Count >= availablePlayers.Count;

        // Capture the player count up front: availablePlayers shrinks inside the loop, so using
        // its live Count as the bound only assigned about half of the lobby.
        int assignmentsNeeded = availablePlayers.Count;
        for (int reveal = 0; reveal < assignmentsNeeded; reveal++)
        {
            PlayerWheelEntry player = availablePlayers[rng.NextInt(availablePlayers.Count)];
            WheelEntry character = availableCharacters[rng.NextInt(availableCharacters.Count)];

            plan.Assignments.Add(new PlayerAssignment
            {
                PlayerId = player.PlayerId,
                CharacterId = character.Id,
                RevealIndex = reveal
            });

            availablePlayers.Remove(player);
            if (unique)
            {
                availableCharacters.Remove(character);
            }
        }
    }

    /// <summary>Appends one manually requested modifier roll and refreshes the final set.</summary>
    public static ModifierRoll AppendModifierRoll(SpinPlan plan)
    {
        var rng = new Rng(Rng.Chaotic.NextUnsignedLong());
        ModifierRoll roll = AddRandomModifierRoll(plan, rng);
        RefreshFinalModifiers(plan);
        return roll;
    }

    private static ModifierRoll AddRandomModifierRoll(SpinPlan plan, Rng rng)
    {
        WheelEntry good = PickWeighted(plan.GoodPool, rng);
        WheelEntry bad = PickWeighted(plan.BadPool, rng);

        var roll = new ModifierRoll
        {
            Good = ModifierChoice.From(good),
            Bad = bad.IsNothing ? null : ModifierChoice.From(bad)
        };

        plan.Rolls.Add(roll);
        return roll;
    }

    public static void RefreshFinalModifiers(SpinPlan plan)
    {
        var track = new ModifierTrack();
        foreach (ModifierRoll roll in plan.Rolls)
        {
            if (roll.Good != null)
            {
                track.Apply(roll.Good);
            }

            if (roll.Bad != null)
            {
                track.Apply(roll.Bad);
            }
        }

        plan.FinalModifiers = track.Active.ToList();
    }

    public static List<ModifierModel> CreateMutableModifiers(IEnumerable<ModifierChoice> choices)
    {
        var result = new List<ModifierModel>();
        foreach (ModifierChoice choice in choices)
        {
            try
            {
                ModifierModel? model = CreateMutableModifier(choice);
                if (model != null)
                {
                    result.Add(model);
                }
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn($"Could not create modifier {choice.Id}: {e.Message}");
            }
        }

        return result;
    }

    public static ModifierModel? CreateMutableModifier(ModifierChoice choice)
    {
        if (choice.Id == "NOTHING")
        {
            return null;
        }

        ModifierModel canonical = GetCanonicalModifier(choice.Id);
        ModifierModel mutable = canonical.ToMutable();
        if (mutable is CharacterCards cards && !string.IsNullOrEmpty(choice.CharacterId))
        {
            cards.CharacterModel = ModelId.Deserialize(choice.CharacterId);
        }

        return mutable;
    }

    public static string LabelFor(ModifierChoice choice)
    {
        try
        {
            if (choice.Id == "NOTHING")
            {
                return "Safe";
            }

            ModifierModel canonical = GetCanonicalModifier(choice.Id);
            if (canonical is CharacterCards && !string.IsNullOrEmpty(choice.CharacterId))
            {
                CharacterModel character = ModelDb.GetById<CharacterModel>(ModelId.Deserialize(choice.CharacterId));
                return SafeLoc(() => character.CardsModifierTitle.GetFormattedText(), $"{character.Id.Entry} Cards");
            }

            return SafeLoc(() => canonical.Title.GetFormattedText(), canonical.Id.Entry);
        }
        catch
        {
            return choice.Id;
        }
    }

    public static Dictionary<string, int> GetExclusiveGroupMap()
    {
        if (_exclusiveGroups != null)
        {
            return _exclusiveGroups;
        }

        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        int groupIndex = 0;
        foreach (IReadOnlySet<ModifierModel> group in ModelDb.MutuallyExclusiveModifiers)
        {
            foreach (ModifierModel member in group)
            {
                map[member.Id.ToString()] = groupIndex;
            }

            groupIndex++;
        }

        _exclusiveGroups = map;
        return map;
    }

    private static void AddModifierEntry(
        List<WheelEntry> entries,
        HashSet<string> seen,
        ModifierModel model,
        string color)
    {
        var entry = new WheelEntry
        {
            Id = model.Id.ToString(),
            Label = SafeLoc(() => model.Title.GetFormattedText(), model.Id.Entry),
            ColorHex = color
        };

        if (seen.Add(entry.Key))
        {
            entries.Add(entry);
        }
    }

    private static ModifierModel GetCanonicalModifier(string id)
    {
        _canonicalModifiers ??= ModelDb.AllAbstractModelSubtypes
            .Where(t => !t.IsAbstract && typeof(ModifierModel).IsAssignableFrom(t))
            .Select(t => ModelDb.GetById<ModifierModel>(ModelDb.GetId(t)))
            .GroupBy(m => m.Id.ToString(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (_canonicalModifiers.TryGetValue(id, out ModifierModel? model))
        {
            return model;
        }

        return ModelDb.GetById<ModifierModel>(ModelId.Deserialize(id));
    }

    /// <summary>
    /// Best-effort compatibility with modifiers from other mods. BaseLib's CustomModifierModel
    /// exposes an Alignment property; if a modifier is not in the curated good/bad lists yet,
    /// read that property and slot it into the right wheel.
    /// </summary>
    private static void AddAlignmentModifiers(
        List<WheelEntry> entries,
        HashSet<string> seen,
        string desiredAlignment,
        string[] colors)
    {
        foreach (Type type in ModelDb.AllAbstractModelSubtypes)
        {
            if (type.IsAbstract
                || !typeof(ModifierModel).IsAssignableFrom(type)
                || type == typeof(CharacterCards))
            {
                continue;
            }

            try
            {
                ModifierModel model = ModelDb.GetById<ModifierModel>(ModelDb.GetId(type));
                var probe = new WheelEntry { Id = model.Id.ToString() };
                if (seen.Contains(probe.Key))
                {
                    continue;
                }

                PropertyInfo? property = type.GetProperty(
                    "Alignment",
                    BindingFlags.Public | BindingFlags.Instance);
                if (property == null)
                {
                    continue;
                }

                string? alignment = property.GetValue(model)?.ToString();
                if (!string.Equals(alignment, desiredAlignment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddModifierEntry(
                    entries,
                    seen,
                    model,
                    colors[entries.Count % colors.Length]);
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn($"Could not inspect modifier {type.Name}: {e.Message}");
            }
        }
    }

    private static WheelEntry PickWeighted(IReadOnlyList<WheelEntry> entries, Rng rng)
    {
        int total = 0;
        foreach (WheelEntry entry in entries)
        {
            total += Math.Max(1, entry.Weight);
        }

        int pick = rng.NextInt(total);
        foreach (WheelEntry entry in entries)
        {
            pick -= Math.Max(1, entry.Weight);
            if (pick < 0)
            {
                return entry;
            }
        }

        return entries[entries.Count - 1];
    }

    public static string PlayerName(StartRunLobby lobby, StartRunLobbyPlayer player)
    {
        if (lobby.NetService.Type == NetGameType.Singleplayer)
        {
            return "You";
        }

        try
        {
            string name = PlatformUtil.GetPlayerNameRaw(lobby.NetService.Platform, player.id);
            return string.IsNullOrWhiteSpace(name) ? $"Player {player.slotId + 1}" : name;
        }
        catch
        {
            return $"Player {player.slotId + 1}";
        }
    }

    private static string SafeLoc(Func<string> get, string fallback)
    {
        try
        {
            string text = get();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch
        {
            return fallback;
        }
    }

    private static string ToHex(Color color)
    {
        return $"#{color.R8:X2}{color.G8:X2}{color.B8:X2}";
    }
}
