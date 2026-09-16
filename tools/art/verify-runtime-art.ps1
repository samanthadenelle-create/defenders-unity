<#
.SYNOPSIS
    Verify tracked runtime art inputs and exact Addressables bindings; warn on absent source packs.
.DESCRIPTION
    Authority: tools/art/REQUIRED_PACKS.md; WO-1338 remote migration supersedes the
    July Resources-only contract. This read-only static check does NOT prove bundles
    were built, published, downloaded, or rendered. Remote hero/enemy bodies require
    the matching catalog and bundles. No bare-clone offline-playability guarantee.
    Missing CRITICAL/COMMITTED inputs fail. PACK warnings fail only with -Strict.
#>

[CmdletBinding()]
param(
    # Treat gitignored-pack warnings as failures too (strict CI mode).
    [switch]$Strict
)

$ErrorActionPreference = 'Stop'

# Resolve repo root = two levels up from tools/art/ (this script's folder).
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot  = Split-Path -Parent (Split-Path -Parent $scriptDir)

Write-Host ""
Write-Host "=== verify-runtime-art :: Defenders of the Realm ==="
Write-Host "Repo root: $repoRoot"
Write-Host "Ruling   : WO-1338 remote migration  |  Manifest: tools/art/REQUIRED_PACKS.md"
Write-Host ""

# ---------------------------------------------------------------------------
# The checklist. Each entry: Tier, RelPath, Needed-by note, and MatchKind:
#   'Dir'     - directory must exist
#   'File'    - exact file must exist
#   'AnyPng'  - directory must exist AND contain at least one *.png (textures)
#   'AnyFbx'  - directory must exist AND contain at least one *.fbx/*.gltf (bodies)
# ---------------------------------------------------------------------------
$checks = @(
    # ---- CRITICAL: tracked remote runtime inputs (exact GUID/address/group contract) ----
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Skeleton_Warrior.fbx'; Guid='8a53b52f760a558418a313e7c01609f9'; Address='Enemies/Skeleton_Warrior'; Group='Enemy_Models'; Note='Hollow Warrior body (AccuRig)' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Skeleton_Rogue.fbx'; Guid='86367789586a0f043a38ffefad914b87'; Address='Enemies/Skeleton_Rogue'; Group='Enemy_Models'; Note='Hollow Skirmisher body' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Skeleton_Mage.fbx'; Guid='d6bad9e773bd99047b3fe07c4a18f64b'; Address='Enemies/Skeleton_Mage'; Group='Enemy_Models'; Note='Hollow Caster body' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Skeleton_Healer.fbx'; Guid='70784628d067e0b48a44c665010def8d'; Address='Enemies/Skeleton_Healer'; Group='Enemy_Models'; Note='Hollow Acolyte body' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Skeleton_Golem.fbx'; Guid='3bddc9f232c05a442806dd4f6dbf412a'; Address='Enemies/Skeleton_Golem'; Group='Enemy_Models'; Note='Hollow Brute/Golem body' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Skeleton_Minion.fbx'; Guid='6066d47ca793874439884eacffdf1165'; Address='Enemies/Skeleton_Minion'; Group='Enemy_Models'; Note='Hollow Walker body' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Necromancer.fbx'; Guid='63f0d2098c640f24b89f4eb3812374f8'; Address='Enemies/Necromancer'; Group='Enemy_Models'; Note='Necromancer of the Wound body' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Boss_Dragon.prefab'; Guid='b17eda4b37a8fe1459cab240101ae1fa'; Address='Enemies/Boss_Dragon'; Group='Enemy_Models'; Note='Alduin/dragon boss prefab' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/Orc_Warrior.fbx'; Guid='ea8b49aa47e59be47b3b28812a80bc20'; Address='Enemies/Orc_Warrior'; Group='Enemy_Models'; Note='Orc family body' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/EnemyContent/SkeletonHumanoid.controller'; Guid='60e38b0cb9939d045a8c68fab937a640'; Address='Enemies/SkeletonHumanoid'; Group='Enemy_Controllers'; Note='shared humanoid animator path' }
    @{ Tier='CRITICAL';  Kind='File'; Path='Assets/Resources/NPCs/NPC_Blacksmith.prefab';    Note='Blacksmith NPC' }
    @{ Tier='CRITICAL';  Kind='File'; Path='Assets/Resources/NPCs/NPC_Merchant.prefab';      Note='Merchant NPC' }
    @{ Tier='CRITICAL';  Kind='File'; Path='Assets/Resources/NPCs/NPC_Peasant_Mevina.prefab';Note='Peasant NPC (Mevina)' }
    @{ Tier='CRITICAL';  Kind='Addressable'; Path='Assets/HeroContent/KnightV3.fbx'; Guid='12bb05bc97c0490ab4987a5a6c62fdac'; Address='Heroes/KnightV3'; Group='Hero_KnightV3'; Note='hero body (CC/AccuRig)' }

    # ---- COMMITTED: the tracked People pack (LFS) - Bryn-class NPC bodies + their textures ----
    @{ Tier='COMMITTED'; Kind='File';   Path='Assets/Models/People/0_FighterClass_High_High_1024_LOD0.Fbx'; Note='FighterClass body (People pack)' }
    @{ Tier='COMMITTED'; Kind='AnyPng'; Path='Assets/Models/People/Blacksmith/Textures';     Note='Blacksmith textures' }
    @{ Tier='COMMITTED'; Kind='AnyPng'; Path='Assets/Models/People/Peasant/Textures';        Note='Peasant textures' }

    # ---- PACK: gitignored source packs (travel by zip) - WARN only; not a runtime delivery proof ----
    @{ Tier='PACK'; Kind='AnyFbx'; Path='Assets/Models/KayKit/KayKit Skeletons 1.1/characters'; Note='KayKit Skeletons source bodies' }
    @{ Tier='PACK'; Kind='AnyFbx'; Path='Assets/Models/KayKit Adventurers 2.0/Characters';      Note='KayKit Adventurers troop/hero bodies' }
    @{ Tier='PACK'; Kind='AnyFbx'; Path='Assets/Models/KayKit/dungeon';                         Note='KayKit Dungeon Remastered geometry' }
    @{ Tier='PACK'; Kind='Dir';    Path='Assets/Models/People/textures';                        Note='People shared skin textures (the untextured-Bryn gap)' }
)

