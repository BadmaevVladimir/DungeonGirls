# PixelLab order BA-PILOT-001

Статус: ready to queue. Дата фиксации баланса: 2026-09-12.

Цель пилота — проверить три крайних режима до массового заказа: быстрая атака, многофазный
гуманоид и длительное состояние. Генератор: PixelLab `animate_image`. Все исходники — edit targets,
их дизайн, палитра, пропорции, направление взгляда, прозрачность, холст и опорная точка неизменны.

## Общие запреты для каждого motion prompt

`Animate only the existing character. Preserve the exact character design, anatomy, equipment,
palette, pixel density, facing direction, transparent background, canvas and planted position.
No camera movement, no background, no new props, no cropping, no translation across the canvas,
no rotation of the whole sprite, no added glow or particles.`

## Пилот A — Гончая Ямы

Исходник: `Assets/Art/Enemies/Bosses/PitHound.png`, 128×128.
Баланс: обычная атака каждые 0,526 с; «Рывок» — телеграф 1,5 с, кулдаун 7 с.

| ID | Клип | Кадры PixelLab | Проигрывание | Motion prompt |
|---|---|---:|---|---|
| PH-P1-IDLE-A | Idle A | 4 + source | loop, 6 fps | `Low tense breathing in place; ribcage and shoulders move subtly, head makes a tiny alert motion, all four paws stay firmly planted; return exactly to the source pose.` |
| PH-P1-IDLE-B | Idle B | 4 + source | loop, 6 fps | `Barely restrained predatory idle: a shallow breath, slight neck tension and one small tail twitch; all four paws remain fixed; return exactly to the source pose.` |
| PH-P1-ATTACK-A | Regular attack A | 4 + source | one-shot, 10 fps, impact candidate 2–3 | `A very fast compact snap bite toward the player on the left, minimal windup, immediate jaw contact, then a short recoil back to the source stance; paws do not travel.` |
| PH-P1-ATTACK-B | Regular attack B | 4 + source | one-shot, 10 fps, impact candidate 2–3 | `A fast claw-and-bite feint toward the left, tiny shoulder dip, sharp contact, rapid return to the original crouch; keep the movement compact enough for half a second.` |
| PH-P1-CHARGE-A | Рывок A | 8 + source | telegraph hold 1,5 s, impact chosen after review | `Crouch much lower and coil the body for a powerful leap toward the left, hold a clearly readable launch pose, then make one explosive full-body lunge and recoil to the source stance; remain within the canvas.` |
| PH-P1-CHARGE-B | Рывок B | 8 + source | telegraph hold 1,5 s, impact chosen after review | `Draw the head and shoulders back while hind legs compress, pause in an unmistakable ready-to-pounce silhouette, burst forward with jaws open toward the left, then return to the exact source stance.` |

Для обоих Idle исходный PNG передаётся и как first, и как last frame. Для attack/charge исходник
передаётся как first frame; возврат оценивается визуально. Лучший вариант каждого типа остаётся,
второй удаляется после приёмки.

## Пилот B — Владыка Ямы

Исходники Unity: `PitLord.png` и `PitLord_P2.png`, 1254×1254. Перед отправкой создаются отдельные
128×128 production references в `Assets/ArtSource/PixelLab/Bosses/PitLord/`; оригиналы не меняются.
Фаза 1: обычная атака 1,25 с, «Приговор» — телеграф 2,5 с. Фазы 2–3: интервал обычной атаки
0,781 с, «Казнь»/«Последний приговор» — телеграф 1,5 с.

Первая очередь этого пилота ограничена фазой 1:

| ID | Клип | Кадры PixelLab | Проигрывание | Motion prompt |
|---|---|---:|---|---|
| PL-P1-IDLE-A/B | Idle variants | 4 + source | loop, 6 fps | `Commanding armored idle; slow controlled breathing, a subtle shift of weight and a very small cape movement; sword point and both feet remain anchored; return exactly to the source pose.` |
| PL-P1-ATTACK-A/B | Regular attack variants | 8 + source | one-shot, 8 fps | `A disciplined compact sword cut toward the player on the left: short draw-back, clear blade contact, controlled recovery to the exact source guard; keep both feet planted and the cape secondary.` |
| PL-P1-VERDICT-A/B | Приговор variants | 8 + source | windup stretched/held to 2,5 s | `Raise the sword into a solemn execution stance, hold a strong readable apex with the blade high, then deliver one heavy downward verdict strike toward the left and recover to the source guard; no magic effects.` |

После проверки идентичности и масштаба заказываются фазы 2–3, поднятие панциря, открытая защита,
казнь, ярость и оба phase-enter. До этого дорогой humanoid-набор не размножается.

## Пилот C — Костяной Левиафан

Исходники: `BoneLeviathan.png`, `BoneLeviathan_P2.png`, 128×128.
Баланс: обычная атака 0,667 с; «Захлёст» — телеграф 2,5 с; «Погружение» — 3 с
неуязвимости без атак.

| ID | Клип | Кадры PixelLab | Проигрывание | Motion prompt |
|---|---|---:|---|---|
| BL-P1-IDLE-A/B | Idle variants | 4 + source | loop, 6 fps | `Slow segmented breathing wave through the skeletal body, tiny jaw motion, base remains fixed; return exactly to the source pose.` |
| BL-P1-ATTACK-A/B | Regular attack variants | 4 + source | one-shot, 7 fps | `A compact fast jaw snap toward the player on the left, only a small neck recoil and immediate return; keep the body base fixed and finish within two thirds of a second.` |
| BL-P1-LASH-A/B | Захлёст variants | 8 + source | windup held to 2,5 s | `Coil the segmented body backward into a clear high-tension arc, hold the readable apex, then whip the skull and upper body toward the left in one heavy lash and settle back to the source pose.` |
| BL-P1-DIVE-ENTER-A/B | Погружение enter | 4 + source | one-shot | `Collapse downward into the ground in place, skeletal segments folding and sinking vertically until only a compact submerged silhouette remains; no lateral movement.` |

Loop и Exit для погружения заказываются после выбора последнего кадра `DIVE-ENTER`: этот кадр
станет first/last anchor петли и first anchor выхода. Так переходы будут физически непрерывными.

## Очередность постановки

1. Шесть вариантов Гончей — самый дешёвый полный вертикальный срез.
2. Отсмотр и фиксация выбранных кадров/impact.
3. Шесть вариантов первой фазы Владыки после подготовки 128×128 references.
4. Восемь вариантов Левиафана; затем зависимые Dive Loop/Exit.
5. Только после интеграционного теста — основной заказ BA-WAVE-001.

## Приёмка

- alpha сохранена, фон действительно прозрачный;
- кадры имеют один размер и не обрезают силуэт;
- нет новых деталей, изменения количества конечностей или дрейфа палитры;
- опорная точка не прыгает более чем на один пиксель;
- Idle замыкается без скачка;
- обычная атака завершается до следующего игрового удара;
- выбранный impact читается однозначно и синхронизируется с эффектом с точностью до кадра;
- последний кадр action возвращается в Idle либо имеет отдельный короткий recovery bridge.

## Бюджет пилота

Гончая: 6 вызовов (Idle 1 gen каждый, Attack 1, Charge 2) = около 8 генераций.
Владыка P1: после downscale 6 вызовов = около 10 генераций.
Левиафан: 8 первичных вызовов = около 12 генераций, плюс Dive Loop/Exit около 2–4.
Весь пилот: ориентир 32–36 генераций до повторных роллов.
