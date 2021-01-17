using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using JetBrains.Annotations;
using UnityEngine;
using Modding;
using RandomizerMod.Randomization;

namespace RandomizerMod
{
    public static class RandoLogger
    {
        public static ProgressionManager pm;
        public static HashSet<string> obtainedLocations;
        public static HashSet<string> uncheckedLocations;
        public static HashSet<string> randomizedLocations;
        public static HashSet<string> obtainedTransitions;
        public static HashSet<string> uncheckedTransitions;
        public static HashSet<string> randomizedTransitions;

        private static void MakeHelperLists()
        {
            {
                randomizedLocations = ItemManager.GetRandomizedLocations();
                obtainedLocations = new HashSet<string>(RandomizerMod.Instance.Settings.GetLocationsFound());
                uncheckedLocations = new HashSet<string>();
                pm = new ProgressionManager(RandomizerState.Completed, concealRandomItems: true);

                if (RandomizerMod.Instance.Settings.RandomizeRooms)
                {
                    pm.Add(LogicManager.GetStartLocation(RandomizerMod.Instance.Settings.StartName).roomTransition);
                }
                else
                {
                    pm.Add(LogicManager.GetStartLocation(RandomizerMod.Instance.Settings.StartName).waypoint);
                    if (RandomizerMod.Instance.Settings.RandomizeAreas)
                    {
                        pm.Add(LogicManager.GetStartLocation(RandomizerMod.Instance.Settings.StartName).areaTransition);
                    }
                }


                foreach (string item in RandomizerMod.Instance.Settings.GetItemsFound())
                {
                    if (LogicManager.GetItemDef(item).progression)
                    {
                        pm.Add(LogicManager.RemoveDuplicateSuffix(item));
                    }
                }

                if (RandomizerMod.Instance.Settings.RandomizeTransitions)
                {
                    obtainedTransitions = new HashSet<string>();
                    uncheckedTransitions = new HashSet<string>();
                    randomizedTransitions = new HashSet<string>(LogicManager.TransitionNames());

                    foreach (string transition in RandomizerMod.Instance.Settings.GetTransitionsFound())
                    {
                        obtainedTransitions.Add(transition);
                        pm.Add(transition);
                    }
                }
            }

            foreach (string location in randomizedLocations)
            {
                string altLocation = location; // clumsy way to be able to switch out items without spoiling their costs

                if (obtainedLocations.Contains(location)) continue;

                if (!LogicManager.ShopNames.Contains(location))
                {
                    if (LogicManager.GetItemDef(location).costType == Actions.AddYNDialogueToShiny.CostType.Essence)
                    {
                        altLocation = "Seer";
                    }
                    else if (LogicManager.GetItemDef(location).costType == Actions.AddYNDialogueToShiny.CostType.Grub)
                    {
                        altLocation = "Grubfather";
                    }
                }

                if (pm.CanGet(altLocation))
                {
                    uncheckedLocations.Add(altLocation);
                }
            }

            if (!RandomizerMod.Instance.Settings.RandomizeTransitions) return;

            foreach (string transition in randomizedTransitions)
            {
                if (obtainedTransitions.Contains(transition))
                {
                    continue;
                }
                if (pm.Has(transition))
                {
                    obtainedTransitions.Add(transition);
                }
                else if (pm.CanGet(transition))
                {
                    uncheckedTransitions.Add(transition);
                }
            }
        }

        public static void LogHelper(string message)
        {
            File.AppendAllText(Path.Combine(Application.persistentDataPath, "RandomizerHelperLog.txt"), message + Environment.NewLine);
        }

