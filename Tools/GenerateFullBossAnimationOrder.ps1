param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$OrderId = 'BA-FULL-001'
)

$ErrorActionPreference = 'Stop'

function Decode-YamlText([string]$Value) {
    $value = $Value.Trim()
    if ($value.StartsWith('"')) { $value = $value.Trim('"') }
    return [regex]::Unescape($value)
}

function Read-Guid([string]$Path) {
    $match = [regex]::Match((Get-Content -LiteralPath $Path -Raw), 'guid: ([a-f0-9]+)')
    return $match.Groups[1].Value
}

function To-ProjectRelative([string]$Path) {
    return ($Path.Substring($ProjectRoot.Length).TrimStart('\') -replace '\\', '/')
}

function Resolve-AnimationSource([string]$SourcePath) {
    Add-Type -AssemblyName System.Drawing -ErrorAction SilentlyContinue
    $image = [System.Drawing.Image]::FromFile($SourcePath)
    $tooLarge = $image.Width -gt 256 -or $image.Height -gt 256
    $image.Dispose()
    if (-not $tooLarge) {
        return To-ProjectRelative $SourcePath
    }

    $stem = [System.IO.Path]::GetFileNameWithoutExtension($SourcePath)
    $prepared = Join-Path $ProjectRoot "Assets/ArtSource/PixelLab/Bosses/$stem/$stem`_128.png"
    if (-not (Test-Path -LiteralPath $prepared)) {
        throw "PixelLab source exceeds 256px and lacks prepared 128px reference: $prepared"
    }
    return To-ProjectRelative $prepared
}

$assetByGuid = @{}
Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets') -Recurse -File -Filter '*.meta' | ForEach-Object {
    $guid = Read-Guid $_.FullName
    if ($guid) { $assetByGuid[$guid] = $_.FullName.Substring(0, $_.FullName.Length - 5) }
}

$monsterByGuid = @{}
Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets/ScriptableObjects/Monsters') -File -Filter 'Monster_*.asset' | ForEach-Object {
    $yaml = Get-Content -LiteralPath $_.FullName -Raw
    if ($yaml -notmatch 'isBoss: 1') { return }
    $kitGuid = [regex]::Match($yaml, 'bossKit: \{fileID: 11400000, guid: ([a-f0-9]+)').Groups[1].Value
    $name = Decode-YamlText ([regex]::Match($yaml, 'monsterName: (.+)').Groups[1].Value)
    $attackSpeed = [float]([regex]::Match($yaml, 'attackSpeed: ([0-9.]+)').Groups[1].Value)
    $doesNotAttack = $yaml -match 'doesNotAttack: 1'
    $guid = Read-Guid ($_.FullName + '.meta')
    $monsterByGuid[$guid] = [pscustomobject]@{
        AssetPath = $_.FullName
        Key = $_.BaseName.Substring('Monster_'.Length)
        Name = $name
        KitGuid = $kitGuid
        AttackSpeed = $attackSpeed
        DoesNotAttack = $doesNotAttack
    }
}

$poolPath = Join-Path $ProjectRoot 'Assets/ScriptableObjects/Bosses/BossPool_Main.asset'
$poolGuids = [regex]::Matches((Get-Content -LiteralPath $poolPath -Raw), 'guid: ([a-f0-9]+)') | ForEach-Object { $_.Groups[1].Value }
$poolBosses = $poolGuids | ForEach-Object { $monsterByGuid[$_] } | Where-Object { $_ -and $_.KitGuid -and $_.Key -ne 'Boss' }

