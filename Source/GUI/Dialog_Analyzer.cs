﻿using ColourPicker;
using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.Experimental.PlayerLoop;
using UnityEngine.UIElements;
using Verse;

/*  Naming Wise
 *  Tabs on the side, Ex 'HarmonyPatches', SideTab
 *  Categories for them, Ex 'Tick', SideTabCategories
 *  A Log 'inside' a SideTab, is a 'Log', each Log belongs to a SideTab
 */

namespace DubsAnalyzer
{

    [StaticConstructorOnStartup]
    public class Dialog_Analyzer : Window
    {
        public override Vector2 InitialSize => new Vector2(890, 650);

        private SideTab sideTab;
        private Logs logs;
        private AdditionalInfo additionalInfo;

        public static string GarbageCollectionInfo = string.Empty;

        public static string TimesFilter = string.Empty;
        public static StringBuilder csv = new StringBuilder();

        public static long totalBytesOfMemoryUsed;

        private static Thread CleanupPatches = null;
        public static List<Action> QueuedMessages = new List<Action>();
        public static object messageSync = new object();

        public static float cache = -1;
        public static float cachedWidth = -1;
        public static bool OnFirstLoad = true;
        public override void PreOpen()
        {
            base.PreOpen();
            Reboot();

            if (cachedWidth != -1)
                windowRect.width = cachedWidth;
        }


        public static void Reboot()
        {
            if (AnalyzerState.CanPatch())
            {
                AnalyzerState.State = CurrentState.Patching;
                Log.Message("Applying profiling patches...");
                try
                {
                    if (OnFirstLoad)
                    {
                        LoadModes();
                        OnFirstLoad = false;
                    }


                    Analyzer.harmony.PatchAll(Assembly.GetExecutingAssembly());

                    Log.Message("Done");
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }

            AnalyzerState.State = CurrentState.Open;
            Analyzer.StartProfiling();
        }

        public static void LoadModes()
        {
            var modes = GenTypes.AllTypes.Where(m => m.TryGetAttribute<ProfileMode>(out _)).OrderBy(m => m.TryGetAttribute<ProfileMode>().name).ToList();

            foreach (var mode in modes)
            {
                try
                {
                    var att = mode.TryGetAttribute<ProfileMode>();
                    att.Settings = new Dictionary<FieldInfo, Setting>();

                    foreach (var fieldInfo in mode.GetFields().Where(m => m.TryGetAttribute<Setting>(out _)))
                    {
                        var sett = fieldInfo.TryGetAttribute<Setting>();
                        att.Settings.SetOrAdd(fieldInfo, sett);
                    }
                    att.MouseOver = AccessTools.Method(mode, "MouseOver");
                    att.Clicked = AccessTools.Method(mode, "Clicked");
                    att.Selected = AccessTools.Method(mode, "Selected");
                    att.Checkbox = AccessTools.Method(mode, "Checkbox");
                    att.typeRef = mode;

                    foreach (var profileTab in AnalyzerState.SideTabCategories)
                    {
                        if (att.mode == profileTab.UpdateMode)
                            profileTab.Modes.SetOrAdd(att, mode);
                    }
                }
                catch (Exception e)
                {
                    Log.Error(e.ToString());
                }
            }

            foreach (var profileMode in ProfileMode.instances)
            {
                foreach (var profileTab in AnalyzerState.SideTabCategories)
                {
                    if (profileMode.mode == profileTab.UpdateMode)
                    {
                        if (profileTab.Modes.Keys.All(x => x.name != profileMode.name))
                        {
                            profileTab.Modes.Add(profileMode, null);
                        }
                    }
                }
            }
        }

        public override void PostClose()
        {
            base.PostClose();
            Analyzer.StopProfiling();
            Analyzer.Reset();
            Analyzer.Settings.Write();

            foreach (var tab in AnalyzerState.SideTabCategories)
            {
                foreach (var mode in tab.Modes)
                {
                    mode.Key.SetActive(false);
                }
            }

            // we add new functionality
            if (AnalyzerState.CanCleanup())
            {
                CleanupPatches = new Thread(() => Analyzer.UnPatchMethods());
                CleanupPatches.Start();
            }


        }

        public Dialog_Analyzer()
        {
            layer = WindowLayer.Super;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnCancel = false;
            soundAppear = SoundDefOf.CommsWindow_Open;
            soundClose = SoundDefOf.CommsWindow_Close;
            doCloseButton = false;
            doCloseX = true;
            draggable = true;
            drawShadow = true;
            preventCameraMotion = false;
            onlyOneOfTypeAllowed = true;
            resizeable = true;

            sideTab = new SideTab(this);
            logs = new Logs(this);
            additionalInfo = new AdditionalInfo(this);
        }

        public override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(50f, (UI.screenHeight - InitialSize.y) / 2f, InitialSize.x, InitialSize.y);
            windowRect = windowRect.Rounded();
        }