        public static void UpdateHelperLog()
        {
            new Thread(() =>
            {
                Stopwatch helperWatch = new Stopwatch();
                helperWatch.Start();

                string log = string.Empty;
                void AddToLog(string message) => log += message + Environment.NewLine;

                MakeHelperLists();

                AddToLog($"Current scene: {Translator.TranslateSceneName(GameManager.instance.sceneName)}");
                if (RandomizerMod.Instance.Settings.RandomizeTransitions)
                {
                    if (!string.IsNullOrEmpty(RandomizerMod.Instance.LastRandomizedEntrance) && !string.IsNullOrEmpty(RandomizerMod.Instance.LastRandomizedExit))
                    {
                        AddToLog($"Last randomized transition: {{{Translator.TranslateTransitionName(RandomizerMod.Instance.LastRandomizedEntrance)}}}-->{{{Translator.TranslateTransitionName(RandomizerMod.Instance.LastRandomizedExit)}}}");
                    }
                    else
                    {
                        AddToLog($"Last randomized transition: n/a");
                    }
                }

                if (!RandomizerMod.Instance.Settings.RandomizeGrubs)
                {
                    AddToLog(Environment.NewLine + "Reachable grubs: " + pm.obtained[LogicManager.grubIndex]);
                }
                if (!RandomizerMod.Instance.Settings.RandomizeWhisperingRoots)
                {
                    AddToLog("Reachable essence: " + pm.obtained[LogicManager.essenceIndex]);
                }

                // UNCHECKED ITEMS
                {
                    AddToLog(Environment.NewLine + Environment.NewLine + "REACHABLE ITEM LOCATIONS");
                    AddToLog($"There are {uncheckedLocations.Count} unchecked reachable locations.");

                    Dictionary<string, List<string>> AreaSortedItems = new Dictionary<string, List<string>>();
                    List<string> shops = LogicManager.ShopNames.Union(new List<string> { "Seer", "Grubfather" }).ToList();

                    foreach (string location in uncheckedLocations)
                    {
                        if (shops.Contains(location))
                        {
                            if (!AreaSortedItems.ContainsKey("Shops"))
                            {
                                AreaSortedItems.Add("Shops", new List<string>());
                            }
                            AreaSortedItems["Shops"].Add(location);
                            continue;
                        }

                        if (AreaSortedItems.ContainsKey(LogicManager.GetItemDef(location).areaName)) continue;

                        AreaSortedItems.Add(
                            LogicManager.GetItemDef(location).areaName,
                            uncheckedLocations.Where(loc => !shops.Contains(loc) && LogicManager.GetItemDef(loc).areaName == LogicManager.GetItemDef(location).areaName).ToList()
                            );
                    }

                    foreach (var area in AreaSortedItems)
                    {
                        AddToLog(Environment.NewLine + area.Key.Replace('_', ' '));
                        foreach (string location in area.Value)
                        {
                            AddToLog(" - " + location.Replace('_', ' '));
                        }
                    }
                }

                // UNCHECKED TRANSITIONS (AREA RANDOMIZER VERSION)
                if (RandomizerMod.Instance.Settings.RandomizeAreas)
                {
                    AddToLog(Environment.NewLine + Environment.NewLine + "REACHABLE TRANSITIONS");

                    Dictionary<string, List<string>> AreaSortedTransitions = new Dictionary<string, List<string>>();
                    foreach (string transition in uncheckedTransitions)
                    {
                        if (AreaSortedTransitions.ContainsKey(LogicManager.GetTransitionDef(transition).areaName)) continue;

                        AreaSortedTransitions.Add(
                            LogicManager.GetTransitionDef(transition).areaName,
                            uncheckedTransitions.Where(t => LogicManager.GetTransitionDef(t).areaName == LogicManager.GetTransitionDef(transition).areaName).ToList()
                            );
                    }

                    foreach (var area in AreaSortedTransitions)
                    {
                        AddToLog(Environment.NewLine + area.Key.Replace('_', ' '));
                        foreach (string transition in area.Value)
                        {
                            AddToLog(" - " + transition);
                        }
                    }
                }
                else if (RandomizerMod.Instance.Settings.RandomizeRooms)
                {
                    AddToLog(Environment.NewLine + Environment.NewLine + "REACHABLE TRANSITIONS");

                    Dictionary<string, List<string>> SceneSortedTransitions = new Dictionary<string, List<string>>();
                    foreach (string transition in uncheckedTransitions)
                    {
                        if (SceneSortedTransitions.ContainsKey(LogicManager.GetTransitionDef(transition).sceneName.Split('-').First())) continue;

                        SceneSortedTransitions.Add(
                            LogicManager.GetTransitionDef(transition).sceneName.Split('-').First(),
                            uncheckedTransitions.Where
                                (t => LogicManager.GetTransitionDef(t).sceneName.Split('-').First()
                                    == LogicManager.GetTransitionDef(transition).sceneName.Split('-').First()).ToList()
                            );
                    }

                    foreach (var room in SceneSortedTransitions)
                    {
                        AddToLog(Environment.NewLine + Translator.TranslateSceneName(room.Key).Replace('_', ' '));
                        foreach (string transition in room.Value)
                        {
                            AddToLog(" - " + Translator.TranslateTransitionName(transition));
                        }
                    }
                }

                {
                    AddToLog(Environment.NewLine + Environment.NewLine + "CHECKED ITEM LOCATIONS");
                    Dictionary<string, List<string>> AreaSortedItems = new Dictionary<string, List<string>>();
                    List<string> shops = LogicManager.ShopNames.Union(new List<string> { "Seer", "Grubfather" }).ToList();

                    foreach (string location in obtainedLocations)
                    {
                        if (shops.Contains(location))
                        {
                            if (!AreaSortedItems.ContainsKey("Shops"))
                            {
                                AreaSortedItems.Add("Shops", new List<string>());
                            }
                            AreaSortedItems["Shops"].Add(location);
                            continue;
                        }

                        if (AreaSortedItems.ContainsKey(LogicManager.GetItemDef(location).areaName)) continue;

                        AreaSortedItems.Add(
                            LogicManager.GetItemDef(location).areaName,
                            obtainedLocations.Where(loc => !shops.Contains(loc) && LogicManager.GetItemDef(loc).areaName == LogicManager.GetItemDef(location).areaName).ToList()
                            );
                    }

                    foreach (var area in AreaSortedItems)
                    {
                        AddToLog(Environment.NewLine + area.Key.Replace('_', ' '));
                        foreach (string location in area.Value)
                        {
                            AddToLog(" - " + location.Replace('_', ' '));
                        }
                    }
                }

                helperWatch.Stop();
                File.Create(Path.Combine(Application.persistentDataPath, "RandomizerHelperLog.txt")).Dispose();
                LogHelper("Generating helper log:");
                LogHelper(log);
                LogHelper("Generated helper log in " + helperWatch.Elapsed.TotalSeconds + " seconds.");
            }).Start();
        }