$effectMotion = @{
    0  = 'Perform a clearly readable heavy strike toward the player on the left: decisive windup, one strong impact pose, then recover.'
    1  = 'Brace and raise a defensive guard, holding a clear protection pose before settling back.'
    2  = 'Make a forceful binding or command gesture toward the player, then recover.'
    3  = 'Deliberately open the guard and expose the body in a readable vulnerable pose, then recover.'
    4  = 'Lift the held focus or hand in a deliberate rekindling gesture, then return to idle.'
    5  = 'Draw the hands or body inward and release a clear freezing-cast gesture, then recover.'
    6  = 'Make a short hostile curse or chilling gesture toward the player, then recover.'
    7  = 'Stagger visibly as if losing balance, then regain the original stance.'
    8  = 'Release a clear poisonous breath, spit, or curse gesture toward the player, then recover.'
    9  = 'Make a deliberate malignant curse gesture, then return to the source pose.'
    10 = 'Deliver a visible armour-breaking strike or corrosive curse gesture, then recover.'
    11 = 'Shake, stomp, or convulse in a readable ground-shaking pulse, then settle back.'
    12 = 'Roar or tense into an unmistakable enraged posture, then return to ready stance.'
    13 = 'Sink or fold downward into a clear temporary withdrawal pose without moving sideways.'
    14 = 'Contract behind a protective shell or guard and visibly restore composure.'
    15 = 'Perform a distinct summoning, splitting, or spawning gesture, then recover.'
    16 = 'Open the mouth or body and perform a clear swallowing motion, then recover.'
    17 = 'Reach out with a decisive disabling or stealing gesture, then recover.'
    18 = 'Perform a dramatic summoning gesture that calls an allied defeated foe, then recover.'
}

$idleMotion = @{
    Boss = 'Armoured sentinel idle: slow weight shift, cape edge barely moves, weapon remains grounded.'
    PitHound = 'Low restrained canine breathing with a small ear twitch; all paws remain planted.'
    StoneIdol = 'Almost motionless carved stone; one faint settling shift and a tiny dustless torso sway.'
    SporeMother = 'Organic body slowly expands and contracts as if breathing; no attack preparation.'
    Jailer = 'Heavy jailer weight shift with a short chain sway; weapon stays down.'
    DrunkGiant = 'Unsteady but non-attacking balance correction, shoulders slowly rise and fall.'
    TwinShade = 'Weightless spectral hover with cloak hem drifting gently.'
    RustSmith = 'Heavy forge-worker breathing, shoulders rise and fall while hammer remains at rest.'
    ClockworkTitan = 'Subtle mechanical hum: shoulder pipes pulse and the chassis settles without attacking.'
    FrostWraith = 'Gentle hover with veil and sleeves drifting in still cold air.'
    GildedUsurer = 'Measured merchant-like sway; coin-covered shell gives a tiny controlled rattle.'
    Amalgam = 'Slow unstable organic undulation, parts settling without splitting or attacking.'
    Broodmother = 'Large spider abdomen slowly pulses; legs remain rooted and quiet.'
    Butcher = 'Heavy breathing and a small shoulder roll; hook and cleaver remain lowered.'
    CandleKeeper = 'Wax and hood barely sway while the held candle flame flickers softly.'
    MirrorTwin = 'Controlled mirrored stance with a minimal cloak movement; no attack readiness.'
    RelicEater = 'Hungry creature breathes slowly and shifts its weight without opening into a bite.'
    AshInquisitor = 'Robe hem and censer sway gently; posture remains solemn and still.'
    BoneLeviathan = 'Skeletal segments slowly ripple and settle; jaw remains non-threatening.'
    PitLord = 'Commanding armoured breathing; cape and sword move only slightly in a calm guard.'
    DungeonHeart = 'Slow rhythmic organic pulse from the core; no ordinary attack motion.'
}

