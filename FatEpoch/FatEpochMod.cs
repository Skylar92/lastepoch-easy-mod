using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(FatEpoch.FatEpochMod), "FatEpoch", "2.0.0", "Skylar92")]
[assembly: MelonGame("Eleventh Hour Games", "Last Epoch")]

namespace FatEpoch
{
    // Offline single-player only: online, the server generates loot and owns your points.
    //
    // No Harmony patches: on Unity 6000.4 with MelonLoader 0.7.3 any Il2CppInterop detour crashes
    // the .NET runtime when the player spawns. The mod only reads and writes game data.
    public class FatEpochMod : MelonMod
    {
        static MelonPreferences_Category cfg;
        static MelonPreferences_Entry<float> uniqueAndSetMultiplier;
        static MelonPreferences_Entry<float> exaltedChance;
        static MelonPreferences_Entry<float> rarityMultiplier;
        static MelonPreferences_Entry<float> passivePointsMultiplier;
        static MelonPreferences_Entry<float> skillPointsMultiplier;
        static MelonPreferences_Entry<float> resourceMultiplier;
        static MelonPreferences_Entry<float> goldMultiplier;
        static MelonPreferences_Entry<float> itemQuantityMultiplier;
        static MelonPreferences_Entry<int> forgingPotential;
        static MelonPreferences_Entry<bool> maxAffixRolls;
        static MelonPreferences_Entry<bool> maxLegendaryPotential;
        static MelonPreferences_Entry<bool> infiniteForging;

        float nextCheck, nextStatus;
        readonly System.Collections.Generic.HashSet<IntPtr> exaltedLogged = new();

        public override void OnInitializeMelon()
        {
            cfg = MelonPreferences.CreateCategory("FatEpoch");
            uniqueAndSetMultiplier = cfg.CreateEntry("UniqueAndSetMultiplier", 10f, description: "Multiplier for unique and set item drop chance.");
            exaltedChance = cfg.CreateEntry("ExaltedChance", 0.25f, description: "Minimum chance (0-1) for a rare item to become exalted.");
            rarityMultiplier = cfg.CreateEntry("RarityMultiplier", 3f, description: "Chance of an item having at least N affixes is multiplied by this (capped at 100%).");
            passivePointsMultiplier = cfg.CreateEntry("PassivePointsMultiplier", 3f, description: "Passive points earned (levels + quests) are multiplied by this.");
            skillPointsMultiplier = cfg.CreateEntry("SkillPointsMultiplier", 2f, description: "Skill points from skill levels are multiplied by this.");
            resourceMultiplier = cfg.CreateEntry("ResourceMultiplier", 10f, description: "Chance of crafting materials (shards, runes, glyphs) dropping is multiplied by this (capped at 100%).");
            goldMultiplier = cfg.CreateEntry("GoldMultiplier", 10f, description: "Gold dropped by enemies, chests and breakables is multiplied by this.");
            itemQuantityMultiplier = cfg.CreateEntry("ItemQuantityMultiplier", 2f, description: "Number of items dropped by enemies, chests and breakables is multiplied by this.");
            forgingPotential = cfg.CreateEntry("ForgingPotential", 63, description: "Dropped items with forging potential get at least this much (63 is the game's maximum, 0 = off).");
            maxAffixRolls = cfg.CreateEntry("MaxAffixRolls", true, description: "Dropped items get the top value of every affix, implicit and unique roll (tiers stay random).");
            maxLegendaryPotential = cfg.CreateEntry("MaxLegendaryPotential", true, description: "Dropped unique items get the maximum legendary potential (4).");
            infiniteForging = cfg.CreateEntry("InfiniteForging", true, description: "Crafting at the forge does not use forging potential.");
            cfg.SaveToFile(false);

            TryScale("unique/set drop rates", GameConstants.UniqueAndSetRates, uniqueAndSetMultiplier.Value);
            var crafting = TryScale("crafting items alongside drops", GameConstants.CraftingAlongsideChances, resourceMultiplier.Value, 1f);
            if (crafting != IntPtr.Zero)
            {
                try
                {
                    GameConstants.RedirectLowLevelCraftingChance(crafting);
                }
                catch (Exception e)
                {
                    LoggerInstance.Error("low-level crafting chance not changed: " + e.Message);
                }
            }
            TryScale("crafting-only drops", GameConstants.CraftingOnlyChances, resourceMultiplier.Value, 1f);
            LoggerInstance.Msg("FatEpoch loaded (no patches). Use in OFFLINE mode only.");
        }