        public static void LogTracker(string message)
        {
            File.AppendAllText(Path.Combine(Application.persistentDataPath, "RandomizerTrackerLog.txt"), message + Environment.NewLine);
        }
        public static void InitializeTracker()
        {
            File.Create(Path.Combine(Application.persistentDataPath, "RandomizerTrackerLog.txt")).Dispose();
            string log = "Starting tracker log for new randomizer file.";
            void AddToLog(string s) => log += "\n" + s;
            AddToLog("SETTINGS");
            AddToLog($"Seed: {RandomizerMod.Instance.Settings.Seed}");
            AddToLog($"Mode: " + // :)
                        $"{(RandomizerMod.Instance.Settings.RandomizeRooms ? (RandomizerMod.Instance.Settings.ConnectAreas ? "Connected-Area Room Randomizer" : "Room Randomizer") : (RandomizerMod.Instance.Settings.RandomizeAreas ? "Area Randomizer" : "Item Randomizer"))}");
            AddToLog($"Cursed: {RandomizerMod.Instance.Settings.Cursed}");
            AddToLog($"Start location: {RandomizerMod.Instance.Settings.StartName}");
            AddToLog($"Random start items: {RandomizerMod.Instance.Settings.RandomizeStartItems}");
            AddToLog("REQUIRED SKIPS");
            AddToLog($"Mild skips: {RandomizerMod.Instance.Settings.MildSkips}");
            AddToLog($"Shade skips: {RandomizerMod.Instance.Settings.ShadeSkips}");
            AddToLog($"Fireball skips: {RandomizerMod.Instance.Settings.FireballSkips}");
            AddToLog($"Acid skips: {RandomizerMod.Instance.Settings.AcidSkips}");
            AddToLog($"Spike tunnels: {RandomizerMod.Instance.Settings.SpikeTunnels}");
            AddToLog($"Dark Rooms: {RandomizerMod.Instance.Settings.DarkRooms}");
            AddToLog($"Spicy skips: {RandomizerMod.Instance.Settings.SpicySkips}");
            AddToLog("RANDOMIZED Pools");
            AddToLog($"Dreamers: {RandomizerMod.Instance.Settings.RandomizeDreamers}");
            AddToLog($"Skills: {RandomizerMod.Instance.Settings.RandomizeSkills}");
            AddToLog($"Charms: {RandomizerMod.Instance.Settings.RandomizeCharms}");
            AddToLog($"Keys: {RandomizerMod.Instance.Settings.RandomizeKeys}");
            AddToLog($"Geo chests: {RandomizerMod.Instance.Settings.RandomizeGeoChests}");
            AddToLog($"Mask shards: {RandomizerMod.Instance.Settings.RandomizeMaskShards}");
            AddToLog($"Vessel fragments: {RandomizerMod.Instance.Settings.RandomizeVesselFragments}");
            AddToLog($"Pale ore: {RandomizerMod.Instance.Settings.RandomizePaleOre}");
            AddToLog($"Charm notches: {RandomizerMod.Instance.Settings.RandomizeCharmNotches}");
            AddToLog($"Rancid eggs: {RandomizerMod.Instance.Settings.RandomizeRancidEggs}");
            AddToLog($"Relics: {RandomizerMod.Instance.Settings.RandomizeRelics}");
            AddToLog($"Stags: {RandomizerMod.Instance.Settings.RandomizeStags}");
            AddToLog($"Maps: {RandomizerMod.Instance.Settings.RandomizeMaps}");
            AddToLog($"Grubs: {RandomizerMod.Instance.Settings.RandomizeGrubs}");
            AddToLog($"Whispering roots: {RandomizerMod.Instance.Settings.RandomizeWhisperingRoots}");
            AddToLog($"Geo rocks: {RandomizerMod.Instance.Settings.RandomizeRocks}");
            AddToLog($"Soul totems: {RandomizerMod.Instance.Settings.RandomizeSoulTotems}");
            AddToLog($"Lore tablets: {RandomizerMod.Instance.Settings.RandomizeLoreTablets}");
            AddToLog($"Lifeblood cocoons: {RandomizerMod.Instance.Settings.RandomizeLifebloodCocoons}");
            AddToLog($"Grimmkin flames: {RandomizerMod.Instance.Settings.RandomizeGrimmkinFlames}");
            AddToLog($"Palace totems: {RandomizerMod.Instance.Settings.RandomizePalaceTotems}");
            AddToLog($"Duplicate major items: {RandomizerMod.Instance.Settings.DuplicateMajorItems}");
            AddToLog("QUALITY OF LIFE");
            AddToLog($"Grubfather: {RandomizerMod.Instance.Settings.Grubfather}");
            AddToLog($"Salubra: {RandomizerMod.Instance.Settings.CharmNotch}");
            AddToLog($"Early geo: {RandomizerMod.Instance.Settings.EarlyGeo}");
            AddToLog($"Extra platforms: {RandomizerMod.Instance.Settings.ExtraPlatforms}");
            AddToLog($"Levers: {RandomizerMod.Instance.Settings.LeverSkips}");
            AddToLog($"Jiji: {RandomizerMod.Instance.Settings.Jiji}");
            LogTracker(log);
        }
        public static void LogTransitionToTracker(string entrance, string exit)
        {
            string message = string.Empty;
            if (RandomizerMod.Instance.Settings.RandomizeAreas)
            {
                string area1 = LogicManager.GetTransitionDef(entrance).areaName.Replace('_', ' ');
                string area2 = LogicManager.GetTransitionDef(exit).areaName.Replace('_', ' ');
                message = $"TRANSITION --- {{{Translator.TranslateTransitionName(entrance)}}}-->{{{Translator.TranslateTransitionName(exit)}}}" +
                    $"\n                ({area1} to {area2})";
            }
            else if (RandomizerMod.Instance.Settings.RandomizeRooms)
            {
                message = $"TRANSITION --- {{{Translator.TranslateTransitionName(entrance)}}}-->{{{Translator.TranslateTransitionName(exit)}}}";
            }

            LogTracker(message);
        }
        public static void LogItemToTracker(string item, string location)
        {
            // don't spoil duplicate items!
            if (LogicManager.GetItemDef(item).majorItem && RandomizerMod.Instance.Settings.DuplicateMajorItems)
            {
                item = LogicManager.RemoveDuplicateSuffix(item) + $"({new System.Random().Next(10)}?)";
            }

            string message = $"ITEM --- {{{item}}} at {{{location}}}";
            LogTracker(message);
        }