$attackMotion = @{
    Boss = 'A compact sentinel weapon strike toward the player on the left.'
    PitHound = 'A fast compact claw-and-bite snap toward the player on the left.'
    StoneIdol = 'A slow heavy short-arm stone swipe toward the player on the left.'
    SporeMother = 'A compact organic lash or spore-vent jab toward the player on the left.'
    Jailer = 'A short downward club strike toward the player on the left.'
    DrunkGiant = 'An awkward side swing with the barrel toward the player on the left.'
    TwinShade = 'A quick upward dagger thrust toward the player on the left.'
    RustSmith = 'A compact overhead hammer strike toward the player on the left.'
    ClockworkTitan = 'A piston-driven punch toward the player on the left.'
    FrostWraith = 'A quick icy claw sweep toward the player on the left.'
    GildedUsurer = 'A short swing of chained scales toward the player on the left.'
    Amalgam = 'A compact acidic body lash toward the player on the left.'
    Broodmother = 'A short venomous bite toward the player on the left.'
    Butcher = 'A diagonal cleaver cut toward the player on the left.'
    CandleKeeper = 'A quick candle-hand jab toward the player on the left.'
    MirrorTwin = 'A sharp mirrored weapon strike toward the player on the left.'
    RelicEater = 'A compact heavy claw or maw strike toward the player on the left.'
    AshInquisitor = 'A short brand or censer swing toward the player on the left.'
    BoneLeviathan = 'A fast compact jaw snap toward the player on the left.'
    PitLord = 'A disciplined compact sword cut toward the player on the left.'
}

function Parse-Kit([string]$KitPath) {
    $phases = @()
    $phase = $null
    $ability = $null
    foreach ($line in Get-Content -LiteralPath $KitPath) {
        if ($line -match '^  - phaseName: (.*)$') {
            if ($ability) { $phase.Abilities += [pscustomobject]$ability; $ability = $null }
            if ($phase) { $phases += [pscustomobject]$phase }
            $phase = [ordered]@{ Name = Decode-YamlText $Matches[1]; Threshold = 100; SpriteGuid = ''; SpeedMultiplier = 1.0; Abilities = @() }
        } elseif ($phase -and $line -match '^    hpThresholdPercent: (.*)$') {
            $phase.Threshold = [float]$Matches[1]
        } elseif ($phase -and $line -match '^    phaseSprite: \{fileID: [^,]+, guid: ([a-f0-9]+)') {
            $phase.SpriteGuid = $Matches[1]
        } elseif ($phase -and $line -match '^    enterAttackSpeedMultiplier: (.*)$') {
            $phase.SpeedMultiplier = [float]$Matches[1]
        } elseif ($phase -and $line -match '^    - displayName: (.*)$') {
            if ($ability) { $phase.Abilities += [pscustomobject]$ability }
            $ability = [ordered]@{ Name = Decode-YamlText $Matches[1]; Effect = 0; TelegraphSeconds = 0.0 }
        } elseif ($ability -and $line -match '^      effectKind: (.*)$') {
            $ability.Effect = [int]$Matches[1]
        } elseif ($ability -and $line -match '^      telegraphSeconds: (.*)$') {
            $ability.TelegraphSeconds = [float]$Matches[1]
        }
    }
    if ($ability) { $phase.Abilities += [pscustomobject]$ability }
    if ($phase) { $phases += [pscustomobject]$phase }
    return $phases
}

$jobs = [System.Collections.Generic.List[object]]::new()
function Add-Job([hashtable]$Job) { $jobs.Add([pscustomobject]$Job) }

