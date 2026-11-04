using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace SpinTheSpire.SpinTheSpireCode;

/// <summary>
/// Full-screen replacement/overlay for the custom-run lobby. The vanilla NCustomRunScreen
/// stays underneath as the network/lobby engine; this overlay owns the visible flow.
/// </summary>
public sealed partial class SpinOverlay : Control
{
    private NCustomRunScreen _screen = null!;
    private NPauseMenuButton? _spinButton;
    private StartRunLobby? _lobby;

    private bool _bound;
    private bool _isHost;
    private bool _isSingleplayer;
    private bool _spinStarted;
    private bool _showingVanilla;
    private bool _disconnected;
    private bool _abortRequested;
    private bool _unique;
    private bool _registered;
    private bool _returning;
    private bool _startingRun;
    private ulong _localId;
    private string? _localAssignedCharacterId;
    private float _pollTimer;
    private int _sequenceEpoch;
    private SpinPlan? _plan;

    private readonly Dictionary<ulong, bool> _ready = new();
    private readonly Dictionary<ulong, bool> _votes = new();
    private readonly Dictionary<Node, bool> _vanillaVisibility = new();

    private Control _lobbyPanel = null!;
    private Control _spinPanel = null!;
    private Control _summaryPanel = null!;
    private Control _disconnectedPanel = null!;

    private NRemoteLobbyPlayerContainer? _mpPlayers;
    private VBoxContainer _spPlayers = null!;
    private readonly List<NRemoteLobbyPlayer> _spRows = new();
    private string _playerSignature = "";
    private Control _uniqueTickbox = null!;
    private Control _uniqueRow = null!;
    private Control? _uniqueTicked;
    private Control? _uniqueUnticked;
    private NPauseMenuButton _readyButton = null!;
    private NPauseMenuButton _startButton = null!;
    private NPauseMenuButton _inviteButton = null!;
    private NPauseMenuButton _vanillaButton = null!;
    private NPauseMenuButton _leaveButton = null!;
    private Label _lobbyStatus = null!;

    private ColorRect _fade = null!;

    private Label _stageTitle = null!;
    private Label _stageProgress = null!;
    private HBoxContainer _stageStatus = null!;
    private NPauseMenuButton _nextButton = null!;
    private NPauseMenuButton _cancelButton = null!;
    private TaskCompletionSource<bool>? _advanceTcs;
    private int _advanceReceived;
    private int _advanceWaitStage;
    private int _advanceStageCounter;
    private HBoxContainer _wheelRow = null!;
    private PrizeWheel _wheelA = null!;
    private PrizeWheel _wheelB = null!;
    private PrizeWheel _wheelSolo = null!;
    private VBoxContainer _activeModifierPanel = null!;
    private Label _activeModifierTitle = null!;
    private HBoxContainer _activeModifierColumns = null!;

    private HBoxContainer _summarySections = null!;
    private VBoxContainer _characterSummary = null!;
    private VBoxContainer _ascensionSummary = null!;
    private VBoxContainer _modifierSummary = null!;
    private VBoxContainer _voteOptions = null!;
    private NPauseMenuButton _rideButton = null!;
    private NPauseMenuButton _notButton = null!;
    private HBoxContainer _rideIcons = null!;
    private HBoxContainer _notIcons = null!;
    private Label _voteStatus = null!;
    private bool _localVoted;
    private bool _voteSettled;
    private bool _resultPlaying;

    private readonly List<VoteIconView> _voteIcons = new();
    private VoteIconView? _highlightedVoteIcon;

    public bool IsBound => _bound;

    /// <summary>True while the overlay is showing the untouched vanilla custom-run UI.</summary>
    public bool ShowsVanilla => _showingVanilla;

    private sealed class VoteIconView
    {
        public ulong PlayerId;
        public bool Ride;
        public TextureRect Icon = null!;
        public Vector2 BaseScale = Vector2.One;
    }

    internal void Initialize(NCustomRunScreen screen, NPauseMenuButton? spinButton)
    {
        try
        {
            _screen = screen;
            _spinButton = spinButton;
            BuildUi();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not build Spin the Spire overlay: {e}");
        }
    }

