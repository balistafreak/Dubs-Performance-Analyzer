﻿using System;
using HarmonyLib;
using Verse;

namespace DubsAnalyzer
{
    //[HarmonyPatch(typeof(StaticConstructorOnStartupUtility), nameof(StaticConstructorOnStartupUtility.CallAll))]
    //internal static class HarmonyCallAll
    //{

    //    public static void Postfix()
    //    {
    //        try
    //        {
    //            foreach (var item in DefDatabase<ThingDef>.AllDefsListForReading)
    //            {
    //                if (!item.comps.Any(x => x.compClass == typeof(TickOptimizer)))
    //                {
    //                    item.comps.Insert(0, new CompProperties { compClass = typeof(TickOptimizer) });
    //                }
    //            }

    //            Log.Warning("Tick optimizer inserted everywhere");
    //        }
    //        catch (Exception e)
    //        {
    //            Log.Error("Something went wrong with tickoptiplus startup");
    //        }
    //    }
    //}


    //[HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.Tick))]
    //internal static class TickOptiPlus
    //{ 
    //    private static bool Prefix(ThingWithComps __instance)
    //    {
    //        if (TickOptimizer.Tickcomp_optimizer)
    //        {
    //             __instance.comps[0].CompTick();
    //            return false;

    //        }
    //        return true;
    //    }
    //}

    //public class TickChain
    //{
    //    public TickChain next;
    //    public ThingComp me;

    //    public void Tick()
    //    {
    //        me.CompTick();
    //        next?.Tick();
    //    }
    //}

    //public class TickOptimizer : ThingComp
    //{

    //    [TweakValue("__Profiler")]
    //    public static bool Tickcomp_optimizer = false;

    //    public List<TickChain> Chain = new List<TickChain>();
    //    public TickChain alpha;

    //    public override void CompTick()
    //    {
    //        if (Tickcomp_optimizer)
    //        {
    //            alpha?.Tick();
    //        }
    //        //   Log.Warning("K", true);
    //    }

    //    public override void PostSpawnSetup(bool respawningAfterLoad)
    //    {
    //        base.PostSpawnSetup(respawningAfterLoad);
    //        if (!parent.comps.NullOrEmpty() && parent.def.tickerType == TickerType.Normal)
    //        {
    //            CreateChain(parent.comps);
    //        }
    //    }

    //    public void CreateChain(List<ThingComp> comps)
    //    {
    //        foreach (var thingComp in comps)
    //        {
    //            if (thingComp is TickOptimizer)
    //            {

    //            }
    //            else
    //            {
    //                TickChain tc = new TickChain { me = thingComp };
    //                Chain.Add(tc);
    //            }
    //        }

    //        for (var i = 0; i < Chain.Count; i++)
    //        {
    //            var cc = Chain[i];
    //            cc.next = Chain.ElementAtOrDefault(i + 1);
    //        }

    //        alpha = Chain.ElementAtOrDefault(0);
    //    }
    //}


    //[HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.JobTrackerTick))]
    //internal class JobTrackerTick
    //{
    //    [TweakValue("__Profiler", 30, 3000)]
    //    public static int JobTrackerTick_Interval = 30;

    //    private static bool Prefix(Pawn_JobTracker __instance)
    //    {
    //        __instance.jobsGivenThisTick = 0;
    //        __instance.jobsGivenThisTickTextual = string.Empty;
    //        if (__instance.pawn.IsHashIntervalTick(JobTrackerTick_Interval))
    //        {
    //            var thinkResult = __instance.DetermineNextConstantThinkTreeJob();
    //            if (thinkResult.IsValid && __instance.ShouldStartJobFromThinkTree(thinkResult))
    //            {
    //                __instance.CheckLeaveJoinableLordBecauseJobIssued(thinkResult);
    //                __instance.StartJob(thinkResult.Job, JobCondition.InterruptForced, thinkResult.SourceNode,
    //                    false, false, __instance.pawn.thinker.ConstantThinkTree, thinkResult.Tag);
    //            }
    //        }

    //        if (__instance.curDriver != null)
    //        {
    //            if (__instance.curJob.expiryInterval > 0 &&
    //                (Find.TickManager.TicksGame - __instance.curJob.startTick) % __instance.curJob.expiryInterval ==
    //                0 && Find.TickManager.TicksGame != __instance.curJob.startTick)
    //            {
    //                if (!__instance.curJob.expireRequiresEnemiesNearby ||
    //                    PawnUtility.EnemiesAreNearby(__instance.pawn, 25))
    //                {
    //                    if (__instance.debugLog)
    //                    {
    //                        __instance.DebugLogEvent("Job expire");
    //                    }

    //                    if (!__instance.curJob.checkOverrideOnExpire)
    //                    {
    //                        __instance.EndCurrentJob(JobCondition.Succeeded);
    //                    }
    //                    else
    //                    {
    //                        __instance.CheckForJobOverride();
    //                    }

