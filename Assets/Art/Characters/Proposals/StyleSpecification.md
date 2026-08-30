# Dungeon Girls character-art specification

## Почему предыдущая генерация не совпала

Новые боевые кандидаты были сгенерированы как крупные пиксельные иллюстрации и только затем уменьшены. Оригиналы (`Jennifer.png`, `Violet.png`, `Sasha.png`) нарисованы непосредственно в финальном разрешении 23×45–40×58 px. Поэтому у оригиналов крупные осмысленные кластеры, а у уменьшенных генераций — шум, ломаные тонкие линии, слишком длинные конечности и современная «мобильная JRPG» детализация.

## Измеримые признаки оригинальных боевых спрайтов

- Canvas: Jennifer 32×48, Violet 23×45, Sasha 40×58 px.
- Пропорции: компактный super-deformed/chibi силуэт примерно 2.5–3 головы высотой; крупная голова, короткий торс и конечности.
- Камера: полный рост, 3/4 вправо; лицо и грудная клетка направлены вправо, ноги почти под корпусом.
- Поза: спокойная готовность к бою, без широких выпадов и без оружия, разрывающего компактный силуэт.
- Контур: непрерывный тёмно-коричневый/почти чёрный контур толщиной ровно 1 финальный пиксель.
- Пиксельные кластеры: поверхности строятся пятнами размером 2–6 пикселей; одиночные шумовые пиксели почти не используются.
- Свет: один верхне-левый источник; на материале 2–3 тона плюс редкие блики.
- Цвет: одна доминирующая классовая гамма и один металлический/кожаный акцент; без радужной палитры.
- Лицо: 1–2 читаемых глаза, нос/рот сведены к одному пикселю или отсутствуют.
- Фон: прозрачный; никакого свечения, тени или рамки.

## Production-промпт боевого спрайта

> Create one original Dungeon Girls battle sprite, drawn natively at final pixel resolution — never generate a large illustration for later downscaling. Canvas must be class-appropriate: 32×48 px for a compact Warrior or Rogue, up to 40×58 px only when hair or a two-handed weapon requires it. Authentic hand-authored 1990s Japanese PC-98 / 16-bit dungeon-RPG sprite. Compact super-deformed anatomy, exactly 2.5–3 heads tall: oversized head, short torso, short limbs, feet directly beneath the body. Full-body neutral combat-ready stance, three-quarter view facing screen-right, weapon kept close to the torso. Continuous one-pixel near-black/brown outline. Build every surface from deliberate 2–6 pixel clusters; use 2–3 shades per material and a single upper-left light source. One dominant class palette plus restrained metal/leather accents. Face must remain readable with one- or two-pixel eyes and minimal mouth/nose. Transparent background. No aura, floor shadow, frame, text or UI. Avoid tall adult anatomy, wide dynamic poses, painterly gradients, high-resolution pixel blocks, anti-aliasing, noisy single pixels, micro-accessories and modern mobile-JRPG rendering.

## Измеримые признаки оригинальных диалоговых артов

- Canvas: строго 1024×1536 px, вертикаль 2:3.
- Фигура почти касается верхней и нижней границы: высота непрозрачного силуэта около 1520–1529 px.
- Jennifer/Violet занимают примерно 600–617 px по ширине; Sasha с волосами и топором — до 929 px.
- Пропорции: взрослая аниме-героиня примерно 6.5–7 голов высотой, длинные ноги, узкая талия, выраженный читаемый костюм.
- Композиция: полный рост, 3/4 вправо, лицо на уровне верхней четверти, нейтрально-уверенная поза; оружие видно целиком.
- Рендер: fine-grained PC-98 visual-novel pixel illustration — тонкий 1–2 px тёмный контур, мелкие контролируемые пиксельные штрихи и dithering, а не крупные квадратные пиксели.
- Свет: верхне-левый ключевой; металл имеет узкие белые блики, кожа/ткань — 3–5 ступеней тени.
- Фон: прозрачный силуэт с очень мягким полупрозрачным классовым ореолом (золото/фиолетовый/красный), без окружения и предметной сцены.

## Production-промпт диалогового спрайта

> Create a full-body 1024×1536 transparent dialogue sprite for Dungeon Girls. Use the supplied original dialogue sprite only as an exact style, rendering, anatomy, camera, lighting and composition reference; use the supplied character concept only for the new character’s identity, hair, outfit, colors and equipment. Match a late-1980s/early-1990s Japanese PC-98 visual-novel character illustration rendered with fine pixel clusters: adult anime anatomy 6.5–7 heads tall, long legs, narrow waist, small detailed face, full figure nearly touching the top and bottom canvas edges, three-quarter view facing screen-right, neutral confident pose, weapon fully visible. Use a continuous 1–2 px dark brown/near-black outline, controlled dithering, stepped 3–5 tone shading, crisp upper-left highlights and no smooth vector or painterly surfaces. Preserve the class reference’s density of detail, eye rendering and material treatment. Transparent empty background with only a subtle soft semi-transparent class-colored aura behind the silhouette. No scenery, floor, cast shadow, frame, text, UI, cropped feet, chibi anatomy, giant low-resolution pixels, photorealism, 3D rendering or modern glossy mobile-game style.
