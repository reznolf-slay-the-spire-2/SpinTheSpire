using System;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.addons.mega_text;

namespace SpinTheSpire.SpinTheSpireCode;

internal static class SpinUi
{
    public static readonly Color Gold = new("#EFC851");
    public static readonly Color Cream = new("#FFF6E3");
    public static readonly Color Muted = new("#B9AFA0");
    public static readonly Color Deep = new("#0B1020");
    public static readonly Color Wine = new("#2A0E12");
    public static readonly Color Panel = new(0.05f, 0.06f, 0.10f, 0.92f);
    public static readonly Color PanelLight = new(0.10f, 0.11f, 0.16f, 0.94f);

    private static Font? _titleFont;
    private static Font? _bodyFont;
    private static Font? _boldFont;
    private static Font? _wheelFont;

    public static Font TitleFont => _titleFont ??=
        GD.Load<Font>("res://themes/spectral_bold_shared.tres") ?? ThemeDB.FallbackFont;

    public static Font BodyFont => _bodyFont ??=
        GD.Load<Font>("res://themes/kreon_regular_shared.tres") ?? ThemeDB.FallbackFont;

    public static Font BoldFont => _boldFont ??=
        GD.Load<Font>("res://themes/kreon_bold_glyph_space_one.tres") ?? ThemeDB.FallbackFont;

    /// <summary>Same bold face the game's popup/confirm buttons use, with wider glyph spacing.</summary>
    public static Font WheelFont => _wheelFont ??=
        GD.Load<Font>("res://themes/kreon_bold_glyph_space_two.tres") ?? BoldFont;

    public static Color Hex(string hex)
    {
        return Color.FromHtml(hex);
    }

    public static Color TextOn(Color background)
    {
        float luminance = 0.2126f * background.R + 0.7152f * background.G + 0.0722f * background.B;
        return luminance > 0.62f ? new Color(0.05f, 0.05f, 0.07f) : Cream;
    }