        IntPtr TryScale(string label, float[] expected, float multiplier, float max = float.MaxValue)
        {
            try
            {
                return GameConstants.Scale(label, expected, multiplier, max);
            }
            catch (Exception e)
            {
                LoggerInstance.Error(label + " not changed: " + e.Message);
                return IntPtr.Zero;
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            nextStatus = 0f;
            Drops.Forget();
            GroundItems.Forget();
            LoggerInstance.Msg($"[status] scene loaded: {sceneName}");
        }

        public override void OnUpdate()
        {
            if (Time.unscaledTime < nextCheck)
                return;
            nextCheck = Time.unscaledTime + 1f;
            try
            {
                Tick();
            }
            catch (Exception e)
            {
                LoggerInstance.Error("Tick failed: " + e);
                nextCheck = Time.unscaledTime + 30f;
            }
        }

        void Tick()
        {
            RarityTables.Apply(rarityMultiplier.Value);

            // GenerateItems.instance stays null offline; the generator is found in the scene instead.
            foreach (var gen in UnityEngine.Object.FindObjectsOfType<GenerateItems>())
            {
                float target = Math.Min(1f, exaltedChance.Value);
                if (gen._ChanceForRareItemsToBecomeExalted0To1_k__BackingField < target)
                {
                    float was = gen._ChanceForRareItemsToBecomeExalted0To1_k__BackingField;
                    gen._ChanceForRareItemsToBecomeExalted0To1_k__BackingField = target;
                    if (exaltedLogged.Add(gen.Pointer))
                        LoggerInstance.Msg($"exalted chance: {was} -> {gen._ChanceForRareItemsToBecomeExalted0To1_k__BackingField}");
                }
            }

            Drops.Apply(goldMultiplier.Value, itemQuantityMultiplier.Value);
            GroundItems.Apply(forgingPotential.Value, maxAffixRolls.Value, maxLegendaryPotential.Value);
            if (infiniteForging.Value)
                Forge.Apply();

            var trees = UnityEngine.Object.FindObjectsOfType<LocalTreeData>();
            foreach (var tree in trees)
                {
                Points.Apply(tree, passivePointsMultiplier.Value);
                SkillLevels.Apply(tree, skillPointsMultiplier.Value);
            }

            if (Time.unscaledTime >= nextStatus)
            {
                nextStatus = Time.unscaledTime + 30f;
                LoggerInstance.Msg($"[status] trees={trees.Length} " + string.Join(" | ", trees.Select(Points.Describe)) +
                    $" boundaries={Dump(GenerateItems.boundaries)}");
            }
        }

        static string Dump(Il2CppStructArray<float> a) =>
            a == null ? "null" : "[" + string.Join(", ", Enumerable.Range(0, a.Length).Select(i => a[i].ToString("0.#####"))) + "]";
    }

    // Every enemy, chest and breakable carries an ItemDrop with its own multipliers. Scale them once
    // per object; a changed value means the object is new (or was reset) and gets scaled again.
    static class Drops
    {
        static readonly System.Collections.Generic.Dictionary<IntPtr, (float gold, float chance)> applied = new();
        static int logged;

        internal static void Forget() => applied.Clear();

        internal static void Apply(float goldMult, float itemMult)
        {
            foreach (var drop in UnityEngine.Object.FindObjectsOfType<ItemDrop>())
            {
                if (applied.TryGetValue(drop.Pointer, out var a) && a.gold == drop.goldMultiplier && a.chance == drop.itemDropChanceMultiplier)
                    continue;
                // itemMultiplier is 0 on enemies (meaning "default"), so scale the drop chance multiplier instead.
                float gold = drop.goldMultiplier, chance = drop.itemDropChanceMultiplier;
                drop.goldMultiplier = gold * goldMult;
                drop.itemDropChanceMultiplier = chance * itemMult;
                applied[drop.Pointer] = (drop.goldMultiplier, drop.itemDropChanceMultiplier);
                if (logged++ < 5)
                    MelonLogger.Msg($"drop '{drop.gameObject.name}': gold x{gold} -> x{drop.goldMultiplier}, item chance x{chance} -> x{drop.itemDropChanceMultiplier}");
            }
        }
    }

