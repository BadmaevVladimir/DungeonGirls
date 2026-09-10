# Runs only tests that do not construct native Unity objects. This is not an EditMode run.
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$engineRoot = 'C:/Program Files/Unity/Hub/Editor/6000.5.8f1/Editor/Data/Managed/UnityEngine'
[void][Reflection.Assembly]::LoadFrom((Join-Path $engineRoot 'UnityEngine.CoreModule.dll'))
[void][Reflection.Assembly]::LoadFrom((Join-Path $engineRoot 'UnityEngine.dll'))
$nunitPath = Get-ChildItem (Join-Path $projectRoot 'Library/PackageCache') -Recurse -Filter nunit.framework.dll | Select-Object -First 1 -ExpandProperty FullName
[void][Reflection.Assembly]::LoadFrom($nunitPath)
[void][Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'Temp/bin/Debug/DungeonGirls.Runtime.dll'))
$testAssembly = [Reflection.Assembly]::LoadFrom((Join-Path $projectRoot 'Temp/bin/Debug/DungeonGirls.Tests.dll'))
$checks = @(
    @('DamageCalculatorTests', 'ApplyPhysicalDamage_DamageBelowDefense_IsFullyBlockedAndWearsArmor', @()),
    @('DamageCalculatorTests', 'ApplyPhysicalDamage_DamageAboveDefense_DealsRemainderToHP', @()),
    @('DamageCalculatorTests', 'ApplyMagicalDamage_DamageExceedsShield_DealsRemainderToHP', @()),
    @('DamageCalculatorTests', 'ApplyDamage_WithResistance_ReducesDamageBeforeDefense', @()),
    @('DamageCalculatorTests', 'ComputeDamageRange_ReturnsFloorAndCeilOfPlusMinus20Percent', @()),
    @('SeptemberBalanceInteractionTests', 'DamageRoundsToNearestWholePoint', @([single]0.49, [single]0)),
    @('SeptemberBalanceInteractionTests', 'DamageRoundsToNearestWholePoint', @([single]0.5, [single]1)),
    @('SeptemberBalanceInteractionTests', 'DamageRoundsToNearestWholePoint', @([single]1.5, [single]2)),
    @('SeptemberBalanceInteractionTests', 'FractionalLegacyHealthCannotSurviveAsDisplayedZero', @()),
    @('SeptemberBalanceInteractionTests', 'PhysicalDamageRoundsAfterFractionalArmorMitigation', @()),
    @('SeptemberBalanceInteractionTests', 'SpellEaterDoesNotSpendMainDamageOnMagicShield', @()),
    @('SeptemberBalanceInteractionTests', 'EachArmorBreakRankImprovesChance', @()),
    @('SeptemberBalanceInteractionTests', 'MonsterModifiersGetStrongerWithLevel', @()),
    @('SeptemberBalanceInteractionTests', 'InvulnerableTargetIgnoresDirectAndSpellEaterDamage', @()),
    @('SeptemberBalanceInteractionTests', 'SharedHealingRespectsPositiveAndNegativeBonuses', @([single]50, [single]65)),
    @('SeptemberBalanceInteractionTests', 'SharedHealingRespectsPositiveAndNegativeBonuses', @([single]-50, [single]55)),
    @('SeptemberBalanceInteractionTests', 'SharedHealingRespectsPositiveAndNegativeBonuses', @([single]-200, [single]50)),
    @('SeptemberBalanceInteractionTests', 'CritConversionIncludesEquipmentFoodAndRest', @()),
    @('SeptemberBalanceInteractionTests', 'ApprovedRogueSkillRanksUseDistinctGrowth', @())
)
$passed = 0
foreach ($check in $checks) {
    $type = $testAssembly.GetType($check[0], $true)
    $fixture = [Activator]::CreateInstance($type)
    try {
        $type.GetMethod($check[1]).Invoke($fixture, [object[]]$check[2]) | Out-Null
        Write-Output ('PASS ' + $check[1])
        $passed++
    } catch {
        Write-Output ('FAIL ' + $check[1] + ': ' + $_.Exception.ToString())
        exit 1
    }
}
Write-Output ("Managed checks passed: $passed/$($checks.Count). Native Unity combat tests were not executed.")
