﻿using HarmonyLib;
using RimWorld;
using System.Reflection;

namespace DubsAnalyzer
{
    [ProfileMode("DoTabs", UpdateMode.GUI)]
    [HarmonyPatch(typeof(InspectPaneUtility), "DoTabs")]
    internal class H_DoTabs
    {
        public static bool Active=false;

        [HarmonyPriority(Priority.Last)]
        public static void Prefix(MethodBase __originalMethod, ref Profiler __state)
        {
            if (Active)
            {
                __state = Analyzer.Start("InspectPaneUtility.DoTabs", null, null, null, null, __originalMethod);
            }
        }

        [HarmonyPriority(Priority.First)]
        public static void Postfix(Profiler __state)
        {
            if (Active)
            {
                __state.Stop();
            }
        }
    }
}