        public override void DoWindowContents(Rect canvas)
        {
            if (Event.current.type == EventType.Layout) return;

            try
            {
                cachedWidth = windowRect.width;

                /*
                 * Draw our side tab, including our:
                 * - Categories (Home, Modding Tools, Tick, Update, GUI)
                 * - Content (Modes inside each of the above categories)
                 */
                sideTab.Draw(canvas);

                /*
                 * Draw the actual screen we want, either:
                 * - Home Screen, Modders Tools, or one of the categories
                 */
                Rect inner = canvas;
                inner.x += SideTab.width;
                inner.width -= SideTab.width;

                switch (AnalyzerState.CurrentSideTabCategory)
                {
                    case SideTabCategory.Home:
                        if (cache != -1)
                        {
                            windowRect.width -= 450;
                            cache = -1;
                        }
                        Analyzer.Settings.DoSettings(inner);
                        break;
                    default: // We are in one of our categories, which means we want to display our logs
                        /*
                         * Draw our top row, we will always show this, unconditionally
                         */
                        bool save = false;

                        if (!(AnalyzerState.State == CurrentState.Open) || AnalyzerState.CurrentProfileKey == "") // if we aren't currently 'open' draw a loading sign, and leave
                        {
                            DrawLoading(inner);
                            return;
                        }

                        if (Analyzer.Settings.SidePanel)
                        {
                            if (cache == -1)
                            {
                                windowRect.width += 450;
                                cache = windowRect.width;
                            }

                            inner = canvas.LeftPartPixels(Logs.width);
                            inner.x += SideTab.width;

                            DrawTopRow(inner.TopPartPixels(20f), ref save);
                            inner.y += 25;
                            inner.height -= 25;
                            Widgets.DrawMenuSection(inner);
                            logs.Draw(inner, save);

                            var AdditionalInfoBox = canvas.RightPartPixels(canvas.width - (Logs.width + SideTab.width - 1f)).Rounded();
                            if (AnalyzerState.CurrentProfileKey == "Overview")
                            {
                                Dialog_StackedGraph.Display(AdditionalInfoBox);
                            }
                            else
                            {
                                additionalInfo.DrawPanel(AdditionalInfoBox);
                            }
                        }
                        else
                        {
                            if (cache != -1)
                            {
                                windowRect.width -= 450;
                                cache = -1;
                            }

                            DrawTopRow(inner.TopPartPixels(20f), ref save);
                            inner.y += 25;
                            inner.height -= 25;

                            var AdditionalInfoBox = canvas.BottomPart(.5f);
                            if (AnalyzerState.CurrentProfileKey == "Overview")
                            {
                                Widgets.DrawMenuSection(inner);
                                logs.Draw(inner, save);
                                Dialog_StackedGraph.Display(AdditionalInfoBox);
                            }
                            else
                            {
                                inner = inner.TopPart(0.5f);
                                Widgets.DrawMenuSection(inner);
                                logs.Draw(inner, save);
                                additionalInfo.Draw(AdditionalInfoBox);
                            }
                        }

                        break;
                }

                // Now we are outside the scope of all of our gui, lets print the messages we had queued during this time
                lock (messageSync)
                {
                    foreach (var action in QueuedMessages)
                        action();

                    QueuedMessages.Clear();
                }
            }
            catch (Exception e)
            {
                Log.Error($"[Analyzer] Caught the error {e.Message}, handling and closing scroll and gui scope");

                GUI.EndScrollView();
                GUI.EndGroup();
            }

        }


        private void DrawLoading(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, $"Loading{GenText.MarchingEllipsis(0f)}");
            DubGUI.ResetFont();
        }

        private void DrawTopRow(Rect topRow, ref bool save)
        {
            Rect row = topRow.LeftPartPixels(25f);

            if (Widgets.ButtonImage(row, TexButton.SpeedButtonTextures[AnalyzerState.CurrentlyRunning ? 0 : 1]))
            {
                AnalyzerState.CurrentlyRunning = !AnalyzerState.CurrentlyRunning;
                AnalyzerState.CurrentTab.SetActive(AnalyzerState.CurrentlyRunning);
            }

            TooltipHandler.TipRegion(topRow, "startstoplogTip".Translate());
            save = false;

            Rect searchbox = topRow.LeftPartPixels(topRow.width - 350f);
            searchbox.x += 25f;
            DubGUI.InputField(searchbox, "Search", ref TimesFilter, DubGUI.MintSearch);
            row.x = searchbox.xMax + 5;
            row.width = 130f;
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            Widgets.FillableBar(row, Mathf.Clamp01(Mathf.InverseLerp(H_RootUpdate.LastMinGC, H_RootUpdate.LastMaxGC, totalBytesOfMemoryUsed)), DubResources.darkgrey);
            Widgets.Label(row, GarbageCollectionInfo);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(row, "garbageTip".Translate());

            row.x = row.xMax + 5;
            row.width = 50f;
            Widgets.Label(row, H_RootUpdate._fpsText);
            TooltipHandler.TipRegion(row, "fpsTipperino".Translate());
            row.x = row.xMax + 5;
            row.width = 90f;
            Widgets.Label(row, H_RootUpdate.tps);
            TooltipHandler.TipRegion(row, "tpsTipperino".Translate());
            row.x = row.xMax + 5;
            row.width = 30f;
            Text.Font = GameFont.Medium;
            save = Widgets.ButtonImageFitted(row, DubResources.sav);
            TooltipHandler.TipRegion(row, "savecsvTip".Translate(GenFilePaths.FolderUnderSaveData("Profiling")));

            row.x = row.xMax;
            row.width = 25f;
        }


        internal class SideTab
        {
            private Dialog_Analyzer super = null;
            public static float width = 220f;
            private static Vector2 ScrollPosition = Vector2.zero;
            public static Listing_Standard listing = new Listing_Standard();
            public static float yOffset = 0f;
            private float ListHeight = 0;

            public SideTab(Dialog_Analyzer super)
            {
                this.super = super;
            }

            public void Draw(Rect rect)
            {
                var ListerBox = rect.LeftPartPixels(width);
                ListerBox.width -= 10f;
                Widgets.DrawMenuSection(ListerBox);
                ListerBox = ListerBox.ContractedBy(4f);

                var baseRect = ListerBox.AtZero();
                baseRect.width -= 16f;
                baseRect.height = ListHeight;

                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;

                yOffset = 0f;

                { // Begin Scope for Scroll & GUI Group/View
                    Widgets.BeginScrollView(ListerBox, ref ScrollPosition, baseRect);
                    GUI.BeginGroup(baseRect);
                    listing.Begin(baseRect);

                    foreach (var maintab in AnalyzerState.SideTabCategories)
                        if (!(maintab.label == "Modder Added" && maintab.Modes.Count == 0))
                            DrawSideTabList(maintab);

                    listing.End();
                    GUI.EndGroup();
                    Widgets.EndScrollView();
                }


                DubGUI.ResetFont();
                ListHeight = yOffset;
            }