    private void BuildUi()
    {
        Name = "SpinTheSpireOverlay";
        SpinUi.SetFullRect(this);
        MouseFilter = MouseFilterEnum.Stop;
        Visible = false;

        var dim = new ColorRect
        {
            Color = new Color(0.015f, 0.015f, 0.025f, 0.35f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        SpinUi.SetFullRect(dim);
        AddChild(dim);

        _lobbyPanel = BuildLobbyPanel();
        _spinPanel = BuildSpinPanel();
        _summaryPanel = BuildSummaryPanel();
        _disconnectedPanel = BuildDisconnectedPanel();
        _promptPanel = BuildPromptPanel();

        AddChild(_lobbyPanel);
        AddChild(_spinPanel);
        AddChild(_summaryPanel);
        AddChild(_disconnectedPanel);
        AddChild(_promptPanel);

        _fade = new ColorRect
        {
            Color = Colors.Black,
            MouseFilter = MouseFilterEnum.Ignore,
            Modulate = new Color(1, 1, 1, 0)
        };
        SpinUi.SetFullRect(_fade);
        AddChild(_fade);

        ShowPanel(_lobbyPanel);
    }

    private Control BuildLobbyPanel()
    {
        var root = new Control { Name = "LobbyPanel" };
        SpinUi.SetFullRect(root);

        var center = new CenterContainer();
        SpinUi.SetFullRect(center);
        root.AddChild(center);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(980, 0),
            Alignment = BoxContainer.AlignmentMode.Center
        };
        vbox.AddThemeConstantOverride("separation", 10);
        center.AddChild(vbox);

        vbox.AddChild(SpinUi.MakeTitle("SPIN THE SPIRE", 58));
        vbox.AddChild(SpinUi.MakeLabel(
            "Fortune favors the bold ... and the fool.",
            24,
            SpinUi.Muted,
            alignment: HorizontalAlignment.Center));

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        var playersPanel = SpinUi.MakeStonePanel();
        playersPanel.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        playersPanel.CustomMinimumSize = new Vector2(660, 240);

        PackedScene? remotePlayersScene = GD.Load<PackedScene>(
            "res://scenes/screens/remote_player_container.tscn");
        if (remotePlayersScene != null)
        {
            _mpPlayers = remotePlayersScene.Instantiate<NRemoteLobbyPlayerContainer>();
            _mpPlayers.Name = "SpinPlayers";
            _mpPlayers.CustomMinimumSize = new Vector2(0, 260);
            _mpPlayers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            playersPanel.AddChild(_mpPlayers);
            if (_mpPlayers.GetNodeOrNull<FlowContainer>("Container") is { } flow)
            {
                flow.Alignment = FlowContainer.AlignmentMode.Center;
            }
        }

        _spPlayers = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Visible = false
        };
        _spPlayers.AddThemeConstantOverride("separation", 4);
        playersPanel.AddChild(_spPlayers);

        vbox.AddChild(playersPanel);

        var uniqueRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _uniqueRow = uniqueRow;
        uniqueRow.AddThemeConstantOverride("separation", 12);
        vbox.AddChild(uniqueRow);

        var uniqueText = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        uniqueText.AddThemeConstantOverride("separation", 0);
        var uniqueTitle = SpinUi.MakeLabel("Unique characters", 22, SpinUi.Cream, bold: true);
        var uniqueDescription = SpinUi.MakeLabel(
            "When enabled, a character can only be rolled once.",
            18,
            SpinUi.Muted);
        uniqueText.AddChild(uniqueTitle);
        uniqueText.AddChild(uniqueDescription);
        uniqueRow.AddChild(uniqueText);

        PackedScene? tickboxScene = GD.Load<PackedScene>("res://scenes/screens/settings_tickbox.tscn");
        if (tickboxScene != null)
        {
            _uniqueTickbox = tickboxScene.Instantiate<Control>();
            _uniqueTickbox.Name = "UniqueCharactersTickbox";
            _uniqueTickbox.CustomMinimumSize = new Vector2(64f, 64f);
            _uniqueTickbox.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            _uniqueTickbox.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            _uniqueTickbox.MouseFilter = MouseFilterEnum.Stop;
            _uniqueTickbox.FocusMode = FocusModeEnum.All;
            _uniqueTickbox.GuiInput += OnUniqueTickboxInput;
            _uniqueTickbox.GetNodeOrNull<Control>("SelectionReticle")?.Hide();
            _uniqueTicked = _uniqueTickbox.GetNodeOrNull<Control>("TickboxVisuals/Ticked");
            _uniqueUnticked = _uniqueTickbox.GetNodeOrNull<Control>("TickboxVisuals/NotTicked");
            uniqueRow.AddChild(_uniqueTickbox);
            UpdateUniqueTickbox();
        }

        _lobbyStatus = SpinUi.MakeLabel("", 22, SpinUi.Muted, alignment: HorizontalAlignment.Center);
        _lobbyStatus.CustomMinimumSize = new Vector2(0, 32);
        vbox.AddChild(_lobbyStatus);

        var buttons = new VBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        buttons.AddThemeConstantOverride("separation", 0);
        vbox.AddChild(buttons);

        _startButton = SpinUi.MakeNativeButton(
            buttons, "START SPINNING", StartSpinHost, new Vector2(360f, 78f), 30);
        _startButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        _inviteButton = SpinUi.MakeNativeButton(
            buttons, "INVITE", OpenInviteDialog, new Vector2(320f, 72f), 26);
        _inviteButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        _readyButton = SpinUi.MakeNativeButton(
            buttons, "READY", ToggleReady, new Vector2(320f, 72f), 26);
        _readyButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        _vanillaButton = SpinUi.MakeNativeButton(
            buttons, "VANILLA MODE", ShowModToVanilla, new Vector2(320f, 72f), 26);
        _vanillaButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        buttons.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });

        _leaveButton = SpinUi.MakeNativeButton(
            buttons, "LEAVE LOBBY", LeaveLobby, new Vector2(320f, 72f), 24, redAccent: true);
        _leaveButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        return root;
    }

    private Control BuildSpinPanel()
    {
        var root = new Control { Name = "SpinPanel" };
        SpinUi.SetFullRect(root);

        var margin = new MarginContainer();
        SpinUi.SetFullRect(margin);
        margin.AddThemeConstantOverride("margin_left", 48);
        margin.AddThemeConstantOverride("margin_right", 48);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        root.AddChild(margin);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        margin.AddChild(vbox);

        _stageTitle = SpinUi.MakeTitle("CHARACTERS", 46);
        vbox.AddChild(_stageTitle);

        _stageProgress = SpinUi.MakeLabel("", 22, SpinUi.Gold, bold: true, alignment: HorizontalAlignment.Center);
        vbox.AddChild(_stageProgress);

        _wheelRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _wheelRow.AddThemeConstantOverride("separation", 34);
        vbox.AddChild(_wheelRow);

        _wheelA = new PrizeWheel { Name = "WheelA", Visible = false };
        _wheelB = new PrizeWheel { Name = "WheelB", Visible = false };
        _wheelSolo = new PrizeWheel { Name = "WheelSolo", Visible = false };
        _wheelRow.AddChild(_wheelA);
        _wheelRow.AddChild(_wheelB);
        _wheelRow.AddChild(_wheelSolo);

        _stageStatus = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            CustomMinimumSize = new Vector2(0, 72),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        _stageStatus.AddThemeConstantOverride("separation", 26);

        var footer = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        footer.AddThemeConstantOverride("separation", 18);
        vbox.AddChild(footer);

        footer.AddChild(_cancelButton = SpinUi.MakeNativeButton(
            footer, "CANCEL", OnCancelPressed, new Vector2(260f, 72f), 28, redAccent: true));
        _cancelButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        _cancelButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;

        footer.AddChild(_stageStatus);

        _manualSpinButton = SpinUi.MakeNativeButton(
            footer, "SPIN", OnManualSpinPressed, new Vector2(230f, 72f), 28,
            redAccent: false, goldAccent: true);
        _manualSpinButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _manualSpinButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _manualSpinButton.Visible = false;

        _nextButton = SpinUi.MakeNativeButton(
            footer, "NEXT", OnNextPressed, new Vector2(260f, 72f), 28);
        _nextButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        _nextButton.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _nextButton.SetEnabled(false);

        _activeModifierPanel = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 250),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            Visible = false
        };
        _activeModifierPanel.AddThemeConstantOverride("separation", 6);
        _activeModifierTitle = SpinUi.MakeLabel("ACTIVE MODIFIERS", 20, SpinUi.Gold, bold: true);
        _activeModifierPanel.AddChild(_activeModifierTitle);
        _activeModifierColumns = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Begin,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _activeModifierColumns.AddThemeConstantOverride("separation", 16);
        _activeModifierPanel.AddChild(_activeModifierColumns);
        vbox.AddChild(_activeModifierPanel);

        return root;
    }

    private Control BuildSummaryPanel()
    {
        var root = new Control { Name = "SummaryPanel" };
        SpinUi.SetFullRect(root);

        var center = new CenterContainer();
        SpinUi.SetFullRect(center);
        root.AddChild(center);

        var vbox = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(1180, 0),
            Alignment = BoxContainer.AlignmentMode.Center
        };
        vbox.AddThemeConstantOverride("separation", 12);
        center.AddChild(vbox);

        vbox.AddChild(SpinUi.MakeTitle("THE SPIRE HAS DECIDED", 50));

        _summarySections = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _summarySections.AddThemeConstantOverride("separation", 18);
        vbox.AddChild(_summarySections);

        _summarySections.AddChild(MakeSummarySection("CHARACTERS", out _characterSummary));
        _summarySections.AddChild(MakeSummarySection("ASCENSION", out _ascensionSummary));
        _summarySections.AddChild(MakeSummarySection("MODIFIERS", out _modifierSummary));

        _voteStatus = SpinUi.MakeLabel("", 22, SpinUi.Muted, alignment: HorizontalAlignment.Center);
        vbox.AddChild(_voteStatus);

        BuildVotePanel(out _voteOptions);
        vbox.AddChild(_voteOptions);

        return root;
    }

    private static PanelContainer MakeSummarySection(string title, out VBoxContainer lines)
    {
        PanelContainer panel = SpinUi.MakeStonePanel();
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = SizeFlags.ExpandFill;
        panel.CustomMinimumSize = new Vector2(360f, 0f);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        vbox.AddChild(SpinUi.MakeLabel(title, 22, SpinUi.Gold, bold: true));

        lines = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        lines.AddThemeConstantOverride("separation", 2);
        vbox.AddChild(lines);
        return panel;
    }

    private Control BuildDisconnectedPanel()
    {
        var root = new Control { Name = "DisconnectedPanel" };
        SpinUi.SetFullRect(root);

        var center = new CenterContainer();
        SpinUi.SetFullRect(center);
        root.AddChild(center);

        var vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        vbox.AddThemeConstantOverride("separation", 18);
        center.AddChild(vbox);

        vbox.AddChild(SpinUi.MakeTitle("CONNECTION LOST", 52));
        vbox.AddChild(SpinUi.MakeLabel(
            "The host has left the lobby.\nBummer.",
            30,
            SpinUi.Cream,
            alignment: HorizontalAlignment.Center));
        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });
        NPauseMenuButton bummer = SpinUi.MakeNativeButton(
            vbox, "Bummer", ReturnToMainMenu, new Vector2(300f, 80f), 30);
        bummer.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        return root;
    }

    private void ShowPanel(Control panel)
    {
        _lobbyPanel.Visible = panel == _lobbyPanel;
        _spinPanel.Visible = panel == _spinPanel;
        _summaryPanel.Visible = panel == _summaryPanel;
        _disconnectedPanel.Visible = panel == _disconnectedPanel;
        if (_promptPanel != null && IsInstanceValid(_promptPanel))
        {
            _promptPanel.Visible = false;
        }
    }

    private void SnapshotVanillaVisibility()
    {
        if (_vanillaVisibility.Count > 0 || _screen == null)
        {
            return;
        }

        foreach (Node child in _screen.GetChildren())
        {
            if (child == this || child.Name == "SpinTheSpireVanillaButton")
            {
                continue;
            }

            if (child is Control control)
            {
                _vanillaVisibility[child] = control.Visible;
            }
        }
    }

    private void SetVanillaChildrenVisible(bool visible)
    {
        foreach ((Node node, bool original) in _vanillaVisibility)
        {
            if (IsInstanceValid(node) && node is Control control)
            {
                control.Visible = visible ? original : false;
            }
        }
    }

    public void Bind(NCustomRunScreen screen, StartRunLobby lobby)
    {
        if (_bound)
        {
            return;
        }

        try
        {
            _screen = screen;
            _lobby = lobby;
            _bound = true;
            _plan = null;
            SpinSession.ActivePlan = null;
            _localId = lobby.NetService.NetId;
            _localAssignedCharacterId = null;
            _isHost = lobby.NetService.Type != NetGameType.Client;
            _isSingleplayer = lobby.NetService.Type == NetGameType.Singleplayer;
            if (_isSingleplayer)
            {
                _unique = false;
            }

            _spinStarted = false;
            _showingVanilla = false;
            _disconnected = false;
            _abortRequested = false;
            _startingRun = false;
            ++_sequenceEpoch;
            _returning = false;
            _localVoted = false;
            _voteSettled = false;
            _resultPlaying = false;
            _pendingReturnToLobby = false;
            _playerSignature = "";
            ResetAdvanceState();
            _votes.Clear();
            SnapshotVanillaVisibility();

            _ready.Clear();
            foreach (StartRunLobbyPlayer player in lobby.Players)
            {
                _ready[player.id] = false;
            }

            if (_isSingleplayer && lobby.Players.Count > 0)
            {
                _ready[lobby.Players[0].id] = true;
            }

            lobby.NetService.RegisterMessageHandler<SpinMessage>(HandleMessage);
            _registered = true;

            if (_spinButton != null)
            {
                _spinButton.Visible = _isHost;
                _spinButton.Enable();
            }

            if (SpinConfig.StartInVanilla)
            {
                ApplyShowVanilla(true, false);
            }
            else
            {
                ApplyShowMod(true, false);
            }

            if (_isHost)
            {
                BroadcastReadyState();
            }
            else
            {
                Send(SpinMessageKind.Hello, _localId);
            }

            ApplyDefaultLobbyCharacter();
            RefreshLobbyUi();
            MainFile.Logger.Info($"Spin overlay bound as {lobby.NetService.Type}.");
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not bind Spin the Spire overlay: {e}");
        }
    }

    public void Unbind()
    {
        try
        {
            if (_registered && _lobby != null)
            {
                _lobby.NetService.UnregisterMessageHandler<SpinMessage>(HandleMessage);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not unregister spin messages: {e.Message}");
        }

        try
        {
            _mpPlayers?.Cleanup();
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not clean up the player list: {e.Message}");
        }

        _registered = false;
        _bound = false;
        _abortRequested = true;
        _playerSignature = "";
        ResetAdvanceState();
        _lobby = null;
    }

    public override void _ExitTree()
    {
        Unbind();
    }

    public override void _Process(double delta)
    {
        if (!_bound || _startingRun || _lobby == null || !_isHost || _isSingleplayer || _disconnected)
        {
            return;
        }

        _pollTimer += (float)delta;
        if (_pollTimer < 0.4f)
        {
            return;
        }

        _pollTimer = 0f;

        var current = _lobby.Players.Select(p => p.id).ToHashSet();
        bool changed = current.Count != _ready.Count || current.Any(id => !_ready.ContainsKey(id));
        if (!changed)
        {
            return;
        }

        foreach (ulong id in current)
        {
            _ready.TryAdd(id, false);
        }

        foreach (ulong id in _ready.Keys.ToList())
        {
            if (!current.Contains(id))
            {
                _ready.Remove(id);
            }
        }

        if (_spinStarted)
        {
            _abortRequested = true;
            _voteSettled = true;
            RunTask(() => FadeAndReturnToLobbyAsync(broadcast: true));
            return;
        }

        BroadcastReadyState();
        RefreshLobbyUi();
    }

    private void RunTask(Func<Task> action)
    {
        TaskHelper.RunSafely(action());
    }

    private void Send(
        SpinMessageKind kind,
        ulong playerId = 0,
        bool flag = false,
        int number = 0,
        string payload = "")
    {
        if (_lobby == null)
        {
            return;
        }

        try
        {
            _lobby.NetService.SendMessage(new SpinMessage
            {
                Kind = kind,
                PlayerId = playerId,
                Flag = flag,
                Number = number,
                Payload = payload
            });
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not send spin message {kind}: {e.Message}");
        }
    }

    private void HandleMessage(SpinMessage message, ulong senderId)
    {
        try
        {
            if (!_bound || _startingRun || _disconnected || _lobby == null)
            {
                return;
            }

            switch (message.Kind)
            {
                case SpinMessageKind.Hello:
                    if (_isHost)
                    {
                        BroadcastReadyState();
                    }
                    break;

                case SpinMessageKind.Ready:
                    if (_isHost)
                    {
                        _ready[message.PlayerId] = message.Flag;
                        BroadcastReadyState();
                        RefreshLobbyUi();
                    }
                    break;

                case SpinMessageKind.ReadyState:
                    if (!_isHost)
                    {
                        ApplyReadyState(message.Payload);
                    }
                    break;

                case SpinMessageKind.Start:
                    if (!_isHost && _plan == null)
                    {
                        BeginClientSequence(message.Payload, message.Number);
                    }
                    break;

                case SpinMessageKind.Vote:
                    if (_isHost && !_voteSettled)
                    {
                        _votes[message.PlayerId] = message.Flag;
                        BroadcastVoteState();
                        RefreshVoteUi();
                        CheckVotesComplete();
                    }
                    break;

                case SpinMessageKind.VoteState:
                    if (!_isHost && !_voteSettled && !_resultPlaying)
                    {
                        ApplyVoteState(message.Payload);
                    }
                    break;

                case SpinMessageKind.VoteResult:
                    if (!_isHost)
                    {
                        var result = SpinPayload.Deserialize<VoteResultPayload>(message.Payload);
                        if (result != null)
                        {
                            _voteSettled = true;
                            _localVoted = true;
                            _rideButton.Disable();
                            _notButton.Disable();
                            _voteStatus.Text = "The Spire has decided.";
                            RunTask(() => PlayVoteResultAsync(result));
                        }
                    }
                    break;

                case SpinMessageKind.ReturnToLobby:
                    _abortRequested = true;
                    if (_resultPlaying)
                    {
                        _pendingReturnToLobby = true;
                    }
                    else
                    {
                        RunTask(() => FadeAndReturnToLobbyAsync(broadcast: _isHost));
                    }
                    break;

                case SpinMessageKind.ShowVanilla:
                    ApplyShowVanilla(false, true);
                    break;

                case SpinMessageKind.ShowMod:
                    ApplyShowMod(true, false);
                    break;

                case SpinMessageKind.Advance:
                    _advanceReceived = Math.Max(_advanceReceived, message.Number);
                    if (_advanceTcs != null && _advanceReceived >= _advanceWaitStage)
                    {
                        TaskCompletionSource<bool> tcs = _advanceTcs;
                        _advanceTcs = null;
                        tcs.TrySetResult(true);
                    }
                    else if (_manualActionTcs != null)
                    {
                        ResolveManualAction(ManualAction.Next);
                    }
                    else if (_plan is { ManualRolls: true })
                    {
                        _pendingManualNext = true;
                    }
                    break;

                case SpinMessageKind.ManualSpin:
                    HandleManualSpinMessage(message);
                    break;

                case SpinMessageKind.PromptOpen:
                    HandlePromptOpenMessage(message.Number, message.Flag);
                    break;

                case SpinMessageKind.PromptVote:
                    HandlePromptVoteMessage(message);
                    break;

                case SpinMessageKind.PromptState:
                    HandlePromptStateMessage(message.Payload);
                    break;

                case SpinMessageKind.PromptResult:
                    HandlePromptResultMessage(message.Payload);
                    break;

                case SpinMessageKind.Abort:
                    ShowDisconnected();
                    break;
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Spin message handler failed: {e}");
        }
    }

    private void BroadcastReadyState()
    {
        if (!_isHost || _lobby == null)
        {
            return;
        }

        var payload = new LobbyStatePayload
        {
            Unique = _unique,
            Started = _spinStarted,
            Vanilla = _showingVanilla,
            Ready = _ready.Where(kv => kv.Value).Select(kv => kv.Key).ToList()
        };

        Send(SpinMessageKind.ReadyState, payload: SpinPayload.Serialize(payload));
    }

    private void ApplyReadyState(string json)
    {
        var payload = SpinPayload.Deserialize<LobbyStatePayload>(json);
        if (payload == null || _lobby == null)
        {
            return;
        }

        _unique = payload.Unique;
        _spinStarted = payload.Started;

        _ready.Clear();
        foreach (StartRunLobbyPlayer player in _lobby.Players)
        {
            _ready[player.id] = payload.Ready.Contains(player.id);
        }

        if (payload.Vanilla)
        {
            ApplyShowVanilla(false, false);
        }
        else if (!_spinStarted)
        {
            ApplyShowMod(false, false);
        }

        RefreshLobbyUi();
    }

    private void ToggleReady()
    {
        if (_spinStarted || _showingVanilla || _lobby == null)
        {
            return;
        }

        bool value = !_ready.GetValueOrDefault(_localId);
        _ready[_localId] = value;

        if (_isHost)
        {
            BroadcastReadyState();
        }
        else
        {
            Send(SpinMessageKind.Ready, _localId, value);
        }

        RefreshLobbyUi();
    }

    private void OnUniqueTickboxInput(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            return;
        }

        if (!_isHost || _spinStarted || _showingVanilla)
        {
            return;
        }

        _unique = !_unique;
        BroadcastReadyState();
        RefreshLobbyUi();
    }

    private void UpdateUniqueTickbox()
    {
        if (_uniqueTicked != null && IsInstanceValid(_uniqueTicked))
        {
            _uniqueTicked.Visible = _unique;
        }

        if (_uniqueUnticked != null && IsInstanceValid(_uniqueUnticked))
        {
            _uniqueUnticked.Visible = !_unique;
        }
    }

    private void OnNextPressed()
    {
        if (!_isHost)
        {
            return;
        }

        if (_plan is { ManualRolls: true } && _spinStarted)
        {
            if (_promptOpen || _manualAnimating || _manualActionTcs == null
                || !ManualSectionCanAdvance())
            {
                return;
            }

            if (_manualSection == SpinSection.Modifiers
                && _plan.Rolls.Count < _plan.TargetRolls)
            {
                OpenPrompt(_manualSection, advance: true);
                return;
            }

            _nextButton.SetEnabled(false);
            Send(SpinMessageKind.Advance, number: 0);
            ResolveManualAction(ManualAction.Next);
            return;
        }

        int stage = _advanceWaitStage;
        _advanceReceived = Math.Max(_advanceReceived, stage);
        _nextButton.SetEnabled(false);
        Send(SpinMessageKind.Advance, number: stage);

        TaskCompletionSource<bool>? tcs = _advanceTcs;
        _advanceTcs = null;
        tcs?.TrySetResult(true);
    }

    private void OnCancelPressed()
    {
        if (_returning || _disconnected)
        {
            return;
        }

        _abortRequested = true;
        if (_isHost)
        {
            RunTask(() => FadeAndReturnToLobbyAsync(broadcast: true));
        }
        else
        {
            Send(SpinMessageKind.ReturnToLobby);
            RunTask(() => FadeAndReturnToLobbyAsync(broadcast: false));
        }
    }

    private void SetStageStatusText(string text, Color? color = null)
    {
        ClearStageStatus();
        if (!string.IsNullOrEmpty(text))
        {
            AddStageStatusPart(text, color ?? SpinUi.Cream);
        }
    }

    private void SetStageStatusParts(params (string Text, Color Color)[] parts)
    {
        ClearStageStatus();
        foreach ((string text, Color color) in parts)
        {
            if (!string.IsNullOrEmpty(text))
            {
                AddStageStatusPart(text, color);
            }
        }
    }

    private void AddStageStatusPart(string text, Color color)
    {
        Label label = SpinUi.MakeLabel(
            text, 30, color, bold: true, alignment: HorizontalAlignment.Center);
        label.AutowrapMode = TextServer.AutowrapMode.Off;
        label.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _stageStatus.AddChild(label);
    }

    private void ClearStageStatus()
    {
        foreach (Node child in _stageStatus.GetChildren())
        {
            _stageStatus.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void StartSpinHost()
    {
        if (!_isHost || _lobby == null || _spinStarted || _showingVanilla)
        {
            return;
        }

        if (!AllPlayersReady())
        {
            return;
        }

        try
        {
            _abortRequested = false;
            int epoch = ++_sequenceEpoch;
            ResetManualUi();
            _abortRequested = false;
            _manualSection = SpinSection.Characters;
            _votes.Clear();
            _localVoted = false;
            _voteSettled = false;
            _resultPlaying = false;
            _pendingReturnToLobby = false;
            _plan = SpinRules.BuildPlan(
                _lobby, _unique, SpinConfig.RollCoefficient, SpinConfig.RollManually);
            _spinStarted = true;
            SpinSession.ActivePlan = _plan;
            CloseHostLobby();

            BroadcastReadyState();
            Send(SpinMessageKind.Start, number: 500, payload: _plan.ToJson());
            RefreshLobbyUi();

            RunTask(() => BeginSequenceAfterDelayAsync(500, _plan, epoch));
        }
        catch (Exception e)
        {
            _spinStarted = false;
            _plan = null;
            SpinSession.ActivePlan = null;
            MainFile.Logger.Warn($"Could not start the spin: {e}");
            _lobbyStatus.Text = "Could not start the shuffling. Check the log.";
            RefreshLobbyUi();
        }
    }

    private void BeginClientSequence(string json, int delayMs)
    {
        SpinPlan? plan = SpinPlan.FromJson(json);
        if (plan == null)
        {
            MainFile.Logger.Warn("Received an invalid spin plan; aborting.");
            ShowDisconnected();
            return;
        }

        _plan = plan;
        SpinSession.ActivePlan = plan;
        _spinStarted = true;
        _abortRequested = false;
        int epoch = ++_sequenceEpoch;
        ResetManualUi();
        _manualSection = SpinSection.Characters;
        _votes.Clear();
        _localVoted = false;
        _voteSettled = false;
        _resultPlaying = false;
        _pendingReturnToLobby = false;
        ApplyShowMod(false, false);
        RefreshLobbyUi();
        RunTask(() => BeginSequenceAfterDelayAsync(Math.Max(100, delayMs), plan, epoch));
    }

    private void ShowModToVanilla()
    {
        if (!_isHost || _lobby == null || _spinStarted)
        {
            return;
        }

        Send(SpinMessageKind.ShowVanilla);
        ApplyShowVanilla(true, true);
    }

    public void ShowModFromVanilla()
    {
        if (!_isHost || _lobby == null || _spinStarted || _disconnected)
        {
            return;
        }

        Send(SpinMessageKind.ShowMod);
        ApplyShowMod(true, true);
    }

    private void ApplyShowVanilla(bool resetReady, bool broadcast)
    {
        _showingVanilla = true;
        _plan = null;
        SpinSession.ActivePlan = null;
        Visible = false;
        SetVanillaChildrenVisible(true);
        SetVanillaConfirmEnabled(true);
        if (_spinButton != null)
        {
            _spinButton.Visible = _isHost && !_disconnected;
        }

        if (resetReady)
        {
            TrySetVanillaReady(false);
            if (_isHost)
            {
                foreach (ulong id in _ready.Keys.ToList())
                {
                    _ready[id] = false;
                }

                if (broadcast)
                {
                    BroadcastReadyState();
                }
            }
            else
            {
                _ready[_localId] = false;
            }
        }

        RefreshLobbyUi();
    }

    private void ApplyShowMod(bool resetReady, bool broadcast)
    {
        if (_disconnected)
        {
            return;
        }

        _showingVanilla = false;
        Visible = true;
        SetVanillaChildrenVisible(false);
        SetVanillaConfirmEnabled(false);
        if (_spinButton != null)
        {
            _spinButton.Visible = false;
        }

        if (resetReady)
        {
            TrySetVanillaReady(false);
            if (_isHost)
            {
                foreach (ulong id in _ready.Keys.ToList())
                {
                    _ready[id] = false;
                }

                if (broadcast)
                {
                    BroadcastReadyState();
                }
            }
            else
            {
                _ready[_localId] = false;
            }
        }

        ShowLobby();
    }

    private void SetVanillaConfirmEnabled(bool enabled)
    {
        if (_screen == null || !GodotObject.IsInstanceValid(_screen))
        {
            return;
        }

        NButton? button = _screen.GetNodeOrNull<NButton>("ConfirmButton");
        if (button == null)
        {
            return;
        }

        if (enabled)
        {
            button.Enable();
        }
        else
        {
            button.Disable();
        }
    }

    private void TrySetVanillaReady(bool value)
    {
        try
        {
            _lobby?.SetReady(value);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not reset vanilla ready state: {e.Message}");
        }
    }

    private void CloseHostLobby()
    {
        try
        {
            if (_lobby?.NetService is INetHostGameService host)
            {
                host.NetHost?.SetHostIsClosed(true);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not close lobby: {e.Message}");
        }
    }

    private void ReopenHostLobby()
    {
        try
        {
            if (_lobby?.NetService is INetHostGameService host)
            {
                host.NetHost?.SetHostIsClosed(false);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not reopen lobby: {e.Message}");
        }
    }

    private bool AllPlayersReady()
    {
        if (_lobby == null || _lobby.Players.Count == 0)
        {
            return false;
        }

        if (_isSingleplayer)
        {
            return true;
        }

        return _lobby.Players.All(p => _ready.GetValueOrDefault(p.id));
    }

    private void EnsureSingleplayerReady()
    {
        if (!_isSingleplayer || _spinStarted || _lobby == null || _lobby.Players.Count == 0)
        {
            return;
        }

        _ready[_localId] = true;
    }

    private void RefreshLobbyUi()
    {
        if (_lobby == null || _readyButton == null)
        {
            return;
        }

        EnsureSingleplayerReady();
        RefreshPlayerList();

        UpdateUniqueTickbox();
        if (_uniqueRow != null && IsInstanceValid(_uniqueRow))
        {
            _uniqueRow.Visible = !_isSingleplayer;
        }

        if (_uniqueTickbox != null && IsInstanceValid(_uniqueTickbox))
        {
            bool interactive = _isHost && !_isSingleplayer && !_spinStarted && !_showingVanilla;
            _uniqueTickbox.MouseFilter = interactive ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
            _uniqueTickbox.Visible = !_isSingleplayer;
            _uniqueTickbox.Modulate = interactive
                ? Colors.White
                : new Color(1f, 1f, 1f, 0.45f);
        }

        _readyButton.Visible = !_isSingleplayer;
        _readyButton.SetEnabled(!_spinStarted);
        SpinUi.SetNativeButtonText(
            _readyButton,
            _ready.GetValueOrDefault(_localId) ? "UNREADY" : "READY");

        _startButton.Visible = _isHost;
        _startButton.SetEnabled(_isHost && !_spinStarted && AllPlayersReady());

        _vanillaButton.Visible = _isHost;
        _vanillaButton.SetEnabled(_isHost && !_spinStarted);

        if (_spinButton != null)
        {
            _spinButton.Visible = _isHost && _showingVanilla && !_disconnected;
        }

        if (_spinStarted)
        {
            _lobbyStatus.Text = "The wheels are turning...";
        }
        else if (AllPlayersReady())
        {
            _lobbyStatus.Text = _isHost
                ? "Everyone is ready. Start when you are."
                : "Everyone is ready. Waiting for the host...";
        }
        else
        {
            _lobbyStatus.Text = "Waiting for all players to ready up.";
        }

        bool canInvite = false;
        try
        {
            canInvite = !_isSingleplayer
                && _lobby != null
                && PlatformUtil.SupportsInviteDialog(_lobby.NetService.Platform);
        }
        catch
        {
            // Treat unsupported platforms as no invite button.
        }

        _inviteButton.Visible = canInvite && !_disconnected;
        _inviteButton.SetEnabled(canInvite && !_spinStarted);
        HideVanillaInviteButton();
    }

    private void OpenInviteDialog()
    {
        if (_lobby == null)
        {
            return;
        }

        try
        {
            if (!PlatformUtil.TryOpenInviteDialog(_lobby.NetService))
            {
                MainFile.Logger.Warn("Could not open the platform invite dialog.");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Invite dialog failed: {e.Message}");
        }
    }

    private void HideVanillaInviteButton()
    {
        if (_mpPlayers?.GetNodeOrNull<Control>("Container/InviteButtonContainer") is { } wrapper)
        {
            wrapper.Visible = false;
        }
    }

    private void RefreshPlayerList()
    {
        if (_lobby == null)
        {
            return;
        }

        string signature = string.Join(
            "|",
            _lobby.Players
                .OrderBy(p => p.slotId)
                .Select(p => $"{p.id}:{p.character?.Id}"));

        bool playersChanged = signature != _playerSignature;
        if (playersChanged)
        {
            _playerSignature = signature;
        }

        if (_isSingleplayer)
        {
            _spPlayers.Visible = true;
            if (_mpPlayers != null)
            {
                _mpPlayers.Visible = false;
            }

            if (playersChanged)
            {
                RebuildSingleplayerRows();
            }
        }
        else
        {
            _spPlayers.Visible = false;
            if (_mpPlayers != null)
            {
                _mpPlayers.Visible = true;
            }

            if (playersChanged)
            {
                RebuildMultiplayerRows();
            }
        }

        UpdateVanillaReadyIndicators();
    }

    private void RebuildSingleplayerRows()
    {
        foreach (Node child in _spPlayers.GetChildren())
        {
            child.QueueFree();
        }

        _spRows.Clear();
        if (_lobby == null)
        {
            return;
        }

        foreach (StartRunLobbyPlayer player in _lobby.Players.OrderBy(p => p.slotId))
        {
            try
            {
                NRemoteLobbyPlayer row = NRemoteLobbyPlayer.Create(
                    player,
                    _lobby.NetService.Platform,
                    isSingleplayer: true);
                row.CustomMinimumSize = new Vector2(360f, 68f);
                row.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
                _spPlayers.AddChild(row);
                _spRows.Add(row);
            }
            catch (Exception e)
            {
                MainFile.Logger.Warn($"Could not create the singleplayer player row: {e.Message}");
            }
        }
    }

    private void RebuildMultiplayerRows()
    {
        if (_mpPlayers == null || _lobby == null)
        {
            return;
        }

        try
        {
            _mpPlayers.Initialize(_lobby, displayLocalPlayer: true);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not rebuild the multiplayer player list: {e.Message}");
        }
    }

    private void UpdateVanillaReadyIndicators()
    {
        if (_lobby == null)
        {
            return;
        }

        if (_isSingleplayer)
        {
            foreach (NRemoteLobbyPlayer row in _spRows)
            {
                SetReadyIndicator(row, _ready.GetValueOrDefault(row.PlayerId));
            }

            return;
        }

        if (_mpPlayers == null)
        {
            return;
        }

        Container? container = _mpPlayers.GetNodeOrNull<Container>("Container");
        if (container == null)
        {
            return;
        }

        foreach (NRemoteLobbyPlayer row in container.GetChildren().OfType<NRemoteLobbyPlayer>())
        {
            SetReadyIndicator(row, _ready.GetValueOrDefault(row.PlayerId));
        }
    }

    private static void SetReadyIndicator(NRemoteLobbyPlayer row, bool ready)
    {
        if (!IsInstanceValid(row))
        {
            return;
        }

        if (row.GetNodeOrNull<TextureRect>("%ReadyIndicator") is { } indicator)
        {
            indicator.Visible = ready;
        }
    }

    private void ShowLobby()
    {
        if (_disconnected)
        {
            return;
        }

        SetVanillaChildrenVisible(false);
        SetVanillaConfirmEnabled(false);
        ShowPanel(_lobbyPanel);
        Visible = true;
        _playerSignature = "";
        RefreshLobbyUi();
    }

    public void ShowDisconnected()
    {
        _disconnected = true;
        _abortRequested = true;
        ++_sequenceEpoch;
        _spinStarted = false;
        SpinSession.ActivePlan = null;
        _plan = null;
        StopAllWheels();
        ResetAdvanceState();
        ResetManualUi();
        SetVanillaChildrenVisible(false);
        ShowPanel(_disconnectedPanel);
        Visible = true;
        if (_spinButton != null)
        {
            _spinButton.Visible = false;
        }
    }

    public void OnRemotePlayerLeft(ulong playerId)
    {
        if (!_bound || !_isHost)
        {
            return;
        }

        _ready.Remove(playerId);
        _votes.Remove(playerId);

        if (_spinStarted)
        {
            _abortRequested = true;
            _voteSettled = true;
            RunTask(() => FadeAndReturnToLobbyAsync(broadcast: true));
            return;
        }

        BroadcastReadyState();
        RefreshLobbyUi();
    }

    private async Task FadeAndReturnToLobbyAsync(bool broadcast)
    {
        if (_returning || _disconnected)
        {
            return;
        }

        _returning = true;
        try
        {
            if (broadcast && _isHost)
            {
                Send(SpinMessageKind.ReturnToLobby);
            }

            await FadeTo(1f, 0.22f);
            ResetToLobby(reopenHost: _isHost);
            await FadeTo(0f, 0.32f);
        }
        finally
        {
            _returning = false;
        }
    }

    private async Task FadeTo(float alpha, float duration)
    {
        if (!IsInsideTree())
        {
            return;
        }

        _fade.Visible = true;
        _fade.MouseFilter = alpha > 0.01f ? MouseFilterEnum.Stop : MouseFilterEnum.Ignore;
        var tween = CreateTween();
        tween.TweenProperty(_fade, "modulate:a", alpha, duration);
        await ToSignal(tween, Tween.SignalName.Finished);
        if (alpha <= 0.01f)
        {
            _fade.Visible = false;
            _fade.MouseFilter = MouseFilterEnum.Ignore;
        }
    }

    private void StopAllWheels()
    {
        _wheelA?.StopSpin();
        _wheelB?.StopSpin();
        _wheelSolo?.StopSpin();
    }

    private void ResetAdvanceState()
    {
        _advanceReceived = 0;
        _advanceWaitStage = 0;
        TaskCompletionSource<bool>? tcs = _advanceTcs;
        _advanceTcs = null;
        tcs?.TrySetResult(false);

        if (_nextButton != null)
        {
            _nextButton.Visible = true;
            _nextButton.SetEnabled(false);
        }
    }

    private void ResetToLobby(bool reopenHost)
    {
        StopAllWheels();
        ResetAdvanceState();
        ++_sequenceEpoch;
        _abortRequested = true;
        ResetManualUi();
        _plan = null;
        SpinSession.ActivePlan = null;
        _spinStarted = false;
        _showingVanilla = false;
        _startingRun = false;
        _localAssignedCharacterId = null;
        _localVoted = false;
        _voteSettled = false;
        _resultPlaying = false;
        _pendingReturnToLobby = false;
        _votes.Clear();
        _playerSignature = "";
        _ready.Clear();

        if (_lobby != null)
        {
            foreach (StartRunLobbyPlayer player in _lobby.Players)
            {
                _ready[player.id] = false;
            }

            if (_isSingleplayer && _lobby.Players.Count > 0)
            {
                _ready[_lobby.Players[0].id] = true;
            }

            TrySetVanillaReady(false);
            ResetLocalCharacter();

            if (_isHost)
            {
                try
                {
                    _lobby.SetModifiers(Array.Empty<ModifierModel>());
                    _lobby.SyncAscensionChange(0);
                }
                catch (Exception e)
                {
                    MainFile.Logger.Warn($"Could not clear lobby state: {e.Message}");
                }

                if (reopenHost)
                {
                    ReopenHostLobby();
                }

                BroadcastReadyState();
            }
        }

        ShowLobby();
        RunTask(RefreshLobbyAfterResetAsync);
    }

    /// <summary>
    /// The local character change can be applied after the first lobby refresh (queued lobby
    /// messages), which left the spun character icon in the player list until the next click.
    /// Re-assert the random default and rebuild the rows once the frame has settled.
    /// </summary>
    private async Task RefreshLobbyAfterResetAsync()
    {
        await Pause(0.15f);
        if (!IsInsideTree() || _disconnected || _lobby == null)
        {
            return;
        }

        ApplyDefaultLobbyCharacter();
        _playerSignature = "";
        RefreshLobbyUi();
    }

    private void ResetLocalCharacter()
    {
        ApplyDefaultLobbyCharacter();
    }

    /// <summary>
    /// The lobby defaults to the "Random" character pick from the standard character select,
    /// instead of whatever the vanilla screen had selected (usually the Ironclad).
    /// </summary>
    private void ApplyDefaultLobbyCharacter()
    {
        if (_lobby == null)
        {
            return;
        }

        try
        {
            _lobby.SetLocalCharacter(ModelDb.Character<RandomCharacter>());
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not set random lobby character: {e.Message}");
        }
    }

    private void LeaveLobby()
    {
        try
        {
            if (_screen.GetParent() is NSubmenuStack stack)
            {
                stack.Pop();
            }
            else
            {
                _screen.QueueFree();
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not leave lobby: {e.Message}");
        }
    }

    private void ReturnToMainMenu()
    {
        try
        {
            if (_screen.GetParent() is NSubmenuStack stack)
            {
                int guard = 0;
                while (stack.SubmenusOpen && guard++ < 8)
                {
                    stack.Pop();
                }
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not return to main menu: {e.Message}");
        }
    }

    private void BroadcastVoteState()
    {
        if (!_isHost)
        {
            return;
        }

        var payload = new VoteStatePayload
        {
            Votes = _votes.Select(kv => new VoteEntry { PlayerId = kv.Key, Ride = kv.Value }).ToList()
        };
        Send(SpinMessageKind.VoteState, payload: SpinPayload.Serialize(payload));
    }

    private void ApplyVoteState(string json)
    {
        var payload = SpinPayload.Deserialize<VoteStatePayload>(json);
        if (payload == null)
        {
            return;
        }

        _votes.Clear();
        foreach (VoteEntry vote in payload.Votes)
        {
            _votes[vote.PlayerId] = vote.Ride;
        }

        RefreshVoteUi();
    }
}
