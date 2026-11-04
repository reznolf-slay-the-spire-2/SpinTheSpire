using Godot;

namespace SpinTheSpire.SpinTheSpireCode;

/// <summary>
/// Small persisted configuration. Kept in Godot's user:// directory so it never ends up
/// inside the mod folder (the mod loader scans every JSON file there).
/// </summary>
public static class SpinConfig
{
    private const string ConfigPath = "user://spin_the_spire.cfg";
    private const string Section = "spin_the_spire";

    public const float DefaultDurationSeconds = 60f;
    public const float MinDurationSeconds = 15f;
    public const float MaxDurationSeconds = 120f;

    public const int DefaultRollCoefficient = 2;
    public const int MinRollCoefficient = 0;
    public const int MaxRollCoefficient = 5;

    public static bool StartInVanilla { get; private set; }
    public static float DurationSeconds { get; private set; } = DefaultDurationSeconds;
    public static bool RollManually { get; private set; }
    public static int RollCoefficient { get; private set; } = DefaultRollCoefficient;

    public static event System.Action? Changed;

    public static void Load()
    {
        try
        {
            var file = new ConfigFile();
            Error error = file.Load(ConfigPath);
            if (error == Error.Ok)
            {
                StartInVanilla = (bool)file.GetValue(Section, "start_in_vanilla", false);
                DurationSeconds = Mathf.Clamp(
                    (float)file.GetValue(Section, "duration_seconds", DefaultDurationSeconds),
                    MinDurationSeconds,
                    MaxDurationSeconds);
                RollManually = (bool)file.GetValue(Section, "roll_manually", false);
                RollCoefficient = Mathf.Clamp(
                    (int)file.GetValue(Section, "roll_coefficient", DefaultRollCoefficient),
                    MinRollCoefficient,
                    MaxRollCoefficient);
            }
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Warn($"Could not load config, using defaults: {e.Message}");
        }
    }

    public static void Save()
    {
        try
        {
            var file = new ConfigFile();
            file.SetValue(Section, "start_in_vanilla", StartInVanilla);
            file.SetValue(Section, "duration_seconds", DurationSeconds);
            file.SetValue(Section, "roll_manually", RollManually);
            file.SetValue(Section, "roll_coefficient", RollCoefficient);
            Error error = file.Save(ConfigPath);
            if (error != Error.Ok)
            {
                MainFile.Logger.Warn($"Could not save config: {error}");
            }
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Warn($"Could not save config: {e.Message}");
        }
    }

    public static void SetStartInVanilla(bool value)
    {
        if (StartInVanilla == value)
        {
            return;
        }

        StartInVanilla = value;
        Save();
        Changed?.Invoke();
    }

    public static void SetRollManually(bool value)
    {
        if (RollManually == value)
        {
            return;
        }

        RollManually = value;
        Save();
        Changed?.Invoke();
    }

    public static void SetRollCoefficient(int value)
    {
        int clamped = Mathf.Clamp(value, MinRollCoefficient, MaxRollCoefficient);
        if (RollCoefficient == clamped)
        {
            return;
        }

        RollCoefficient = clamped;
        Save();
        Changed?.Invoke();
    }

    public static void SetDurationSeconds(float value)
    {
        float clamped = Mathf.Clamp(value, MinDurationSeconds, MaxDurationSeconds);
        if (Mathf.IsEqualApprox(DurationSeconds, clamped))
        {
            return;
        }

        DurationSeconds = clamped;
        Save();
        Changed?.Invoke();
    }
}
