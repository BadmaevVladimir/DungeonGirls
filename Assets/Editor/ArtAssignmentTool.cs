// Assets/Editor/ArtAssignmentTool.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// 10.6: одноразовый инструмент — присваивает спрайты дизайнера в поля Sprite соответствующих
// ScriptableObject-ассетов по таблице соответствий (Global Constraints плана). Не часть игрового
// рантайма — вызывается один раз через меню редактора, результат — изменённые .asset файлы.
public static class ArtAssignmentTool
{
    [MenuItem("DungeonGirls/Art/Assign Sprites From Correspondence Table")]
    public static void AssignSprites()
    {
        AssignCharacter("Assets/Art/Characters/Jennifer.png", "Assets/ScriptableObjects/Characters/Character_Jennifer.asset");

        AssignMonster("Assets/Art/Enemies/Slime.png", "Assets/ScriptableObjects/Monsters/Monster_Slime.asset");
        AssignMonster("Assets/Art/Enemies/Skeleton.png", "Assets/ScriptableObjects/Monsters/Monster_Skeleton.asset");
        AssignMonster("Assets/Art/Enemies/Warlock.png", "Assets/ScriptableObjects/Monsters/Monster_Warlock.asset");
        AssignMonster("Assets/Art/Enemies/Bat.png", "Assets/ScriptableObjects/Monsters/Monster_Bat.asset");
        AssignMonster("Assets/Art/Enemies/Goblin.png", "Assets/ScriptableObjects/Monsters/Monster_GoblinThief.asset");
        AssignMonster("Assets/Art/Enemies/Golem.png", "Assets/ScriptableObjects/Monsters/Monster_StoneGuardian.asset");
        AssignMonster("Assets/Art/Enemies/Spider.png", "Assets/ScriptableObjects/Monsters/Monster_PoisonSpiderling.asset");
        AssignMonster("Assets/Art/Enemies/Harpy.png", "Assets/ScriptableObjects/Monsters/Monster_Harpy.asset");
        AssignMonster("Assets/Art/Enemies/Dark Cleric.png", "Assets/ScriptableObjects/Monsters/Monster_DarkPriest.asset");
        AssignMonster("Assets/Art/Enemies/Dark Knight.png", "Assets/ScriptableObjects/Monsters/Monster_DarkKnight.asset");

        Debug.LogWarning("[ArtAssignmentTool] Monster_Boss.asset has NO matching art file in this drop — its sprite field stays null. Assign it manually once the designer sends boss art.");

        AssignItems("Assets/Art/Items/Weapons/Sword.png", new[] {
            "Assets/ScriptableObjects/Items/Weapons/Sword/Item_Sword_Common_IronSword.asset",
            "Assets/ScriptableObjects/Items/Weapons/Sword/Item_Sword_Rare_SteelGladius.asset",
            "Assets/ScriptableObjects/Items/Weapons/Sword/Item_Sword_Epic_BloodSword.asset",
        });
        AssignItems("Assets/Art/Items/Weapons/Axe.png", new[] {
            "Assets/ScriptableObjects/Items/Weapons/Axe/Item_Axe_Common_IronAxe.asset",
            "Assets/ScriptableObjects/Items/Weapons/Axe/Item_Axe_Rare_SteelAxe.asset",
            "Assets/ScriptableObjects/Items/Weapons/Axe/Item_Axe_Epic_Rubilo.asset",
        });
        AssignItems("Assets/Art/Items/Weapons/Spear.png", new[] {
            "Assets/ScriptableObjects/Items/Weapons/Spear/Item_Spear_Common_IronSpear.asset",
            "Assets/ScriptableObjects/Items/Weapons/Spear/Item_Spear_Rare_SteelSpear.asset",
            "Assets/ScriptableObjects/Items/Weapons/Spear/Item_Spear_Epic_SwiftSpear.asset",
        });
        AssignItems("Assets/Art/Items/Weapons/Hammer.png", new[] {
            "Assets/ScriptableObjects/Items/Weapons/Hammer/Item_Hammer_Common_IronHammer.asset",
            "Assets/ScriptableObjects/Items/Weapons/Hammer/Item_Hammer_Rare_SteelHammer.asset",
            "Assets/ScriptableObjects/Items/Weapons/Hammer/Item_Hammer_Epic_SmithHammer.asset",
        });
        AssignItems("Assets/Art/Items/Armor/Shield.png", new[] {
            "Assets/ScriptableObjects/Items/Weapons/Shield/Item_Shield_Common_WoodenShield.asset",
        });
        AssignItems("Assets/Art/Items/Armor/Armor.png", new[] {
            "Assets/ScriptableObjects/Items/Armor/Item_Armor_Common_IronCuirass.asset",
            "Assets/ScriptableObjects/Items/Armor/Item_Armor_Rare_SteelCuirass.asset",
            "Assets/ScriptableObjects/Items/Armor/Item_Armor_Epic_EtherealArmor.asset",
        });
        AssignItems("Assets/Art/Items/Armor/Helmet.png", new[] {
            "Assets/ScriptableObjects/Items/Helmet/Item_Helmet_Common_SimpleHelmet.asset",
            "Assets/ScriptableObjects/Items/Helmet/Item_Helmet_Rare_SteelHelmet.asset",
            "Assets/ScriptableObjects/Items/Helmet/Item_Helmet_Epic_MidasCrown.asset",
        });
        AssignItems("Assets/Art/Items/Armor/Boots.png", new[] {
            "Assets/ScriptableObjects/Items/Boots/Item_Boots_Common_SturdyBoots.asset",
            "Assets/ScriptableObjects/Items/Boots/Item_Boots_Rare_SwiftBoots.asset",
            "Assets/ScriptableObjects/Items/Boots/Item_Boots_Epic_ArmoredBoots.asset",
        });
        AssignItems("Assets/Art/Items/Rings/Ring.png", new[] {
            "Assets/ScriptableObjects/Items/Rings/Item_Ring_Agility.asset",
            "Assets/ScriptableObjects/Items/Rings/Item_Ring_Armor.asset",
            "Assets/ScriptableObjects/Items/Rings/Item_Ring_Fortune.asset",
            "Assets/ScriptableObjects/Items/Rings/Item_Ring_Health.asset",
            "Assets/ScriptableObjects/Items/Rings/Item_Ring_Mage.asset",
            "Assets/ScriptableObjects/Items/Rings/Item_Ring_Power.asset",
            "Assets/ScriptableObjects/Items/Rings/Item_Ring_Speed.asset",
        });
        AssignItems("Assets/Art/Items/Amulets/Amulet.png", new[] {
            "Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Dexterity.asset",
            "Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Luck.asset",
            "Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Might.asset",
            "Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Resilience.asset",
            "Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Sorcerer.asset",
            "Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Swiftness.asset",
            "Assets/ScriptableObjects/Items/Accessories/Item_Accessory_Vitality.asset",
        });

        AssetDatabase.SaveAssets();
        Debug.Log("[ArtAssignmentTool] Sprite assignment complete. Check the Console above for any warnings (missing art / missing assets).");
    }