    //                    __instance.FinalizeTick();
    //                    return false;
    //                }

    //                if (__instance.debugLog)
    //                {
    //                    __instance.DebugLogEvent("Job expire skipped because there are no enemies nearby");
    //                }
    //            }

    //            __instance.curDriver.DriverTick();
    //        }

    //        if (__instance.curJob == null && !__instance.pawn.Dead && __instance.pawn.mindState.Active &&
    //            __instance.CanDoAnyJob())
    //        {
    //            if (__instance.debugLog)
    //            {
    //                __instance.DebugLogEvent("Starting job from Tick because curJob == null.");
    //            }

    //            __instance.TryFindAndStartJob();
    //        }

    //        __instance.FinalizeTick();

    //        return false;
    //    }
    //}

    [ProfileMode("Selection", UpdateMode.Tick, "TickThingsSelectTipKey", true)]
    internal class H_TickSelection
    {
        public static bool Active = false;
    }

    [ProfileMode("TickDef", UpdateMode.Tick, "TickThingByDefTipKey", true)]
    internal class H_TickDef
    {
        public static bool Active = false;
    }


    [ProfileMode("TickThing", UpdateMode.Tick, "LogTipThingTickByClass", true)]
    [HarmonyPatch(typeof(TickList), nameof(TickList.Tick))]
    internal class H_TickListTick
    {
        public static bool Active = false;

        public static void LogMe(Thing sam, Action ac, string fix)
        {
            var logme = false;
            if (H_TickSelection.Active)
            {
                if (Find.Selector.selected.Any(x => x == sam))
                {
                    logme = true;
                }
            }
            else
            {
                logme = true;
            }

            if (logme)
            {
                var key = sam.GetType().Name;

                if (H_TickDef.Active)
                {
                    key = sam.def.defName;
                }

                if (H_TickSelection.Active)
                {
                    key = sam.ThingID;
                }

                string Namer()
                {
                    if (H_TickDef.Active)
                    {
                        return $"{sam.def.defName} - {sam?.def?.modContentPack?.Name} - {fix} ";
                    }

                    if (H_TickSelection.Active)
                    {
                        return
                            $"{sam.def.defName} - {sam.GetHashCode()} - {sam?.def?.modContentPack?.Name} - {fix}";
                    }

                    return $"{sam.GetType()} {fix}";
                }

                var prof = Analyzer.Start(key, Namer, sam.GetType(), sam.def);
                ac();
                prof.Stop();
            }
            else
            {
                ac();
            }
        }

        private static bool Prefix(TickList __instance)
        {
            if (!Active && !H_TickSelection.Active && !H_TickDef.Active)
            {
                return true;
            }

            for (var i = 0; i < __instance.thingsToRegister.Count; i++)
            {
                __instance.BucketOf(__instance.thingsToRegister[i]).Add(__instance.thingsToRegister[i]);
            }

            __instance.thingsToRegister.Clear();
            for (var j = 0; j < __instance.thingsToDeregister.Count; j++)
            {
                __instance.BucketOf(__instance.thingsToDeregister[j]).Remove(__instance.thingsToDeregister[j]);
            }

            __instance.thingsToDeregister.Clear();
            if (DebugSettings.fastEcology)
            {
                Find.World.tileTemperatures.ClearCaches();
                for (var k = 0; k < __instance.thingLists.Count; k++)
                {
                    var list = __instance.thingLists[k];
                    for (var l = 0; l < list.Count; l++)
                    {
                        if (list[l].def.category == ThingCategory.Plant)
                        {
                            list[l].TickLong();
                        }
                    }
                }
            }

            var list2 = __instance.thingLists[Find.TickManager.TicksGame % __instance.TickInterval];
            for (var m = 0; m < list2.Count; m++)
            {
                var sam = list2[m];
                if (!sam.Destroyed)
                {
                    try
                    {
                        var tickerType = __instance.tickType;
                        if (tickerType != TickerType.Normal)
                        {
                            if (tickerType != TickerType.Rare)
                            {
                                if (tickerType == TickerType.Long)
                                {
                                    LogMe(sam, sam.TickLong, "TickLong");
                                }
                            }
                            else
                            {
                                LogMe(sam, sam.TickRare, "TickRare");
                            }
                        }
                        else
                        {
                            LogMe(sam, sam.Tick, "Tick");
                        }
                    }
                    catch (Exception ex)
                    {
                        var text = !list2[m].Spawned ? string.Empty : " (at " + list2[m].Position + ")";
                        if (Prefs.DevMode)
                        {
                            Log.Error(string.Concat("Exception ticking ", list2[m].ToStringSafe(), text, ": ", ex));
                        }
                        else
                        {
                            Log.ErrorOnce(
                                string.Concat("Exception ticking ", list2[m].ToStringSafe(), text,
                                    ". Suppressing further errors. Exception: ", ex),
                                list2[m].thingIDNumber ^ 576876901);
                        }
                    }
                }
            }

            return false;
        }
    }



}