    // Offline, dropped items wait in GroundItemManager until picked up. Improve each new one there:
    // forging potential up to the configured value, every roll at its top (rolls are bytes, 255 = max).
    static class GroundItems
    {
        const byte TopRoll = 255;
        static readonly System.Collections.Generic.HashSet<uint> done = new();
        static int logged;

        internal static void Forget() => done.Clear();

        internal static void Apply(int potential, bool maxRolls, bool maxLegendary)
        {
            var items = GroundItemManager.instance?.offlineItems?.items;
            if (items == null)
                return;
            for (int i = 0; i < items.Count; i++)
            {
                var ground = items[i];
                var data = ground?.itemData;
                if (data == null || !done.Add(ground.id))
                    continue;

                int fpBefore = data.forgingPotential;
                if (potential > 0 && fpBefore > 0 && fpBefore < potential)
                    data.forgingPotential = (byte)Math.Min(ItemData.MaxForgingPotential, potential);

                int lpBefore = data.legendaryPotential;
                if (maxLegendary && data.isUnique() && data.weaversWill == 0)
                    data.legendaryPotential = (byte)ItemData.MaxLegendaryPotential;

                int affixes = 0;
                if (maxRolls)
                {
                    var list = data.affixes;
                    if (list != null)
                        for (int k = 0; k < list.Count; k++, affixes++)
                            list[k].affixRoll = TopRoll;
                    Top(data.implicitRolls);
                    Top(data.uniqueRolls);
                }
                if (logged++ < 10)
                    MelonLogger.Msg($"ground item {ground.id} (type {data.itemType}, rarity {data.rarity}): FP {fpBefore} -> {data.forgingPotential}, LP {lpBefore} -> {data.legendaryPotential}, {affixes} affixes maxed");
            }
        }

        static void Top(Il2CppStructArray<byte> rolls)
        {
            if (rolls == null)
                return;
            for (int i = 0; i < rolls.Length; i++)
                rolls[i] = TopRoll;
        }
    }

    // The forge has a developer switch that skips the forging potential cost; it lives on each
    // ItemContainersManager's CraftingManager (client and the offline server side).
    static class Forge
    {
        static readonly System.Collections.Generic.HashSet<IntPtr> logged = new();

        internal static void Apply()
        {
            foreach (var containers in UnityEngine.Object.FindObjectsOfType<ItemContainersManager>())
            {
                var crafting = containers.craftingManager;
                if (crafting == null || crafting.debugNoForgingPotentialCost)
                    continue;
                crafting.debugNoForgingPotentialCost = true;
                if (logged.Add(crafting.Pointer))
                    MelonLogger.Msg("infinite forging: on");
            }
        }
    }

    // Skill points come from skill level plus "level of skills" stats, which the game recomputes from
    // the character's stats all the time. Instead of fighting that, give the player a long buff with
    // LevelOfSkills, so the game itself grants the extra points: highest skill level x (multiplier - 1).
    static class SkillLevels
    {
        const string BuffName = "FatEpoch skill levels";
        const int MaxSkillLevel = 20;
        static int bonus = -1;
        static AT tags = AT.None;
        static float checkAt;
        static bool tagsResolved;

        internal static void Apply(LocalTreeData tree, float multiplier)
        {
            var buffs = tree.actor?.statBuffs;
            var skills = tree.specialisedSkillTrees;
            if (buffs == null || skills == null || skills.Count == 0)
                return;

            int highest = 0;
            for (int i = 0; i < skills.Count; i++)
                if (skills[i] != null)
                    highest = Math.Max(highest, skills[i].level);
            // Capped at max skill level, in case the game counts the buff into the level itself.
            int wanted = (int)Math.Round(Math.Min(highest, MaxSkillLevel) * (multiplier - 1f));
            if (wanted <= 0)
                return;

            var active = buffs.activeBuffNames;
            bool present = active != null && active.ContainsKey(BuffName);
            if (present && wanted == bonus)
            {
                ResolveTags(buffs, skills);
                return;
            }
            if (present)
                buffs.removeBuff(active[BuffName], true);
            Add(buffs, wanted);
        }