            private void DrawSideTabList(ProfileTab tab)
            {
                DubGUI.ResetFont();
                yOffset += 40f;

                var row = listing.GetRect(30f);

                if (tab.Selected) Widgets.DrawOptionSelected(row);

                if (tab.label == "Home" || tab.label == "Modder Tools")
                {
                    if (Widgets.ButtonInvisible(row))
                        tab.clickedAction();
                }
                else
                {
                    if (Widgets.ButtonInvisible(row.LeftPartPixels(row.width - row.height)))
                        tab.clickedAction();


                    if (Widgets.ButtonImage(row.RightPartPixels(row.height), tab.Collapsed ? DubGUI.DropDown : DubGUI.FoldUp))
                        tab.Collapsed = !tab.Collapsed;
                }
                row.x += 5f;
                Widgets.Label(row, tab.label);

                TooltipHandler.TipRegion(row, tab.Tip);

                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;

                if (tab.Collapsed) return;

                foreach (var mode in tab.Modes)
                {
                    DrawSideTab(ref row, mode, tab.UpdateMode);
                }
            }

            private void DrawSideTab(ref Rect row, KeyValuePair<ProfileMode, Type> mode, UpdateMode updateMode)
            {
                if (!mode.Key.Basics && !Analyzer.Settings.AdvancedMode) return;

                row = listing.GetRect(30f);
                Widgets.DrawHighlightIfMouseover(row);

                if (AnalyzerState.CurrentTab == mode.Key)
                    Widgets.DrawOptionSelected(row);

                row.x += 20f;
                yOffset += 30f;

                Widgets.Label(row, mode.Key.name);

                if (Widgets.ButtonInvisible(row))
                {
                    AnalyzerState.SwapTab(mode, updateMode);
                }
                if (mode.Key.Closable)
                {
                    if (Input.GetMouseButtonDown(1)) // mouse button right
                    {
                        if (row.Contains(Event.current.mousePosition))
                        {
                            List<FloatMenuOption> options = new List<FloatMenuOption>()
                            {
                                new FloatMenuOption("Close", () => AnalyzerState.RemoveTab(mode))
                            };
                            Find.WindowStack.Add(new FloatMenu(options));
                        }
                    }
                }

                TooltipHandler.TipRegion(row, mode.Key.tip);

                if (AnalyzerState.CurrentTab == mode.Key)
                {
                    bool firstEntry = true;
                    foreach (var keySetting in mode.Key.Settings)
                    {
                        if (keySetting.Key.FieldType == typeof(bool))
                        {
                            row = listing.GetRect(30f);
                            row.x += 20f;
                            GUI.color = Widgets.OptionSelectedBGBorderColor;
                            Widgets.DrawLineVertical(row.x, row.y, 15f);

                            if (!firstEntry)
                            {
                                Widgets.DrawLineVertical(row.x, row.y - 15f, 15f);
                            }

                            row.x += 10f;
                            Widgets.DrawLineHorizontal(row.x - 10f, row.y + 15f, 10f);
                            GUI.color = Color.white;
                            yOffset += 30f;

                            bool cur = (bool)keySetting.Key.GetValue(null);

                            if (DubGUI.Checkbox(row, keySetting.Value.name, ref cur))
                            {
                                keySetting.Key.SetValue(null, cur);
                                Analyzer.Reset();
                            }
                        }

                        if (keySetting.Value.tip != null)
                        {
                            TooltipHandler.TipRegion(row, keySetting.Value.tip);
                        }

                        firstEntry = false;
                    }
                }
            }

        }
        internal class Logs
        {
            private Dialog_Analyzer super = null;
            private static Vector2 ScrollPosition = Vector2.zero;
            public Rect GizmoListRect;
            private const float boxHeight = 40f;
            public static Listing_Standard listing = new Listing_Standard();
            public static float ListHeight = 999999999;
            public static float width = 630f;


            public static string TipCache = "";
            public static string TipLabel = "";

            public Logs(Dialog_Analyzer super)
            {
                this.super = super;
            }

            public void Draw(Rect rect, bool save)
            {
                DrawLogs(rect, save);
            }

            private void DrawLogs(Rect rect, bool save)
            {
                if (!AnalyzerState.CurrentTab?.IsPatched ?? true)
                {

                    DubGUI.Heading(rect, $"Loading{GenText.MarchingEllipsis(0f)}");
                    return;
                }

                var innerRect = rect.AtZero();
                //innerRect.width -= 16f;
                innerRect.height = ListHeight;

                GizmoListRect = rect.AtZero();
                GizmoListRect.y += ScrollPosition.y;

                Widgets.BeginScrollView(rect, ref ScrollPosition, innerRect, false);
                GUI.BeginGroup(innerRect);
                listing.Begin(innerRect);

                float currentListHeight = 0;

                // Lets have a 'tab' summary 
                // We will get stats like a; total time on tab
                Rect visible = listing.GetRect(20);

                Text.Anchor = TextAnchor.MiddleCenter;
                DrawTabOverview(visible);
                currentListHeight += 24;
                listing.GapLine(0f);

                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Tiny;

                lock (Analyzer.sync)
                {
                    foreach (var log in AnalyzerState.Logs)
                    {
                        DrawLog(log, save, ref currentListHeight);
                    }
                }

                if (save)
                {
                    var path = GenFilePaths.FolderUnderSaveData("Profiling") + $"/{AnalyzerState.CurrentTab.name}_{DateTime.Now.ToFileTime()}.csv";
                    File.WriteAllText(path, csv.ToString());
                    csv.Clear();
                    Messages.Message($"Saved to {path}", MessageTypeDefOf.TaskCompletion, false);
                }

                ListHeight = currentListHeight;

                listing.End();
                GUI.EndGroup();
                Widgets.EndScrollView();

                DubGUI.ResetFont();
            }

