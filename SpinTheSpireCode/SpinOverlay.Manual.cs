using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Random;

namespace SpinTheSpire.SpinTheSpireCode;

public sealed partial class SpinOverlay
{
    // Testing flag: keep the re-spin confirmation prompt even in singleplayer.
    // Disabled for release: singleplayer resets/skips immediately.
    private const bool ShowPromptInSingleplayer = false;

    private enum SpinSection
    {
        Characters = 1,
        Ascension = 2,
        Modifiers = 3
    }

    private enum ManualAction
    {
        Spin,
        Next
    }

    private NPauseMenuButton _manualSpinButton = null!;
    private SpinSection _manualSection = SpinSection.Characters;
    private TaskCompletionSource<ManualAction>? _manualActionTcs;
    private int _pendingManualSpins;
    private bool _pendingManualNext;
    private bool _manualAnimating;

    private Control _promptPanel = null!;
    private Label _promptTitle = null!;
    private Label _promptBody = null!;
    private NPauseMenuButton _promptNoButton = null!;
    private NPauseMenuButton _promptYesButton = null!;
    private HBoxContainer _promptNoIcons = null!;
    private HBoxContainer _promptYesIcons = null!;
    private Label _promptStatus = null!;
    private readonly Dictionary<ulong, bool> _promptVotes = new();
    private readonly List<VoteIconView> _promptIcons = new();
    private VoteIconView? _highlightedPromptIcon;
    private bool _promptOpen;
    private bool _promptSettled;
    private bool _promptResultPlaying;
    private bool _promptAdvance;
    private SpinSection _promptSection;

    private Control BuildPromptPanel()
    {
        var root = new Control
        {
            Name = "PromptPanel",
            Visible = false,
            MouseFilter = MouseFilterEnum.Stop
        };
        SpinUi.SetFullRect(root);

        var dim = new ColorRect
        {
            Color = new Color(0.01f, 0.01f, 0.02f, 0.78f),
            MouseFilter = MouseFilterEnum.Stop
        };
        SpinUi.SetFullRect(dim);
        root.AddChild(dim);

        var center = new CenterContainer();
        SpinUi.SetFullRect(center);
        root.AddChild(center);

        var panel = new Control { CustomMinimumSize = new Vector2(716f, 816f) };
        center.AddChild(panel);

        Texture2D? popupTexture = null;
        try
        {
            popupTexture = GD.Load<Texture2D>(
                "res://images/atlases/ui_atlas.sprites/popup_vertical.tres");
        }
        catch
        {
            // Fall through to the flat fallback background.
        }

        if (popupTexture != null)
        {
            var background = new TextureRect
            {
                Texture = popupTexture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore
            };
            SpinUi.SetFullRect(background);

            try
            {
                Shader? hsv = GD.Load<Shader>("res://shaders/hsv.gdshader");
                if (hsv != null)
                {
                    var material = new ShaderMaterial { Shader = hsv };
                    material.SetShaderParameter("h", 0.505f);
                    material.SetShaderParameter("s", 1.0f);
                    material.SetShaderParameter("v", 0.75f);
                    background.Material = material;
                }
            }
            catch
            {
                // A brown popup is better than a broken prompt.
            }

            panel.AddChild(background);
        }
        else
        {
            var fallback = new ColorRect
            {
                Color = new Color(0.09f, 0.15f, 0.21f, 0.98f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            SpinUi.SetFullRect(fallback);
            panel.AddChild(fallback);
        }

        var margin = new MarginContainer();
        SpinUi.SetFullRect(margin);
        margin.AddThemeConstantOverride("margin_left", 64);
        margin.AddThemeConstantOverride("margin_right", 64);
        margin.AddThemeConstantOverride("margin_top", 48);
        margin.AddThemeConstantOverride("margin_bottom", 40);
        panel.AddChild(margin);

        var vbox = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        vbox.AddThemeConstantOverride("separation", 14);
        margin.AddChild(vbox);

        _promptTitle = SpinUi.MakeTitle("SPIN AGAIN?", 38);
        vbox.AddChild(_promptTitle);

        _promptBody = SpinUi.MakeLabel(
            "",
            24,
            SpinUi.Cream,
            alignment: HorizontalAlignment.Center);
        _promptBody.CustomMinimumSize = new Vector2(0f, 160f);
        _promptBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_promptBody);

        var buttons = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        buttons.AddThemeConstantOverride("separation", 28);
        vbox.AddChild(buttons);

        buttons.AddChild(BuildPromptOption(
            "Nevermind",
            OnPromptNoPressed,
            out _promptNoButton,
            out _promptNoIcons,
            redAccent: true));

        buttons.AddChild(BuildPromptOption(
            "Let me Spin!",
            OnPromptYesPressed,
            out _promptYesButton,
            out _promptYesIcons,
            goldAccent: true));

        _promptStatus = SpinUi.MakeLabel(
            "Everyone must choose.",
            20,
            SpinUi.Muted,
            alignment: HorizontalAlignment.Center);
        vbox.AddChild(_promptStatus);

        return root;
    }

    private static VBoxContainer BuildPromptOption(
        string title,
        Action onPressed,
        out NPauseMenuButton button,
        out HBoxContainer icons,
        bool redAccent = false,
        bool goldAccent = false)
    {
        var vbox = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter
        };
        vbox.AddThemeConstantOverride("separation", 8);

        button = SpinUi.MakeNativeButton(
            vbox, title, onPressed, new Vector2(280f, 76f), 28, redAccent, goldAccent);
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

    private async Task<ManualAction?> WaitManualActionAsync(int epoch)
    {
        if (epoch != _sequenceEpoch || _abortRequested || _disconnected || _plan == null)
        {
            return null;
        }

        if (_pendingManualSpins > 0)
        {
            _pendingManualSpins--;
            return ManualAction.Spin;
        }

        if (_pendingManualNext)
        {
            _pendingManualNext = false;
            return ManualAction.Next;
        }

        var tcs = new TaskCompletionSource<ManualAction>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _manualActionTcs = tcs;
        UpdateManualSectionButtons();
        ManualAction action = await tcs.Task;
        if (_manualActionTcs == tcs)
        {
            _manualActionTcs = null;
        }

        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            return null;
        }

        return action;
    }