function Test-HydratedFile {
    param([string]$path)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    $stream = [IO.File]::OpenRead($path)
    try {
        if ($stream.Length -eq 0) { return $false }
        $bytes = New-Object byte[] 128
        $read = $stream.Read($bytes, 0, $bytes.Length)
        return -not ([Text.Encoding]::ASCII.GetString($bytes, 0, $read).StartsWith('version https://git-lfs.github.com/spec/v1'))
    } finally { $stream.Dispose() }
}

function Test-Addressable {
    param($entry)
    $full = Join-Path $repoRoot $entry.Path
    if (-not (Test-HydratedFile $full) -or -not (Test-Path -LiteralPath ($full + '.meta'))) { return $false }
    if ((Get-Content -LiteralPath ($full + '.meta') -Raw) -notmatch ('(?m)^guid: ' + $entry.Guid + '\r?$')) { return $false }
    $base = Join-Path $repoRoot 'Assets/AddressableAssetsData'
    $group = Join-Path $base ('AssetGroups/' + $entry.Group + '.asset')
    $schema = Join-Path $base ('AssetGroups/Schemas/' + $entry.Group + '_BundledAssetGroupSchema.asset')
    foreach ($required in @($group, ($group + '.meta'), $schema, ($schema + '.meta'))) {
        if (-not (Test-Path -LiteralPath $required)) { return $false }
    }
    $groupText = Get-Content -LiteralPath $group -Raw
    $binding = '(?m)^  - m_GUID: ' + $entry.Guid + '\r?\n    m_Address: ' + [regex]::Escape($entry.Address) + '\r?$'
    if ([regex]::Matches($groupText, $binding).Count -ne 1) { return $false }
    # Same address may intentionally serve model and controller, but this GUID must have one entry.
    $allGroups = Get-ChildItem -LiteralPath (Join-Path $base 'AssetGroups') -Filter '*.asset' -File
    $count = 0
    foreach ($g in $allGroups) { $count += [regex]::Matches((Get-Content -LiteralPath $g.FullName -Raw), ('(?m)^  - m_GUID: ' + $entry.Guid + '\r?$')).Count }
    if ($count -ne 1) { return $false }
    $groupGuid = [regex]::Match((Get-Content -LiteralPath ($group + '.meta') -Raw), '(?m)^guid: (\w+)').Groups[1].Value
    $schemaGuid = [regex]::Match((Get-Content -LiteralPath ($schema + '.meta') -Raw), '(?m)^guid: (\w+)').Groups[1].Value
    $settings = Get-Content -LiteralPath (Join-Path $base 'AddressableAssetSettings.asset') -Raw
    if (-not $groupGuid -or -not $schemaGuid -or $settings -notmatch ('guid: ' + $groupGuid + ',') -or $groupText -notmatch ('guid: ' + $schemaGuid + ',')) { return $false }
    foreach ($profilePattern in @('m_Id: ad0e68328bd7fd54ea79f0a9ab1dd9b1\s+m_Name: Remote\.BuildPath', 'm_Id: cf151d4962873af43b9302d323a9d707\s+m_Name: Remote\.LoadPath')) {
        if ($settings -notmatch $profilePattern) { return $false }
    }
    $schemaText = Get-Content -LiteralPath $schema -Raw
    foreach ($pattern in @('(?m)^  m_IncludeInBuild: 1\r?$', '(?m)^  m_IncludeAddressInCatalog: 1\r?$', 'm_BuildPath:\s+m_Id: ad0e68328bd7fd54ea79f0a9ab1dd9b1', 'm_LoadPath:\s+m_Id: cf151d4962873af43b9302d323a9d707')) {
        if ($schemaText -notmatch $pattern) { return $false }
    }
    return $true
}