            private void DrawTabOverview(Rect rect)
            {
                Widgets.Label(rect, AnalyzerState.CurrentTab?.name);

                Widgets.DrawHighlightIfMouseover(rect);

                if (Widgets.ButtonInvisible(rect))
                    AnalyzerState.CurrentProfileKey = "Overview";

                if (AnalyzerState.CurrentProfileKey == "Overview")
                    Widgets.DrawHighlightSelected(rect);
            }

            private void DrawLog(ProfileLog log, bool save, ref float currentListHeight)
            {
                if (!log.Label.Has(TimesFilter)) return;

                Rect visible = listing.GetRect(boxHeight);

                if (!visible.Overlaps(GizmoListRect)) // if we don't overlap, continue, but continue to adjust for further logs.
                {
                    listing.GapLine(0f);
                    currentListHeight += 4f;
                    currentListHeight += visible.height;

                    return;
                }

                var profile = AnalyzerState.GetProfile(log.Key);

                bool on = true;

                if (AnalyzerState.CurrentTab.Selected != null)
                {
                    on = (bool)AnalyzerState.CurrentTab.Selected.Invoke(null, new object[] { profile, log });
                }

                if (AnalyzerState.CurrentTab.Checkbox != null)
                {
                    var checkboxRect = new Rect(visible.x, visible.y, 25f, visible.height);
                    visible.x += 25f;
                    if (DubGUI.Checkbox(checkboxRect, "", ref on))
                    {
                        AnalyzerState.CurrentTab.Checkbox?.Invoke(null, new object[] { profile, log });
                        Analyzer.Settings.Write();
                    }
                }

                Widgets.DrawHighlightIfMouseover(visible);


                if (AnalyzerState.CurrentProfileKey == log.Key)
                {
                    Widgets.DrawHighlightSelected(visible);
                    AnalyzerState.CurrentLog = log; // because we create new ones, instead of recycle the same log, we need to update the ref.
                }

                // onhover tooltip
                if (Mouse.IsOver(visible))
                    DrawHover(log, visible);

                // onclick work, left click view stats, right click internal patch, ctrl + left click unpatch
                if (Widgets.ButtonInvisible(visible))
                    ClickWork(log, profile);

                // draw the bar
                {
                    var color = DubResources.grey;

                    if (log.Percent > 0.25f) color = DubResources.blue;
                    else if (log.Percent > 0.75f) color = DubResources.red;

                    Widgets.FillableBar(visible.BottomPartPixels(8f), log.Percent, color, DubResources.clear, false);
                }
                visible = visible.LeftPartPixels(60);


                if (!on)
                    GUI.color = Color.grey;

                Widgets.Label(visible, $" {log.Max:0.000}ms");

                visible.x = visible.xMax + 15;

                visible.width = 2000;
                Widgets.Label(visible, log.Label);

                GUI.color = Color.white;

                if (save)
                {
                    foreach (var historyTime in profile.History.times)
                    {
                        csv.Append($",{historyTime}");
                    }
                    csv.AppendLine();
                }


                listing.GapLine(0f);
                currentListHeight += 4f;
                currentListHeight += visible.height;
            }

            public static void DrawHover(ProfileLog log, Rect visible)
            {
                if (log.Meth != null)
                {
                    if (log.Label != TipLabel)
                    {
                        TipLabel = log.Label;
                        StringBuilder builder = new StringBuilder();
                        var patches = Harmony.GetPatchInfo(log.Meth);
                        if (patches != null)
                        {
                            foreach (var patch in patches.Prefixes) GetString("Prefix", patch);
                            foreach (var patch in patches.Postfixes) GetString("Postfix", patch);
                            foreach (var patch in patches.Transpilers) GetString("Transpiler", patch);
                            foreach (var patch in patches.Finalizers) GetString("Finalizer", patch);

                            void GetString(string type, Patch patch)
                            {
                                if (patch.owner != Analyzer.harmony.Id && patch.owner != Analyzer.perfharmony.Id && patch.owner != InternalMethodUtility.Harmony.Id)
                                {
                                    var ass = patch.PatchMethod.DeclaringType.Assembly.FullName;
                                    var assname = AnalyzerCache.AssemblyToModname[ass];

                                    if (Analyzer.Settings.AdvancedMode)
                                        builder.AppendLine($"{type} from {assname} with the index {patch.index} and the priority {patch.priority}\n");
                                    else
                                        builder.AppendLine($"{type} from {assname}\n");
                                }
                            }

                            TipCache = builder.ToString();
                        }
                    }
                    TooltipHandler.TipRegion(visible, TipCache);
                }
            }
            public static void ClickWork(ProfileLog log, Profiler profile)
            {
                if (Event.current.button == 0) // left click
                {
                    if (Input.GetKey(KeyCode.LeftControl))
                    {
                        AnalyzerState.CurrentTab.Clicked?.Invoke(null, new object[] { profile, log });
                        Analyzer.Settings.Write();
                    }
                    else
                    {
                        AdditionalInfo.Graph.RunKey(log.Key);
                        AnalyzerState.CurrentProfileKey = log.Key;
                        AnalyzerState.CurrentLog = log;
                        StackTraceRegex.Reset();
                    }
                }
                else if (Event.current.button == 1) // right click
                {
                    if (log.Meth != null)
                    {
                        List<FloatMenuOption> options = RightClickDropDown(log.Meth as MethodInfo).ToList();
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                    else
                    {
                        try
                        {
                            var methnames = PatchUtils.GetSplitString(log.Key);
                            foreach (var n in methnames)
                            {
                                var meth = AccessTools.Method(n);
                                List<FloatMenuOption> options = RightClickDropDown(meth).ToList();
                                Find.WindowStack.Add(new FloatMenu(options));
                            }
                        }
                        catch (Exception) { }
                    }
                }
            }
            private static IEnumerable<FloatMenuOption> RightClickDropDown(MethodInfo meth)
            {
                if (Analyzer.Settings.AdvancedMode)
                {
                    if (AnalyzerState.CurrentProfileKey.Contains("Harmony")) // we can return an 'unpatch'
                    {
                        yield return new FloatMenuOption("Unpatch Method", delegate
                        {
                            PatchUtils.UnpatchMethod(meth);
                        });
                    }

                    yield return new FloatMenuOption("Unpatch methods that patch", delegate
                    {
                        PatchUtils.UnpatchMethodsOnMethod(meth);
                    });

                    yield return new FloatMenuOption("Profile the internal methods of", delegate
                    {
                        PatchUtils.PatchInternalMethod(meth);
                    });
                }
            }

        }
        internal class AdditionalInfo
        {
            public Dialog_Analyzer super = null;
            public Graph graph = null;
            public Listing_Standard listing = null;
            public static Vector2 ScrollPosition = Vector2.zero;
            public GameFont font = GameFont.Tiny;
            public float CurX = 0;
            public AdditionalInfo(Dialog_Analyzer super)
            {
                this.super = super;
                this.graph = new Graph(this);
            }

