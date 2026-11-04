using System;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace SpinTheSpire.SpinTheSpireCode;

/// <summary>
/// Adds the mod's two settings rows to Settings > General. Uses the game's own settings
/// scenes so the rows look native without shipping a PCK.
/// </summary>
internal static class SpinSettings
{
    private const string VBoxPath = "ScrollContainer/Mask/Clipper/GeneralSettings/VBoxContainer";
    private const string TemplateRowName = "ShowMpDrawings";
    private const string TemplateDividerName = "ShowMpDrawingsDivider";
    private const string TickboxScenePath = "res://scenes/screens/settings_tickbox.tscn";
    private const string SliderScenePath = "res://scenes/screens/settings_slider.tscn";

    private static Control? _ticked;
    private static Control? _unticked;
    private static Control? _manualTicked;
    private static Control? _manualUnticked;
    private static Control? _manualSliderRow;
    private static Control? _manualSlider;
    private static Label? _manualValueLabel;

    public static void Install(NSettingsScreen screen)
    {
        try
        {
            InstallCore(screen);
        }
        catch (Exception e)
        {
            MainFile.Logger.Warn($"Could not add the Spin the Spire settings rows: {e}");
        }
    }

    private static void InstallCore(NSettingsScreen screen)
    {
        Control? vbox = screen.GetNodeOrNull<Control>(VBoxPath);
        if (vbox == null)
        {
            MainFile.Logger.Warn("Settings list not found; settings will use the config file defaults.");
            return;
        }

        if (vbox.GetNodeOrNull<Control>("SpinTheSpireVanillaRow") != null)
        {
            return;
        }

        Control? templateRow = vbox.GetNodeOrNull<Control>(TemplateRowName);
        Control? templateDivider = vbox.GetNodeOrNull<Control>(TemplateDividerName);
        RichTextLabel? templateLabel = templateRow?.GetNodeOrNull<RichTextLabel>("Label");
        if (templateRow == null || templateDivider == null || templateLabel == null)
        {
            MainFile.Logger.Warn("Settings template row not found; mod settings were not added.");
            return;
        }

        PackedScene? tickboxScene = GD.Load<PackedScene>(TickboxScenePath);
        PackedScene? sliderScene = GD.Load<PackedScene>(SliderScenePath);
        if (tickboxScene == null || sliderScene == null)
        {
            MainFile.Logger.Warn("Settings scenes could not be loaded; mod settings were not added.");
            return;
        }

        Control tickbox = tickboxScene.Instantiate<Control>();
        tickbox.Name = "SpinTheSpireVanillaTickbox";
        tickbox.MouseFilter = Control.MouseFilterEnum.Stop;
        tickbox.FocusMode = Control.FocusModeEnum.All;
        tickbox.GuiInput += OnTickboxInput;
        tickbox.GetNodeOrNull<Control>("SelectionReticle")?.Hide();
        _ticked = tickbox.GetNodeOrNull<Control>("TickboxVisuals/Ticked");
        _unticked = tickbox.GetNodeOrNull<Control>("TickboxVisuals/NotTicked");

        Control manualTickbox = tickboxScene.Instantiate<Control>();
        manualTickbox.Name = "SpinTheSpireManualTickbox";
        manualTickbox.MouseFilter = Control.MouseFilterEnum.Stop;
        manualTickbox.FocusMode = Control.FocusModeEnum.All;
        manualTickbox.GuiInput += OnManualTickboxInput;
        manualTickbox.GetNodeOrNull<Control>("SelectionReticle")?.Hide();
        _manualTicked = manualTickbox.GetNodeOrNull<Control>("TickboxVisuals/Ticked");
        _manualUnticked = manualTickbox.GetNodeOrNull<Control>("TickboxVisuals/NotTicked");

        AddSettingRow(
            vbox,
            templateLabel,
            templateDivider,
            "[Spin the Spire] Roll manually",
            manualTickbox,
            "SpinTheSpireManualRow",
            "SpinTheSpireManualDivider");

        MarginContainer coefficientRow = AddSettingRow(
            vbox,
            templateLabel,
            templateDivider,
            "[Spin the Spire] Modifier roll coefficient",
            CreateSlider(
                sliderScene,
                0f,
                5f,
                1f,
                SpinConfig.RollCoefficient,
                value => $"×{Mathf.RoundToInt(value)}",
                value => SpinConfig.SetRollCoefficient(Mathf.RoundToInt(value))),
            "SpinTheSpireCoefficientRow",
            "SpinTheSpireCoefficientDivider");

        SetSliderValue(coefficientRow, SpinConfig.RollCoefficient);
        _manualSliderRow = coefficientRow;
        _manualSlider = coefficientRow.GetNodeOrNull<Control>("SettingsSlider/Slider");
        _manualValueLabel = coefficientRow.GetNodeOrNull<Label>("SettingsSlider/SliderValue");

        AddSettingRow(
            vbox,
            templateLabel,
            templateDivider,
            "[Spin the Spire] Start in the vanilla custom screen",
            tickbox,
            "SpinTheSpireVanillaRow",
            "SpinTheSpireVanillaDivider");

        MarginContainer durationRow = AddSettingRow(
            vbox,
            templateLabel,
            templateDivider,
            "[Spin the Spire] Spin duration cap",
            CreateSlider(
                sliderScene,
                SpinConfig.MinDurationSeconds,
                SpinConfig.MaxDurationSeconds,
                1f,
                SpinConfig.DurationSeconds,
                value => $"{Mathf.RoundToInt(value)}s",
                SpinConfig.SetDurationSeconds),
            "SpinTheSpireDurationRow",
            "SpinTheSpireDurationDivider");

        SetSliderValue(durationRow, SpinConfig.DurationSeconds);

        SpinConfig.Changed -= UpdateSettingsVisuals;
        SpinConfig.Changed += UpdateSettingsVisuals;
        UpdateSettingsVisuals();

        if (screen.GetNodeOrNull<Control>("ScrollContainer/Mask/Clipper/GeneralSettings") is { } panel
            && panel.HasMethod("RefreshSize"))
        {
            panel.CallDeferred("RefreshSize");
        }

        MainFile.Logger.Info("Spin the Spire settings rows added to Settings > General.");
    }