        public static void LogHintToTracker(string hint, bool jiji = true, bool quirrel = false)
        {
            if (jiji) LogTracker("HINT " + RandomizerMod.Instance.Settings.JijiHintCounter + " --- " + hint);
            else if (quirrel) LogTracker("HINT (QUIRREL) --- " + hint);
            else LogTracker("HINT --- " + hint);
        }

        public static void LogSpoiler(string message)
        {
            File.AppendAllText(Path.Combine(Application.persistentDataPath, "RandomizerSpoilerLog.txt"), message + Environment.NewLine);
        }

        public static void InitializeSpoiler()
        {
            File.Create(Path.Combine(Application.persistentDataPath, "RandomizerSpoilerLog.txt")).Dispose();
            LogSpoiler("Randomization completed with seed: " + RandomizerMod.Instance.Settings.Seed);
        }

        public static void LogTransitionToSpoiler(string entrance, string exit)
        {
            string message = "Entrance " + entrance + " linked to exit " + exit;
            LogSpoiler(message);
        }

        public static void LogItemToSpoiler(string item, string location)
        {
            string message = $"Putting item \"{item.Replace('_', ' ')}\" at \"{location.Replace('_', ' ')}\"";
            LogSpoiler(message);
        }
        public static void LogAllToSpoiler((int, string, string)[] orderedILPairs, (string, string)[] transitionPlacements)
        {
            RandomizerMod.Instance.Log("Generating spoiler log...");
            new Thread(() =>
            {
                Stopwatch spoilerWatch = new Stopwatch();
                spoilerWatch.Start();

                string log = string.Empty;
                void AddToLog(string message) => log += message + Environment.NewLine;

                AddToLog(GetItemSpoiler(orderedILPairs));
                AddToLog(GetTransitionSpoiler(transitionPlacements));

                try
                {
                    AddToLog(Environment.NewLine + "SETTINGS");
                    AddToLog($"Seed: {RandomizerMod.Instance.Settings.Seed}");
                    AddToLog($"Mode: " + // :)
                        $"{(RandomizerMod.Instance.Settings.RandomizeRooms ? (RandomizerMod.Instance.Settings.ConnectAreas ? "Connected-Area Room Randomizer" : "Room Randomizer") : (RandomizerMod.Instance.Settings.RandomizeAreas ? "Area Randomizer" : "Item Randomizer"))}");
                    AddToLog($"Cursed: {RandomizerMod.Instance.Settings.Cursed}");
                    AddToLog($"Start location: {RandomizerMod.Instance.Settings.StartName}");
                    AddToLog($"Random start items: {RandomizerMod.Instance.Settings.RandomizeStartItems}");
                    AddToLog("REQUIRED SKIPS");
                    AddToLog($"Mild skips: {RandomizerMod.Instance.Settings.MildSkips}");
                    AddToLog($"Shade skips: {RandomizerMod.Instance.Settings.ShadeSkips}");
                    AddToLog($"Fireball skips: {RandomizerMod.Instance.Settings.FireballSkips}");
                    AddToLog($"Acid skips: {RandomizerMod.Instance.Settings.AcidSkips}");
                    AddToLog($"Spike tunnels: {RandomizerMod.Instance.Settings.SpikeTunnels}");
                    AddToLog($"Dark Rooms: {RandomizerMod.Instance.Settings.DarkRooms}");
                    AddToLog($"Spicy skips: {RandomizerMod.Instance.Settings.SpicySkips}");
                    AddToLog("RANDOMIZED LOCATIONS");
                    AddToLog($"Dreamers: {RandomizerMod.Instance.Settings.RandomizeDreamers}");
                    AddToLog($"Skills: {RandomizerMod.Instance.Settings.RandomizeSkills}");
                    AddToLog($"Charms: {RandomizerMod.Instance.Settings.RandomizeCharms}");
                    AddToLog($"Keys: {RandomizerMod.Instance.Settings.RandomizeKeys}");
                    AddToLog($"Geo chests: {RandomizerMod.Instance.Settings.RandomizeGeoChests}");
                    AddToLog($"Mask shards: {RandomizerMod.Instance.Settings.RandomizeMaskShards}");
                    AddToLog($"Vessel fragments: {RandomizerMod.Instance.Settings.RandomizeVesselFragments}");
                    AddToLog($"Pale ore: {RandomizerMod.Instance.Settings.RandomizePaleOre}");
                    AddToLog($"Charm notches: {RandomizerMod.Instance.Settings.RandomizeCharmNotches}");
                    AddToLog($"Rancid eggs: {RandomizerMod.Instance.Settings.RandomizeRancidEggs}");
                    AddToLog($"Relics: {RandomizerMod.Instance.Settings.RandomizeRelics}");
                    AddToLog($"Stags: {RandomizerMod.Instance.Settings.RandomizeStags}");
                    AddToLog($"Maps: {RandomizerMod.Instance.Settings.RandomizeMaps}");
                    AddToLog($"Grubs: {RandomizerMod.Instance.Settings.RandomizeGrubs}");
                    AddToLog($"Whispering roots: {RandomizerMod.Instance.Settings.RandomizeWhisperingRoots}");
                    AddToLog($"Geo rocks: {RandomizerMod.Instance.Settings.RandomizeRocks}");
                    AddToLog($"Soul totems: {RandomizerMod.Instance.Settings.RandomizeSoulTotems}");
                    AddToLog($"Lore tablets: {RandomizerMod.Instance.Settings.RandomizeLoreTablets}");
                    AddToLog($"Lifeblood cocoons: {RandomizerMod.Instance.Settings.RandomizeLifebloodCocoons}");
                    AddToLog($"Grimmkin flames: {RandomizerMod.Instance.Settings.RandomizeGrimmkinFlames}");
                    AddToLog($"Palace totems: {RandomizerMod.Instance.Settings.RandomizePalaceTotems}");
                    AddToLog($"Duplicate major items: {RandomizerMod.Instance.Settings.DuplicateMajorItems}");
                    AddToLog("QUALITY OF LIFE");
                    AddToLog($"Grubfather: {RandomizerMod.Instance.Settings.Grubfather}");
                    AddToLog($"Salubra: {RandomizerMod.Instance.Settings.CharmNotch}");
                    AddToLog($"Early geo: {RandomizerMod.Instance.Settings.EarlyGeo}");
                    AddToLog($"Extra platforms: {RandomizerMod.Instance.Settings.ExtraPlatforms}");
                    AddToLog($"Levers: {RandomizerMod.Instance.Settings.LeverSkips}");
                    AddToLog($"Jiji: {RandomizerMod.Instance.Settings.Jiji}");
                }
                catch
                {
                    AddToLog("Error logging randomizer settings!?!?");
                }

                spoilerWatch.Stop();
                LogSpoiler(log);
                LogSpoiler("Generated spoiler log in " + spoilerWatch.Elapsed.TotalSeconds + " seconds.");
            }).Start();
        }