            /*
             * Bottom Panel
             */
            public void Draw(Rect rect)
            {
                var oldFont = Text.Font;
                listing = new Listing_Standard();

                var ListerBox = rect.TopPart(.4f);
                Widgets.DrawMenuSection(rect);
                ListerBox = ListerBox.AtZero();

                { // Begin Scope for Scroll & GUI Group/View
                    Widgets.BeginScrollView(rect, ref ScrollPosition, ListerBox);
                    GUI.BeginGroup(ListerBox);
                    listing.Begin(ListerBox);
                    Text.Font = font;

                    DrawGeneral(ListerBox);

                    { // graph settings
                        Rect graphRect = ListerBox.BottomPartPixels(30f).RightPartPixels(325);
                        graph.DrawSettings(graphRect.ContractedBy(2f), Graph.Entries);
                    }

                    listing.End();
                    GUI.EndGroup();
                    Widgets.EndScrollView();
                }
                var GraphBox = rect.BottomPart(.6f);
                graph.Draw(GraphBox);

                Text.Font = oldFont;
                CurX = 0;
            }
            private float GetLongestStat()
            {
                string[] s1 = new string[]{
                $" Entries: {CurrentLogStats.stats.Entries}", $" Highest Time: {CurrentLogStats.stats.HighestTime:0.000}ms",
                $" Highest Calls (per frame): {CurrentLogStats.stats.HighestCalls}", $" μ calls (per frame): {CurrentLogStats.stats.MeanCallsPerFrame:0.00}",
                $" μ time (per call): {CurrentLogStats.stats.MeanTimePerCall:0.000}ms", $" μ time (per frame): {CurrentLogStats.stats.MeanTimePerFrame:0.000}ms",
                $" σ {CurrentLogStats.stats.OutlierCutoff:0.000}ms", $" Number of Spikes: {CurrentLogStats.stats.Spikes.Count}", $" % in category {AnalyzerState.CurrentLog.Average_s}"};

                Vector2 vec = Vector2.zero;
                foreach (var str in s1)
                {
                    if (Text.CalcSize(str).x > vec.x)
                        vec = Text.CalcSize(str);
                }

                return vec.x;
            }
            private void DrawGeneral(Rect rect)
            {
                if (!LogStats.IsActiveThread)
                {
                    var s = new LogStats();
                    s.GenerateStats();
                }

                if (CurrentLogStats.stats == null)
                {
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    listing.Label($"Loading{GenText.MarchingEllipsis(0f)}");
                    DubGUI.ResetFont();
                }
                else
                {
                    lock (CurrentLogStats.sync)
                    {
                        //DrawStatsPageSidePanel();
                        float longestStat = GetLongestStat();
                        var statRect = rect.LeftPartPixels(longestStat);
                        Widgets.Label(statRect,
                        $" Entries: {CurrentLogStats.stats.Entries}\n Highest Time: {CurrentLogStats.stats.HighestTime:0.000}\n" +
                        $" Highest Calls (per frame): {CurrentLogStats.stats.HighestCalls}\n μ calls (per frame): {CurrentLogStats.stats.MeanCallsPerFrame:0.00}\n" +
                        $" % in category {AnalyzerState.CurrentLog.Average_s}\n" + $" μ time (per call): {CurrentLogStats.stats.MeanTimePerCall:0.000}ms\n" +
                        $"μ time (per frame): {CurrentLogStats.stats.MeanTimePerFrame:0.000}ms\n σ {CurrentLogStats.stats.OutlierCutoff:0.000}ms\n Number of Spikes: {CurrentLogStats.stats.Spikes.Count}");
                        CurX = longestStat;
                    }
                }
            }

            /*
             * Side Panel
             */
            public void DrawPanel(Rect rect)
            {
                listing = new Listing_Standard();

                var ListerBox = rect.TopPart(.75f);
                ListerBox.width -= 10f;
                Widgets.DrawMenuSection(ListerBox);
                ListerBox = ListerBox.AtZero();

                { // Begin Scope for Scroll & GUI Group/View
                    Widgets.BeginScrollView(rect, ref ScrollPosition, ListerBox);
                    GUI.BeginGroup(ListerBox);
                    listing.Begin(ListerBox);

                    DrawGeneralSidePanel();
                    DrawStatisticsSidePanel();
                    if (Analyzer.Settings.AdvancedMode)
                    {
                        DrawStackTraceSidePanel();
                        //DrawHarmonyOptionsSidePanel();
                    }

                    listing.End();
                    GUI.EndGroup();
                    Widgets.EndScrollView();
                }
                var GraphBox = rect.BottomPart(.25f);
                GraphBox.width -= 10f;
                graph.Draw(GraphBox);
            }

