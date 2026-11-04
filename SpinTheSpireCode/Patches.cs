using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Runs;

namespace SpinTheSpire.SpinTheSpireCode;

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen._Ready))]
internal static class SettingsScreenReadyPatch
{
    private static void Postfix(NSettingsScreen __instance)
    {
        SpinSettings.Install(__instance);
    }
}

[HarmonyPatch(typeof(NSingleplayerSubmenu), nameof(NSingleplayerSubmenu._Ready))]
internal static class SingleplayerSubmenuReadyPatch
{
    private static void Postfix(NSingleplayerSubmenu __instance)
    {
        if (SpinConfig.StartInVanilla)
        {
            return;
        }

        RelabelCustomButton(__instance);
    }

    internal static void RelabelCustomButton(Node submenu)
    {
        try
        {
            Control? button = submenu.GetNodeOrNull<Control>("CustomRunButton");
            if (button == null)
            {
                return;
            }

            Label? title = button.GetNodeOrNull<Label>("%Title");
            if (title != null)
            {
                title.Text = "Spin the Spire";
            }

            RichTextLabel? description = button.GetNodeOrNull<RichTextLabel>("%Description");
            if (description != null)
            {
                description.Text = "Let the wheels decide your fate.";
            }

            if (button.GetNodeOrNull<TextureRect>("Icon") is { } icon
                && GetModImage() is { } image)
            {
                icon.Texture = image;
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not relabel the Custom button: {e.Message}");
        }
    }

    private static Texture2D? _modImage;
    private static bool _modImageLoaded;

    /// <summary>
    /// Loads the mod's own "mod image" (png or jpg) from the mod folder for the game-type
    /// button artwork. Loose files are used because this mod ships no PCK.
    /// </summary>
    private static Texture2D? GetModImage()
    {
        if (_modImageLoaded)
        {
            return _modImage;
        }

        _modImageLoaded = true;
        try
        {
            string directory = Path.GetDirectoryName(typeof(MainFile).Assembly.Location) ?? "";
            string[] candidates =
            {
                "submenu_spinTheSpire.png",
                "submenu_spin_the_spire.png",
                "mod_image.png", "mod image.png", "mod_image.jpg", "mod image.jpg"
            };

            foreach (string name in candidates)
            {
                string path = Path.Combine(directory, name);
                if (!File.Exists(path))
                {
                    continue;
                }

                var image = new Image();
                Error error = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    ? image.LoadPngFromBuffer(File.ReadAllBytes(path))
                    : image.LoadJpgFromBuffer(File.ReadAllBytes(path));

                if (error != Error.Ok)
                {
                    continue;
                }

                _modImage = ImageTexture.CreateFromImage(image);
                break;
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not load mod image: {e.Message}");
        }

        return _modImage;
    }
}

[HarmonyPatch(typeof(NMultiplayerHostSubmenu), nameof(NMultiplayerHostSubmenu._Ready))]
internal static class MultiplayerHostSubmenuReadyPatch
{
    private static void Postfix(NMultiplayerHostSubmenu __instance)
    {
        if (SpinConfig.StartInVanilla)
        {
            return;
        }

        SingleplayerSubmenuReadyPatch.RelabelCustomButton(__instance);
    }
}

[HarmonyPatch(typeof(NSubmenuButton), nameof(NSubmenuButton.RefreshLabels))]
internal static class SubmenuButtonRefreshLabelsPatch
{
    private static void Postfix(NSubmenuButton __instance)
    {
        if (SpinConfig.StartInVanilla || __instance.Name != "CustomRunButton")
        {
            return;
        }

        Node? parent = __instance.GetParent();
        if (parent is not NSingleplayerSubmenu && parent is not NMultiplayerHostSubmenu)
        {
            return;
        }

        SingleplayerSubmenuReadyPatch.RelabelCustomButton(parent);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen._Ready))]
internal static class CustomRunScreenReadyPatch
{
    private static void Postfix(NCustomRunScreen __instance)
    {
        try
        {
            if (__instance.GetNodeOrNull<SpinOverlay>("SpinTheSpireOverlay") != null)
            {
                return;
            }

            NPauseMenuButton spinButton = CreateSpinButton(__instance);
            var overlay = new SpinOverlay();
            __instance.AddChild(overlay);
            overlay.Initialize(__instance, spinButton);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not install the Spin the Spire overlay: {e}");
        }
    }

