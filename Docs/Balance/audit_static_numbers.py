"""Arithmetic audit of current asset fields. This is NOT a combat simulation."""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent

def fields(path):
    result = {}
    for key, value in re.findall(r'^  (\w+): (.*)$', path.read_text(encoding='utf-8-sig'), re.M):
        try:
            result[key] = float(value)
        except ValueError:
            result[key] = value
    return result

def scale(value, level):
    return value if value <= 0 else value + max(1, round(value * .1)) * (level - 1)

monsters = []
for path in sorted((ROOT / 'Assets/ScriptableObjects/Monsters').glob('*.asset')):
    d = fields(path)
    for floor in (1, 4, 7, 10):
        if floor < d.get('minFloorTier', 1):
            continue
        level = 1 if d['isBoss'] else 4
        lo = scale(d['damageMin'] * 1.15 ** (floor - 1), level)
        hi = scale(d['damageMax'] * 1.15 ** (floor - 1), level)
        monsters.append(dict(monster=path.stem, floor=floor, monster_level=level,
            hp=round(scale(d['hp'] * 1.25 ** (floor - 1), level), 3),
            armor=round(scale(d['physicalDefense'] * 1.15 ** (floor - 1), level), 3),
            shield=d['magicDefense'], raw_auto_dps=round((lo + hi) * .5 * d['attackSpeed'], 3)))

weapons = []
for path in sorted((ROOT / 'Assets/ScriptableObjects/Items').rglob('*.asset')):
    d = fields(path)
    base = d.get('baseDamage', 0)
    if base <= 0:
        continue
    for level in (1, 15):
        cursed = d.get('tier') == 3
        damage = base * 2.2 + max(1, round(base * .1)) * (level - 1) if cursed else scale(base, level)
        if d.get('isTwoHanded') and not cursed:
            damage *= 1.3
        count = 2 if d.get('isPairedWeapon') else 1
        weapons.append(dict(item=path.stem, level=level, damage=round(damage, 3),
            raw_auto_dps=round(damage * d['attackSpeed'] * count, 3)))

report = dict(method='Asset arithmetic only: no crit, armour mitigation, bonuses, procs or random modifiers. Weapon damage before range rounding, without dual-wield penalties.',
    monsters=monsters, weapons=weapons,
    examples=dict(bleed5_dps_at_2_connected_hits_per_second_75pct_crit=20+60*.75+2*.75*60,
        rogue_poison5_max_dps=10, oathbreaker_currency_at_1_5_hits_50pct_crit_per_second=30*1.5*.5,
        wall5_damage_from_level15_shield=scale(3, 15)*.5,
        xp_for_level15=sum(i*25 for i in range(1,15)),
        xp_10floors_7combats_2successes_per_floor=sum(7*(10+3*(f-1))+2*(5+f-1)+50 for f in range(1,11)),
        xp_10floors_8combats_1success_per_floor=sum(8*(10+3*(f-1))+(5+f-1)+50 for f in range(1,11)),
        chest_currency_10floors_7combats_per_floor=sum(7*.5*10*f+50*f for f in range(1,11))))
(OUT / '2026-09-10-static-numbers.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report['examples'], indent=2))
print('\nFloor 10 monsters:')
for row in monsters:
    if row['floor'] == 10: print(row)
