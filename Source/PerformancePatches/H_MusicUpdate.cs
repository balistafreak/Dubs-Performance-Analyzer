﻿using HarmonyLib;
using RimWorld;
using Verse;

namespace DubsAnalyzer
{
    [PerformancePatch]
    internal class H_MusicUpdate
    {
        public static void PerformancePatch(Harmony harmony)
        {
            var skiff = AccessTools.Method(typeof(MusicManagerPlay), nameof(MusicManagerPlay.MusicUpdate));
            harmony.Patch(skiff, new HarmonyMethod(typeof(H_MusicUpdate), nameof(Prefix)));
        }

        public static bool Prefix()
        {
            if (Analyzer.Settings.KillMusicMan)
            {
                return false;
            }
            return true;
        }
    }
}