        static void Add(StatBuffs buffs, int value)
        {
            buffs.addBuff(1e9f, SP.LevelOfSkills, value, 0f, new Il2CppSystem.Collections.Generic.List<float>(), tags, BuffName);
            bonus = value;
            checkAt = Time.unscaledTime + 3f;
            MelonLogger.Msg($"skill levels buff: +{value} (tags {tags})");
        }

        // A tagless LevelOfSkills stat may not apply to any skill; if the game ignores it, use every tag.
        static void ResolveTags(StatBuffs buffs, Il2CppSystem.Collections.Generic.List<LocalTreeData.SkillTreeData> skills)
        {
            if (tagsResolved || Time.unscaledTime < checkAt)
                return;
            int applied = 0;
            for (int i = 0; i < skills.Count; i++)
                if (skills[i] != null)
                    applied = Math.Max(applied, skills[i].additionalMaxPointsFromStats);
            if (applied >= bonus)
            {
                tagsResolved = true;
                MelonLogger.Msg($"skill levels buff works with tags {tags}");
                return;
            }
            if (tags != AT.None)
            {
                tagsResolved = true;
                MelonLogger.Warning("skill levels buff is not applied by the game");
                return;
            }
            tags = Enum.GetValues(typeof(AT)).Cast<AT>().Aggregate(AT.None, (x, y) => x | y);
            buffs.removeBuff(buffs.activeBuffNames[BuffName], true);
            Add(buffs, bonus);
        }
    }

    // Points are recalculated by the game (level up, load, gear change). Each time the game writes a
    // new value, take it as the base and write the multiplied value back.
    static class Points
    {
        static readonly System.Collections.Generic.Dictionary<IntPtr, int> passiveApplied = new();

        internal static void Apply(LocalTreeData tree, float passiveMult)
        {
            var passive = tree.passiveTree;
            if (passive != null)
            {
                int current = passive.pointsEarnt;
                if (!passiveApplied.TryGetValue(passive.Pointer, out int applied) || applied != current)
                {
                    int target = Math.Min(ushort.MaxValue, (int)Math.Round(current * passiveMult));
                    if (target != current)
                    {
                        passive.pointsEarnt = (ushort)target;
                        MelonLogger.Msg($"passive points: {current} -> {passive.pointsEarnt}");
                    }
                    passiveApplied[passive.Pointer] = passive.pointsEarnt;
                }
            }

        }

        internal static string Describe(LocalTreeData tree)
        {
            var skills = tree.specialisedSkillTrees;
            var list = skills == null ? "" : string.Join(",", Enumerable.Range(0, skills.Count).Select(i => skills[i])
                .Where(x => x != null).Select(x => $"lvl{x.level}+{x.additionalMaxPointsFromStats} max={x.getMaxPoints()} unspent={x.getUnspentPoints()}"));
            return $"lvl {tree.characterLevel} passive {tree.passiveTree?.pointsEarnt} skills [{list}]";
        }
    }

    // Drop rates are C# constants, compiled into GameAssembly's read-only data. Each group below is a
    // run of adjacent floats that occurs once in the module and is read by a single loot function
    // (checked against the game's code). Rewrite them in memory once at startup.
    static class GameConstants
    {
        [DllImport("kernel32.dll")]
        static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

        const uint PageReadWrite = 0x04;

        // set, low-level unique, unique
        internal static readonly float[] UniqueAndSetRates = { 0.00156f, 0.00695f, 0.0073f };
        // crafting items dropping alongside an item: late game, early game
        internal static readonly float[] CraftingAlongsideChances = { 0.086f, 0.104f };
        // crafting-only drops: early game, late game
        internal static readonly float[] CraftingOnlyChances = { 0.049f, 0.057f };