        private static string GetTransitionSpoiler((string, string)[] transitionPlacements)
        {
            string log = string.Empty;
            void AddToLog(string message) => log += message + Environment.NewLine;

            try
            {
                if (RandomizerMod.Instance.Settings.RandomizeAreas)
                {
                    Dictionary<string, List<string>> areaTransitions = new Dictionary<string, List<string>>();
                    foreach (string transition in LogicManager.TransitionNames())
                    {
                        string area = LogicManager.GetTransitionDef(transition).areaName;
                        if (!areaTransitions.ContainsKey(area))
                        {
                            areaTransitions[area] = new List<string>();
                        }
                    }

                    foreach ((string, string) pair in transitionPlacements)
                    {
                        string area = LogicManager.GetTransitionDef(pair.Item1).areaName;
                        areaTransitions[area].Add(pair.Item1 + " --> " + pair.Item2);
                    }

                    AddToLog(Environment.NewLine + "TRANSITIONS");
                    foreach (KeyValuePair<string, List<string>> kvp in areaTransitions)
                    {
                        if (kvp.Value.Count > 0)
                        {
                            AddToLog(Environment.NewLine + kvp.Key.Replace('_', ' ') + ":");
                            foreach (string transition in kvp.Value) AddToLog(transition);
                        }
                    }
                }

                if (RandomizerMod.Instance.Settings.RandomizeRooms)
                {
                    Dictionary<string, List<string>> roomTransitions = new Dictionary<string, List<string>>();
                    foreach (string transition in LogicManager.TransitionNames())
                    {
                        string room = LogicManager.GetTransitionDef(transition).sceneName;
                        if (!roomTransitions.ContainsKey(room))
                        {
                            roomTransitions[room] = new List<string>();
                        }
                    }

                    foreach ((string, string) pair in transitionPlacements)
                    {
                        string room = LogicManager.GetTransitionDef(pair.Item1).sceneName;
                        roomTransitions[room].Add(Translator.TranslateTransitionName(pair.Item1) + " --> " + Translator.TranslateTransitionName(pair.Item2));
                    }

                    AddToLog(Environment.NewLine + "TRANSITIONS");
                    foreach (KeyValuePair<string, List<string>> kvp in roomTransitions)
                    {
                        if (kvp.Value.Count > 0)
                        {
                            AddToLog(Environment.NewLine + kvp.Key.Replace('_', ' ') + ":");
                            foreach (string transition in kvp.Value) AddToLog(transition);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                RandomizerMod.Instance.LogError("Error while creating transition spoiler log: " + e);
            }
            return log;
        }

        private static string GetItemSpoiler((int, string, string)[] orderedILPairs)
        {
            string log = string.Empty;
            void AddToLog(string message) => log += message + Environment.NewLine;
            try
            {
                orderedILPairs = orderedILPairs.OrderBy(triplet => triplet.Item1).ToArray();

                Dictionary<string, List<string>> areaItemLocations = new Dictionary<string, List<string>>();
                foreach (var triplet in orderedILPairs)
                {
                    string location = triplet.Item3;
                    if (LogicManager.TryGetItemDef(location, out ReqDef locationDef))
                    {
                        string area = locationDef.areaName;
                        if (!areaItemLocations.ContainsKey(area))
                        {
                            areaItemLocations[area] = new List<string>();
                        }
                    }
                    else if (!areaItemLocations.ContainsKey(location))
                    {
                        areaItemLocations[location] = new List<string>();
                    }
                }

                List<string> progression = new List<string>();
                foreach ((int, string, string) pair in orderedILPairs)
                {
                    string cost = "";
                    if (LogicManager.TryGetItemDef(pair.Item3, out ReqDef itemDef)) {
                        if (itemDef.cost != 0) cost = $" [{itemDef.cost} {itemDef.costType.ToString("g")}]";
                    }
                    else cost = $" [{RandomizerMod.Instance.Settings.GetShopCost(pair.Item2)} Geo]";

                    if (LogicManager.GetItemDef(pair.Item2).progression) progression.Add($"({pair.Item1}) {pair.Item2}<---at--->{pair.Item3}{cost}");
                    if (LogicManager.TryGetItemDef(pair.Item3, out ReqDef locationDef))
                    {
                        areaItemLocations[locationDef.areaName].Add($"({pair.Item1}) {pair.Item2}<---at--->{pair.Item3}{cost}");
                    }
                    else areaItemLocations[pair.Item3].Add($"{pair.Item2}{cost}");
                }

                AddToLog(Environment.NewLine + "PROGRESSION ITEMS");
                foreach (string item in progression) AddToLog(item.Replace('_', ' '));

                AddToLog(Environment.NewLine + "ALL ITEMS");
                foreach (KeyValuePair<string, List<string>> kvp in areaItemLocations)
                {
                    if (kvp.Value.Count > 0)
                    {
                        string title = kvp.Key;
                        if (LogicManager.ShopNames.Contains(title)) title = $"({orderedILPairs.First(triplet => triplet.Item3 == title).Item1}) {title}";
                        title = CleanAreaName(title);
                        AddToLog(Environment.NewLine + title + ":");
                        foreach (string item in kvp.Value) AddToLog(item.Replace('_', ' '));
                    }
                }
            }
            catch (Exception e)
            {
                RandomizerMod.Instance.LogError("Error while creating item spoiler log: " + e);
            }
            return log;
        }

        public static void LogCondensedSpoiler(string message)
        {
            File.AppendAllText(Path.Combine(Application.persistentDataPath, "RandomizerCondensedSpoilerLog.txt"), message + Environment.NewLine);
        }

        public static void InitializeCondensedSpoiler()
        {
            File.Create(Path.Combine(Application.persistentDataPath, "RandomizerCondensedSpoilerLog.txt")).Dispose();
        }

        public static void LogItemsToCondensedSpoiler((int, string, string)[] orderedILPairs)
        {
            RandomizerMod.Instance.Log("Generating condensed spoiler log...");
            new Thread(() =>
            {
                Stopwatch spoilerWatch = new Stopwatch();
                spoilerWatch.Start();

                string log = GetCondensedItemSpoiler(orderedILPairs);

                spoilerWatch.Stop();
                LogCondensedSpoiler(log);
                LogCondensedSpoiler("Generated condensed spoiler log in " + spoilerWatch.Elapsed.TotalSeconds + " seconds.");
            }).Start();
        }

        private static string GetCondensedItemSpoiler((int, string, string)[] orderedILPairs)
        {
            string log = string.Empty;
            void AddToLog(string message) => log += message + Environment.NewLine;
            try
            {
                // Major progression
                string dash = "Mothwing/Shade Cloak:" + Environment.NewLine;
                string claw = "Mantis Claw:" + Environment.NewLine;
                string wings = "Monarch Wings:" + Environment.NewLine;
                string cdash = "Crystal Heart:" + Environment.NewLine;
                string tear = "Isma's Tear:" + Environment.NewLine;
                string dnail = "Dream Nail:" + Environment.NewLine;

                // Spells
                string vs = "Vengeful Spirit:" + Environment.NewLine;
                string dive = "Desolate Dive:" + Environment.NewLine;
                string wraiths = "Howling Wraiths:" + Environment.NewLine;

                // Nail arts
                string cyclone = "Cyclone Slash <---at---> ";
                string dashslash = "Dash Slash <---at---> ";
                string greatslash = "Great Slash <---at---> ";

                // Dreamers
                string lurien = "Lurien <---at---> ";
                string monomon = "Monomon <---at---> ";
                string herrah = "Herrah <---at---> ";
                string dreamer = "";

                // White Fragments
                string wf = "White Fragments:" + Environment.NewLine;

                // Stags
                string dirtmouth = "Dirtmouth Stag <---at---> ";
                string xroads = "Crossroads Stag <---at---> ";
                string gp = "Greenpath Stag <---at---> ";
                string qs = "Queen's Station Stag <---at---> ";
                string qg = "Queen's Gardens Stag <---at---> ";
                string storerooms = "City Storerooms Stag <---at---> ";
                string ks = "King's Station Stag <---at---> ";
                string rg = "Resting Grounds Stag <---at---> ";
                string dv = "Distant Village Stag <---at---> ";
                string hs = "Hidden Station Stag <---at---> ";
                string stagnest = "Stag Nest Stag <---at---> ";

                // Keys
                string skeys = "Simple Keys:" + Environment.NewLine;
                string shopkey = "Shopkeeper's Key <---at---> ";
                string ekey = "Elegant Key <---at---> ";
                string love = "Love Key <---at---> ";
                string tram = "Tram Pass <---at---> ";
                string lantern = "Lumafly Lantern <---at---> ";
                string brand = "King's Brand <---at---> ";
                string crest = "City Crest <---at---> ";

                // Important charms
                string grimmchild = "Grimmchild <---at---> ";
                string dashmaster = "Dashmaster <---at---> ";
                string shaman = "Shaman Stone <---at---> ";
                string twister = "Spell Twister <---at---> ";
                string strength = "Fragile Strength <---at---> ";
                string quickslash = "Quick Slash <---at---> ";

                // Baldur killers
                string elegy = "Grubberfly's Elegy <---at---> ";
                string gwomb = "Glowing Womb <---at---> ";
                string weaversong = "Weaversong <---at---> ";
                string spore = "Spore Shroom <---at---> ";
                string mop = "Mark of Pride <---at---> ";

                foreach (var triplet in orderedILPairs)
                {
                    string cost = "";
                    if (LogicManager.TryGetItemDef(triplet.Item3, out ReqDef itemDef)) {
                        if (itemDef.cost != 0) cost = $" [{itemDef.cost} {itemDef.costType.ToString("g")}]";
                    }
                    else cost = $" [{RandomizerMod.Instance.Settings.GetShopCost(triplet.Item2)} Geo]";

                    switch (triplet.Item2)
                    {
                        case "Mothwing_Cloak":
                        case "Mothwing_Cloak_(1)":
                        case "Shade_Cloak":
                        case "Shade_Cloak_(1)":
                            dash += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Mantis_Claw":
                        case "Mantis_Claw_(1)":
                            claw += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Monarch_Wings":
                        case "Monarch_Wings_(1)":
                            wings += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Crystal_Heart":
                        case "Crystal_Heart_(1)":
                            cdash += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Isma's_Tear":
                        case "Isma's_Tear_(1)":
                            tear += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Dream_Nail":
                        case "Dream_Nail_(1)":
                        case "Awoken_Dream_Nail":
                        case "Dream_Gate":
                            dnail += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Vengeful_Spirit":
                        case "Vengeful_Spirit_(1)":
                        case "Shade_Soul":
                            vs += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Desolate_Dive":
                        case "Desolate_Dive_(1)":
                        case "Descending_Dark":
                            dive += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Howling_Wraiths":
                        case "Howling_Wraiths_(1)":
                        case "Abyss_Shriek":
                            wraiths += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Cyclone_Slash":
                            cyclone += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Dash_Slash":
                            dashslash += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Great_Slash":
                            greatslash += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Lurien":
                            lurien += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Monomon":
                            monomon += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Herrah":
                            herrah += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Dreamer_(1)":
                            dreamer += "Dreamer <---at---> " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "King_Fragment":
                        case "Queen_Fragment":
                        case "Void_Heart":
                        case "Void_Heart_(1)":
                            wf += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Dirtmouth_Stag":
                            dirtmouth += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Crossroads_Stag":
                            xroads += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Greenpath_Stag":
                            gp += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Queen's_Station_Stag":
                            qs += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Queen's_Gardens_Stag":
                            qg += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "City_Storerooms_Stag":
                            storerooms += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "King's_Station_Stag":
                            ks += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Resting_Grounds_Stag":
                            rg += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Distant_Village_Stag":
                            dv += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Hidden_Station_Stag":
                            hs += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Stag_Nest_Stag":
                            stagnest += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Simple_Key-City":
                        case "Simple_Key-Sly":
                        case "Simple_Key-Lurker":
                        case "Simple_Key-Basin":
                            skeys += "- " + triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Shopkeeper's_Key":
                            shopkey += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Elegant_Key":
                            ekey += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Love_Key":
                            love += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Tram_Pass":
                            tram += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Lumafly_Lantern":
                            lantern += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "King's_Brand":
                            brand += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "City_Crest":
                            crest += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Grimmchild":
                            grimmchild += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Dashmaster":
                            dashmaster += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Shaman_Stone":
                            shaman += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Spell_Twister":
                            twister += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Fragile_Strength":
                            strength += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Quick_Slash":
                            quickslash += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Grubberfly's_Elegy":
                            elegy += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Glowing_Womb":
                            gwomb += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Weaversong":
                            weaversong += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Spore_Shroom":
                            spore += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        case "Mark_of_Pride":
                            mop += triplet.Item3 + cost + Environment.NewLine;
                            break;
                        default:
                            break;
                    }
                }
                    
                if (RandomizerMod.Instance.Settings.RandomizeSkills) {
                    AddToLog("----------Major Progression:----------");
                    AddToLog(dash + claw + wings + cdash + tear + dnail);
                    AddToLog("----------Spells:----------");
                    AddToLog(vs + dive + wraiths);
                    AddToLog("----------Nail Arts:----------");
                    AddToLog(cyclone + dashslash + greatslash);
                }

                if (RandomizerMod.Instance.Settings.RandomizeDreamers) {
                    AddToLog("----------Dreamers:----------");
                    AddToLog(lurien + monomon + herrah + dreamer);
                }

                if (RandomizerMod.Instance.Settings.RandomizeCharms) {
                    AddToLog("----------White Fragments:----------");
                    AddToLog(wf);
                }

                if (RandomizerMod.Instance.Settings.RandomizeStags) {
                    AddToLog("----------Stag Stations:----------");
                    AddToLog(dirtmouth + xroads + gp + qs + qg + storerooms + ks + rg + dv + hs + stagnest);
                }

                if (RandomizerMod.Instance.Settings.RandomizeKeys) {
                    AddToLog("----------Keys:----------");
                    AddToLog(skeys + shopkey + ekey + love + tram + lantern + brand + crest);
                }

                if (RandomizerMod.Instance.Settings.RandomizeCharms) {
                    AddToLog("----------Important Charms:----------");
                    AddToLog(grimmchild + dashmaster + shaman + twister + strength + quickslash);
                    AddToLog("----------Baldur Killers:-----------");
                    AddToLog(elegy + gwomb + weaversong + spore + mop);
                }
            }
            catch (Exception e)
            {
                RandomizerMod.Instance.LogError("Error while creating condensed item spoiler log: " + e);
            }
            return log;
        }

        public static string CleanAreaName(string name)
        {
            string newName = name.Replace('_', ' ');
            switch (newName)
            {
                case "Kings Pass":
                    newName = "King's Pass";
                    break;
                case "Queens Station":
                    newName = "Queen's Station";
                    break;
                case "Kings Station":
                    newName = "King's Station";
                    break;
                case "Queens Gardens":
                    newName = "Queen's Gardens";
                    break;
                case "Hallownests Crown":
                    newName = "Hallownest's Crown";
                    break;
                case "Kingdoms Edge":
                    newName = "Kingdom's Edge";
                    break;
                case "Weavers Den":
                    newName = "Weaver's Den";
                    break;
                case "Beasts Den":
                    newName = "Beast's Den";
                    break;
                case "Spirits Glade":
                    newName = "Spirit's Glade";
                    break;
                case "Ismas Grove":
                    newName = "Isma's Grove";
                    break;
                case "Teachers Archives":
                    newName = "Teacher's Archives";
                    break;
            }
            return newName;
        }
    }
}