function Test-Entry {
    param($entry)
    $full = Join-Path $repoRoot $entry.Path
    switch ($entry.Kind) {
        'File'   { return (Test-HydratedFile $full) }
        'Addressable' { return (Test-Addressable $entry) }
        'Dir'    { return (Test-Path -LiteralPath $full -PathType Container) }
        'AnyPng' {
            if (-not (Test-Path -LiteralPath $full -PathType Container)) { return $false }
            return @(Get-ChildItem -LiteralPath $full -Filter *.png -File -ErrorAction SilentlyContinue).Count -gt 0
        }
        'AnyFbx' {
            if (-not (Test-Path -LiteralPath $full -PathType Container)) { return $false }
            $n = @(Get-ChildItem -LiteralPath $full -Recurse -Include *.fbx,*.gltf -File -ErrorAction SilentlyContinue).Count
            return $n -gt 0
        }
        default  { return $false }
    }
}

$missingCritical  = @()
$missingCommitted = @()
$missingPack      = @()

foreach ($c in $checks) {
    $ok = Test-Entry $c
    if ($ok) {
        Write-Host ("  [ OK ]   {0,-9} {1}" -f $c.Tier, $c.Path)
    }
    else {
        Write-Host ("  [MISS]   {0,-9} {1}   <- {2}" -f $c.Tier, $c.Path, $c.Note)
        switch ($c.Tier) {
            'CRITICAL'  { $missingCritical  += $c }
            'COMMITTED' { $missingCommitted += $c }
            'PACK'      { $missingPack       += $c }
        }
    }
}

Write-Host ""
Write-Host "--- summary ---"

if ($missingPack.Count -gt 0) {
    Write-Host ""
    Write-Host ("WARN: {0} gitignored source pack(s) not copied in (review required for content used by this build):" -f $missingPack.Count)
    foreach ($m in $missingPack) {
        Write-Host ("   - {0}  ({1})" -f $m.Path, $m.Note)
    }
    Write-Host "   Fix: copy the pack in from the owner's zip / source folder (NOT git). See tools/art/REQUIRED_PACKS.md."
}

$hardMissing = $missingCritical.Count + $missingCommitted.Count

if ($hardMissing -gt 0) {
    Write-Host ""
    Write-Host ("FAIL: {0} tracked runtime input/binding check(s) failed:" -f $hardMissing)
    foreach ($m in ($missingCritical + $missingCommitted)) {
        Write-Host ("   - {0,-9} {1}  ({2})" -f $m.Tier, $m.Path, $m.Note)
    }
    if ($missingCommitted.Count -gt 0) {
        Write-Host "   Hint: COMMITTED misses often mean LFS did not hydrate -> run 'git lfs pull'."
    }
    Write-Host ""
    Write-Host "RESULT: RUNTIME-ART FAIL"
    exit 1
}

if ($Strict -and $missingPack.Count -gt 0) {
    Write-Host ""
    Write-Host "RESULT: RUNTIME-ART FAIL (strict mode: gitignored packs required)"
    exit 1
}

Write-Host ""
if ($missingPack.Count -gt 0) {
    Write-Host "RESULT: RUNTIME-ART OK (static tracked inputs/bindings verified; source pack warnings)"
} else {
    Write-Host "RESULT: RUNTIME-ART OK (static tracked inputs/bindings and listed packs present)"
}
exit 0