foreach ($boss in $poolBosses) {
    $kitPath = $assetByGuid[$boss.KitGuid]
    $phases = Parse-Kit $kitPath
    $effectiveSpeed = [double]$boss.AttackSpeed
    for ($index = 0; $index -lt $phases.Count; $index++) {
        $phase = $phases[$index]
        $effectiveSpeed *= [double]$phase.SpeedMultiplier
        $sourcePath = $assetByGuid[$phase.SpriteGuid]
        if (-not $sourcePath) { throw "No phase sprite for $($boss.Key) phase $($index + 1)" }
        $relativeSource = Resolve-AnimationSource $sourcePath
        $phaseKey = "$($boss.Key)_P$($index + 1)"
        $common = 'Preserve exact character design, anatomy, palette, pixel density, left-facing direction, transparent background, canvas, scale, and planted position. Animate only the existing character. No camera movement, background, new props, glow, particles, cropping, translation, or rotation of the whole sprite.'
        $idle = $idleMotion[$boss.Key]
        if (-not $idle) { $idle = 'Calm phase-specific idle loop: subtle breathing and one small character-defining motion.' }
        $idleSource = $relativeSource
        if ($boss.Key -eq 'PitHound' -and $index -eq 0) {
            $idleSource = 'Assets/ArtSource/PixelLab/Reviews/BA-PILOT-001/PH-P1-IDLE-BASE-RELAXED.png'
        }
        Add-Job @{ clipId = "$phaseKey`_Idle"; boss = $boss.Name; bossKey = $boss.Key; phase = $index + 1; phaseName = $phase.Name; sourcePath = $idleSource; kind = 'Idle'; effectKind = $null; abilityName = $null; frameCount = 4; firstFrame = 'source'; lastFrame = 'source'; telegraphSeconds = 0; prompt = "$idle No attack anticipation. Return exactly to the supplied final pose. $common" }
        if (-not $boss.DoesNotAttack -and $effectiveSpeed -gt 0) {
            $interval = 1.0 / $effectiveSpeed
            $frames = if ($interval -lt 0.75) { 4 } else { 8 }
            $attack = $attackMotion[$boss.Key]
            if (-not $attack) { $attack = 'A compact regular attack toward the player on the left.' }
            Add-Job @{ clipId = "$phaseKey`_Attack"; boss = $boss.Name; bossKey = $boss.Key; phase = $index + 1; phaseName = $phase.Name; sourcePath = $relativeSource; kind = 'Attack'; effectKind = $null; abilityName = $null; frameCount = $frames; firstFrame = 'source'; lastFrame = 'source'; telegraphSeconds = 0; gameplayIntervalSeconds = [math]::Round($interval, 3); prompt = "$attack Use a clear contact pose and immediate recovery. The complete movement must fit a $([math]::Round($interval, 3))-second attack interval. Return exactly to the supplied final pose. $common" }
        }
        foreach ($ability in $phase.Abilities) {
            $motion = $effectMotion[$ability.Effect]
            if (-not $motion) { $motion = 'Perform a clear distinct active ability gesture, then recover.' }
            $frames = if ($ability.TelegraphSeconds -gt 0) { 8 } else { 4 }
            $nameSuffix = ([regex]::Replace($ability.Name, '[^\p{L}\p{Nd}]+', '_').Trim('_'))
            $effectSuffix = ([regex]::Replace($ability.Effect, '[^\p{L}\p{Nd}]+', '_').Trim('_'))
            $suffix = "$nameSuffix`_$effectSuffix"
            Add-Job @{ clipId = "$phaseKey`_Action_$suffix"; boss = $boss.Name; bossKey = $boss.Key; phase = $index + 1; phaseName = $phase.Name; sourcePath = $relativeSource; kind = 'Action'; effectKind = $ability.Effect; abilityName = $ability.Name; frameCount = $frames; firstFrame = 'source'; lastFrame = 'source'; telegraphSeconds = $ability.TelegraphSeconds; prompt = "$motion This is the active ability '$($ability.Name)' for phase '$($phase.Name)'. $(if ($ability.TelegraphSeconds -gt 0) { "Create an obvious held anticipation pose for a $($ability.TelegraphSeconds)-second telegraph before impact." }) Return exactly to the supplied final pose. $common" }
        }
        if ($index -gt 0) {
            $previousSource = Resolve-AnimationSource $assetByGuid[$phases[$index - 1].SpriteGuid]
            Add-Job @{ clipId = "$phaseKey`_PhaseEnter"; boss = $boss.Name; bossKey = $boss.Key; phase = $index + 1; phaseName = $phase.Name; sourcePath = $previousSource; endSourcePath = $relativeSource; kind = 'PhaseEnter'; effectKind = $null; abilityName = $null; frameCount = 4; firstFrame = 'previousPhase'; lastFrame = 'currentPhase'; telegraphSeconds = 0; prompt = "Transition from the supplied previous phase into the supplied current phase with a dramatic but contained transformation or escalation. Keep the feet or base planted and end exactly in the new phase pose. $common" }
        }
    }
}