            private void DrawGeneralSidePanel()
            {
                DubGUI.Heading(listing, "General");
                Text.Font = font;

                if (AnalyzerState.CurrentProfiler()?.meth != null)
                {
                    var ass = AnalyzerState.CurrentProfiler().meth.DeclaringType.Assembly.FullName;
                    var assname = "";
                    if (ass.Contains("Assembly-CSharp")) assname = "Rimworld - Core";
                    else if (ass.Contains("UnityEngine")) assname = "Rimworld - Unity";
                    else if (ass.Contains("System")) assname = "Rimworld - System";
                    else
                    {
                        try
                        {
                            assname = AnalyzerCache.AssemblyToModname[ass];
                        }
                        catch (Exception) { assname = "Failed to locate assembly information"; }
                    }

                    DubGUI.InlineDoubleMessage($" Mod: {assname}", $" Assembly: {ass.Split(',').First()}.dll", listing, true);
                    var anch = Text.Anchor;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    var str = $"{AnalyzerState.CurrentProfiler().meth.DeclaringType.FullName}:{AnalyzerState.CurrentProfiler().meth.Name}";
                    var strLen = Text.CalcHeight(str, listing.ColumnWidth * .95f);

                    var rect = listing.GetRect(strLen);

                    Widgets.Label(rect, str);

                    if (Analyzer.Settings.AdvancedMode)
                    {
                        Widgets.DrawHighlightIfMouseover(rect);
                        if (Input.GetMouseButtonDown(1) && rect.Contains(Event.current.mousePosition)) // mouse button right
                        {
                            List<FloatMenuOption> options = new List<FloatMenuOption>()
                            {
                                new FloatMenuOption("Open In Github", () => OpenGithub()),
                            };
                            if (Analyzer.Settings.DevMode)
                            {
                                options.Add(new FloatMenuOption("Open In Dnspy (requires local path)", () => OpenDnspy()));
                            }
                            Find.WindowStack.Add(new FloatMenu(options));
                        }
                    }

                    void OpenGithub()
                    {
                        Application.OpenURL(@"https://github.com/search?l=C%23&q=" + $"{AnalyzerState.CurrentProfiler().meth.DeclaringType}.{AnalyzerState.CurrentProfiler().meth.Name}" + "&type=Code");
                    }

                    void OpenDnspy()
                    {
                        if (Analyzer.Settings.PathToDnspy == "" || Analyzer.Settings.PathToDnspy == null)
                        {
                            Log.ErrorOnce("You have not given a local path to dnspy", 10293838);
                            return;
                        }

                        var meth = AnalyzerState.CurrentProfiler().meth;
                        var path = meth.DeclaringType.Assembly.Location;
                        if (path == null || path.Length == 0)
                        {
                            var contentPack = LoadedModManager.RunningMods.FirstOrDefault(m => m.assemblies.loadedAssemblies.Contains(meth.DeclaringType.Assembly));
                            if (contentPack != null)
                            {
                                path = ModContentPack.GetAllFilesForModPreserveOrder(contentPack, "Assemblies/", p => p.ToLower() == ".dll", null)
                                    .Select(fileInfo => fileInfo.Item2.FullName)
                                    .First(dll =>
                                    {
                                        var assembly = Assembly.ReflectionOnlyLoadFrom(dll);
                                        return assembly.GetType(meth.DeclaringType.FullName) != null;
                                    });
                            }
                        }
                        var token = meth.MetadataToken;
                        if (token != 0)
                            Process.Start(Analyzer.Settings.PathToDnspy, $"\"{path}\" --select 0x{token:X8}");

                    }
                }
                else
                {
                    listing.Label("Failed to grab the method associated with this entry - please report this");
                }
                listing.GapLine(0f);
            }