    private void ResolveManualAction(ManualAction action)
    {
        TaskCompletionSource<ManualAction>? tcs = _manualActionTcs;
        if (tcs == null)
        {
            if (action == ManualAction.Spin)
            {
                _pendingManualSpins++;
            }
            else
            {
                _pendingManualNext = true;
            }

            return;
        }

        _manualActionTcs = null;
        tcs.TrySetResult(action);
    }

    private bool ManualSectionCanAdvance()
    {
        if (_plan == null)
        {
            return false;
        }

        return _manualSection switch
        {
            SpinSection.Characters => _plan.RevealsDone >= _plan.Assignments.Count,
            SpinSection.Ascension => _plan.Ascension >= 0,
            SpinSection.Modifiers => true,
            _ => false
        };
    }

    private void SetManualAnimating(bool value)
    {
        _manualAnimating = value;
        UpdateManualSectionButtons();
    }

    private void UpdateManualSectionButtons()
    {
        if (_plan == null || !_plan.ManualRolls)
        {
            if (_manualSpinButton != null && IsInstanceValid(_manualSpinButton))
            {
                _manualSpinButton.Visible = false;
            }

            return;
        }

        _manualSpinButton.Visible = true;
        bool spinEnabled =
            _isHost && _spinStarted && !_promptOpen && !_manualAnimating && _manualActionTcs != null;
        _manualSpinButton.SetEnabled(spinEnabled);
        _manualSpinButton.Modulate = spinEnabled
            ? Colors.White
            : new Color(1f, 1f, 1f, 0.5f);
        _nextButton.SetEnabled(
            _isHost && !_promptOpen && !_manualAnimating && ManualSectionCanAdvance());
    }

