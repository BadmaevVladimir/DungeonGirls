using System.Collections.Generic;
using UnityEngine;

// Имя файла намеренно совпадает с именем класса (единственный тип в файле, не считая
// зависимого IngredientDropRule — см. комментарий в FoodRecipeData про CreateAsset/MonoScript):
// Unity привязывает MonoScript-идентичность файла к типу, чьё имя совпадает с filename, а при
// отсутствии совпадения — к первому объявленному типу. Раньше RoomRewardConfig делил файл с
// IRewardRandom/UnityRewardRandom/SeededRewardRandom/RoomRewardResult/RoomRewardGrant (см.
// RewardRandom.cs) под именем RoomRewards.cs — ни с одним из типов не совпадающим, из-за чего
// Unity привязывала файл к IRewardRandom (первый объявленный тип) и логировала
// "'IRewardRandom' is missing the class attribute 'ExtensionOfNativeClass'!" при каждом
// восстановлении сцены — RoomRewardConfig.asset (guid 7a31a2db23b24ad7b21c19eecba19626,
// нетронутый этим переносом) от этого не ломался функционально, но засорял консоль.
[CreateAssetMenu(fileName = "RoomRewardConfig", menuName = "DungeonGirls/Room Reward Config")]
public class RoomRewardConfig : ScriptableObject
{
    [Range(0f, 1f)] public float combatChestDropChance = 0.50f;
    [Range(0f, 1f)] public float combatIngredientDropChance = 0.30f;
    [Range(0f, 1f)] public float successfulTrapIngredientDropChance = 0.45f;
    [Range(0f, 1f)] public float supportedSpecialIngredientDropChance = 0.60f;
    [Range(0f, 1f)] public float normalCombatForgeMaterialChance = 0.05f;
    [Range(0f, 1f)] public float successfulTrapForgeMaterialChance = 0.08f;
    public List<IngredientDropRule> combatIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.RawMeat, weight=65 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterEggs, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=5 }
    };
    public List<IngredientDropRule> trapIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=45 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.CaveMushrooms, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Grain, weight=5 }
    };
    public List<IngredientDropRule> specialIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.Grain, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.CaveMushrooms, weight=22 },
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Dairy, weight=15 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.EtherealSpice, weight=3 }
    };
    public List<IngredientDropRule> bossIngredientDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterEggs, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Dairy, weight=20 },
        new IngredientDropRule { resourceId=PersistentResourceIds.CaveMushrooms, weight=15 },
        new IngredientDropRule { resourceId=PersistentResourceIds.HealingHerbs, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RawMeat, weight=10 },
        new IngredientDropRule { resourceId=PersistentResourceIds.Grain, weight=5 },
        new IngredientDropRule { resourceId=PersistentResourceIds.RootVegetables, weight=5 },
        new IngredientDropRule { resourceId=PersistentResourceIds.EtherealSpice, weight=5 }
    };
    public List<IngredientDropRule> combatForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=60 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=25 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=15 }
    };
    public List<IngredientDropRule> trapForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=45 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=35 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=20 }
    };
    public List<IngredientDropRule> bossForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=35 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=30 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=25 },
        new IngredientDropRule { resourceId=PersistentResourceIds.AncientShard, weight=10 }
    };
    public List<IngredientDropRule> abandonedForgeMaterialDrops = new List<IngredientDropRule>
    {
        new IngredientDropRule { resourceId=PersistentResourceIds.TemperedSteel, weight=45 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MagicCrystal, weight=35 },
        new IngredientDropRule { resourceId=PersistentResourceIds.MonsterCore, weight=15 },
        new IngredientDropRule { resourceId=PersistentResourceIds.AncientShard, weight=5 }
    };
}