            private void DrawStatisticsSidePanel()
            {

                DubGUI.CollapsableHeading(listing, "Statistics", ref AnalyzerState.HideStatistics);
                Text.Font = font;

                if (!LogStats.IsActiveThread)
                {
                    var s = new LogStats();
                    s.GenerateStats();
                }

                if (CurrentLogStats.stats == null)
                {
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    listing.Label($"Loading{GenText.MarchingEllipsis(0f)}");
                    DubGUI.ResetFont();
                }
                else
                {
                    if (!AnalyzerState.HideStatistics)
                    {
                        lock (CurrentLogStats.sync)
                        {
                            //var headings = new string[] { " Total:", " Highest:" };
                            //Text.Font = GameFont.Medium;
                            //var highest = Mathf.Max(Text.CalcHeight(headings[0], listing.ColumnWidth / 2.0f), Text.CalcHeight(headings[1], listing.ColumnWidth / 2.0f));

                            //var headingRect = listing.GetRect(highest);

                            //var leftSide = headingRect.LeftPart(.48f);
                            //var rightSide = headingRect.RightPart(.48f);

                            //Widgets.Label(leftSide, headings[0]);
                            //Widgets.Label(rightSide, headings[1]);

                            //Text.Font = GameFont.Small;
                            //DubGUI.InlineDoubleMessageNC($" Entries: {CurrentLogStats.stats.Entries}", $" Number of Spikes: {CurrentLogStats.stats.Spikes.Count} ", listing, false);
                            //DubGUI.InlineDoubleMessageNC($" Calls: {CurrentLogStats.stats.TotalCalls}", $" Highest Calls (per frame): {CurrentLogStats.stats.HighestCalls}", listing, false);
                            //DubGUI.InlineDoubleMessageNC($" Time: {CurrentLogStats.stats.TotalTime:0.000}ms ", $" Highest Time: {CurrentLogStats.stats.HighestTime:0.000}ms", listing, true);

                            //var secondheadings = new string[] { " Per Frame ", " Per Call " };

                            //Text.Font = GameFont.Medium;
                            //listing.Label(secondheadings[0]);
                            //Text.Font = GameFont.Small;

                            //listing.Label($" μ calls (per frame): {CurrentLogStats.stats.MeanCallsPerFrame:0.00}");
                            //listing.Label($" μ time (per frame): {CurrentLogStats.stats.MeanTimePerFrame:0.000}ms ");


                            //Text.Font = GameFont.Medium;
                            //listing.Label(secondheadings[1]);
                            //Text.Font = GameFont.Small;

                            //listing.Label($" μ time (per call): {CurrentLogStats.stats.MeanTimePerCall:0.000}ms ");


                            DubGUI.InlineTripleMessage($" Entries: {CurrentLogStats.stats.Entries}", $" Σ Calls: {CurrentLogStats.stats.TotalCalls}", $" Σ Time: {CurrentLogStats.stats.TotalTime:0.000}ms ", listing, true);
                            DubGUI.InlineTripleMessage($" Highest Time: {CurrentLogStats.stats.HighestTime:0.000}ms", $" Highest Calls (per frame): {CurrentLogStats.stats.HighestCalls}", $" μ time (per call): {CurrentLogStats.stats.MeanTimePerCall:0.000}ms ", listing, true);
                            DubGUI.InlineTripleMessage($" % in category {AnalyzerState.CurrentLog.Average_s}", $" μ calls (per frame): {CurrentLogStats.stats.MeanCallsPerFrame:0.00}", $" μ time (per frame): {CurrentLogStats.stats.MeanTimePerFrame:0.000}ms ", listing, true);
                            DubGUI.InlineDoubleMessage($" σ {CurrentLogStats.stats.OutlierCutoff:0.000}ms", $" Number of Spikes: {CurrentLogStats.stats.Spikes.Count} ", listing, true);
                        }
                    }
                }
            }

            private void DrawStackTraceSidePanel()
            {
                if (Analyzer.Settings.AdvancedMode)
                {
                    DubGUI.CollapsableHeading(listing, "Stack Trace", ref AnalyzerState.HideStacktrace);
                    Text.Font = font;
                }

                if (!AnalyzerState.HideStacktrace)
                {
                    listing.Label($"Stacktraces: {StackTraceRegex.traces.Count}");

                    foreach (var st in StackTraceRegex.traces.OrderBy(w => w.Value.Count).Reverse())
                    {
                        int i = 0;
                        var traceInfo = st.Value;

                        for (i = 0; i < st.Value.TranslatedArr().Count() - 2; i++)
                        {
                            DrawTrace(i, false);
                        }

                        DrawTrace(i, true);

                        void DrawTrace(int idx, bool capoff)
                        {
                            var rect = DubGUI.InlineDoubleMessage(
                                traceInfo.TranslatedArr()[idx], traceInfo.methods[idx].Item2.Count.ToString(), listing,
                                capoff).LeftPart(.5f);

                            if (Mouse.IsOver(rect))
                            {
                                StringBuilder builder = new StringBuilder();
                                foreach (var p in traceInfo.methods[idx].Item2)
                                    GetString(p);

                                void GetString(StackTraceInformation.HarmonyPatch patch)
                                {
                                    if (patch.id != Analyzer.harmony.Id && patch.id != Analyzer.perfharmony.Id &&
                                        patch.id != InternalMethodUtility.Harmony.Id)
                                    {
                                        var ass = patch.patch.DeclaringType.Assembly.FullName;
                                        var assname = AnalyzerCache.AssemblyToModname[ass];

                                        builder.AppendLine(
                                            $"{patch.type} from {assname} with the index {patch.index} and the priority {patch.priority}\n");
                                    }
                                }

                                TooltipHandler.TipRegion(rect, builder.ToString());
                            }
                        }
                    }
                }
            }

            //private void DrawHarmonyOptionsSidePanel()
            //{
            //    DubGUI.Heading(listing, "Information");
            //}

            internal class Graph
            {
                public AdditionalInfo super = null;

                private static int entryCount = 300;
                public static string key = string.Empty;

                private static Vector2 last = Vector2.zero;
                private static Vector2 lastMEM = Vector2.zero;
                private static int hoverVal;
                private static string hoverValStr = string.Empty;
                private static int ResetRange;

                private static float WindowMax;

                private static double max;
                private static string MaxStr;
                private static string totalBytesStr;
                public static int Entries = 0;

                //private static bool ShowMemory = false;

                public Graph(AdditionalInfo super)
                {
                    this.super = super;
                }

                public static void RunKey(string s)
                {
                    reset();
                    key = s;
                }

                public static void reset()
                {
                    WindowMax = 0;
                    max = 0;
                    totalBytesStr = string.Empty;
                    key = string.Empty;
                    hoverValStr = string.Empty;
                    MaxStr = string.Empty;
                    lastMEM = Vector2.zero;
                    last = Vector2.zero;
                }

                public void DisplayColorPicker(Rect rect, bool LineCol)
                {
                    Widgets.DrawBoxSolid(rect, (LineCol) ? Analyzer.Settings.LineCol : Analyzer.Settings.GraphCol);

                    if (Widgets.ButtonInvisible(rect, true))
                    {
                        if (Find.WindowStack.WindowOfType<colourPicker>() != null) // if we already have a colour window open, close it
                            Find.WindowStack.RemoveWindowsOfType(typeof(colourPicker));

                        else
                        {
                            var cp = new colourPicker();
                            if (LineCol)
                                cp.Setcol = () => Analyzer.Settings.LineCol = colourPicker.CurrentCol;
                            else
                                cp.Setcol = () => Analyzer.Settings.GraphCol = colourPicker.CurrentCol;

                            cp.SetColor((LineCol) ? Analyzer.Settings.LineCol : Analyzer.Settings.GraphCol);

                            Find.WindowStack.Add(cp);
                        }
                    }
                }

