using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace SpinTheSpire.SpinTheSpireCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "SpinTheSpire";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        var assembly = Assembly.GetExecutingAssembly();

        SpinConfig.Load();

        Harmony harmony = new(ModId);
        harmony.PatchAll(assembly);

        Logger.Info(
            $"Loaded. StartInVanilla={SpinConfig.StartInVanilla}, Duration={SpinConfig.DurationSeconds:0}s");
    }
}