    static Sprite LoadSprite(string path)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            Debug.LogError($"[ArtAssignmentTool] Could not load Sprite at '{path}' — check the file exists and imported as Texture Type = Sprite.");
        }
        return sprite;
    }

    static void AssignCharacter(string spritePath, string assetPath)
    {
        var sprite = LoadSprite(spritePath);
        var data = AssetDatabase.LoadAssetAtPath<CharacterData>(assetPath);
        if (data == null) { Debug.LogError($"[ArtAssignmentTool] CharacterData not found at '{assetPath}'."); return; }
        data.portrait = sprite;
        EditorUtility.SetDirty(data);
    }

    static void AssignMonster(string spritePath, string assetPath)
    {
        var sprite = LoadSprite(spritePath);
        var data = AssetDatabase.LoadAssetAtPath<MonsterData>(assetPath);
        if (data == null) { Debug.LogError($"[ArtAssignmentTool] MonsterData not found at '{assetPath}'."); return; }
        data.sprite = sprite;
        EditorUtility.SetDirty(data);
    }

    static void AssignItems(string spritePath, IEnumerable<string> assetPaths)
    {
        var sprite = LoadSprite(spritePath);
        foreach (var assetPath in assetPaths)
        {
            var data = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);
            if (data == null) { Debug.LogError($"[ArtAssignmentTool] ItemData not found at '{assetPath}'."); continue; }
            data.icon = sprite;
            EditorUtility.SetDirty(data);
        }
    }
}