                public void DrawSettings(Rect rect, int entries)
                {
                    var sliderRect = rect.RightPartPixels(200f);
                    sliderRect.x -= 15;
                    entryCount = (int)Widgets.HorizontalSlider(sliderRect, entryCount, 10, 2000, true, string.Intern($"{entryCount} Entries"));
                    sliderRect = new Rect(sliderRect.xMax + 5, sliderRect.y + 2, 10, 10);

                    DisplayColorPicker(sliderRect, true);
                    sliderRect.y += 12;
                    DisplayColorPicker(sliderRect, false);

                    rect.width -= 220;
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(rect, hoverValStr);
                    Text.Anchor = TextAnchor.UpperLeft;
                }

                public void Draw(Rect position)
                {
                    ResetRange++;
                    if (ResetRange >= 500)
                    {
                        ResetRange = 0;
                        WindowMax = 0;
                    }

                    Text.Font = GameFont.Small;

                    var prof = AnalyzerState.CurrentProfiler();
                    if (prof == null || prof.History.times.Length <= 0) return;

                    var entries = (prof.History.times.Length > entryCount) ? entryCount : prof.History.times.Length;

                    if (Analyzer.Settings.SidePanel)
                    {
                        DrawSettings(position.TopPartPixels(30f).ContractedBy(2f), entries);
                        position = position.BottomPartPixels(position.height - 30f);
                    }

                    Widgets.DrawBoxSolid(position, Analyzer.Settings.GraphCol);

                    GUI.color = Color.grey;
                    Widgets.DrawBox(position, 2);
                    GUI.color = Color.white;

                    var gap = position.width / entries;

                    GUI.BeginGroup(position);
                    position = position.AtZero();

                    var LastMax = max;
                    max = prof.History.times[0];

                    for (var i = 1; i < entries; i++)
                    {
                        var entry = prof.History.times[i];

                        if (entry > max)
                            max = entry;
                    }

                    if (max > WindowMax)
                        WindowMax = (float)max;

                    //bool DoHover = false;

                    for (var i = 0; i < entries; i++)
                    {
                        float entry = (float)prof.History.times[i];
                        float y = GenMath.LerpDoubleClamped(0, WindowMax, position.height, position.y, entry);

                        var screenPoint = new Vector2(position.xMax - (gap * i), y);

                        if (i != 0)
                        {
                            Widgets.DrawLine(last, screenPoint, Analyzer.Settings.LineCol, 1f);

                            var relevantArea = new Rect(screenPoint.x - gap / 2f, position.y, gap, position.height);
                            if (Mouse.IsOver(relevantArea))
                            {
                                //DoHover = true;
                                if (i != hoverVal)
                                {
                                    hoverVal = i;
                                    hoverValStr = $"{entry:0.00000}ms {prof.History.hits[i]} calls";
                                }
                                SimpleCurveDrawer.DrawPoint(screenPoint);
                            }
                        }

                        last = screenPoint;
                    }

                    if (LastMax != max)
                        MaxStr = $" Max {max}ms";

                    var LogMaxY = GenMath.LerpDoubleClamped(0, WindowMax, position.height, position.y, (float)max);
                    var crunt = position;
                    crunt.y = LogMaxY;
                    Widgets.Label(crunt, MaxStr);
                    Widgets.DrawLine(new Vector2(position.x, LogMaxY), new Vector2(position.xMax, LogMaxY), Color.red, 1f);

                    last = Vector2.zero;

                    GUI.EndGroup();

                    Entries = entries;
                }
            }
        }
    }
}



//if (ShowMemory)
//{
//    var memr = settings.LeftPartPixels(20f);

//    if (Widgets.ButtonImageFitted(memr, mem, ShowMemory ? Color.white : Color.grey))
//    {
//        ShowMemory = !ShowMemory;
//    }

//    GUI.color = Color.white;
//    TooltipHandler.TipRegion(memr, "Toggle garbage tracking, approximation of total garbage produced by the selected log");

//    memr.x = memr.xMax;
//    memr.width = 300f;

//    if (ShowMemory)
//    {
//        Text.Anchor = TextAnchor.MiddleLeft;
//        Widgets.Label(memr, totalBytesStr);
//    }
//}
//
//   maxBytes = 0;
//   minBytes = 0;
//
//  var bytes = prof.History.mem[i];
//      minBytes = bytes;
//      maxBytes = bytes;
//   if (bytes < minBytes)
//   {
//       minBytes = bytes;
//  }
//
//    if (bytes > maxBytes)
//     {
//         maxBytes = bytes;
//     }
//   if (LASTtotalBytesStr < prof.BytesUsed)
//   {
//       LASTtotalBytesStr = prof.BytesUsed;
//       totalBytesStr = $"Mem {(long)(prof.BytesUsed / (long)1024)} Kb";
//   }
//    var bytes = prof.History.mem[i];
//  var MEMy = GenMath.LerpDoubleClamped(minBytes, maxBytes, position.height, position.y, bytes);
//    var MEMscreenPoint = new Vector2(position.xMax - gap * i, MEMy);
//   if (ShowMem)
//   {
//       Widgets.DrawLine(lastMEM, MEMscreenPoint, Color.grey, 2f);
//   }

//if (Widgets.ButtonInvisible(vag))
//{
//    Log.Warning(prof.History.stack[i]);
//    Find.WindowStack.Windows.Add(new StackWindow { stkRef = prof.History.stack[i] });
//}
//    lastMEM = MEMscreenPoint;