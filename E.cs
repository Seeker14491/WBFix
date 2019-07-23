using System.Reflection;
using Harmony;
using JetBrains.Annotations;
using Spectrum.API.Configuration;
using Spectrum.API.Interfaces.Plugins;
using Spectrum.API.Interfaces.Systems;
using UnityEngine;

namespace WBFix
{
    [UsedImplicitly]
    public class E : IPlugin, IUpdatable
    {
        public static Settings Settings;

        private static NetworkingManager _networkingManager;

        [PublicAPI] public static bool AllowGameplayCheatsInMultiplayer;

        internal static int FramesSinceJump;

        private static UILabel _watermark;

        public void Initialize(IManager manager, string ipcIdentifier)
        {
            Settings = InitializeSettings();

            _watermark = GetAndActivateWatermark();

            var harmony = HarmonyInstance.Create("com.seekr.wbfix");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }

        public void Update()
        {
            _networkingManager = G.Sys.NetworkingManager_;
            var playerManager = G.Sys.PlayerManager_;
            var localPlayer = playerManager ? playerManager.Current_ : null;
            var playerDataLocal = localPlayer?.playerData_;
            var carLogic = playerDataLocal ? playerDataLocal.CarLogic_ : null;
            var boostGadget = carLogic ? carLogic.Boost_ : null;

            var mult = boostGadget ? $"{boostGadget.accelerationMul_:F2}" : "---";
            _watermark.text = $"Boost Mult: {mult}";
        }

        private static Settings InitializeSettings()
        {
            var settings = new Settings("settings");

            var entries = new[]
            {
                new SettingsEntry("debug", false),
                new SettingsEntry("default_boost_multiplier", 1.05f),
                new SettingsEntry("jump_boost_multiplier", 0.79f),
                new SettingsEntry("jump_boost_multiplier_frames", 60f),
                new SettingsEntry("wheel_threshold", 1)
            };

            foreach (var s in entries)
            {
                if (!settings.ContainsKey(s.Name))
                {
                    settings.Add(s.Name, s.DefaultVal);
                }
            }

            settings.Save();
            return settings;
        }

        internal static bool GameplayCheatsAllowed()
        {
            return AllowGameplayCheatsInMultiplayer || !(_networkingManager && _networkingManager.IsOnline_);
        }

        private static UILabel GetAndActivateWatermark()
        {
            var anchorAlphaVersion = GameObject.Find("UI Root").transform.Find("Panel/Anchor : AlphaVersion");
            var alphaVersion = anchorAlphaVersion.Find("AlphaVersion");

            anchorAlphaVersion.gameObject.SetActive(true);
            alphaVersion.gameObject.SetActive(true);

            return alphaVersion.GetComponent<UILabel>();
        }
    }

    internal class SettingsEntry
    {
        public readonly string Name;
        public readonly object DefaultVal;

        public SettingsEntry(string name, object defaultVal)
        {
            Name = name;
            DefaultVal = defaultVal;
        }
    }

    [HarmonyPatch(typeof(CheatsManager))]
    [HarmonyPatch("GameplayCheatsUsedThisLevel_", MethodType.Getter)]
    internal static class BlockLeaderboardUpdatingWhenCheating
    {
        [UsedImplicitly]
        // ReSharper disable once InconsistentNaming
        private static void Postfix(out bool __result)
        {
            __result = true;
        }
    }
    
    [HarmonyPatch(typeof(BoostGadget), "GadgetFixedUpdate")]
    internal static class WbFixImpl
    {
        private static float _prevJumpTimer;
        
        [UsedImplicitly]
        // ReSharper disable once InconsistentNaming
        private static bool Prefix(BoostGadget __instance)
        {
            var carLogic = Traverse.Create(__instance).Field("carLogic_").GetValue<CarLogic>();
            var carStats = carLogic ? carLogic.CarStats_ : null;
            var jumpGadget = carLogic ? carLogic.Jump_ : null;
            
            if (!carLogic.IsLocalCar_ || jumpGadget == null || !E.GameplayCheatsAllowed())
            {
                return true;
            }

            var debug = E.Settings.GetItem<bool>("debug");
            var jumpTimer = Traverse.Create(jumpGadget).Field("jumpTimer_").GetValue<float>();

            if (Timex.PhysicsFrameCount_ % 50 == 0 && debug)
            {
                System.Console.WriteLine($"Boost Mult: {__instance.accelerationMul_}x; FramesSinceJump: {E.FramesSinceJump}");
            }

            var wheelsContacting = Traverse.Create(carStats).Field("wheelsContactingSmooth_").GetValue<int>();
            var wheelThreshold = E.Settings.GetItem<int>("wheel_threshold");

            if (jumpTimer < _prevJumpTimer) // Just jumped
            {
                if (debug)
                {
                    System.Console.WriteLine("Jumped");
                }
                
                E.FramesSinceJump = 0;
            }
            else
            {
                E.FramesSinceJump += 1;
            }

            if (wheelsContacting >= wheelThreshold ||
                E.FramesSinceJump >= E.Settings.GetItem<int>("jump_boost_multiplier_frames"))
            {
                __instance.accelerationMul_ = E.Settings.GetItem<float>("default_boost_multiplier");
            }
            else
            {
                __instance.accelerationMul_ = E.Settings.GetItem<float>("jump_boost_multiplier");
            }

            _prevJumpTimer = jumpTimer;

            return true;
        }
    }

    [HarmonyPatch(typeof(CarLogic), "OnEventCarDeath")]
    internal static class PatchCarDeath
    {
        [UsedImplicitly]
        private static void Postfix()
        {
            E.FramesSinceJump = E.Settings.GetItem<int>("jump_boost_multiplier_frames");
        }
    }

    [HarmonyPatch(typeof(GameManager), "SceneLoaded")]
    internal static class PatchSceneLoaded
    {
        [UsedImplicitly]
        private static void Postfix()
        {
            E.FramesSinceJump = E.Settings.GetItem<int>("jump_boost_multiplier_frames");
        }
    }

    [HarmonyPatch(typeof(BoostGadget), "SetFlameIntensity")]
    internal static class PatchWbFixFlameColor
    {
        [UsedImplicitly]
        private static bool Prefix(ref float intensity)
        {
            var buffMult = E.Settings.GetItem<float>("default_boost_multiplier");
            var nerfMult = E.Settings.GetItem<float>("jump_boost_multiplier");
            // ReSharper disable once CompareOfFloatsByEqualityOperator
            if (buffMult != nerfMult)
            {
                intensity = (intensity + buffMult - 2.0f * nerfMult) / (buffMult - nerfMult);
            }

            return true;
        }
    }
}