        internal static unsafe IntPtr Scale(string label, float[] expected, float multiplier, float max = float.MaxValue)
        {
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            byte* start = (byte*)module.BaseAddress;
            var image = new ReadOnlySpan<byte>(start, module.ModuleMemorySize);
            var pattern = MemoryMarshal.AsBytes(expected.AsSpan()).ToArray();

            int at = image.IndexOf(pattern);
            if (at < 0)
                throw new InvalidOperationException($"{label}: constants not found (game updated?)");
            if (image.Slice(at + 1).IndexOf(pattern) >= 0)
                throw new InvalidOperationException($"{label}: constants found more than once");

            var address = (IntPtr)(start + at);
            VirtualProtect(address, (UIntPtr)pattern.Length, PageReadWrite, out uint old);
            float* values = (float*)address;
            for (int i = 0; i < expected.Length; i++)
                values[i] = Math.Min(max, expected[i] * multiplier);
            VirtualProtect(address, (UIntPtr)pattern.Length, old, out _);
            MelonLogger.Msg($"{label} x{multiplier}: {string.Join(", ", expected)} -> {string.Join(", ", Enumerable.Range(0, expected.Length).Select(i => values[i]))}");
            return address;
        }

        const uint PageExecuteReadWrite = 0x40;

        // Below level 12 the crafting drop function reads a different chance, 0.28, from a constant that
        // two other functions share. Point only that one load (movss xmm, [rip+disp32]) in the crafting
        // function at our scaled early-game chance instead, found next to the loads of the scaled pair.
        internal static unsafe void RedirectLowLevelCraftingChance(IntPtr pair)
        {
            var module = Process.GetCurrentProcess().Modules.Cast<ProcessModule>()
                .First(m => m.ModuleName.Equals("GameAssembly.dll", StringComparison.OrdinalIgnoreCase));
            byte* start = (byte*)module.BaseAddress;
            int size = module.ModuleMemorySize;
            byte* shared = (byte*)pair + 0x14;
            if (*(float*)shared != 0.28f)
                throw new InvalidOperationException("low-level crafting chance not where expected");

            int pairRef = -1;
            for (int i = 0; i < size - 4; i++)
                if (start + i + 4 + *(int*)(start + i) == (byte*)pair)
                {
                    pairRef = i;
                    break;
                }
            if (pairRef < 0)
                throw new InvalidOperationException("crafting drop function not found");

            int hit = -1;
            for (int i = Math.Max(4, pairRef - 0x400); i < Math.Min(size - 4, pairRef + 0x400); i++)
                if (start + i + 4 + *(int*)(start + i) == shared && start[i - 4] == 0xF3 && start[i - 3] == 0x0F && start[i - 2] == 0x10)
                {
                    if (hit >= 0)
                        throw new InvalidOperationException("more than one low-level crafting load");
                    hit = i;
                }
            if (hit < 0)
                throw new InvalidOperationException("low-level crafting load not found");

            byte* target = (byte*)pair + 4; // scaled early-game chance
            var at = (IntPtr)(start + hit);
            VirtualProtect(at, (UIntPtr)4, PageExecuteReadWrite, out uint old);
            *(int*)(start + hit) = (int)(target - (start + hit + 4));
            VirtualProtect(at, (UIntPtr)4, old, out _);
            MelonLogger.Msg($"low-level crafting chance: 0.28 -> {*(float*)target}");
        }
    }

    // Rarity rolls compare a 0-1 roll with four thresholds (0..4 affixes); the game blends the
    // starts/earlyMids/lateMids/ends tables by level. Raising P(at least N affixes) = 1 - threshold
    // by a multiplier pushes items towards rare.
    static class RarityTables
    {
        static Il2CppStructArray<float>[] tables;
        static float[][] originals;

        internal static void Apply(float multiplier)
        {
            var current = new[] { GenerateItems.starts, GenerateItems.earlyMids, GenerateItems.lateMids, GenerateItems.ends };
            if (current.Any(t => t == null))
                return;
            if (tables != null && tables.Zip(current, (a, b) => a.Pointer == b.Pointer).All(x => x))
                return;
            tables = current;
            originals = current.Select(t => Enumerable.Range(0, t.Length).Select(i => t[i]).ToArray()).ToArray();
            for (int k = 0; k < tables.Length; k++)
                for (int i = 0; i < tables[k].Length; i++)
                {
                    float chance = 1f - originals[k][i];
                    if (chance > 0f)
                        tables[k][i] = Math.Max(0f, 1f - chance * multiplier);
                }
            MelonLogger.Msg($"rarity x{multiplier}: ends {string.Join(", ", originals[3])} -> {string.Join(", ", Enumerable.Range(0, tables[3].Length).Select(i => tables[3][i]))}");
        }
    }
}
