# ImageGen prompt manifest

Tool: built-in ImageGen, one generation per character.

Shared prompt:

> Use case: style-transfer. Asset type: new Unity 2D battle sprite for DungeonGirls. The supplied class sprite is a strict reference for tiny PC-98 proportions, dark outline, palette density and rendering only. Create one isolated full-body female character, feet visible, tightly cropped. Authentic tiny hand-pixeled PC-98 JRPG battle sprite, limited palette, one-pixel dark outline and the same anatomy scale/detail density as the reference. Genuinely transparent background and transparent empty pixels; no glow, shadow, environment, text, UI, frame, anti-aliasing or watermark.

Class references:

- Warrior: `Assets/Art/Characters/Jennifer.png`; target visual size about 32×48 px.
- Rogue: `Assets/Art/Characters/Violet.png`; target visual size about 24×45 px.
- Barbarian: `Assets/Art/Characters/Sasha.png`; target visual size about 40×58 px.

Subjects:

- Warrior_Elina: blonde braided shield duelist; blue-steel armor; round shield and one-handed sword; defensive stance.
- Warrior_Rina: short auburn-haired spear captain; bronze-and-green light armor; diagonal spear; forward-driving stance.
- Warrior_Marta: dark bobbed-haired forge sentinel; soot-marked heavy armor; war hammer and heater shield; grounded stance.
- Rogue_Kira: silver-haired cardsharp; burgundy hooded leather; two blades and lucky coin; evasive side-on stance.
- Rogue_Mirel: midnight-blue-haired shadow dancer; charcoal-and-teal leather; two curved blades and flowing scarf; spinning stance.
- Rogue_Iona: black-haired dungeon alchemist; moss-green hood; jagged blade and poison vials; stalking stance.
- Barbarian_Freya: pale-braided blood shaman; crimson markings, fur/leather and bone-charms two-handed axe.
- Barbarian_Ragna: black-undercut executioner; iron jaw guard, dark-red heavy leather, trophy belt and chipped great axe.
- Barbarian_Tora: copper-red storm raider; blue war paint, fur mantle, twin axes and iron charms; lunging stance.

Post-processing:

- Full generated files are preserved under `Concepts/`.
- `GameReady/` candidates are alpha-cropped, resized with nearest-neighbor to class-appropriate canvases (Warrior 40×58, Rogue 32×58, Barbarian 48×64), and use a hard alpha edge.
- No existing game art was overwritten.
