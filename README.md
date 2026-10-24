# FatEpoch (Last Epoch, MelonLoader)

Offline single-player only. Online the server generates loot and owns your points.

No Harmony patches: on Unity 6000.4 with MelonLoader 0.7.3 any Il2CppInterop detour crashes the
.NET runtime when the player spawns. The mod only reads and writes game data:

| Setting | Default | How |
|---|---|---|
| PassivePointsMultiplier | 3 | `LocalTreeData.passiveTree.pointsEarnt`, rescaled whenever the game recalculates it |
| SkillPointsMultiplier | 2 | long player buff `LevelOfSkills +highest skill level × (mult − 1)`; the game grants the points itself |
| UniqueAndSetMultiplier | 10 | unique/set rates are C# constants: rewritten in GameAssembly's read-only data |
| RarityMultiplier | 3 | P(at least N affixes) in the static rarity tables `starts/earlyMids/lateMids/ends` |
| ExaltedChance | 0.25 | floor for `GenerateItems` exalted chance (found in the scene, `instance` is null offline) |
| ResourceMultiplier | 10 | crafting drop constants; below level 12 one load in the crafting function is redirected to the scaled chance |
| GoldMultiplier | 10 | `ItemDrop.goldMultiplier` on every enemy, chest and breakable |
| ItemQuantityMultiplier | 2 | `ItemDrop.itemDropChanceMultiplier` (`itemMultiplier` is 0 = default on enemies) |
| ForgingPotential | 63 | items on the ground (`GroundItemManager.offlineItems`) get at least this FP (63 = game maximum) |
| MaxAffixRolls | true | items on the ground get roll 255 on every affix, implicit and unique roll |
| MaxLegendaryPotential | true | unique items on the ground (without Weaver's Will) get legendary potential 4 |
| InfiniteForging | true | the game's own `CraftingManager.debugNoForgingPotentialCost` on every `ItemContainersManager` |

Settings live in `UserData/MelonPreferences.cfg` under `[FatEpoch]`; edit them with the game closed.

## Setup

1. MelonLoader v0.7.3 (`version.dll` + `MelonLoader/`) in the game folder.
2. `Mods/FatEpoch.dll`.
3. Unity 6000.4 fix: MelonLoader writes `MelonLoader/Il2CppAssemblies/UnityEngine.CoreModule.dll`
   with an unsorted nested-type table, so the support module fails with
   `Duplicate type with name '<>O'` and no mod loads. Re-save it through Cecil:

```
dotnet tools/AsmTool/bin/Release/net10.0/AsmTool.dll rewrite <Il2CppAssemblies>/UnityEngine.CoreModule.dll.bak <Il2CppAssemblies>/UnityEngine.CoreModule.dll
```

   Redo this whenever MelonLoader regenerates assemblies (after a game patch). A game patch can also
   move the constants; the mod then logs "constants not found" and leaves those rates alone.

## Build

```
dotnet build FatEpoch -c Release
```