    private void OnManualSpinPressed()
    {
        if (!_isHost || !_spinStarted || _plan == null || !_plan.ManualRolls
            || _promptOpen || _manualAnimating || _manualActionTcs == null)
        {
            return;
        }

        bool multiplayer = (_lobby?.Players.Count ?? 0) > 1;

        switch (_manualSection)
        {
            case SpinSection.Characters:
                if (_plan.RevealsDone >= _plan.Assignments.Count)
                {
                    if (ShowPromptInSingleplayer || multiplayer)
                    {
                        OpenPrompt(_manualSection);
                    }
                    else
                    {
                        ResetCharactersAndSpin();
                    }
                }
                else
                {
                    PerformCharacterSpin();
                }

                break;

            case SpinSection.Ascension:
                if (_plan.Ascension >= 0)
                {
                    if (ShowPromptInSingleplayer || multiplayer)
                    {
                        OpenPrompt(_manualSection);
                    }
                    else
                    {
                        ResetAscensionAndSpin();
                    }
                }
                else
                {
                    PerformAscensionSpin();
                }

                break;

            case SpinSection.Modifiers:
                int target = _plan.TargetRolls;
                if (target > 0 && _plan.Rolls.Count >= target
                    && (ShowPromptInSingleplayer || multiplayer))
                {
                    OpenPrompt(_manualSection);
                }
                else
                {
                    AppendModifierSpin();
                }

                break;
        }
    }

    private void PerformCharacterSpin()
    {
        if (_plan == null || _plan.RevealsDone >= _plan.Assignments.Count)
        {
            return;
        }

        _plan.RevealsDone++;
        SetManualAnimating(true);
        BroadcastManualPlan(reset: false);
        ResolveManualAction(ManualAction.Spin);
    }

    private void ResetCharactersAndSpin()
    {
        if (_plan == null)
        {
            return;
        }

        SpinRules.RebuildCharacterAssignments(_plan);
        ApplyDefaultLobbyCharacter();
        _localAssignedCharacterId = null;
        _plan.RevealsDone = Math.Min(1, _plan.Assignments.Count);
        SetManualAnimating(true);
        BroadcastManualPlan(reset: true);
        ResolveManualAction(ManualAction.Spin);
    }

    private void PerformAscensionSpin()
    {
        if (_plan == null)
        {
            return;
        }

        _plan.Ascension = new Rng(Rng.Chaotic.NextUnsignedLong()).NextInt(0, 11);
        _plan.AscensionChosen = true;
        _plan.TargetRolls = SpinRules.TargetRolls(_plan);
        SetManualAnimating(true);
        BroadcastManualPlan(reset: false);
        ResolveManualAction(ManualAction.Spin);
    }

    private void ResetAscensionAndSpin()
    {
        if (_plan == null)
        {
            return;
        }

        _plan.Ascension = -1;
        _plan.AscensionChosen = false;
        _plan.Ascension = new Rng(Rng.Chaotic.NextUnsignedLong()).NextInt(0, 11);
        _plan.AscensionChosen = true;
        _plan.TargetRolls = SpinRules.TargetRolls(_plan);
        SetManualAnimating(true);
        BroadcastManualPlan(reset: true);
        ResolveManualAction(ManualAction.Spin);
    }

    private void AppendModifierSpin()
    {
        if (_plan == null)
        {
            return;
        }

        SpinRules.AppendModifierRoll(_plan);
        SetManualAnimating(true);
        BroadcastManualPlan(reset: false);
        ResolveManualAction(ManualAction.Spin);
    }

    private void BroadcastManualPlan(bool reset)
    {
        if (_plan == null)
        {
            return;
        }

        SpinSession.ActivePlan = _plan;
        MainFile.Logger.Info(
            $"Manual spin sync: section={_manualSection} reset={reset} " +
            $"reveals={_plan.RevealsDone}/{_plan.Assignments.Count} " +
            $"rolls={_plan.Rolls.Count}/{_plan.TargetRolls}");
        Send(
            SpinMessageKind.ManualSpin,
            number: (int)_manualSection,
            flag: reset,
            payload: _plan.ToJson());
    }

