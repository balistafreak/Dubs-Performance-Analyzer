﻿using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DubsAnalyzer
{
    [ProfileMode("Room", UpdateMode.Tick)]
    [HarmonyPatch(typeof(Room), "UpdateRoomStatsAndRole")]
    internal class H_Room
    {
        public static bool Active = false;

        private static bool Prefix(MethodBase __originalMethod, Room __instance)
        {
            if (!Active)
            {
                return true;
            }

            __instance.statsAndRoleDirty = false;
            if (!__instance.TouchesMapEdge && __instance.RegionType == RegionType.Normal && __instance.regions.Count <= 36)
            {
                if (__instance.stats == null)
                {
                    __instance.stats = new DefMap<RoomStatDef, float>();
                }
                foreach (RoomStatDef roomStatDef in DefDatabase<RoomStatDef>.AllDefs.OrderByDescending((RoomStatDef x) => x.updatePriority))
                {
                    var str = roomStatDef.defName;
                    var prof = Analyzer.Start(str, () => $"{str} - {roomStatDef.workerClass}", null, null, null, __originalMethod);
                    __instance.stats[roomStatDef] = roomStatDef.Worker.GetScore(__instance);
                    prof.Stop();
                }
                __instance.role = DefDatabase<RoomRoleDef>.AllDefs.MaxBy((RoomRoleDef x) => x.Worker.GetScore(__instance));
            }
            else
            {
                __instance.stats = null;
                __instance.role = RoomRoleDefOf.None;
            }

            return false;
        }
    }
}