    public static void SetFullRect(Control control)
    {
        control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        control.SetOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    public static Label MakeLabel(
        string text,
        int size = 28,
        Color? color = null,
        bool bold = false,
        HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        label.AddThemeFontOverride("font", bold ? BoldFont : BodyFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color ?? Cream);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.45f));
        label.AddThemeConstantOverride("shadow_offset_x", 2);
        label.AddThemeConstantOverride("shadow_offset_y", 2);
        return label;
    }

    public static Label MakeTitle(string text, int size = 52, HorizontalAlignment alignment = HorizontalAlignment.Center)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.AddThemeFontOverride("font", TitleFont);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Gold);
        label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.55f));
        label.AddThemeConstantOverride("shadow_offset_x", 4);
        label.AddThemeConstantOverride("shadow_offset_y", 4);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", new Color(0.10f, 0.07f, 0.03f, 0.9f));
        return label;
    }

    public static Button MakeButton(string text, Action onPressed, int fontSize = 26)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(260, 56),
            FocusMode = Control.FocusModeEnum.All
        };
        StyleButton(button, fontSize);
        button.Pressed += onPressed;
        return button;
    }

    public static Button MakeSmallButton(string text, Action onPressed, int fontSize = 20)
    {
        var button = MakeButton(text, onPressed, fontSize);
        button.CustomMinimumSize = new Vector2(160, 42);
        return button;
    }

    /// <summary>
    /// A main-menu text button with the game's fuzzy panel behind it, so it keeps the
    /// vanilla label/hover/SFX behaviour but also has a visible vanilla button shape.
    /// </summary>
    public sealed class VanillaTextButton
    {
        private readonly NinePatchRect _background;
        private readonly Color _backgroundTint = Colors.White;

        public Control Root { get; }
        public NMainMenuTextButton Button { get; }

        public VanillaTextButton(Control root, NMainMenuTextButton button, NinePatchRect background)
        {
            Root = root;
            Button = button;
            _background = background;
        }

        public bool Visible
        {
            get => Root.Visible;
            set => Root.Visible = value;
        }

        public Control.SizeFlags SizeFlagsHorizontal
        {
            get => Root.SizeFlagsHorizontal;
            set => Root.SizeFlagsHorizontal = value;
        }

        public void SetEnabled(bool enabled)
        {
            Button.SetEnabled(enabled);
            _background.Modulate = enabled
                ? _backgroundTint
                : new Color(0.55f, 0.55f, 0.55f, 0.8f);
        }

        public void Enable() => SetEnabled(true);

        public void Disable() => SetEnabled(false);

        public void SetText(string text) => SetVanillaButtonText(this, text);
    }

    public static VanillaTextButton MakeVanillaTextButton(
        Node parent,
        string text,
        Action onPressed,
        int fontSize = 32,
        float minWidth = 300f)
    {
        var root = new Control
        {
            CustomMinimumSize = new Vector2(minWidth, 64f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        _buttonPanelTexture ??= GD.Load<Texture2D>(
            "res://images/ui/reward_screen/reward_item_button.png");
        var background = new NinePatchRect
        {
            Texture = _buttonPanelTexture,
            PatchMarginLeft = 18,
            PatchMarginTop = 14,
            PatchMarginRight = 18,
            PatchMarginBottom = 14,
            Modulate = Colors.White,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        SetFullRect(background);
        root.AddChild(background);

        var label = new MegaLabel
        {
            Text = text,
            AutoSizeEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SelfModulate = Cream
        };
        label.Theme = GD.Load<Theme>("res://themes/main_menu_text_button.tres");
        label.AddThemeFontOverride(
            "font",
            GD.Load<Font>("res://themes/kreon_regular_shared.tres") ?? BodyFont);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        label.Resized += () => label.PivotOffset = label.Size * 0.5f;

        var button = new NMainMenuTextButton
        {
            FocusMode = Control.FocusModeEnum.All,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        button.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        button.AddChild(label);

        root.AddChild(button);
        parent.AddChild(root);
        label.Text = text;
        label.PivotOffset = label.Size * 0.5f;

        button.Released += _ => onPressed();
        return new VanillaTextButton(root, button, background);
    }

    public static void SetVanillaButtonText(VanillaTextButton button, string text)
    {
        if (button.Button.label == null)
        {
            return;
        }

        button.Button.label.Text = text;
        button.Button.label.PivotOffset = button.Button.label.Size * 0.5f;
    }

    /// <summary>
    /// Native pause-menu button from the game scene. This is the actual teal
    /// reward_item_button look, not a StyleBoxFlat imitation.
    /// </summary>
    public static NPauseMenuButton MakeNativeButton(
        Node parent,
        string text,
        Action onPressed,
        Vector2 size,
        int fontSize = 28,
        bool redAccent = false,
        bool goldAccent = false)
    {
        PackedScene? scene = GD.Load<PackedScene>("res://scenes/pause_menu/pause_menu_button.tscn");
        if (scene == null)
        {
            throw new InvalidOperationException("pause_menu_button.tscn was not found.");
        }

        NPauseMenuButton button = scene.Instantiate<NPauseMenuButton>();
        button.CustomMinimumSize = size;
        button.Size = size;
        button.FocusMode = Control.FocusModeEnum.All;
        button.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        button.GetNode<Control>("ButtonImage").PivotOffset = size / 2f;

        MegaLabel label = button.GetNode<MegaLabel>("Label");
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.MinFontSize = Mathf.Max(16, fontSize - 8);
        label.MaxFontSize = fontSize;
        label.Text = text;

        if (redAccent && button.GetNodeOrNull<Control>("ButtonImage") is { } image
            && image.Material is ShaderMaterial material)
        {
            var redMaterial = (ShaderMaterial)material.Duplicate();
            redMaterial.SetShaderParameter("h", 0.5f);
            redMaterial.SetShaderParameter("s", 1.1f);
            redMaterial.SetShaderParameter("v", 1.0f);
            image.Material = redMaterial;
            label.AddThemeColorOverride("font_outline_color", new Color(0.35f, 0.07f, 0f, 1f));
        }
        else if (goldAccent && button.GetNodeOrNull<Control>("ButtonImage") is { } goldImage
                 && goldImage.Material is ShaderMaterial goldBase)
        {
            // Rotate the teal button art towards gold; only the image material changes so the
            // label keeps its native cream colour.
            var goldMaterial = (ShaderMaterial)goldBase.Duplicate();
            // 0.62 rotates the teal art into gold; v > 1 lifts the dark base into a bright
            // gold while leaving the label's cream colour untouched.
            goldMaterial.SetShaderParameter("h", 0.62f);
            goldMaterial.SetShaderParameter("s", 0.8f);
            // Match the native button's unhovered v; otherwise the first display is much
            // brighter than every state after the first hover.
            goldMaterial.SetShaderParameter("v", 0.9f);
            goldImage.Material = goldMaterial;
        }

        parent.AddChild(button);
        try
        {
            label.SetTextAutoSize(text);
        }
        catch
        {
            label.Text = text;
        }

        button.Released += _ => onPressed();
        return button;
    }

    public static void SetNativeButtonText(NPauseMenuButton button, string text)
    {
        if (button.GetNodeOrNull<MegaLabel>("Label") is { } label)
        {
            try
            {
                label.SetTextAutoSize(text);
            }
            catch
            {
                label.Text = text;
            }
        }
    }

    /// <summary>
    /// Adds the card rare-glow VFX behind a button while it is hovered/focused.
    /// Uses the same NCardRareGlow scene as rare card rewards.
    /// </summary>
    public static void AttachRareGlowOnHover(NPauseMenuButton button)
    {
        NCardRareGlow? glow = null;

        void Show()
        {
            if (glow != null && GodotObject.IsInstanceValid(glow))
            {
                return;
            }

            glow = NCardRareGlow.Create();
            if (glow == null)
            {
                return;
            }

            glow.Position = button.Size * 0.5f;
            glow.Scale = new Vector2(1.0f, 0.45f);
            button.AddChild(glow);
            button.MoveChild(glow, 0);
        }

        void Hide()
        {
            if (glow == null)
            {
                return;
            }

            NCardRareGlow fading = glow;
            glow = null;
            fading.Kill();

            Tween tween = fading.CreateTween();
            tween.TweenInterval(0.3f);
            tween.TweenCallback(Callable.From(() =>
            {
                if (GodotObject.IsInstanceValid(fading))
                {
                    fading.QueueFree();
                }
            }));
        }

        button.MouseEntered += Show;
        button.MouseExited += Hide;
        button.Focused += _ => Show();
        button.Unfocused += _ => Hide();
    }

    private static Texture2D? LoadFuzzyPanelTexture()
    {
        if (!_fuzzyTextureLoaded)
        {
            _fuzzyPanelTexture = GD.Load<Texture2D>("res://images/ui/fuzzy_nine_patch_char_select.png");
            _fuzzyTextureLoaded = true;
        }

        return _fuzzyPanelTexture;
    }

    public static CheckBox MakeCheckBox(string text, bool value, Action<bool> onChanged, int fontSize = 22)
    {
        var check = new CheckBox
        {
            Text = text,
            ButtonPressed = value,
            FocusMode = Control.FocusModeEnum.All,
            CustomMinimumSize = new Vector2(0, 44)
        };
        check.AddThemeFontOverride("font", BodyFont);
        check.AddThemeFontSizeOverride("font_size", fontSize);
        check.AddThemeColorOverride("font_color", Cream);
        check.AddThemeColorOverride("font_hover_color", Gold);
        check.AddThemeColorOverride("font_pressed_color", Gold);
        check.Toggled += pressed => onChanged(pressed);
        return check;
    }

    public static void StyleButton(Button button, int fontSize = 26)
    {
        var normal = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.09f, 0.14f, 0.96f),
            BorderColor = new Color(0.55f, 0.43f, 0.19f, 0.95f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 8,
            CornerRadiusTopRight = 8,
            CornerRadiusBottomLeft = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft = 18,
            ContentMarginRight = 18,
            ContentMarginTop = 8,
            ContentMarginBottom = 8
        };
        var hover = (StyleBoxFlat)normal.Duplicate();
        hover.BgColor = new Color(0.14f, 0.13f, 0.16f, 0.98f);
        hover.BorderColor = Gold;
        var pressed = (StyleBoxFlat)normal.Duplicate();
        pressed.BgColor = new Color(0.20f, 0.15f, 0.08f, 0.98f);
        pressed.BorderColor = Gold;
        var disabled = (StyleBoxFlat)normal.Duplicate();
        disabled.BgColor = new Color(0.06f, 0.06f, 0.08f, 0.75f);
        disabled.BorderColor = new Color(0.35f, 0.30f, 0.22f, 0.6f);

        button.AddThemeStyleboxOverride("normal", normal);
        button.AddThemeStyleboxOverride("hover", hover);
        button.AddThemeStyleboxOverride("pressed", pressed);
        button.AddThemeStyleboxOverride("disabled", disabled);
        button.AddThemeStyleboxOverride("focus", hover);
        button.AddThemeFontOverride("font", BoldFont);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        button.AddThemeColorOverride("font_color", Cream);
        button.AddThemeColorOverride("font_hover_color", Gold);
        button.AddThemeColorOverride("font_pressed_color", Gold);
        button.AddThemeColorOverride("font_disabled_color", new Color(0.55f, 0.53f, 0.50f));
    }

    private static Texture2D? _fuzzyPanelTexture;
    private static Texture2D? _buttonPanelTexture;
    private static bool _fuzzyTextureLoaded;

    public static PanelContainer MakePanel(Color? color = null)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.33f, 0.40f, 0.30f),
            BorderColor = new Color(0.35f, 0.82f, 0.90f, 0.55f),
            BorderWidthLeft = 0,
            BorderWidthTop = 1,
            BorderWidthRight = 0,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 2,
            CornerRadiusTopRight = 2,
            CornerRadiusBottomLeft = 2,
            CornerRadiusBottomRight = 2,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 18,
            ContentMarginBottom = 18
        };

        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    /// <summary>
    /// The game's slate stone panel (used by the reward screen), nine-patched so it can size
    /// to the lobby player list, active modifier list and summary sections without distortion.
    /// </summary>
    public static PanelContainer MakeStonePanel(int textureMargin = 64, int contentMargin = 64)
    {
        try
        {
            Texture2D? texture = GD.Load<Texture2D>(
                "res://images/ui/reward_screen/reward_panel.png");
            if (texture != null)
            {
                var style = new StyleBoxTexture
                {
                    Texture = texture,
                    TextureMarginLeft = textureMargin,
                    TextureMarginTop = textureMargin,
                    TextureMarginRight = textureMargin,
                    TextureMarginBottom = textureMargin,
                    ContentMarginLeft = contentMargin,
                    ContentMarginRight = contentMargin,
                    ContentMarginTop = Math.Max(12, contentMargin - 4),
                    ContentMarginBottom = Math.Max(12, contentMargin - 4)
                };

                var panel = new PanelContainer();
                panel.AddThemeStyleboxOverride("panel", style);
                return panel;
            }
        }
        catch
        {
            // Fall through to the flat teal panel if the asset cannot be loaded.
        }

        return MakePanel();
    }

    public static TextureRect MakeCharacterIcon(CharacterModel character, float size = 56f)
    {
        Texture2D? texture = null;
        try
        {
            texture = character.IconTexture;
        }
        catch
        {
            // Modded characters may not ship the standard top-panel icon.
        }

        return new TextureRect
        {
            CustomMinimumSize = new Vector2(size, size),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Texture = texture
        };
    }

    public static TextureRect? MakeVoteIcon(CharacterModel character, float size = 48f)
    {
        PackedScene? scene = GD.Load<PackedScene>("res://scenes/ui/multiplayer_vote_icon.tscn");
        if (scene == null)
        {
            return null;
        }

        Texture2D? texture = null;
        Texture2D? outline = null;
        try
        {
            texture = character.IconTexture;
            outline = character.IconOutlineTexture;
        }
        catch
        {
            // Fall back to whatever the scene provides.
        }

        var icon = scene.Instantiate<TextureRect>();
        icon.CustomMinimumSize = new Vector2(size, size);
        icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        icon.MouseFilter = Control.MouseFilterEnum.Ignore;
        icon.Texture = texture;
        if (icon.GetNodeOrNull<TextureRect>("Outline") is { } outlineNode && outline != null)
        {
            outlineNode.Texture = outline;
        }

        return icon;
    }

    public static TextureRect MakeGradientBackground()
    {
        var gradient = new Gradient();
        gradient.SetColor(0, Deep);
        gradient.SetColor(1, Wine);
        var texture = new GradientTexture2D
        {
            Gradient = gradient,
            Width = 1920,
            Height = 1080,
            Fill = GradientTexture2D.FillEnum.Linear,
            FillFrom = new Vector2(0, 0),
            FillTo = new Vector2(1, 1)
        };
        var rect = new TextureRect
        {
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        SetFullRect(rect);
        return rect;
    }

    public static HSeparator MakeSeparator()
    {
        var separator = new HSeparator
        {
            Modulate = new Color(1, 1, 1, 0.25f),
            CustomMinimumSize = new Vector2(0, 2)
        };
        return separator;
    }
}