    private void HandleManualSpinMessage(SpinMessage message)
    {
        if (_isHost || !_spinStarted)
        {
            return;
        }

        SpinPlan? plan = SpinPlan.FromJson(message.Payload);
        if (plan == null)
        {
            MainFile.Logger.Warn("Received an invalid manual spin plan.");
            return;
        }

        if (_plan != null)
        {
            _plan.CopyFrom(plan);
        }
        else
        {
            _plan = plan;
        }

        SpinSession.ActivePlan = _plan;

        if (message.Flag && (SpinSection)message.Number == SpinSection.Characters)
        {
            // The host restarted character assignment: every client resets its own lobby pick.
            ApplyDefaultLobbyCharacter();
            _localAssignedCharacterId = null;
        }

        _manualSection = (SpinSection)message.Number;
        SetManualAnimating(true);

        if (_promptOpen)
        {
            _pendingManualSpins++;
            return;
        }

        ResolveManualAction(ManualAction.Spin);
    }

    private void OpenPrompt(SpinSection section, bool advance = false)
    {
        if (!_isHost || _promptOpen || _lobby == null)
        {
            return;
        }

        _promptSection = section;
        _promptAdvance = advance;
        _promptOpen = true;
        _promptSettled = false;
        _promptResultPlaying = false;
        _promptVotes.Clear();
        _highlightedPromptIcon = null;
        _promptTitle.Text = advance ? "PROCEED?" : "SPIN AGAIN?";
        _promptBody.Text = advance
            ? "Not all spins have been made. Proceed?"
            : section switch
            {
                SpinSection.Characters =>
                    "All characters have been already assigned. Spinning again will reset the assignment. Proceed?",
                SpinSection.Ascension =>
                    "Ascension level has already been chosen. Spinning again will reroll it. Proceed?",
                SpinSection.Modifiers =>
                    "All modifiers spins are complete. Spinning again will add more than the limit. Proceed?",
                _ => ""
            };
        _promptStatus.Text = "Everyone must choose.";
        _promptNoButton.Enable();
        _promptYesButton.Enable();
        SpinUi.SetNativeButtonText(_promptYesButton, advance ? "Skip" : "Let me Spin!");
        RefreshPromptVoteUi();
        _promptPanel.Visible = true;
        UpdateManualSectionButtons();
        MainFile.Logger.Info(
            advance
                ? $"Advance prompt opened for {section} (rolls below target)."
                : $"Re-spin prompt opened for {section}.");
        Send(SpinMessageKind.PromptOpen, number: (int)section, flag: advance);
    }

    private void HandlePromptOpenMessage(int section, bool advance)
    {
        if (_isHost || !_spinStarted)
        {
            return;
        }

        _manualSection = (SpinSection)section;
        _promptSection = (SpinSection)section;
        _promptAdvance = advance;
        _promptOpen = true;
        _promptSettled = false;
        _promptResultPlaying = false;
        _promptVotes.Clear();
        _highlightedPromptIcon = null;
        _promptTitle.Text = advance ? "PROCEED?" : "SPIN AGAIN?";
        _promptBody.Text = advance
            ? "Not all spins have been made. Proceed?"
            : (SpinSection)section switch
            {
                SpinSection.Characters =>
                    "All characters have been already assigned. Spinning again will reset the assignment. Proceed?",
                SpinSection.Ascension =>
                    "Ascension level has already been chosen. Spinning again will reroll it. Proceed?",
                SpinSection.Modifiers =>
                    "All modifiers spins are complete. Spinning again will add more than the limit. Proceed?",
                _ => ""
            };
        _promptStatus.Text = "Everyone must choose.";
        _promptNoButton.Enable();
        _promptYesButton.Enable();
        SpinUi.SetNativeButtonText(_promptYesButton, advance ? "Skip" : "Let me Spin!");
        RefreshPromptVoteUi();
        _promptPanel.Visible = true;
        UpdateManualSectionButtons();
    }

    private void OnPromptNoPressed()
    {
        VotePrompt(false);
    }

    private void OnPromptYesPressed()
    {
        VotePrompt(true);
    }

    private void VotePrompt(bool accept)
    {
        if (!_promptOpen || _promptSettled || _lobby == null || _promptVotes.ContainsKey(_localId))
        {
            return;
        }

        _promptVotes[_localId] = accept;
        _promptNoButton.Disable();
        _promptYesButton.Disable();
        RefreshPromptVoteUi();

        if (_isHost)
        {
            BroadcastPromptState();
            CheckPromptComplete();
        }
        else
        {
            Send(SpinMessageKind.PromptVote, _localId, accept);
        }
    }

