using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ManagementScripts;
using SettingScripts;
using SimulationScripts;
using SimulationScripts.BibiteScripts;
using UnityEngine;
using Utility;

namespace Reproduction
{
    [BepInPlugin("Reproduction", "Competitive Reproduction", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        public void Awake()
        {
            Log = Logger;
            //Log.LogInfo("Competitive Reproduction: Awake() start");
            try
            {
                var harmony = new Harmony("Reproduction");
                harmony.PatchAll();
                //Log.LogInfo("Competitive Reproduction: PatchAll() completed with no exception");

                //foreach (var method in harmony.GetPatchedMethods())
                //{
                    //Log.LogInfo($"Competitive Reproduction: patched {method.DeclaringType?.Name}.{method.Name}");
                //}
            }
            catch (Exception e)
            {
                Log.LogError("Competitive Reproduction: PatchAll() THREW - patches may be partially applied");
                Log.LogError(e.ToString());
            }
        }
    }

    public sealed class EliteConfig
    {
        public int EggInterval;
        public float? GenePercent;
        public float BrainPercent;
        public int BrainTransferType;
        public float[] GeneValues;
        public NEATBrain.Node[] BrainNodes;
        public NEATBrain.Synaps[] BrainSynapses;
        public BibiteGenes SourceGenes;
    }

    public static class Registry
    {
        public const int DefaultInterval = 3;
        public const float DefaultBrainPercent = 50f;

        // TAG FORMAT: mix-{interval}-{brainTransferType}-{genePercent}-{brainPercent}-{customTargetTag}

        // NULLABLE
        // No "customTargetTag" = applies to all bibites instead of bibites with this custom target tag
        // No "genePercent" = switch genes to random crossover instead of a fixed percentage
        // No "brainPercent" = defaults to 50% (see below)

        // "brainTransferType" has two options: "t1" or "t2" (if null, "t2" by default)
        // t1: simple - chance is based on {brainPercent}, and will replace the brain with the elite entirely
        // t2: realistic - {brainPercent} crossover similar to gene crossover

        private const string DefaultSlotKey = "\0default";

        private static readonly Dictionary<string, EliteConfig> Elites = new Dictionary<string, EliteConfig>();
        private static readonly Dictionary<BibiteGenes, int> EggCounts = new Dictionary<BibiteGenes, int>();

        private static AccessTools.FieldRef<BibiteSpawner, List<BibiteSpawnInfo>> _bibiteSpawnInfosRef;
        private static bool _bibiteSpawnInfosRefFailed;

        private static AccessTools.FieldRef<BibiteSpawner, List<BibiteSpawnInfo>> GetBibiteSpawnInfosRef()
        {
            if (_bibiteSpawnInfosRef != null || _bibiteSpawnInfosRefFailed) return _bibiteSpawnInfosRef;
            try
            {
                _bibiteSpawnInfosRef = AccessTools.FieldRefAccess<BibiteSpawner, List<BibiteSpawnInfo>>("bibiteSpawnInfos");
                //Plugin.Log?.LogInfo("Registry: bound BibiteSpawner.bibiteSpawnInfos field ref OK");
            }
            catch (Exception e)
            {
                _bibiteSpawnInfosRefFailed = true;
                Plugin.Log?.LogError("Registry: FAILED to bind BibiteSpawner.bibiteSpawnInfos - " + e);
            }
            return _bibiteSpawnInfosRef;
        }

        public static int NextEggCount(BibiteGenes mother)
        {
            EggCounts.TryGetValue(mother, out int n);
            n++;
            EggCounts[mother] = n;
            return n;
        }
        private static bool TryParseTag(string tag, out int interval, out int brainTransferType, out float? genePct, out float brainPct, out string slotKey)
        {
            interval = DefaultInterval;
            brainTransferType = 2;
            genePct = null;
            brainPct = DefaultBrainPercent;
            slotKey = null;

            if (string.IsNullOrEmpty(tag) || !tag.StartsWith("mix-")) return false;

            string[] parts = tag.Split('-');
            if (parts.Length < 2 || string.IsNullOrEmpty(parts[1])) return false;

            if (int.TryParse(parts[1], out int parsedInterval) && parsedInterval > 0)
                interval = parsedInterval;

            int cursor = 2;

            if (cursor < parts.Length && parts[cursor].Length >= 2 && (parts[cursor][0] == 't' || parts[cursor][0] == 'T')
                && int.TryParse(parts[cursor].Substring(1), out int parsedType) && (parsedType == 1 || parsedType == 2))
            {
                brainTransferType = parsedType;
                cursor++;
            }

            if (cursor < parts.Length && float.TryParse(parts[cursor], out float parsedGenePct))
            {
                genePct = Mathf.Clamp(parsedGenePct, 0f, 100f);
                cursor++;
                if (cursor < parts.Length && float.TryParse(parts[cursor], out float parsedBrainPct))
                {
                    brainPct = Mathf.Clamp(parsedBrainPct, 0f, 100f);
                    cursor++;
                }
            }

            string targetTag = cursor < parts.Length ? string.Join("-", parts, cursor, parts.Length - cursor) : null;
            slotKey = string.IsNullOrEmpty(targetTag) ? DefaultSlotKey : targetTag;
            return true;
        }
        public static bool HasAnyElites => Elites.Count > 0;

        public static void TryRegister(BibiteBody body)
        {
            if (body.gene == null) return;
            string tag = body.gene.speciesTag;
            if (string.IsNullOrEmpty(tag) || !tag.StartsWith("mix-")) return;

            if (!TryParseTag(tag, out int interval, out int brainTransferType, out float? genePct, out float brainPct, out string slotKey))
            {
                Plugin.Log?.LogWarning($"Registry.TryRegister: '{tag}' starts with mix- but failed to parse (need at least mix-<interval>)");
                return;
            }

            Elites[slotKey] = new EliteConfig
            {
                EggInterval = interval,
                GenePercent = genePct,
                BrainPercent = brainPct,
                BrainTransferType = brainTransferType,
                GeneValues = body.gene.genes,
                BrainNodes = body.brain.Nodes,
                BrainSynapses = body.brain.Synapses,
                SourceGenes = body.gene
            };
            //Plugin.Log?.LogInfo($"Registry.TryRegister: registered slot '{(slotKey == DefaultSlotKey ? "<default>" : slotKey)}' interval={interval} gene%={(genePct.HasValue ? genePct.Value.ToString() : "random")} brainType={brainTransferType} brain%={brainPct} (from living bibite)");
        }

        public static void Unregister(BibiteBody body)
        {
            if (body.gene == null) return;

            EggCounts.Remove(body.gene);

            List<string> toRemove = null;
            foreach (var kv in Elites)
            {
                if (kv.Value.SourceGenes == body.gene)
                {
                    if (toRemove == null) toRemove = new List<string>();
                    toRemove.Add(kv.Key);
                }
            }
            if (toRemove == null) return;
            for (int i = 0; i < toRemove.Count; i++) Elites.Remove(toRemove[i]);
            //Plugin.Log?.LogInfo($"Registry.Unregister: removed {toRemove.Count} slot(s) sourced from a bibite that just died");
        }

        public static void RescanTemplates(BibiteSpawner spawner)
        {
            var infosRef = GetBibiteSpawnInfosRef();
            if (infosRef == null) return;

            List<BibiteSpawnInfo> infos = infosRef(spawner);
            if (infos == null)
            {
                Plugin.Log?.LogWarning("Registry.RescanTemplates: bibiteSpawnInfos was null");
                return;
            }

            HashSet<string> seenThisPass = new HashSet<string>();

            for (int i = 0; i < infos.Count; i++)
            {
                BibiteSpawnInfo info = infos[i];
                BibiteSettings settings = info.settings;
                BibiteTemplate template = info.template;
                if (settings == null || template == null) continue;
                if (settings.tagging.val != Tagging.CustomTagging) continue;

                string tag = settings.customTag.val;
                if (!TryParseTag(tag, out int interval, out int brainTransferType, out float? genePct, out float brainPct, out string slotKey))
                {
                    if (!string.IsNullOrEmpty(tag) && tag.StartsWith("mix-"))
                        Plugin.Log?.LogWarning($"Registry.RescanTemplates: '{tag}' starts with mix- but failed to parse");
                    continue;
                }

                Elites[slotKey] = new EliteConfig
                {
                    EggInterval = interval,
                    GenePercent = genePct,
                    BrainPercent = brainPct,
                    BrainTransferType = brainTransferType,
                    GeneValues = template.genes,
                    BrainNodes = template.nodes,
                    BrainSynapses = template.synapses,
                    SourceGenes = null
                };
                seenThisPass.Add(slotKey);
                //Plugin.Log?.LogInfo($"Registry.RescanTemplates: registered slot '{(slotKey == DefaultSlotKey ? "<default>" : slotKey)}' from template '{settings.templateName}' interval={interval} gene%={(genePct.HasValue ? genePct.Value.ToString() : "random")} brainType={brainTransferType} brain%={brainPct}");
            }

            List<string> stale = null;
            foreach (var kv in Elites)
            {
                if (kv.Value.SourceGenes == null && !seenThisPass.Contains(kv.Key))
                {
                    if (stale == null) stale = new List<string>();
                    stale.Add(kv.Key);
                }
            }
            if (stale != null)
                for (int i = 0; i < stale.Count; i++) Elites.Remove(stale[i]);
        }

        public static bool TryGetConfigFor(string motherTag, out EliteConfig config)
        {
            if (!string.IsNullOrEmpty(motherTag) && Elites.TryGetValue(motherTag, out config))
                return true;
            return Elites.TryGetValue(DefaultSlotKey, out config);
        }

        public static void Clear()
        {
            //Plugin.Log?.LogInfo($"Registry.Clear: wiping {Elites.Count} slot(s) and {EggCounts.Count} egg counter(s)");
            Elites.Clear();
            EggCounts.Clear();
        }
    }

    [HarmonyPatch(typeof(EggHatching), "InitEgg")]
    public static class CrossbreedOnInitEgg
    {
        private static readonly MethodInfo InitEggMethod = AccessTools.Method(typeof(EggHatching), "InitEgg");
        private static readonly HashSet<EggHatching> InProgress = new HashSet<EggHatching>();

        static void Postfix(EggHatching __instance)
        {
            if (!Registry.HasAnyElites) return;
            if (InProgress.Contains(__instance)) return;

            BibiteGenes eggGene = __instance.eggGene;
            NEATBrain eggBrain = __instance.eggBrain;
            if (eggGene == null || eggBrain == null) return;
            if (eggGene.parent1 == null) return;

            BibiteGenes motherGenes = eggGene.parent1.GetComponent<BibiteGenes>();
            if (motherGenes == null) return;

            string motherTag = motherGenes.speciesTag;
            if (!string.IsNullOrEmpty(motherTag) && motherTag.StartsWith("mix-")) return;

            if (!Registry.TryGetConfigFor(motherTag, out EliteConfig config)) return;
            if (config.GeneValues == null || config.BrainNodes == null || config.BrainSynapses == null) return;

            if (Registry.NextEggCount(motherGenes) % config.EggInterval != 0) return;

            if (config.GenePercent.HasValue)
            {
                if (config.GenePercent.Value > 0f)
                {
                    float t = config.GenePercent.Value / 100f;
                    float[] eliteGenes = config.GeneValues;
                    float[] childGenes = eggGene.genes;
                    int n = Mathf.Min(childGenes.Length, eliteGenes.Length);
                    for (int i = 0; i < n; i++)
                        childGenes[i] = Mathf.Lerp(childGenes[i], eliteGenes[i], t);
                    eggGene.CapGenes();
                }
            }
            else
            {
                CrossoverGenes(eggGene, config.GeneValues);
            }

            if (config.BrainPercent > 0f)
            {
                if (config.BrainTransferType == 2)
                    RealisticCrossoverBrain(eggBrain, eggGene, eggBrain.Nodes, eggBrain.Synapses, config.BrainNodes, config.BrainSynapses, config.BrainPercent / 100f);
                else if (UnityEngine.Random.Range(0f, 100f) < config.BrainPercent)
                    eggBrain.CopyBrain(config.BrainNodes, config.BrainSynapses, false, true, false);
            }

            InProgress.Add(__instance);
            try { InitEggMethod.Invoke(__instance, null); }
            finally { InProgress.Remove(__instance); }
        }
        private static void CrossoverGenes(BibiteGenes eggGene, float[] eliteGenes)
        {
            float[] childGenes = eggGene.genes;
            int n = Mathf.Min(childGenes.Length, eliteGenes.Length);
            if (n <= 0) return;

            bool takeChild = UnityEngine.Random.value < 0.5f;
            int run = 0;
            int runLength = eggGene.RandChi(n);
            for (int i = 0; i < n; i++)
            {
                if (!takeChild) childGenes[i] = eliteGenes[i];
                run++;
                if (run >= runLength)
                {
                    takeChild = !takeChild;
                    runLength = eggGene.RandChi(n - run);
                    run = 0;
                }
            }
            eggGene.CapGenes();
        }

        private static readonly Dictionary<long, NEATBrain.Node> ScratchChildHidden = new Dictionary<long, NEATBrain.Node>();
        private static readonly Dictionary<long, NEATBrain.Node> ScratchEliteHidden = new Dictionary<long, NEATBrain.Node>();
        private static readonly Dictionary<long, int> ScratchIndexByInov = new Dictionary<long, int>();
        private static readonly List<NEATBrain.Node> ScratchMergedNodes = new List<NEATBrain.Node>();
        private static readonly HashSet<long> ScratchHiddenInov = new HashSet<long>();
        private static readonly Dictionary<long, NEATBrain.Synaps> ScratchChildSyn = new Dictionary<long, NEATBrain.Synaps>();
        private static readonly Dictionary<long, NEATBrain.Synaps> ScratchEliteSyn = new Dictionary<long, NEATBrain.Synaps>();
        private static readonly HashSet<long> ScratchSynInov = new HashSet<long>();
        private static readonly List<NEATBrain.Synaps> ScratchMergedSynapses = new List<NEATBrain.Synaps>();

        // Segment-based crossover on the brain, mirroring CrossoverGenes' RandChi-run-length approach
        // exactly, but walking the sorted union of both parents' innovation numbers instead of a fixed
        // array. This produces contiguous "chromosome segments" of shared/unique structure inherited
        // together, matching how real crossover swaps stretches of a chromosome rather than shuffling
        // individual genes independently.
        private static void RealisticCrossoverBrain(NEATBrain eggBrain, BibiteGenes eggGene,
            NEATBrain.Node[] childNodes, NEATBrain.Synaps[] childSynapses,
            NEATBrain.Node[] eliteNodes, NEATBrain.Synaps[] eliteSynapses, float eliteChance)
        {
            int coreCount = NEATBrain.NInputs + NEATBrain.NOutputs;

            // --- Nodes: build the sorted union of ALL innovation numbers (core + hidden) from both parents ---
            ScratchChildHidden.Clear();
            for (int i = 0; i < childNodes.Length; i++) ScratchChildHidden[childNodes[i].Inov] = childNodes[i];
            ScratchEliteHidden.Clear();
            for (int i = 0; i < eliteNodes.Length; i++) ScratchEliteHidden[eliteNodes[i].Inov] = eliteNodes[i];

            ScratchHiddenInov.Clear();
            foreach (long inov in ScratchChildHidden.Keys) ScratchHiddenInov.Add(inov);
            foreach (long inov in ScratchEliteHidden.Keys) ScratchHiddenInov.Add(inov);

            List<long> sortedNodeInov = new List<long>(ScratchHiddenInov);
            sortedNodeInov.Sort();

            ScratchMergedNodes.Clear();
            ScratchIndexByInov.Clear();

            // Walk the sorted innovation sequence in contiguous runs, alternating which parent
            // "owns" each run - same algorithm as CrossoverGenes, just over a different sequence.
            bool takeChild = UnityEngine.Random.value >= eliteChance;
            int run = 0;
            int runLength = eggGene.RandChi(sortedNodeInov.Count);
            for (int idx = 0; idx < sortedNodeInov.Count; idx++)
            {
                long inov = sortedNodeInov[idx];
                bool inChild = ScratchChildHidden.TryGetValue(inov, out NEATBrain.Node childNode);
                bool inElite = ScratchEliteHidden.TryGetValue(inov, out NEATBrain.Node eliteNode);

                NEATBrain.Node chosen;
                if (inChild && inElite) chosen = takeChild ? childNode : eliteNode;
                else chosen = inChild ? childNode : eliteNode; // unique to one side always carries over

                ScratchIndexByInov[inov] = ScratchMergedNodes.Count;
                ScratchMergedNodes.Add(chosen);

                run++;
                if (run >= runLength)
                {
                    takeChild = !takeChild;
                    runLength = eggGene.RandChi(sortedNodeInov.Count - idx - 1);
                    run = 0;
                }
            }

            // --- Synapses: same run-length logic, walked in sorted synapse-innovation order ---
            ScratchChildSyn.Clear();
            for (int i = 0; i < childSynapses.Length; i++) ScratchChildSyn[childSynapses[i].Inov] = childSynapses[i];
            ScratchEliteSyn.Clear();
            for (int i = 0; i < eliteSynapses.Length; i++) ScratchEliteSyn[eliteSynapses[i].Inov] = eliteSynapses[i];

            ScratchSynInov.Clear();
            foreach (long inov in ScratchChildSyn.Keys) ScratchSynInov.Add(inov);
            foreach (long inov in ScratchEliteSyn.Keys) ScratchSynInov.Add(inov);

            List<long> sortedSynInov = new List<long>(ScratchSynInov);
            sortedSynInov.Sort();

            ScratchMergedSynapses.Clear();
            takeChild = UnityEngine.Random.value >= eliteChance;
            run = 0;
            runLength = eggGene.RandChi(sortedSynInov.Count);
            for (int idx = 0; idx < sortedSynInov.Count; idx++)
            {
                long inov = sortedSynInov[idx];
                bool inChild = ScratchChildSyn.TryGetValue(inov, out NEATBrain.Synaps childSyn);
                bool inElite = ScratchEliteSyn.TryGetValue(inov, out NEATBrain.Synaps eliteSyn);

                NEATBrain.Synaps syn;
                NEATBrain.Node[] sourceNodes;
                if (inChild && inElite) { syn = takeChild ? childSyn : eliteSyn; sourceNodes = takeChild ? childNodes : eliteNodes; }
                else if (inChild) { syn = childSyn; sourceNodes = childNodes; }
                else { syn = eliteSyn; sourceNodes = eliteNodes; }

                if (ScratchIndexByInov.TryGetValue(sourceNodes[syn.NodeIn].Inov, out int newIn) &&
                    ScratchIndexByInov.TryGetValue(sourceNodes[syn.NodeOut].Inov, out int newOut))
                {
                    ScratchMergedSynapses.Add(new NEATBrain.Synaps(syn.Inov, newIn, newOut, syn.Weight, syn.En));
                }
                // else: this synapse's endpoint node wasn't inherited in this run - drop the synapse safely

                run++;
                if (run >= runLength)
                {
                    takeChild = !takeChild;
                    runLength = eggGene.RandChi(sortedSynInov.Count - idx - 1);
                    run = 0;
                }
            }

            eggBrain.CopyBrain(ScratchMergedNodes.ToArray(), ScratchMergedSynapses.ToArray(), true, true, true);
        }
    }

    [HarmonyPatch(typeof(BibiteBody), "StartBody")]
    public static class RegisterOnNaturalBirth
    {
        static void Postfix(BibiteBody __instance) => Registry.TryRegister(__instance);
    }

    [HarmonyPatch(typeof(BibiteBody), "StartBodyAtGrowthAndNormalize")]
    public static class RegisterOnSpawn
    {
        static void Postfix(BibiteBody __instance) => Registry.TryRegister(__instance);
    }

    [HarmonyPatch(typeof(BibiteBody), "OnDestroy")]
    public static class UnregisterOnDeath
    {
        static void Postfix(BibiteBody __instance) => Registry.Unregister(__instance);
    }

    [HarmonyPatch(typeof(BibiteSpawner), "StartSpawner")]
    public static class ScanTemplatesOnStart
    {
        static void Postfix(BibiteSpawner __instance)
        {
            //Plugin.Log?.LogInfo("BibiteSpawner.StartSpawner fired");
            Registry.RescanTemplates(__instance);
        }
    }

    [HarmonyPatch(typeof(BibiteSpawner), "ResumeSpawner")]
    public static class ScanTemplatesOnResume
    {
        static void Postfix(BibiteSpawner __instance)
        {
            //Plugin.Log?.LogInfo("BibiteSpawner.ResumeSpawner fired");
            Registry.RescanTemplates(__instance);
        }
    }

    [HarmonyPatch(typeof(BibiteSpawner), "DistributeSpawnPriority")]
    public static class ScanTemplatesOnChange
    {
        static void Postfix(BibiteSpawner __instance) => Registry.RescanTemplates(__instance);
    }

    [HarmonyPatch(typeof(MenuInitializer), "Start")]
    public static class ClearOnMenu
    {
        static void Postfix() => Registry.Clear();
    }
}
