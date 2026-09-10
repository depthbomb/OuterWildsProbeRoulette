using HarmonyLib;
using OWML.Common;
using OWML.ModHelper;
using System;
using UnityEngine;

namespace ProbeRoulette;

public sealed class ProbeRouletteMod : ModBehaviour
{
    private         Harmony          _harmony;
    internal        int              FirstLoop          { get; private set; } = 7;
    internal        float            Chance             { get; private set; } = 10f;
    internal        string           Selection          { get; private set; } = "Random (50/50)";
    internal        bool             LogApproach        { get; private set; }
    internal        bool             TargetingEnabled   { get; private set; } = true;
    internal        bool             PlayerHitsKill     { get; private set; } = true;
    internal        float            ProbeHitRadius     { get; private set; } = 1f;
    internal        DeathType        PlayerHitDeathType { get; private set; } = DeathType.Impact;
    internal static ProbeRouletteMod Instance           { get; private set; }

    private void Start()
    {
        Instance = this;
        _harmony = new Harmony(ModHelper.Manifest.UniqueName);
        try
        {
            _harmony.PatchAll(typeof(ProbeRouletteMod).Assembly);
            Log($"Loaded. First loop: {FirstLoop}; targeted chance: {Chance:0.#}%; selection: {Selection}.");
        }
        catch (Exception exception)
        {
            // Don't leave half the mod running if a game update changed a patch target.
            _harmony.UnpatchSelf();
            TargetingEnabled = false;
            Log("Patch setup failed; targeting is disabled. " + exception, MessageType.Error);
        }
    }

    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void Configure(IModConfig config)
    {
        FirstLoop        = Math.Max(1, ReadSetting(config, "First targeted loop", 7));
        Chance           = FiniteClamp(ReadSetting(config, "Targeted shot chance (%)", 10f), 0f, 100f, 10f);
        Selection        = ReadSetting(config, "Target selection", "Random (50/50)");
        LogApproach      = ReadSetting(config, "Log closest approach", false);
        TargetingEnabled = config != null && config.Enabled;
        PlayerHitsKill   = ReadSetting(config, "Probe hits kill player", true);
        ProbeHitRadius   = FiniteClamp(ReadSetting(config, "Probe hit radius (m)", 1f), 0.1f, 10f, 1f);

        var deathChoice = ReadSetting(config, "Player hit death type", "Impact");
        PlayerHitDeathType = deathChoice switch
        {
            "Crushed"      => DeathType.Crushed,
            "Asphyxiation" => DeathType.Asphyxiation,
            "Energy"       => DeathType.Energy,
            "Lava"         => DeathType.Lava,
            "Digestion"    => DeathType.Digestion,
            _              => DeathType.Impact
        };
    }

    internal void Log(string message, MessageType type = MessageType.Info)
    {
        ModHelper.Console.WriteLine("[Probe Roulette] " + message, type);
    }

    private T ReadSetting<T>(IModConfig config, string name, T fallback)
    {
        if (config?.Settings == null || !config.Settings.ContainsKey(name))
        {
            return fallback;
        }

        try
        {
            return config.GetSettingsValue<T>(name);
        }
        catch (Exception)
        {
            // Hand-edited config can get messy. One bad setting shouldn't stop the whole mod.
            ModHelper?.Console.WriteLine($"[Probe Roulette] Invalid setting '{name}'; using {fallback}.", MessageType.Warning);

            return fallback;
        }
    }

    private static float FiniteClamp(float value, float minimum, float maximum, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);
}

[HarmonyPatch(typeof(OrbitalProbeLaunchController))]
internal static class CannonPatches
{
    [HarmonyPostfix]
    [HarmonyPatch("OnStartOfTimeLoop")]
    private static void OnLoopStarted(OrbitalProbeLaunchController __instance,
                                      int                          loopCount,
                                      OWRigidbody                  ____probeBody,
                                      OWRigidbody[]                ____fakeDebrisBodies)
    {
        var mod = ProbeRouletteMod.Instance;
        if (mod == null || !mod.TargetingEnabled || LoadManager.GetCurrentScene() != OWScene.SolarSystem)
        {
            return;
        }

        try
        {
            var state = __instance.GetComponent<LaunchTargeting>();
            if (state == null)
            {
                state = __instance.gameObject.AddComponent<LaunchTargeting>();
                state.Initialize(__instance, ____probeBody, ____fakeDebrisBodies, loopCount, mod);
            }
        }
        catch (Exception exception)
        {
            mod.Log("Could not prepare targeting; normal launch retained. " + exception, MessageType.Error);
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch("LaunchProbe")]
    private static void BeforeLaunch(OrbitalProbeLaunchController __instance)
    {
        try
        {
            __instance.GetComponent<LaunchTargeting>()?.PrepareLaunch();
        }
        catch (Exception exception)
        {
            ProbeRouletteMod.Instance?.Log("Could not refine aim; existing launch retained. " + exception, MessageType.Error);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch("LaunchProbe")]
    private static void AfterLaunch(OrbitalProbeLaunchController __instance, OWRigidbody ____probeBody)
    {
        var mod = ProbeRouletteMod.Instance;
        // Always release the debris, even if targeting was disabled or the aim calculation failed.
        __instance.GetComponent<LaunchTargeting>()?.CompleteLaunch();
        if (mod                           == null                || !mod.TargetingEnabled || ____probeBody == null ||
            LoadManager.GetCurrentScene() != OWScene.SolarSystem || PlayerData.LoadLoopCount()             < mod.FirstLoop)
        {
            return;
        }

        try
        {
            if (____probeBody.GetComponent<ProbePlayerHitDetector>() == null)
            {
                ____probeBody.gameObject.AddComponent<ProbePlayerHitDetector>().Initialize(____probeBody, mod);
            }
        }
        catch (Exception exception)
        {
            mod.Log("Could not attach player hit detection. " + exception, MessageType.Error);
        }
    }
}