    private void HandlePromptVoteMessage(SpinMessage message)
    {
        if (!_isHost || !_promptOpen || _promptSettled)
        {
            return;
        }

        _promptVotes[message.PlayerId] = message.Flag;
        BroadcastPromptState();
        RefreshPromptVoteUi();
        CheckPromptComplete();
    }

    private void BroadcastPromptState()
    {
        var payload = new VoteStatePayload
        {
            Votes = _promptVotes.Select(v => new VoteEntry { PlayerId = v.Key, Ride = v.Value }).ToList()
        };
        Send(SpinMessageKind.PromptState, payload: SpinPayload.Serialize(payload));
    }

    private void HandlePromptStateMessage(string json)
    {
        if (_isHost || !_promptOpen || _promptSettled)
        {
            return;
        }

        var payload = SpinPayload.Deserialize<VoteStatePayload>(json);
        if (payload == null)
        {
            return;
        }

        _promptVotes.Clear();
        foreach (VoteEntry entry in payload.Votes)
        {
            _promptVotes[entry.PlayerId] = entry.Ride;
        }

        RefreshPromptVoteUi();
    }

    private void CheckPromptComplete()
    {
        if (!_isHost || _promptSettled || !_promptOpen || _lobby == null)
        {
            return;
        }

        if (_lobby.Players.Count == 0 || _lobby.Players.Any(p => !_promptVotes.ContainsKey(p.id)))
        {
            return;
        }

        _promptSettled = true;
        _promptStatus.Text = "The Spire has decided.";
        bool split = _promptVotes.Values.Distinct().Count() > 1;
        var entries = _promptVotes.ToList();
        var rng = new Rng(Rng.Chaotic.NextUnsignedLong());
        KeyValuePair<ulong, bool> winner = entries[rng.NextInt(entries.Count)];

        var payload = new VoteResultPayload
        {
            Ride = winner.Value,
            WinnerId = winner.Key,
            Split = split,
            VisualSeed = rng.NextUnsignedLong()
        };

        MainFile.Logger.Info(
            $"Re-spin prompt {_promptSection}: {(winner.Value ? "Let me Spin!" : "Nevermind")} " +
            $"(split={split}, votes={entries.Count}).");
        Send(SpinMessageKind.PromptResult, payload: SpinPayload.Serialize(payload));
        RunTask(() => PlayPromptResultAsync(payload));
    }

    private void HandlePromptResultMessage(string json)
    {
        if (_isHost || !_promptOpen)
        {
            return;
        }

        var payload = SpinPayload.Deserialize<VoteResultPayload>(json);
        if (payload == null)
        {
            return;
        }

        _promptSettled = true;
        _promptNoButton.Disable();
        _promptYesButton.Disable();
        _promptStatus.Text = "The Spire has decided.";
        RunTask(() => PlayPromptResultAsync(payload));
    }

    private async Task PlayPromptResultAsync(VoteResultPayload payload)
    {
        if (_promptResultPlaying)
        {
            return;
        }

        _promptResultPlaying = true;
        int epoch = _sequenceEpoch;
        SpinSection section = _promptSection;
        bool advance = _promptAdvance;

        try
        {
            if (payload.Split)
            {
                await RunPromptTickerAsync(payload, epoch);
            }
            else
            {
                HighlightPromptIcon(null);
                await Pause(0.15f);
            }

            if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
            {
                return;
            }

            await Pause(0.35f);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Prompt result animation failed: {e.Message}");
        }
        finally
        {
            _promptResultPlaying = false;
        }

        if (epoch != _sequenceEpoch || _abortRequested || _disconnected)
        {
            return;
        }

        ClosePrompt();

        if (payload.Ride)
        {
            if (advance)
            {
                ResolveManualAction(ManualAction.Next);
            }
            else if (_isHost)
            {
                switch (section)
                {
                    case SpinSection.Characters:
                        ResetCharactersAndSpin();
                        break;
                    case SpinSection.Ascension:
                        ResetAscensionAndSpin();
                        break;
                    case SpinSection.Modifiers:
                        AppendModifierSpin();
                        break;
                }
            }
        }
    }

