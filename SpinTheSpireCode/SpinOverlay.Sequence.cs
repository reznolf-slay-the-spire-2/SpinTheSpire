using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Random;

namespace SpinTheSpire.SpinTheSpireCode;

public sealed partial class SpinOverlay
{
    private bool _pendingReturnToLobby;

    private async Task BeginSequenceAfterDelayAsync(int delayMs, SpinPlan plan, int epoch)
    {
        await Pause(Math.Max(0, delayMs) / 1000f);
        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            return;
        }

        await RunSequenceAsync(plan, epoch);
    }

    private async Task RunSequenceAsync(SpinPlan plan, int epoch)
    {
        try
        {
            ShowPanel(_spinPanel);
            Visible = true;
            _nextButton.Visible = true;
            _nextButton.SetEnabled(false);
            _activeModifierPanel.Visible = false;
            SetStageStatusText("");

            float total = Mathf.Clamp(SpinConfig.DurationSeconds,
                SpinConfig.MinDurationSeconds,
                SpinConfig.MaxDurationSeconds);
            float characterBudget = total * 0.25f;
            float ascensionBudget = total * 0.10f;
            float modifierBudget = total * 0.47f;

            float characterSpin = Mathf.Clamp(
                characterBudget / Mathf.Max(1, plan.Assignments.Count), 0.55f, 3.5f);
            float ascensionSpin = Mathf.Clamp(ascensionBudget, 1.1f, 4.0f);

            _manualSpinButton.Visible = plan.ManualRolls;
            _manualSpinButton.SetEnabled(false);

            if (plan.ManualRolls)
            {
                if (!await RunManualCharacterStageAsync(plan, characterSpin, epoch))
                {
                    return;
                }

                if (!await RunManualAscensionStageAsync(plan, ascensionSpin, epoch))
                {
                    return;
                }

                plan.TargetRolls = SpinRules.TargetRolls(plan);
                float manualModifierSpin = Mathf.Clamp(
                    modifierBudget / Mathf.Max(1, plan.TargetRolls), 0.42f, 2.8f);

                if (!await RunManualModifierStageAsync(plan, manualModifierSpin, epoch))
                {
                    return;
                }

                ShowSummary(plan);
                return;
            }

            float modifierSpin = Mathf.Clamp(
                modifierBudget / Mathf.Max(1, plan.Rolls.Count), 0.42f, 2.8f);

            _advanceStageCounter = 1;

            await RunCharacterStageAsync(plan, characterSpin, epoch);
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            await RunAscensionStageAsync(plan, ascensionSpin, epoch);
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            if (!await WaitForHostAdvanceNextAsync(epoch))
            {
                return;
            }

            if (plan.Rolls.Count == 0)
            {
                ApplyLobbyModifiers(plan);
                ShowSummary(plan);
                return;
            }

            await RunModifierStageAsync(plan, modifierSpin, epoch);
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            ShowSummary(plan);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Spin sequence failed: {e}");
            if (epoch == _sequenceEpoch && !_disconnected)
            {
                ResetManualUi();
                ShowLobby();
            }
        }
    }

    private async Task<bool> RunManualCharacterStageAsync(SpinPlan plan, float spinDuration, int epoch)
    {
        _stageTitle.Text = "CHARACTERS";
        _activeModifierPanel.Visible = false;
        _manualSection = SpinSection.Characters;
        int count = plan.Assignments.Count;
        SetStageStatusText("");
        _stageProgress.Text = count == 0 ? "No players" : $"Reveal 1 / {count}";

        if (_isSingleplayer)
        {
            SetWheelMode(1);
            ConfigureWheel(_wheelSolo, "", plan.CharacterPool, 620f);
        }
        else
        {
            SetWheelMode(2);
            ConfigureWheel(_wheelA, "Characters", plan.CharacterPool, 470f);
            ConfigureWheel(_wheelB, "Players", plan.Players, 470f);
        }

        UpdateManualSectionButtons();

        while (true)
        {
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return false;
            }

            SetManualAnimating(false);
            _stageProgress.Text = plan.RevealsDone >= count
                ? $"All {count} assigned"
                : $"Reveal {plan.RevealsDone + 1} / {count}";
            UpdateManualSectionButtons();

            ManualAction? action = await WaitManualActionAsync(epoch);
            if (action == null)
            {
                return false;
            }

            if (action == ManualAction.Next)
            {
                return true;
            }

            SetManualAnimating(true);
            if (!await AnimateManualCharacterRevealAsync(plan, spinDuration, epoch))
            {
                return false;
            }
        }
    }

    private async Task<bool> AnimateManualCharacterRevealAsync(
        SpinPlan plan,
        float spinDuration,
        int epoch)
    {
        int index = plan.RevealsDone - 1;
        if (index < 0 || index >= plan.Assignments.Count)
        {
            return true;
        }

        PlayerAssignment assignment = plan.Assignments[index];
        PlayerWheelEntry? playerEntry =
            plan.Players.FirstOrDefault(p => p.PlayerId == assignment.PlayerId);
        WheelEntry? characterEntry =
            plan.CharacterPool.FirstOrDefault(c => c.Id == assignment.CharacterId);
        if (playerEntry == null || characterEntry == null)
        {
            return true;
        }

        List<PlayerAssignment> earlier =
            plan.Assignments.Where(a => a.RevealIndex < index).ToList();
        List<PlayerWheelEntry> remainingPlayers = plan.Players
            .Where(p => earlier.All(a => a.PlayerId != p.PlayerId))
            .ToList();
        bool unique = plan.UniqueCharacters && plan.CharacterPool.Count >= plan.Players.Count;
        List<WheelEntry> characterOptions = unique
            ? plan.CharacterPool.Where(c => earlier.All(a => a.CharacterId != c.Id)).ToList()
            : plan.CharacterPool;
        if (characterOptions.Count == 0)
        {
            characterOptions = plan.CharacterPool;
        }

        ulong seed = plan.VisualSeed + (ulong)(index * 31 + 11);

        if (_isSingleplayer)
        {
            SetWheelMode(1);
            ConfigureWheel(_wheelSolo, "", characterOptions, 620f);
            await _wheelSolo.SpinToAsync(characterEntry.Key, spinDuration, seed);
        }
        else
        {
            SetWheelMode(2);
            ConfigureWheel(_wheelA, "Characters", characterOptions, 470f);
            ConfigureWheel(_wheelB, "Players", remainingPlayers, 470f);
            await Task.WhenAll(
                _wheelA.SpinToAsync(characterEntry.Key, spinDuration, seed),
                _wheelB.SpinToAsync(playerEntry.PlayerId.ToString(), spinDuration, seed + 7));
        }

        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            return false;
        }

        await Pause(0.10f);

        if (_isSingleplayer)
        {
            _wheelSolo.SetResultText(characterEntry.Label);
        }
        else
        {
            _wheelA.SetResultText(characterEntry.Label);
            _wheelB.SetResultText(playerEntry.Name);
            SetStageStatusText($"{playerEntry.Name}  →  {characterEntry.Label}");
        }

        ulong localPlayerId = _lobby?.LocalPlayer.id ?? _localId;
        if (assignment.PlayerId == localPlayerId)
        {
            ApplyLocalCharacter(assignment.CharacterId);
        }

        await Pause(0.35f);
        return true;
    }

    private async Task<bool> RunManualAscensionStageAsync(SpinPlan plan, float spinDuration, int epoch)
    {
        _stageTitle.Text = "ASCENSION";
        _activeModifierPanel.Visible = false;
        _stageProgress.Text = "0 - 10";
        SetStageStatusText("");
        _manualSection = SpinSection.Ascension;
        SetWheelMode(1);
        ConfigureWheel(_wheelSolo, "", SpinRules.BuildAscensionPool(), 620f);

        while (true)
        {
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return false;
            }

            SetManualAnimating(false);
            UpdateManualSectionButtons();

            ManualAction? action = await WaitManualActionAsync(epoch);
            if (action == null)
            {
                return false;
            }

            if (action == ManualAction.Next)
            {
                return true;
            }

            if (plan.Ascension < 0)
            {
                continue;
            }

            SetManualAnimating(true);
            await _wheelSolo.SpinToAsync(
                "ASC_" + plan.Ascension + "|", spinDuration, plan.VisualSeed + 97);

            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return false;
            }

            await Pause(0.10f);
            _wheelSolo.SetResultText(SpinRules.AscensionEntries[plan.Ascension].Label);

            if (_isHost)
            {
                try
                {
                    _lobby?.SyncAscensionChange(plan.Ascension);
                }
                catch (Exception e)
                {
                    MainFile.Logger.Warn($"Could not sync ascension: {e.Message}");
                }
            }

            await Pause(0.6f);
        }
    }

    private async Task<bool> RunManualModifierStageAsync(SpinPlan plan, float spinDuration, int epoch)
    {
        _stageTitle.Text = "MODIFIERS";
        _activeModifierPanel.Visible = true;
        SetStageStatusText("");
        _manualSection = SpinSection.Modifiers;

        plan.TargetRolls = SpinRules.TargetRolls(plan);

        SetWheelMode(2);
        ConfigureWheel(_wheelA, "Blessings", plan.GoodPool, 450f);
        ConfigureWheel(_wheelB, "Curses", plan.BadPool, 450f);
        UpdateManualModifierUi(plan);

        while (true)
        {
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return false;
            }

            SetManualAnimating(false);
            UpdateManualModifierUi(plan);

            ManualAction? action = await WaitManualActionAsync(epoch);
            if (action == null)
            {
                return false;
            }

            if (action == ManualAction.Next)
            {
                ApplyLobbyModifiers(plan);
                return true;
            }

            SetManualAnimating(true);
            if (!await AnimateManualModifierRollAsync(plan, spinDuration, epoch))
            {
                return false;
            }
        }
    }

    private void UpdateManualModifierUi(SpinPlan plan)
    {
        var track = BuildModifierTrack(plan);
        UpdateActiveModifierList(track, plan);

        _stageProgress.Text = plan.TargetRolls <= 0
            ? $"Roll {plan.Rolls.Count} (no limit)"
            : $"Roll {plan.Rolls.Count} / {plan.TargetRolls}";
        UpdateManualSectionButtons();
    }

    private static ModifierTrack BuildModifierTrack(SpinPlan plan)
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

        return track;
    }

    private async Task<bool> AnimateManualModifierRollAsync(
        SpinPlan plan,
        float spinDuration,
        int epoch)
    {
        if (plan.Rolls.Count == 0)
        {
            return true;
        }

        ModifierRoll roll = plan.Rolls[^1];
        string nothingKey = plan.BadPool.FirstOrDefault(e => e.IsNothing)?.Key ?? "NOTHING|";
        string goodKey = roll.Good?.Key ?? "";
        string badKey = roll.Bad?.Key ?? nothingKey;
        ulong seed = plan.VisualSeed + (ulong)((plan.Rolls.Count - 1) * 131 + 17);

        await Task.WhenAll(
            _wheelA.SpinToAsync(goodKey, spinDuration, seed),
            _wheelB.SpinToAsync(badKey, spinDuration, seed + 23));

        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            return false;
        }

        await Pause(0.10f);
        string? goodText = roll.Good != null ? $"+ {SpinRules.LabelFor(roll.Good)}" : null;
        string badText = roll.Bad != null ? $"+ {SpinRules.LabelFor(roll.Bad)}" : "Safe";
        SetStageStatusParts(
            (goodText ?? "", new Color("#8FE39B")),
            (badText, new Color("#F08A8A")));
        UpdateManualModifierUi(plan);
        await Pause(0.2f);
        return true;
    }

    private async Task<bool> WaitForHostAdvanceNextAsync(int epoch)
    {
        int stage = _advanceStageCounter++;
        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            return false;
        }

        return await WaitForHostAdvanceAsync(stage);
    }

    private async Task<bool> WaitForHostAdvanceAsync(int stage)
    {
        if (_advanceReceived >= stage)
        {
            return !_abortRequested && !_disconnected;
        }

        _advanceWaitStage = stage;
        var tcs = new TaskCompletionSource<bool>();
        _advanceTcs = tcs;
        _nextButton.Visible = true;
        _nextButton.SetEnabled(_isHost);

        bool result = await tcs.Task;
        _advanceTcs = null;
        _nextButton.SetEnabled(false);
        return result && !_abortRequested && !_disconnected;
    }

    private async Task RunCharacterStageAsync(SpinPlan plan, float spinDuration, int epoch)
    {
        _stageTitle.Text = "CHARACTERS";
        _activeModifierPanel.Visible = false;
        _stageProgress.Text = "";
        MainFile.Logger.Info(
            $"Character stage: local id {_localId}; plan players " +
            $"[{string.Join(", ", plan.Players.Select(p => p.PlayerId))}].");

        var remainingPlayers = plan.Players.ToList();
        var remainingCharacters = plan.CharacterPool.ToList();
        bool unique = plan.UniqueCharacters && plan.CharacterPool.Count >= plan.Players.Count;

        foreach (PlayerAssignment assignment in plan.Assignments.OrderBy(a => a.RevealIndex))
        {
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            PlayerWheelEntry? playerEntry = plan.Players.FirstOrDefault(p => p.PlayerId == assignment.PlayerId);
            WheelEntry? characterEntry = plan.CharacterPool.FirstOrDefault(c => c.Id == assignment.CharacterId);
            if (playerEntry == null || characterEntry == null)
            {
                continue;
            }

            List<WheelEntry> characterOptions = unique ? remainingCharacters : plan.CharacterPool;
            _stageProgress.Text = $"Reveal {assignment.RevealIndex + 1} / {plan.Assignments.Count}";
            SetStageStatusText("");

            ulong seed = plan.VisualSeed + (ulong)(assignment.RevealIndex * 31 + 11);

            if (_isSingleplayer)
            {
                SetWheelMode(1);
                ConfigureWheel(_wheelSolo, "", characterOptions, 620f);
                await _wheelSolo.SpinToAsync(characterEntry.Key, spinDuration, seed);
            }
            else
            {
                SetWheelMode(2);
                ConfigureWheel(_wheelA, "Characters", characterOptions, 470f);
                ConfigureWheel(_wheelB, "Players", remainingPlayers, 470f);
                await Task.WhenAll(
                    _wheelA.SpinToAsync(characterEntry.Key, spinDuration, seed),
                    _wheelB.SpinToAsync(playerEntry.PlayerId.ToString(), spinDuration, seed + 7));
            }

            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            await Pause(0.10f);

            if (_isSingleplayer)
            {
                _wheelSolo.SetResultText(characterEntry.Label);
            }
            else
            {
                _wheelA.SetResultText(characterEntry.Label);
                _wheelB.SetResultText(playerEntry.Name);
                SetStageStatusText($"{playerEntry.Name}  →  {characterEntry.Label}");
            }

            ulong localPlayerId = _lobby?.LocalPlayer.id ?? _localId;
            if (assignment.PlayerId == localPlayerId)
            {
                ApplyLocalCharacter(assignment.CharacterId);
            }

            remainingPlayers.RemoveAll(p => p.PlayerId == assignment.PlayerId);
            if (unique)
            {
                remainingCharacters.RemoveAll(c => c.Id == assignment.CharacterId);
            }

            await Pause(0.35f);

            if (!await WaitForHostAdvanceNextAsync(epoch))
            {
                return;
            }
        }

        await Pause(0.20f);
    }

    private async Task RunAscensionStageAsync(SpinPlan plan, float spinDuration, int epoch)
    {
        _stageTitle.Text = "ASCENSION";
        _activeModifierPanel.Visible = false;
        _stageProgress.Text = "0 - 10";
        SetStageStatusText("");
        SetWheelMode(1);
        ConfigureWheel(_wheelSolo, "", SpinRules.BuildAscensionPool(), 620f);

        await _wheelSolo.SpinToAsync("ASC_" + plan.Ascension + "|", spinDuration, plan.VisualSeed + 97);
        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            return;
        }

        await Pause(0.10f);
        string label = SpinRules.AscensionEntries[plan.Ascension].Label;
        _wheelSolo.SetResultText(label);

        if (_isHost)
        {
            try
            {
                _lobby?.SyncAscensionChange(plan.Ascension);
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn($"Could not sync ascension: {e.Message}");
            }
        }

        await Pause(0.6f);
    }

    private async Task RunModifierStageAsync(SpinPlan plan, float spinDuration, int epoch)
    {
        _stageTitle.Text = "MODIFIERS";
        _activeModifierPanel.Visible = true;
        _stageProgress.Text = "";
        SetStageStatusText("");
        SetWheelMode(2);
        ConfigureWheel(_wheelA, "Blessings", plan.GoodPool, 450f);
        ConfigureWheel(_wheelB, "Curses", plan.BadPool, 450f);

        var track = new ModifierTrack();
        UpdateActiveModifierList(track, plan);
        string nothingKey = plan.BadPool.FirstOrDefault(e => e.IsNothing)?.Key ?? "NOTHING|";

        for (int i = 0; i < plan.Rolls.Count; i++)
        {
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            ModifierRoll roll = plan.Rolls[i];
            _stageProgress.Text = $"Roll {i + 1} / {plan.Rolls.Count}";
            SetStageStatusText("");

            string goodKey = roll.Good?.Key ?? "";
            string badKey = roll.Bad?.Key ?? nothingKey;
            ulong seed = plan.VisualSeed + (ulong)(i * 131 + 17);

            await Task.WhenAll(
                _wheelA.SpinToAsync(goodKey, spinDuration, seed),
                _wheelB.SpinToAsync(badKey, spinDuration, seed + 23));

            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            await Pause(0.10f);

            string? goodText = null;
            string badText = "Safe";
            if (roll.Good != null)
            {
                goodText = DescribeModifierEvent(track.Apply(roll.Good));
            }

            if (roll.Bad != null)
            {
                badText = DescribeModifierEvent(track.Apply(roll.Bad));
            }

            SetStageStatusParts(
                (goodText ?? "", new Color("#8FE39B")),
                (badText, new Color("#F08A8A")));
            UpdateActiveModifierList(track, plan);

            await Pause(0.20f);

            if (!await WaitForHostAdvanceNextAsync(epoch))
            {
                return;
            }
        }

        ApplyLobbyModifiers(plan);

        await Pause(0.45f);
    }

    /// <summary>
    /// Pushes the current plan modifiers into the real lobby. Called at the end of the modifier
    /// stage and again immediately before readying up, so manual skips/overrides cannot start
    /// the run with an empty modifier list.
    /// </summary>
    private void ApplyLobbyModifiers(SpinPlan? plan)
    {
        if (!_isHost || _lobby == null || plan == null)
        {
            return;
        }

        try
        {
            _lobby.SetModifiers(SpinRules.CreateMutableModifiers(plan.FinalModifiers));
            MainFile.Logger.Info(
                $"Applied {plan.FinalModifiers.Count} modifier(s) to the run: " +
                $"{string.Join(", ", plan.FinalModifiers.Select(SpinRules.LabelFor))}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not set modifiers: {e.Message}");
        }
    }

    private void ApplyLocalCharacter(string characterId)
    {
        try
        {
            if (_lobby == null)
            {
                return;
            }

            CharacterModel character = ModelDb.GetById<CharacterModel>(ModelId.Deserialize(characterId));
            _lobby.SetLocalCharacter(character);
            _localAssignedCharacterId = characterId;
            MainFile.Logger.Info(
                $"Assigned local character {character.Id.Entry} " +
                $"(lobby now: {_lobby.LocalPlayer.character.Id.Entry}).");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not assign local character: {e.Message}");
        }
    }

    private void SetWheelMode(int wheels)
    {
        _wheelA.Visible = wheels == 2;
        _wheelB.Visible = wheels == 2;
        _wheelSolo.Visible = wheels == 1;
    }

    private static void ConfigureWheel(
        PrizeWheel wheel,
        string title,
        IEnumerable<WheelEntry> entries,
        float diameter)
    {
        wheel.Configure(
            title,
            entries.Select(e => new PrizeWheel.Segment
            {
                Key = e.Key,
                Label = e.Label,
                Color = SpinUi.Hex(e.ColorHex),
                Weight = e.Weight
            }),
            diameter);
    }

    private static void ConfigureWheel(
        PrizeWheel wheel,
        string title,
        IEnumerable<PlayerWheelEntry> entries,
        float diameter)
    {
        wheel.Configure(
            title,
            entries.Select(e => new PrizeWheel.Segment
            {
                Key = e.PlayerId.ToString(),
                Label = e.Name,
                Color = SpinUi.Hex(e.ColorHex),
                Weight = 1
            }),
            diameter);
    }

    private string DescribeModifierEvent(ModifierEvent ev)
    {
        string label = SpinRules.LabelFor(ev.Choice);
        return ev.Change switch
        {
            ModifierChange.Added => $"+ {label}",
            ModifierChange.Removed => $"- {label}",
            ModifierChange.Replaced => ev.Evicted.Count > 0
                ? $"{label} replaces {string.Join(", ", ev.Evicted.Select(SpinRules.LabelFor))}"
                : $"+ {label}",
            _ => label
        };
    }

    private void UpdateActiveModifierList(ModifierTrack track, SpinPlan plan)
    {
        foreach (Node child in _activeModifierColumns.GetChildren())
        {
            child.QueueFree();
        }

        _activeModifierTitle.Text = $"ACTIVE MODIFIERS ({track.Active.Count})";

        if (track.Active.Count == 0)
        {
            _activeModifierPanel.CustomMinimumSize = new Vector2(0f, 56f);
            _activeModifierColumns.AddChild(SpinUi.MakeLabel("None", 22, SpinUi.Muted));
            return;
        }

        _activeModifierPanel.CustomMinimumSize = new Vector2(0f, 250f);

        const int rowsPerColumn = 4;
        VBoxContainer? column = null;
        int row = 0;

        foreach (ModifierChoice choice in track.Active)
        {
            if (row % rowsPerColumn == 0)
            {
                PanelContainer columnPanel = SpinUi.MakeStonePanel(56, 64);
                columnPanel.CustomMinimumSize = new Vector2(360f, 220f);
                columnPanel.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;

                column = new VBoxContainer
                {
                    Alignment = BoxContainer.AlignmentMode.Center,
                    SizeFlagsVertical = SizeFlags.ExpandFill
                };
                column.AddThemeConstantOverride("separation", 2);
                columnPanel.AddChild(column);
                _activeModifierColumns.AddChild(columnPanel);
            }

            bool good = plan.GoodPool.Any(e => e.Key == choice.Key);
            column?.AddChild(SpinUi.MakeLabel(
                $"• {SpinRules.LabelFor(choice)}",
                22,
                good ? new Color("#8FE39B") : new Color("#F08A8A")));
            row++;
        }
    }

    private void ShowSummary(SpinPlan plan)
    {
        ShowPanel(_summaryPanel);
        Visible = true;

        ClearSummarySection(_characterSummary);
        ClearSummarySection(_ascensionSummary);
        ClearSummarySection(_modifierSummary);

        foreach (PlayerAssignment assignment in plan.Assignments.OrderBy(a => a.RevealIndex))
        {
            PlayerWheelEntry? player = plan.Players.FirstOrDefault(p => p.PlayerId == assignment.PlayerId);
            string character = CharacterLabel(assignment.CharacterId);
            AddSectionLine(_characterSummary, $"{(player?.Name ?? "Player")}  →  {character}");
        }

        int ascension = Math.Clamp(plan.Ascension, 0, 10);
        AddSectionLine(_ascensionSummary, SpinRules.AscensionEntries[ascension].Label);
        if (ascension == 0)
        {
            AddSectionLine(_ascensionSummary, "No ascension effects.");
        }
        else
        {
            for (int level = 1; level <= ascension; level++)
            {
                try
                {
                    string description = AscensionHelper.GetDescription(level).GetFormattedText();
                    description = Regex.Replace(description, "\\[[^\\]]*\\]", "").Trim();
                    if (!string.IsNullOrWhiteSpace(description))
                    {
                        AddSectionLine(_ascensionSummary, $"• {description}");
                    }
                }
                catch (Exception e)
                {
                    MainFile.Logger.Warn($"Could not read ascension {level} description: {e.Message}");
                }
            }
        }

        if (plan.FinalModifiers.Count == 0)
        {
            AddSectionLine(_modifierSummary, "None");
        }
        else
        {
            foreach (ModifierChoice choice in plan.FinalModifiers)
            {
                bool good = plan.GoodPool.Any(e => e.Key == choice.Key);
                AddSectionColoredLine(
                    _modifierSummary,
                    SpinRules.LabelFor(choice),
                    good ? new Color("#8FE39B") : new Color("#F08A8A"));
            }
        }

        _voteOptions.Visible = true;
        _rideIcons.Visible = !_isSingleplayer;
        _notIcons.Visible = !_isSingleplayer;
        SpinUi.SetNativeButtonText(_rideButton, "LET IT RIDE");
        SpinUi.SetNativeButtonText(_notButton, "Maybe Not");
        _localVoted = _votes.ContainsKey(_localId);
        _voteSettled = false;
        _resultPlaying = false;
        _pendingReturnToLobby = false;

        _voteStatus.Text = "Embrace your given fate and climb?";
        if (!_isSingleplayer && _isHost)
        {
            BroadcastVoteState();
        }

        RefreshVoteUi();
    }

    private static void ClearSummarySection(VBoxContainer section)
    {
        foreach (Node child in section.GetChildren())
        {
            child.QueueFree();
        }
    }

    private static void AddSectionLine(VBoxContainer section, string text)
    {
        section.AddChild(SpinUi.MakeLabel(text, 20, SpinUi.Cream));
    }

    private static void AddSectionColoredLine(VBoxContainer section, string text, Color color)
    {
        section.AddChild(SpinUi.MakeLabel(text, 20, color));
    }

    private static string CharacterLabel(string characterId)
    {
        try
        {
            CharacterModel character = ModelDb.GetById<CharacterModel>(ModelId.Deserialize(characterId));
            return character.Title.GetFormattedText();
        }
        catch
        {
            return characterId;
        }
    }

    private void BuildVotePanel(out VBoxContainer voteOptions)
    {
        voteOptions = new VBoxContainer();
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 30);
        voteOptions.AddChild(row);

        row.AddChild(BuildVoteOption(
            "Maybe Not",
            OnNotPressed,
            out _notButton,
            out _notIcons,
            redAccent: true));

        row.AddChild(BuildVoteOption(
            "LET IT RIDE",
            OnRidePressed,
            out _rideButton,
            out _rideIcons));

        SpinUi.AttachRareGlowOnHover(_rideButton);
    }

    private static VBoxContainer BuildVoteOption(
        string title,
        Action onPressed,
        out NPauseMenuButton button,
        out HBoxContainer icons,
        bool redAccent = false)
    {
        var vbox = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter
        };
        vbox.AddThemeConstantOverride("separation", 8);

        button = SpinUi.MakeNativeButton(
            vbox, title, onPressed, new Vector2(320f, 80f), 30, redAccent);
        button.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        icons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            CustomMinimumSize = new Vector2(0, 58)
        };
        icons.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(icons);

        return vbox;
    }

    private void OnRidePressed()
    {
        HandleVote(true);
    }

    private void OnNotPressed()
    {
        HandleVote(false);
    }

    private void HandleVote(bool ride)
    {
        if (_isSingleplayer)
        {
            if (ride)
            {
                StartRunLocal();
            }
            else
            {
                RunTask(() => FadeAndReturnToLobbyAsync(broadcast: false));
            }

            return;
        }

        if (_localVoted || _voteSettled || _lobby == null)
        {
            return;
        }

        _localVoted = true;
        _votes[_localId] = ride;
        _rideButton.Disable();
        _notButton.Disable();
        RefreshVoteUi();

        if (_isHost)
        {
            BroadcastVoteState();
            CheckVotesComplete();
        }
        else
        {
            Send(SpinMessageKind.Vote, _localId, ride);
        }
    }

    private void CheckVotesComplete()
    {
        if (!_isHost || _voteSettled || _lobby == null || _plan == null)
        {
            return;
        }

        if (_lobby.Players.Count == 0 || _lobby.Players.Any(p => !_votes.ContainsKey(p.id)))
        {
            return;
        }

        _voteSettled = true;
        _voteStatus.Text = "The Spire has decided.";
        bool split = _votes.Values.Distinct().Count() > 1;
        var entries = _votes.ToList();
        var rng = new Rng(Rng.Chaotic.NextUnsignedLong());
        KeyValuePair<ulong, bool> winner = entries[rng.NextInt(entries.Count)];

        var payload = new VoteResultPayload
        {
            Ride = winner.Value,
            WinnerId = winner.Key,
            Split = split,
            VisualSeed = rng.NextUnsignedLong()
        };

        Send(SpinMessageKind.VoteResult, payload: SpinPayload.Serialize(payload));
        RunTask(() => PlayVoteResultAsync(payload));
    }

    private async Task PlayVoteResultAsync(VoteResultPayload payload)
    {
        if (_resultPlaying)
        {
            return;
        }

        _resultPlaying = true;
        int epoch = _sequenceEpoch;

        try
        {
            if (payload.Split)
            {
                await RunSplitVoteTickerAsync(payload, epoch);
            }
            else
            {
                HighlightVoteIcon(null);
                await Pause(0.15f);
            }

            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            BounceVotePanel(payload.Ride);
            await Pause(0.45f);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Vote result animation failed: {e.Message}");
        }
        finally
        {
            _resultPlaying = false;
        }

        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            if (_pendingReturnToLobby && !_disconnected)
            {
                _pendingReturnToLobby = false;
                RunTask(() => FadeAndReturnToLobbyAsync(broadcast: false));
            }

            return;
        }

        if (payload.Ride)
        {
            StartRunLocal();
        }
        else if (_isHost)
        {
            RunTask(() => FadeAndReturnToLobbyAsync(broadcast: true));
        }
        else
        {
            _pendingReturnToLobby = false;
            RunTask(() => FadeAndReturnToLobbyAsync(broadcast: false));
        }
    }

    private async Task RunSplitVoteTickerAsync(VoteResultPayload payload, int epoch)
    {
        var ordered = _voteIcons
            .OrderBy(v => PlayerOrder(v.PlayerId))
            .ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        int winnerIndex = ordered.FindIndex(v => v.PlayerId == payload.WinnerId);
        if (winnerIndex < 0)
        {
            winnerIndex = 0;
        }

        int ticks = 14;
        float step = 1.05f / ticks;
        int count = ordered.Count;

        for (int i = 0; i <= ticks; i++)
        {
            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            int index = ((winnerIndex - (ticks - i)) % count + count) % count;
            HighlightVoteIcon(ordered[index]);
            PlayVoteTick();
            await Pause(step);
        }

        await Pause(0.18f);
        HighlightVoteIcon(null);
    }

    private void HighlightVoteIcon(VoteIconView? view)
    {
        if (_highlightedVoteIcon != null && GodotObject.IsInstanceValid(_highlightedVoteIcon.Icon))
        {
            _highlightedVoteIcon.Icon.Scale = _highlightedVoteIcon.BaseScale;
            _highlightedVoteIcon.Icon.Modulate = Colors.White;
        }

        _highlightedVoteIcon = view;

        if (view != null && GodotObject.IsInstanceValid(view.Icon))
        {
            view.Icon.Scale = view.BaseScale * 1.3f;
            view.Icon.Modulate = SpinUi.Gold;
        }
    }

    private void BounceVotePanel(bool ride)
    {
        HBoxContainer row = ride ? _rideIcons : _notIcons;
        foreach (Node child in row.GetChildren())
        {
            if (child is not TextureRect icon || !GodotObject.IsInstanceValid(icon))
            {
                continue;
            }

            icon.Scale = Vector2.One;
            var tween = CreateTween();
            tween.TweenProperty(icon, "scale", new Vector2(1.35f, 1.35f), 0.12f)
                .SetTrans(Tween.TransitionType.Back)
                .SetEase(Tween.EaseType.Out);
            tween.TweenProperty(icon, "scale", Vector2.One, 0.20f)
                .SetTrans(Tween.TransitionType.Bounce)
                .SetEase(Tween.EaseType.Out);
        }
    }

    private void PlayVoteTick()
    {
        try
        {
            NDebugAudioManager.Instance?.Play("map_split_tick.mp3", 0.15f, PitchVariance.Small);
        }
        catch
        {
            // Cosmetic.
        }
    }

    private void RefreshVoteUi()
    {
        if (_rideIcons == null || _notIcons == null || _resultPlaying)
        {
            return;
        }

        foreach (Node child in _rideIcons.GetChildren())
        {
            child.QueueFree();
        }

        foreach (Node child in _notIcons.GetChildren())
        {
            child.QueueFree();
        }

        _voteIcons.Clear();
        _highlightedVoteIcon = null;

        foreach ((ulong playerId, bool ride) in _votes.OrderBy(v => PlayerOrder(v.Key)))
        {
            CharacterModel? character = ResolveCharacterForPlayer(playerId);
            if (character == null)
            {
                continue;
            }

            TextureRect? icon = SpinUi.MakeVoteIcon(character, 52f);
            if (icon == null)
            {
                continue;
            }

            HBoxContainer target = ride ? _rideIcons : _notIcons;
            target.AddChild(icon);
            icon.PivotOffset = new Vector2(26f, 26f);
            icon.Scale = Vector2.One;

            _voteIcons.Add(new VoteIconView
            {
                PlayerId = playerId,
                Ride = ride,
                Icon = icon,
                BaseScale = Vector2.One
            });
        }

        _localVoted = _votes.ContainsKey(_localId);
        _rideButton.SetEnabled(!_localVoted && !_voteSettled);
        _notButton.SetEnabled(!_localVoted && !_voteSettled);

        if (_isSingleplayer)
        {
            _voteStatus.Text = "Embrace your given fate and climb?";
        }
        else if (_voteSettled)
        {
            _voteStatus.Text = "The Spire has decided.";
        }
        else if (_votes.Count == 0)
        {
            _voteStatus.Text = "Embrace your given fate and climb?";
        }
        else
        {
            int total = _lobby?.Players.Count ?? 0;
            _voteStatus.Text = $"Votes: {_votes.Count} / {total}";
        }
    }

    private CharacterModel? ResolveCharacterForPlayer(ulong playerId)
    {
        try
        {
            PlayerAssignment? assignment = _plan?.Assignments.FirstOrDefault(a => a.PlayerId == playerId);
            if (assignment != null)
            {
                return ModelDb.GetById<CharacterModel>(ModelId.Deserialize(assignment.CharacterId));
            }

            StartRunLobbyPlayer? player = _lobby?.Players.FirstOrDefault(p => p.id == playerId);
            return player?.character;
        }
        catch
        {
            return null;
        }
    }

    private int PlayerOrder(ulong playerId)
    {
        if (_lobby == null)
        {
            return int.MaxValue;
        }

        List<StartRunLobbyPlayer> players = _lobby.Players.OrderBy(p => p.slotId).ToList();
        int index = players.FindIndex(p => p.id == playerId);
        return index < 0 ? int.MaxValue : index;
    }

    private void StartRunLocal()
    {
        try
        {
            _startingRun = true;

            // Re-assert the spun character right before readying up: the vanilla screen reads
            // _lobby.LocalPlayer.character for both the transition animation and the run itself.
            if (_plan != null && _lobby != null)
            {
                PlayerAssignment? assignment =
                    _plan.Assignments.FirstOrDefault(a => a.PlayerId == _lobby.LocalPlayer.id);

                if (assignment == null && _plan.Assignments.Count == 1)
                {
                    assignment = _plan.Assignments[0];
                }

                if (assignment != null)
                {
                    ApplyLocalCharacter(assignment.CharacterId);
                }
                else if (_localAssignedCharacterId != null)
                {
                    ApplyLocalCharacter(_localAssignedCharacterId);
                }

                MainFile.Logger.Info(
                    $"Starting run as {_lobby.LocalPlayer.character.Id.Entry} " +
                    $"(transition: {_lobby.LocalPlayer.character.CharacterSelectTransitionPath}).");
                MainFile.Logger.Info(
                    $"Run plan: ascension={_plan.Ascension}, " +
                    $"characters={string.Join(", ", _plan.Assignments.Select(a => a.CharacterId))}, " +
                    $"modifiers={_plan.FinalModifiers.Count}.");
            }

            ApplyLobbyModifiers(_plan);
            _lobby?.SetReady(true);
        }
        catch (Exception e)
        {
            _startingRun = false;
            MainFile.Logger.Warn($"Could not start run: {e.Message}");
        }
    }

    private async Task Pause(float seconds)
    {
        if (!IsInsideTree())
        {
            return;
        }

        await ToSignal(GetTree().CreateTimer(Mathf.Max(0f, seconds)), SceneTreeTimer.SignalName.Timeout);
    }
}
