using UnityEngine;

// Имя файла намеренно совпадает с именем класса — см. подробный комментарий в
// RoomRewardConfig.cs про Unity MonoScript-идентичность multi-type файлов. Раньше этот класс жил
// в RareRoomRewardHooks.cs вместе с RareRoomContentId/RareRoomFloorState/RareRoomContentResolver/
// RareRoomRewardHooks (см. RareRoomContent.cs) — и хотя RareRoomConfig был объявлен ПЕРВЫМ в
// файле, имя файла точно совпадало с ПОСЛЕДНИМ классом (RareRoomRewardHooks), и Unity
// приоритетно привязывала MonoScript именно к типу с совпадающим именем файла — отсюда
// "'RareRoomRewardHooks' is missing the class attribute 'ExtensionOfNativeClass'!" при каждом
// восстановлении сцены, хотя RareRoomConfig.asset (guid 8c570894012f481f807cd4c917833e6a,
// нетронутый этим переносом) работал функционально корректно.
[CreateAssetMenu(fileName = "RareRoomConfig", menuName = "DungeonGirls/Rare Room Config")]
public class RareRoomConfig : ScriptableObject
{
    [Range(0f, 1f)] public float mushroomCaveChance = 0.10f;
    [Min(1)] public int mushroomCaveMinimumFloor = 2;
    [Min(1)] public int mushroomCavePerFloorLimit = 1;
    [Range(0f, 1f)] public float harpyNestChance = 0.10f;
    [Min(1)] public int harpyNestMinimumFloor = 2;
    [Min(1)] public int harpyNestPerFloorLimit = 1;
    [Range(0f, 1f)] public float abandonedForgeChance = 0.08f;
    [Min(1)] public int abandonedForgeMinimumFloor = 3;
    [Min(1)] public int abandonedForgePerFloorLimit = 1;
    [Min(1)] public int mushroomSafeAmount = 2;
    [Min(1)] public int mushroomRiskAmount = 4;
    [Range(0f, 1f)] public float mushroomPoisonChance = 0.25f;
    [Min(1)] public int mushroomPoisonDurationRooms = 3;
    [Range(0f, 100f)] public float mushroomPoisonHealingPenaltyPercent = 10f;
    [Min(1)] public int harpySuccessMinEggs = 2;
    [Min(1)] public int harpySuccessMaxEggs = 3;
    [Min(1)] public int harpyFailureVictoryEggs = 1;
    [Range(0f, 1f)] public float abandonedForgeTwoMaterialsChance = 0.25f;
}