    private static NPauseMenuButton CreateSpinButton(NCustomRunScreen screen)
    {
        NPauseMenuButton button = SpinUi.MakeNativeButton(
            screen,
            "Spin the Spire",
            () =>
            {
                SpinOverlay? overlay = screen.GetNodeOrNull<SpinOverlay>("SpinTheSpireOverlay");
                overlay?.ShowModFromVanilla();
            },
            new Vector2(260f, 64f),
            24);

        button.Name = "SpinTheSpireVanillaButton";
        button.Visible = false;
        button.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        button.OffsetLeft = -288f;
        button.OffsetTop = 16f;
        button.OffsetRight = -28f;
        button.OffsetBottom = 80f;
        return button;
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuOpened))]
internal static class CustomRunScreenOpenedPatch
{
    private static void Postfix(NCustomRunScreen __instance)
    {
        try
        {
            SpinOverlay? overlay = __instance.GetNodeOrNull<SpinOverlay>("SpinTheSpireOverlay");
            if (overlay == null || __instance.Lobby == null)
            {
                return;
            }

            overlay.Bind(__instance, __instance.Lobby);

            if (!SpinConfig.StartInVanilla)
            {
                __instance.GetNodeOrNull<NButton>("ConfirmButton")?.Disable();
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not bind the Spin the Spire overlay: {e}");
        }
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.OnSubmenuClosed))]
internal static class CustomRunScreenClosedPatch
{
    private static void Postfix(NCustomRunScreen __instance)
    {
        __instance.GetNodeOrNull<SpinOverlay>("SpinTheSpireOverlay")?.Unbind();
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.RemotePlayerDisconnected))]
internal static class CustomRunRemotePlayerLeftPatch
{
    private static void Postfix(NCustomRunScreen __instance, StartRunLobbyPlayer player)
    {
        __instance.GetNodeOrNull<SpinOverlay>("SpinTheSpireOverlay")?.OnRemotePlayerLeft(player.id);
    }
}

[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.LocalPlayerDisconnected))]
internal static class CustomRunLocalPlayerDisconnectedPatch
{
    private static bool Prefix(NCustomRunScreen __instance, NetErrorInfo info)
    {
        if (info.SelfInitiated)
        {
            return true;
        }

        SpinOverlay? overlay = __instance.GetNodeOrNull<SpinOverlay>("SpinTheSpireOverlay");
        if (overlay == null || !overlay.IsBound)
        {
            return true;
        }

        overlay.ShowDisconnected();
        return false;
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
internal static class SingleplayerAscensionPatch
{
    private static void Prefix(ref int ascensionLevel, GameMode gameMode)
    {
        if (gameMode == GameMode.Custom && SpinSession.ActivePlan is { } plan)
        {
            ascensionLevel = plan.Ascension;
        }
    }
}

[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewMultiplayerRun))]
internal static class MultiplayerAscensionPatch
{
    private static void Prefix(StartRunLobby lobby, ref int ascensionLevel)
    {
        if (lobby.GameMode == GameMode.Custom && SpinSession.ActivePlan is { } plan)
        {
            ascensionLevel = plan.Ascension;
        }
    }
}

/// <summary>
/// Tracks whether the next character-select SFX should be swallowed.
/// </summary>
internal static class SpinSfxGate
{
    public static bool SuppressCharacterSelect;
}

/// <summary>
/// NCustomRunScreen.SelectCharacter always plays the selected character's select SFX. The vanilla
/// screen auto-selects the first character button when it opens, so joining the lobby blasts the
/// Ironclad jingle before our overlay takes over. Swallow only that call's SFX.
/// </summary>
[HarmonyPatch(typeof(NCustomRunScreen), nameof(NCustomRunScreen.SelectCharacter))]
internal static class CustomRunSelectCharacterSfxPatch
{
    private static void Prefix(NCustomRunScreen __instance)
    {
        SpinOverlay? overlay = __instance.GetNodeOrNull<SpinOverlay>("SpinTheSpireOverlay");
        SpinSfxGate.SuppressCharacterSelect =
            overlay != null && (!overlay.IsBound || !overlay.ShowsVanilla);
    }

    private static void Postfix()
    {
        SpinSfxGate.SuppressCharacterSelect = false;
    }
}

[HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.Play), new[] { typeof(string), typeof(float) })]
internal static class SfxPlayGatePatch
{
    private static bool Prefix()
    {
        return !SpinSfxGate.SuppressCharacterSelect;
    }
}