# Mirror Twin has three class-specific kits. Its visual phases are shared, so only action jobs are
# added here; idle, attack and phase transitions above remain unique per visual phase.
$mirror = $poolBosses | Where-Object { $_.Key -eq 'MirrorTwin' }
if ($mirror) {
    $seen = @{}
    foreach ($variant in 'BossKit_MirrorTwin_Barbarian.asset', 'BossKit_MirrorTwin_Rogue.asset', 'BossKit_MirrorTwin_Warrior.asset') {
        $variantPath = Join-Path $ProjectRoot "Assets/ScriptableObjects/Bosses/$variant"
        $variantPhases = Parse-Kit $variantPath
        for ($index = 0; $index -lt $variantPhases.Count; $index++) {
            foreach ($ability in $variantPhases[$index].Abilities) {
                $key = "$($index + 1)|$($ability.Name)|$($ability.Effect)"
                if ($seen[$key]) { continue }
                $seen[$key] = $true
                $mainPhases = Parse-Kit $assetByGuid[$mirror.KitGuid]
                $sourcePath = Resolve-AnimationSource $assetByGuid[$mainPhases[$index].SpriteGuid]
                $motion = $effectMotion[$ability.Effect]
                $frames = if ($ability.TelegraphSeconds -gt 0) { 8 } else { 4 }
                $nameSuffix = ([regex]::Replace($ability.Name, '[^\p{L}\p{Nd}]+', '_').Trim('_'))
                $effectSuffix = ([regex]::Replace($ability.Effect, '[^\p{L}\p{Nd}]+', '_').Trim('_'))
                $suffix = "$nameSuffix`_$effectSuffix"
                Add-Job @{ clipId = "MirrorTwin_P$($index + 1)_VariantAction_$suffix"; boss = $mirror.Name; bossKey = 'MirrorTwin'; phase = $index + 1; phaseName = $variantPhases[$index].Name; sourcePath = $sourcePath; kind = 'VariantAction'; effectKind = $ability.Effect; abilityName = $ability.Name; frameCount = $frames; firstFrame = 'source'; lastFrame = 'source'; telegraphSeconds = $ability.TelegraphSeconds; prompt = "$motion This is a class-specific Mirror Twin ability '$($ability.Name)'. $(if ($ability.TelegraphSeconds -gt 0) { "Create an obvious held anticipation pose for a $($ability.TelegraphSeconds)-second telegraph before impact." }) Return exactly to the supplied final pose. Preserve exact character design, anatomy, palette, pixel density, left-facing direction, transparent background, canvas, scale, and planted position. Animate only the existing character. No camera movement, background, new props, glow, particles, cropping, translation, or rotation of the whole sprite." }
            }
        }
    }
}

$outputDir = Join-Path $ProjectRoot 'Docs/Art/BossAnimations'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$outputPath = Join-Path $outputDir "$OrderId.json"
[pscustomobject]@{
    orderId = $OrderId
    createdAt = (Get-Date).ToString('s')
    source = 'Current BossPool_Main + BossKitData assets'
    notes = 'Each row is an independent PixelLab animate_image job. The queue dispatcher must honour the Tier 2 cap of 10 queued/running jobs.'
    jobs = $jobs
} | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding utf8

$summary = $jobs | Group-Object bossKey | ForEach-Object { [pscustomobject]@{ Boss = $_.Name; Jobs = $_.Count } }
[pscustomobject]@{ OutputPath = $outputPath; JobCount = $jobs.Count; BossCount = $summary.Count; PerBoss = $summary } | ConvertTo-Json -Depth 4