    private async Task RunPromptTickerAsync(VoteResultPayload payload, int epoch)
    {
        var ordered = _promptIcons.OrderBy(v => PlayerOrder(v.PlayerId)).ToList();
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
            HighlightPromptIcon(ordered[index]);
            PlayVoteTick();
            await Pause(step);
        }

        await Pause(0.18f);
        HighlightPromptIcon(null);
    }

    private void HighlightPromptIcon(VoteIconView? view)
    {
        if (_highlightedPromptIcon != null
            && GodotObject.IsInstanceValid(_highlightedPromptIcon.Icon))
        {
            _highlightedPromptIcon.Icon.Scale = _highlightedPromptIcon.BaseScale;
            _highlightedPromptIcon.Icon.Modulate = Colors.White;
        }

        _highlightedPromptIcon = view;

        if (view != null && GodotObject.IsInstanceValid(view.Icon))
        {
            view.Icon.Scale = view.BaseScale * 1.3f;
            view.Icon.Modulate = SpinUi.Gold;
        }
    }

    private void RefreshPromptVoteUi()
    {
        if (!IsInstanceValid(_promptNoIcons) || !IsInstanceValid(_promptYesIcons))
        {
            return;
        }

        foreach (Node child in _promptNoIcons.GetChildren())
        {
            child.QueueFree();
        }

        foreach (Node child in _promptYesIcons.GetChildren())
        {
            child.QueueFree();
        }

        _promptIcons.Clear();
        _highlightedPromptIcon = null;

        foreach ((ulong playerId, bool accept) in _promptVotes.OrderBy(v => PlayerOrder(v.Key)))
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

            HBoxContainer target = accept ? _promptYesIcons : _promptNoIcons;
            target.AddChild(icon);
            icon.PivotOffset = new Vector2(26f, 26f);
            icon.Scale = Vector2.One;

            _promptIcons.Add(new VoteIconView
            {
                PlayerId = playerId,
                Ride = accept,
                Icon = icon,
                BaseScale = Vector2.One
            });
        }

        bool voted = _promptVotes.ContainsKey(_localId);
        _promptNoButton.SetEnabled(!voted && !_promptSettled);
        _promptYesButton.SetEnabled(!voted && !_promptSettled);

        if (!_promptSettled)
        {
            int total = _lobby?.Players.Count ?? 0;
            _promptStatus.Text = _promptVotes.Count == 0
                ? "Everyone must choose."
                : $"Votes: {_promptVotes.Count} / {total}";
        }
    }

    private void ClosePrompt()
    {
        _promptOpen = false;
        _promptPanel.Visible = false;
        _promptVotes.Clear();

        if (_highlightedPromptIcon != null
            && GodotObject.IsInstanceValid(_highlightedPromptIcon.Icon))
        {
            _highlightedPromptIcon.Icon.Scale = _highlightedPromptIcon.BaseScale;
            _highlightedPromptIcon.Icon.Modulate = Colors.White;
        }

        _highlightedPromptIcon = null;
        _promptIcons.Clear();

        if (_pendingManualSpins > 0 && _manualActionTcs != null && !_disconnected)
        {
            _pendingManualSpins--;
            ResolveManualAction(ManualAction.Spin);
        }

        UpdateManualSectionButtons();
    }

    private void ResetManualUi()
    {
        TaskCompletionSource<ManualAction>? tcs = _manualActionTcs;
        _manualActionTcs = null;
        tcs?.TrySetResult(ManualAction.Next);
        _pendingManualSpins = 0;
        _pendingManualNext = false;
        _manualAnimating = false;
        _promptOpen = false;
        _promptSettled = false;
        _promptResultPlaying = false;
        _promptAdvance = false;
        _promptVotes.Clear();

        if (_promptPanel != null && IsInstanceValid(_promptPanel))
        {
            _promptPanel.Visible = false;
        }

        if (_manualSpinButton != null && IsInstanceValid(_manualSpinButton))
        {
            _manualSpinButton.Visible = false;
            _manualSpinButton.SetEnabled(false);
        }

        if (_nextButton != null && IsInstanceValid(_nextButton))
        {
            _nextButton.SetEnabled(false);
        }
    }
}