    private static MarginContainer AddSettingRow(
        Control vbox,
        RichTextLabel labelTemplate,
        Control dividerTemplate,
        string text,
        Control content,
        string rowName,
        string dividerName)
    {
        RichTextLabel label = (RichTextLabel)labelTemplate.Duplicate();
        label.Text = text;

        var row = new MarginContainer { Name = rowName, CustomMinimumSize = new Vector2(0f, 64f) };
        row.AddThemeConstantOverride("margin_left", 12);
        row.AddThemeConstantOverride("margin_right", 12);
        row.AddThemeConstantOverride("margin_top", 0);
        row.AddThemeConstantOverride("margin_bottom", 0);
        row.AddChild(label);
        row.AddChild(content);

        Control divider = (Control)dividerTemplate.Duplicate();
        divider.Name = dividerName;

        vbox.AddChild(divider);
        vbox.AddChild(row);

        return row;
    }

    private static void SetSliderValue(Control row, float value)
    {
        if (row.GetNodeOrNull<NSlider>("SettingsSlider/Slider") is { } slider)
        {
            slider.SetValueWithoutAnimation(value);
        }
    }

    private static Control CreateSlider(
        PackedScene scene,
        float min,
        float max,
        float step,
        float value,
        Func<float, string> format,
        Action<float> onChanged)
    {
        Control root = scene.Instantiate<Control>();
        root.Name = "SettingsSlider";
        root.GetNodeOrNull<Control>("SelectionReticle")?.Hide();

        if (root.GetNodeOrNull<Godot.Range>("Slider") is not { } slider)
        {
            return root;
        }

        slider.MinValue = min;
        slider.MaxValue = max;
        slider.Step = step;
        slider.Value = value;

        Label? valueLabel = root.GetNodeOrNull<Label>("SliderValue");
        if (valueLabel != null)
        {
            valueLabel.Text = format(value);
        }

        slider.ValueChanged += raw =>
        {
            float current = (float)raw;
            onChanged(current);

            if (valueLabel != null && GodotObject.IsInstanceValid(valueLabel))
            {
                valueLabel.Text = format(current);
            }
        };

        return root;
    }

    private static void OnTickboxInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            SpinConfig.SetStartInVanilla(!SpinConfig.StartInVanilla);
        }
    }

    private static void OnManualTickboxInput(InputEvent inputEvent)
    {
        if (inputEvent is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
        {
            SpinConfig.SetRollManually(!SpinConfig.RollManually);
        }
    }

    private static void UpdateSettingsVisuals()
    {
        if (_ticked != null && GodotObject.IsInstanceValid(_ticked))
        {
            _ticked.Visible = SpinConfig.StartInVanilla;
        }

        if (_unticked != null && GodotObject.IsInstanceValid(_unticked))
        {
            _unticked.Visible = !SpinConfig.StartInVanilla;
        }

        if (_manualTicked != null && GodotObject.IsInstanceValid(_manualTicked))
        {
            _manualTicked.Visible = SpinConfig.RollManually;
        }

        if (_manualUnticked != null && GodotObject.IsInstanceValid(_manualUnticked))
        {
            _manualUnticked.Visible = !SpinConfig.RollManually;
        }

        if (_manualValueLabel != null && GodotObject.IsInstanceValid(_manualValueLabel))
        {
            _manualValueLabel.Text = $"×{SpinConfig.RollCoefficient}";
        }

        if (_manualSliderRow != null && GodotObject.IsInstanceValid(_manualSliderRow))
        {
            bool enabled = !SpinConfig.RollManually;
            _manualSliderRow.Modulate = enabled
                ? Colors.White
                : new Color(1f, 1f, 1f, 0.45f);
        }

        if (_manualSlider != null && GodotObject.IsInstanceValid(_manualSlider))
        {
            bool enabled = !SpinConfig.RollManually;
            _manualSlider.MouseFilter = enabled
                ? Control.MouseFilterEnum.Stop
                : Control.MouseFilterEnum.Ignore;
            _manualSlider.FocusMode = enabled
                ? Control.FocusModeEnum.All
                : Control.FocusModeEnum.None;
        }

        if (_manualSliderRow?.GetNodeOrNull<NSlider>("SettingsSlider/Slider") is { } coefficient
            && !Mathf.IsEqualApprox(coefficient.Value, SpinConfig.RollCoefficient))
        {
            coefficient.SetValueWithoutAnimation(SpinConfig.RollCoefficient);
        }
    }
}
