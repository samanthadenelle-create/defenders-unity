[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$OutputPath = "docs/localization/manifest.json",
    [switch]$Check
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "../.."))
} else {
    $RepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
}

function Get-RepoRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $prefix = $RepoRoot.TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the repository: $full"
    }
    return $full.Substring($prefix.Length).Replace('\', '/')
}

function ConvertFrom-CSharpLiteral {
    param([string]$Value)

    if ($null -eq $Value) { return $null }
    return $Value.Replace('\"', '"').Replace('\n', "`n").Replace('\r', "`r").Replace('\t', "`t").Replace('\\', '\')
}

function New-ManifestRow {
    param(
        [string]$Kind,
        [AllowNull()][object]$Key,
        [string]$Collection,
        [AllowNull()][object]$English,
        [string]$Owner,
        [string]$Source,
        [int]$Line,
        [string]$Surface,
        [string]$Context,
        [string[]]$Arguments,
        [AllowNull()][Nullable[bool]]$Translatable,
        [AllowNull()][Nullable[double]]$MaxExpansion,
        [AllowNull()][object]$Screenshot,
        [string]$State
    )

    [pscustomobject][ordered]@{
        kind          = $Kind
        key           = $Key
        collection    = $Collection
        english       = $English
        owner         = $Owner
        source        = $Source
        line          = $Line
        surface       = $Surface
        context       = $Context
        arguments     = @($Arguments)
        translatable  = $Translatable
        maxExpansion  = $MaxExpansion
        screenshot    = $Screenshot
        state         = $State
    }
}

function Get-OwnerFromSource {
    param([string]$Source)

    if ($Source -match '^Assets/_Modules/([^/]+)/') { return $Matches[1] }
    if ($Source -match '^Assets/Resources/Data/Canonical/') { return 'Content' }
    if ($Source -match '^Assets/StreamingAssets/Data/Canonical/') { return 'Content' }
    if ($Source -match '^Assets/Art/') { return 'Art' }
    return 'Unassigned'
}

function Get-Arguments {
    param([AllowNull()][string]$Value)

    if ([string]::IsNullOrEmpty($Value)) { return @() }
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($match in [regex]::Matches($Value, '\{(?<name>[A-Za-z_][A-Za-z0-9_]*|[0-9]+)(?:[^}]*)\}')) {
        $name = $match.Groups['name'].Value
        if (-not $names.Contains($name)) { $names.Add($name) }
    }
    return @($names | Sort-Object)
}

function Test-LikelyHumanText {
    param(
        [AllowNull()][string]$Value,
        [bool]$UiHint = $false
    )

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    $text = $Value.Trim()
    if ($text.Length -gt 8000) { return $false }
    if ($text -notmatch '[A-Za-z]') { return $false }
    if ($text -match '^(?:https?|mailto|file)://') { return $false }
    if ($text -match '^[A-Za-z0-9_.-]+/[A-Za-z0-9_./@{}-]+$') { return $false }
    if ($text -match '^#[0-9A-Fa-f]{6,8}$') { return $false }
    if ($text -match '^[a-z][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_-]+)+$') { return $false }
    if ($text -match '^[A-Za-z_][A-Za-z0-9_]*(?:/[A-Za-z0-9_.-]+)+$') { return $false }
    if ($UiHint) { return $true }
    return $text -match '\s|[.!?,:;'']'
}

function Test-UiConstructionCallLine {
    param([string]$Line)

    if ($Line -match 'ShowToast\s*\(') { return $true }
    if ($Line -match 'BuildObsidianModal\s*\(') { return $true }
    if ($Line -match '(?:BuildObsidianButton|\.Button|\bButton)\s*\(') { return $true }
    if ($Line -match '(?:\.Label|\bLabel)\s*\(') { return $true }
    if ($Line -match '(?:SetStatus|ShowStatus|SetMessage)\s*\(') { return $true }
    # Project-local UI-construction kit: every one of ElarionUiKit / ElarionUiKitObsidian /
    # ElarionUiKitDetailCard is the SAME `public static partial class ElarionUiKit` (confirmed
    # 2026-09-17: `class ElarionUiKit\b` in Assets/_Modules/Core/UI/*.cs), so any static call or
    # `new ElarionUiKit.<Type>(` constructor on it is a UI-construction call, whatever the method
    # name (ButtonPack, Label, Header, Card, BuildObsidianPanel, BuildConfirmModal, BuildTab,
    # BuildToggle, BuildNameplate, BuildChatDock, CurrencyChip, DetailCardRow, AddImage, ...).
    if ($Line -match '\bElarionUiKit(?:Obsidian|DetailCard)?\.\w+\s*\(') { return $true }
    # Confirmed project-local UI-construction helpers that are called bare (no class prefix) -
    # each grepped to its declaration this session:
    #   HudKitController.cs:5750       private void AddDockTab(RectTransform panel, int i, string label, Action onTap)
    #   BuildHudController.cs:861      private static Button MakeWordVerb(RectTransform parent, string name, string label, ...)
    #   ArenaAttackPaletteUI.cs:275 (+3 more) private Button AddButton(Transform parent, string label, ...)
    # WARNING: Bare `AddImage` is deliberately NOT in this list. All four declarations found this session
    # (ElarionUiKit.cs:2550, QueueRailView.cs:780, ArenaAttackPaletteUI.cs:233, ArenaPanel.cs:428,
    # HeroInventoryController.cs:636) are `AddImage(Transform parent, string name, ...)` - the
    # literal is a GameObject/sprite name, never player copy, so matching it would be a guaranteed
    # false positive with zero true positives. `ElarionUiKit.AddImage(` is still caught by the
    # kit-wide rule above for consistency with how the tool already treats every other kit method
    # (it does not special-case no-copy kit methods either); that tradeoff is pre-existing, not
    # introduced here - see the report appended to SWEEP_CLASSIFICATION_2026-09-17.md.
    if ($Line -match '\b(?:AddDockTab|MakeWordVerb|AddButton)\s*\(') { return $true }
    return $false
}

function Get-CSharpContext {
    param([string[]]$Lines, [int]$Index)

    $line = $Lines[$Index]
    if (Test-UiConstructionCallLine $line) { return @('ui-construction-call', $true) }
    if ($line -match '\.(?:text|placeholder|caption|title)\s*=') { return @('text-assignment', $true) }
    if ($line -match '(?i)\b(?:title|body|label|caption|tooltip|message|placeholder|status|header|description)\b') {
        return @('named-player-copy', $true)
    }
    # Statement-continuation lookback: a UI-construction call opened a few lines above can carry
    # its literal argument(s) on a later line of the same statement (e.g. a ternary literal, or a
    # multi-argument call wrapped for readability) - EquipmentPanel.cs:915-916 is the proven case:
    #   var action = ElarionUiKit.ButtonPack(_approvedDetailHost,
    #       item.Equipped ? "REMOVE" : "EQUIP", ElarionUiKit.ButtonKind.Gold, ...
    # Bounded to 6 lines back. The boundary/terminator check runs BEFORE the call-match check on
    # each prior line (not after) and strips a trailing `//` comment first - this matters:
    #   - a prior line that is itself a COMPLETE, terminated statement (ends `;` once its trailing
    #     comment is stripped, e.g. EquipmentPanel.cs:1227 `...FontLabel);   // never wraps ...`)
    #     must stop the scan WITHOUT counting as a continuation, even if that terminated line also
    #     happens to contain a UI-construction call - the current line is a new, separate
    #     statement that merely follows it, not a continuation of it.
    #   - any line in the window that matches the diagnostic/metadata exclusion rules
    #     (Test-ExcludedCSharpLine) suppresses the WHOLE statement, checked as a full pass over the
    #     collected window rather than stopping at the first exclusion hit in scan order. Proven
    #     necessary, not just defensive: BuildHudController.cs:494-500 is a `FlowTrace.Step("BuildHud",`
    #     call whose OWN first line carries the exclusion marker, but the very next continuation line
    #     is diagnostic PROSE that happens to mention `ElarionUiKit.BuildObsidianButton(` by name
    #     ("... COMMON kit button ElarionUiKit.BuildObsidianButton(Style1,Yellow) seated ...") - read
    #     scanning strictly outward-to-inward, that prose line matches Test-UiConstructionCallLine
    #     BEFORE the scan ever reaches the opener line that would have excluded it, producing a false
    #     "ui-construction-call-continuation" on a pure diagnostic string. Collecting the whole
    #     window first and checking exclusion across ALL of it (order-independent) closes this: the
    #     opener's exclusion marker suppresses the statement regardless of which line the loop visits
    #     first. (That continuation-line class is exactly the artifact's measured 24.6%
    #     false-positive-flowtrace-or-debug (comment/diagnostic-continuation) bucket.)
    # A line that completes its OWN statement (ends `;` once a trailing comment is stripped) is the
    # boundary of the PREVIOUS, separate statement - it stops the scan and is NOT added to the
    # window, even though it may itself contain a UI-construction call (EquipmentPanel.cs:1227 is
    # the proven case for this half of the rule).
    # A pure comment line (`//`, `///`, `*` continuation, `/*`) never counts as evidence either way:
    # it cannot terminate the statement, but it also must not be checked for a UI-construction call
    # or an exclusion marker. Proven necessary: HeroSkillTreePanelMvvm.cs:1877-1881 is a five-line
    # comment block directly above a ternary-assigned literal ("COMING"), and one of those comment
    # lines reads "...never a label (HeroSkillTreeVM..." - plain prose that happens to contain the
    # substring "label (", which trips the pre-existing (?:\.Label|\bLabel)\s*\( rule (PowerShell
    # -match is case-insensitive by default) even though there is no `Label(` call anywhere nearby.
    # Skipping comment lines when building the window (while still walking past them for boundary
    # purposes) closes that hole without touching the pre-existing same-line comment/prose false
    # positives the artifact already measured and bucketed (its rule 1, `false-positive-flowtrace-
    # or-debug (comment text)`, 26.6% of the baseline) - this only stops a COMMENT from
    # contaminating a DIFFERENT, later, non-comment statement's literal.
    $window = New-Object 'System.Collections.Generic.List[string]'
    for ($back = $Index - 1; $back -ge 0 -and ($Index - $back) -le 6; $back--) {
        $priorLine = $Lines[$back]
        $trimmed = $priorLine.Trim()
        if ($trimmed -eq '' -or $trimmed -eq '{' -or $trimmed -eq '}') { break }
        if ($trimmed -match '^(?://|///|\*|/\*)') { continue }
        $withoutComment = [regex]::Replace($trimmed, '//.*$', '').TrimEnd()
        if ($withoutComment.EndsWith(';')) { break }
        $window.Add($priorLine)
    }
    foreach ($windowLine in $window) {
        if (Test-ExcludedCSharpLine $windowLine) { return @('runtime-code-review', $false) }
    }
    foreach ($windowLine in $window) {
        if (Test-UiConstructionCallLine $windowLine) { return @('ui-construction-call-continuation', $true) }
    }
    return @('runtime-code-review', $false)
}

function Test-ExcludedCSharpLine {
    param([string]$Line)

    if ($Line -match '\b(?:Debug\.Log|FlowTrace\.|Logger\.|Console\.)') { return $true }
    if ($Line -match '^\s*(?:using|namespace)\b') { return $true }
    if ($Line -match '\[(?:Tooltip|Header|MenuItem|RuntimeInitializeOnLoadMethod)\b') { return $true }
    if ($Line -match '\b(?:nameof|typeof)\s*\(') { return $true }
    if ($Line -match '\b(?:throw new|InvalidOperationException|ArgumentException)\b') { return $true }
    if ($Line -match '\bconst\s+string\s+\w*(?:Id|ID|Key|Path|Url|URL|Tag|Scene|Resource|Pref)\w*\s*=') { return $true }
    return $false
}

function Add-CSharpRows {
    param([System.Collections.Generic.List[object]]$Rows)

    $modules = Join-Path $RepoRoot 'Assets/_Modules'
    if (-not (Test-Path -LiteralPath $modules)) { return }

    $files = [System.IO.Directory]::EnumerateFiles($modules, '*.cs', [System.IO.SearchOption]::AllDirectories) |
        Where-Object { $_ -notmatch '[\\/](?:Editor|Tests?|Regression)[\\/]' } |
        Sort-Object |
        ForEach-Object { Get-Item -LiteralPath $_ }

    $keyedPattern = [regex]'LocalText\.Get\s*\(\s*"(?<key>(?:\\.|[^"\\])*)"\s*,\s*"(?<value>(?:\\.|[^"\\])*)"'
    $literalKeyPattern = [regex]'LocalText\.(?:Get|Format)\s*\(\s*"(?<key>(?:\\.|[^"\\])*)"'
    $symbolKeyPattern = [regex]'LocalText\.(?:Get|Format)\s*\(\s*(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\b'
    $wrapperLiteralPattern = [regex]'new\s+LocalizedText(?:\s*<[^>]+>)?\s*\(\s*"(?<key>(?:\\.|[^"\\])*)"'
    $wrapperSymbolPattern = [regex]'new\s+LocalizedText(?:\s*<[^>]+>)?\s*\(\s*(?<symbol>[A-Za-z_][A-Za-z0-9_]*)\b'
    $literalPattern = [regex]'(?<!\\)"(?<value>(?:\\.|[^"\\])*)"'

    foreach ($file in $files) {
        $source = Get-RepoRelativePath $file.FullName
        $owner = Get-OwnerFromSource $source
        $lines = Get-Content -LiteralPath $file.FullName
        $constantMap = @{}
        foreach ($sourceLine in $lines) {
            if (-not $sourceLine.Contains('const string')) { continue }
            if ($sourceLine -match '\bconst\s+string\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*"(?<value>(?:\\.|[^"\\])*)"') {
                $constantName = $Matches['name']
                $constantValue = ConvertFrom-CSharpLiteral $Matches['value']
                if ($constantValue -match '^[a-z][A-Za-z0-9_-]*(?:\.[A-Za-z0-9_-]+)+$') {
                    $constantMap[$constantName] = $constantValue
                }
            }
        }
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index]
            $keyed = $keyedPattern.Matches($line)
            if ($keyed.Count -gt 0) {
                foreach ($match in $keyed) {
                    $key = ConvertFrom-CSharpLiteral $match.Groups['key'].Value
                    $english = ConvertFrom-CSharpLiteral $match.Groups['value'].Value
                    $Rows.Add((New-ManifestRow -Kind 'keyedCall' -Key $key -Collection 'GameStrings' `
                        -English $english -Owner $owner -Source $source -Line ($index + 1) `
                        -Surface 'runtime-localized-call' -Context 'Existing LocalText call; verify against the table authority.' `
                        -Arguments (Get-Arguments $english) -Translatable $true -MaxExpansion 1.4 `
                        -Screenshot $null -State 'migrated'))
                }
                continue
            }

            $resolvedCalls = $null
            if ($line.Contains('LocalText.')) {
                $resolvedCalls = New-Object 'System.Collections.Generic.List[string]'
                foreach ($match in $literalKeyPattern.Matches($line)) {
                    $resolvedCalls.Add((ConvertFrom-CSharpLiteral $match.Groups['key'].Value))
                }
                foreach ($match in $symbolKeyPattern.Matches($line)) {
                    $symbol = $match.Groups['symbol'].Value
                    if ($constantMap.ContainsKey($symbol)) { $resolvedCalls.Add([string]$constantMap[$symbol]) }
                }
            }
            if ($null -ne $resolvedCalls -and $resolvedCalls.Count -gt 0) {
                foreach ($key in @($resolvedCalls | Sort-Object -Unique)) {
                    $Rows.Add((New-ManifestRow -Kind 'keyedCall' -Key $key -Collection 'GameStrings' `
                        -English $null -Owner $owner -Source $source -Line ($index + 1) `
                        -Surface 'runtime-localized-call' -Context 'Existing LocalText key reference; verify against the table authority.' `
                        -Arguments @() -Translatable $true -MaxExpansion 1.4 -Screenshot $null -State 'migrated'))
                }
                continue
            }

            $wrapperCalls = New-Object 'System.Collections.Generic.List[string]'
            foreach ($match in $wrapperLiteralPattern.Matches($line)) {
                $wrapperCalls.Add((ConvertFrom-CSharpLiteral $match.Groups['key'].Value))
            }
            foreach ($match in $wrapperSymbolPattern.Matches($line)) {
                $symbol = $match.Groups['symbol'].Value
                if ($constantMap.ContainsKey($symbol)) { $wrapperCalls.Add([string]$constantMap[$symbol]) }
            }
            if ($wrapperCalls.Count -gt 0) {
                foreach ($key in @($wrapperCalls | Sort-Object -Unique)) {
                    $Rows.Add((New-ManifestRow -Kind 'keyedCall' -Key $key -Collection 'GameStrings' `
                        -English $null -Owner $owner -Source $source -Line ($index + 1) `
                        -Surface 'runtime-localized-call' -Context 'LocalizedText wrapper key reference.' `
                        -Arguments @() -Translatable $true -MaxExpansion 1.4 -Screenshot $null -State 'migrated'))
                }
                continue
            }

            if (Test-ExcludedCSharpLine $line) { continue }
            $contextInfo = Get-CSharpContext -Lines $lines -Index $index
            $surface = [string]$contextInfo[0]
            $uiHint = [bool]$contextInfo[1]
            if (-not $uiHint) { continue }
            foreach ($match in $literalPattern.Matches($line)) {
                $value = ConvertFrom-CSharpLiteral $match.Groups['value'].Value
                if (-not (Test-LikelyHumanText -Value $value -UiHint $uiHint)) { continue }
                $Rows.Add((New-ManifestRow -Kind 'literalCandidate' -Key $null -Collection 'GameStrings' `
                    -English $value -Owner $owner -Source $source -Line ($index + 1) -Surface $surface `
                    -Context 'Unresolved runtime literal; review whether it is player-readable before assigning a key.' `
                    -Arguments (Get-Arguments $value) -Translatable $null -MaxExpansion 1.4 `
                    -Screenshot $null -State 'review'))
            }
        }
    }
}

function Get-JsonLineLookup {
    param([string[]]$Lines)

    $lookup = @{}
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        if ($Lines[$i] -match '^\s*"(?<name>(?:\\.|[^"\\])*)"\s*:') {
            $name = ConvertFrom-CSharpLiteral $Matches['name']
            if (-not $lookup.ContainsKey($name)) { $lookup[$name] = $i + 1 }
        }
    }
    return $lookup
}

function Add-JsonLeaves {
    param(
        [Parameter(Mandatory = $true)][AllowNull()]$Node,
        [string]$Path,
        [string]$Source,
        [hashtable]$LineLookup,
        [bool]$IsEnglishTable,
        [System.Collections.Generic.List[object]]$Rows
    )

    if ($null -eq $Node) { return }
    if ($Node -is [string]) {
        $leaf = if ($Path -match '([^\.\[\]]+)$') { $Matches[1] } else { $Path }
        $line = if ($LineLookup.ContainsKey($leaf)) { [int]$LineLookup[$leaf] } else { 0 }
        if ($IsEnglishTable) {
            $Rows.Add((New-ManifestRow -Kind 'keyedEntry' -Key $Path -Collection 'GameStrings' `
                -English $Node -Owner 'Content' -Source $Source -Line $line -Surface 'canonical-string-table' `
                -Context 'English source entry imported into the GameStrings collection.' `
                -Arguments (Get-Arguments $Node) -Translatable $true -MaxExpansion 1.4 `
                -Screenshot $null -State 'migrated'))
        } else {
            $hint = $Path -match '(?i)(?:name|title|label|description|desc|body|text|message|caption|tooltip|dialogue|summary|prompt|response|flavor|lore|hint|objective|cta|button)'
            # Canonical files also contain thousands of ids, paths, material names,
            # enum values, and implementation notes. Inventory only fields whose
            # schema name signals player copy; unknown fields are reviewed by their
            # owning domain when that domain is migrated, not mislabeled as prose.
            if ($hint -and (Test-LikelyHumanText -Value $Node -UiHint $true)) {
                $Rows.Add((New-ManifestRow -Kind 'literalCandidate' -Key $null -Collection 'Unassigned' `
                    -English $Node -Owner 'Content' -Source $Source -Line $line -Surface 'canonical-authored-content' `
                    -Context ("JSON field '" + $Path + "'; classify as player copy, protected name, or internal data.") `
                    -Arguments (Get-Arguments $Node) -Translatable $null -MaxExpansion 1.4 `
                    -Screenshot $null -State 'review'))
            }
        }
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [pscustomobject])) {
        $arrayIndex = 0
        foreach ($item in $Node) {
            Add-JsonLeaves -Node $item -Path ($Path + '[' + $arrayIndex + ']') -Source $Source `
                -LineLookup $LineLookup -IsEnglishTable $IsEnglishTable -Rows $Rows
            $arrayIndex++
        }
        return
    }

    if ($Node -is [pscustomobject]) {
        foreach ($property in $Node.PSObject.Properties) {
            if ($property.Name.StartsWith('_')) { continue }
            $childPath = if ([string]::IsNullOrEmpty($Path)) { $property.Name } else { $Path + '.' + $property.Name }
            Add-JsonLeaves -Node $property.Value -Path $childPath -Source $Source -LineLookup $LineLookup `
                -IsEnglishTable $IsEnglishTable -Rows $Rows
        }
    }
}

function Add-CanonicalJsonRows {
    param([System.Collections.Generic.List[object]]$Rows)

    $roots = @(
        (Join-Path $RepoRoot 'Assets/Resources/Data/Canonical'),
        (Join-Path $RepoRoot 'Assets/StreamingAssets/Data/Canonical')
    )
    $selected = @{}
    foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($path in ([System.IO.Directory]::EnumerateFiles($root, '*.json', [System.IO.SearchOption]::AllDirectories) | Sort-Object)) {
            $file = Get-Item -LiteralPath $path
            $rootRelative = $file.FullName.Substring($root.TrimEnd([char[]]@('\', '/')).Length + 1).Replace('\', '/')
            if (-not $selected.ContainsKey($rootRelative)) { $selected[$rootRelative] = $file.FullName }
        }
    }

    foreach ($relativeName in @($selected.Keys | Sort-Object)) {
        # Locale tables other than English are translations of the keyed source,
        # not new authored-copy debt. Keep draft and shipped BCP-47 locale files
        # out of literalCandidate inventory; parity gates own them instead.
        if ($relativeName -ne 'en.json' -and
            $relativeName -match '^[a-z]{2,3}(?:-[A-Za-z0-9]{2,8})*\.json$') {
            continue
        }

        $fullPath = [string]$selected[$relativeName]
        $source = Get-RepoRelativePath $fullPath
        $raw = [System.IO.File]::ReadAllText($fullPath)
        try { $node = $raw | ConvertFrom-Json }
        catch { throw "Invalid canonical JSON at ${source}: $($_.Exception.Message)" }
        $lineLookup = Get-JsonLineLookup -Lines (Get-Content -LiteralPath $fullPath)
        Add-JsonLeaves -Node $node -Path '' -Source $source -LineLookup $lineLookup `
            -IsEnglishTable ($relativeName -eq 'en.json') -Rows $Rows
    }
}

function Add-ImageRows {
    param([System.Collections.Generic.List[object]]$Rows)

    $assets = Join-Path $RepoRoot 'Assets'
    if (-not (Test-Path -LiteralPath $assets)) { return }
    $extensions = @('.png', '.jpg', '.jpeg', '.webp', '.tga', '.psd')
    $excluded = '(?i)^Assets/(?:Blink|GoogleSignIn|Plugins|TextMesh Pro|AddressableAssetsData|StreamingAssets/aa)/'
    $nameSignal = '(?i)(?:^|[_ -])(banner|wordmark|logo|title|titlecard|splash|text|label|button|cta|offer|sale)(?:$|[_ -])'

    $reviewRoots = @('Assets/Art', 'Assets/Resources', 'Assets/StreamingAssets', 'Assets/_Modules') |
        ForEach-Object { Join-Path $RepoRoot $_ } |
        Where-Object { Test-Path -LiteralPath $_ }
    $rasterPaths = foreach ($reviewRoot in $reviewRoots) {
        [System.IO.Directory]::EnumerateFiles($reviewRoot, '*', [System.IO.SearchOption]::AllDirectories) |
            Where-Object { $extensions -contains [System.IO.Path]::GetExtension($_).ToLowerInvariant() }
    }
    foreach ($path in @($rasterPaths | Sort-Object -Unique)) {
        $file = Get-Item -LiteralPath $path
        if ($extensions -notcontains $file.Extension.ToLowerInvariant()) { continue }
        $source = Get-RepoRelativePath $file.FullName
        if ($source -match $excluded) { continue }
        $metaSignal = $false
        $metaPath = $file.FullName + '.meta'
        if (Test-Path -LiteralPath $metaPath) {
            $meta = [System.IO.File]::ReadAllText($metaPath)
            $metaSignal = $meta -match '(?im)^\s*(?:userData|assetBundleName):.*(?:text|localiz|language|locale)'
        }
        if ($file.BaseName -notmatch $nameSignal -and -not $metaSignal) { continue }
        $reason = if ($metaSignal) { 'filename or Unity metadata suggests text/localization' } else { 'filename suggests possible baked text' }
        $Rows.Add((New-ManifestRow -Kind 'imageTextCandidate' -Key $null -Collection 'AssetTable' `
            -English $null -Owner (Get-OwnerFromSource $source) -Source $source -Line 0 `
            -Surface 'image-asset-review' -Context ("Review raster for baked words; " + $reason + '.') `
            -Arguments @() -Translatable $null -MaxExpansion $null -Screenshot $null -State 'review'))
    }
}

function New-Manifest {
    $rows = New-Object 'System.Collections.Generic.List[object]'
    Add-CSharpRows -Rows $rows
    Add-CanonicalJsonRows -Rows $rows
    Add-ImageRows -Rows $rows

    $orderedRows = @($rows | Sort-Object `
        @{ Expression = { $_.kind }; Ascending = $true },
        @{ Expression = { if ($null -eq $_.key) { '' } else { $_.key } }; Ascending = $true },
        @{ Expression = { $_.source }; Ascending = $true },
        @{ Expression = { $_.line }; Ascending = $true },
        @{ Expression = { if ($null -eq $_.english) { '' } else { $_.english } }; Ascending = $true })
    $dedupe = @{}
    $sorted = New-Object 'System.Collections.Generic.List[object]'
    foreach ($row in $orderedRows) {
        $identity = "$($row.kind)|$($row.key)|$($row.source)|$($row.line)|$($row.english)"
        if ($dedupe.ContainsKey($identity)) { continue }
        $dedupe[$identity] = $true
        $sorted.Add($row)
    }
    $sorted = $sorted.ToArray()

    $kindCounts = [ordered]@{}
    foreach ($kind in @('keyedEntry', 'keyedCall', 'literalCandidate', 'imageTextCandidate')) {
        $kindCounts[$kind] = @($sorted | Where-Object { $_.kind -eq $kind }).Count
    }

    [pscustomobject][ordered]@{
        schemaVersion = 1
        generatedBy   = 'tools/localization/build-string-manifest.ps1'
        mode          = 'report-only'
        scope         = [pscustomobject][ordered]@{
            runtimeCSharp = 'Assets/_Modules/**/*.cs (Editor/Test/Regression excluded)'
            canonicalJson = 'union of canonical JSON roots; Resources copy wins matching relative paths'
            rasterReview  = 'first-party raster filenames/metadata that suggest baked text'
        }
        summary       = [pscustomobject]$kindCounts
        entries       = $sorted
    }
}

function Assert-LiteralDebtBaseline {
    param($Baseline)

    if ($null -eq $Baseline) { throw 'Existing literalDebtBaseline is null.' }
    if ($Baseline.PSObject.Properties.Name -notcontains 'schemaVersion' -or
        [int]$Baseline.schemaVersion -ne 1) {
        throw 'literalDebtBaseline must declare schemaVersion=1.'
    }
    if ($Baseline.PSObject.Properties.Name -notcontains 'scannerVersion' -or
        [int]$Baseline.scannerVersion -ne 1) {
        throw 'literalDebtBaseline must declare scannerVersion=1.'
    }
    if ($Baseline.PSObject.Properties.Name -notcontains 'reviewed' -or
        $Baseline.reviewed -isnot [bool]) {
        throw 'literalDebtBaseline must declare an explicit Boolean reviewed value.'
    }
    $expectedAlgorithm = 'sha256(normalizedPath\0member\0sink\0decodedValue\0samePathMemberSinkValueOrdinal)'
    if ($Baseline.PSObject.Properties.Name -notcontains 'fingerprintAlgorithm' -or
        [string]$Baseline.fingerprintAlgorithm -cne $expectedAlgorithm) {
        throw "literalDebtBaseline fingerprintAlgorithm must be '$expectedAlgorithm'."
    }
    if ($Baseline.PSObject.Properties.Name -notcontains 'entries' -or
        $null -eq $Baseline.entries -or $Baseline.entries -isnot [System.Array]) {
        throw 'literalDebtBaseline entries must be an array.'
    }
}

function Assert-Manifest {
    param($Manifest)

    $required = @('kind', 'key', 'collection', 'english', 'owner', 'source', 'line', 'surface',
        'context', 'arguments', 'translatable', 'maxExpansion', 'screenshot', 'state')
    $seen = @{}
    foreach ($entry in $Manifest.entries) {
        foreach ($field in $required) {
            if ($entry.PSObject.Properties.Name -notcontains $field) { throw "Manifest entry missing field '$field'." }
        }
        if ([System.IO.Path]::IsPathRooted([string]$entry.source)) { throw "Manifest source is not repo-relative: $($entry.source)" }
        if ([string]$entry.source -match '\\') { throw "Manifest source uses a backslash: $($entry.source)" }
        if ($entry.state -notin @('migrated', 'review', 'nonTranslatable', 'approvedAssetVariant', 'blocked')) {
            throw "Unsupported manifest state '$($entry.state)' at $($entry.source):$($entry.line)"
        }
        $identity = "$($entry.kind)|$($entry.key)|$($entry.source)|$($entry.line)|$($entry.english)"
        if ($seen.ContainsKey($identity)) { throw "Duplicate manifest row: $identity" }
        $seen[$identity] = $true
    }

    $baselineProperty = $Manifest.PSObject.Properties['literalDebtBaseline']
    if ($null -ne $baselineProperty) {
        Assert-LiteralDebtBaseline -Baseline $baselineProperty.Value
    }
}

$outputFullPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    [System.IO.Path]::GetFullPath($OutputPath)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputPath))
}

if ($Check) {
    if (-not (Test-Path -LiteralPath $outputFullPath)) { throw "Manifest is missing: $(Get-RepoRelativePath $outputFullPath)" }
    $actual = [System.IO.File]::ReadAllText($outputFullPath).Replace("`r`n", "`n")
    try { $manifest = $actual | ConvertFrom-Json }
    catch { throw "Manifest is not valid JSON: $($_.Exception.Message)" }
    Assert-Manifest -Manifest $manifest
    $canonical = ($manifest | ConvertTo-Json -Depth 12 -Compress).Replace("`r`n", "`n") + "`n"
    if ($actual -ne $canonical) { throw "Manifest JSON is not in deterministic canonical form. Regenerate it." }
    foreach ($kind in @('keyedEntry', 'keyedCall', 'literalCandidate', 'imageTextCandidate')) {
        $count = @($manifest.entries | Where-Object { $_.kind -eq $kind }).Count
        if ([int]$manifest.summary.$kind -ne $count) { throw "Manifest summary mismatch for ${kind}." }
    }
    Write-Output ("Localization manifest check passed: {0} entries ({1} keyed table, {2} keyed calls, {3} literals, {4} image reviews)." -f `
        $manifest.entries.Count, $manifest.summary.keyedEntry, $manifest.summary.keyedCall,
        $manifest.summary.literalCandidate, $manifest.summary.imageTextCandidate)
    exit 0
}

$existingLiteralDebtBaseline = $null
if (Test-Path -LiteralPath $outputFullPath) {
    try { $existingManifest = [System.IO.File]::ReadAllText($outputFullPath) | ConvertFrom-Json }
    catch { throw "Existing manifest is not valid JSON; refusing to overwrite it: $($_.Exception.Message)" }
    $baselineProperty = $existingManifest.PSObject.Properties['literalDebtBaseline']
    if ($null -ne $baselineProperty) {
        Assert-LiteralDebtBaseline -Baseline $baselineProperty.Value
        $existingLiteralDebtBaseline = $baselineProperty.Value
    }
}

$manifest = New-Manifest
if ($null -ne $existingLiteralDebtBaseline) {
    $manifest | Add-Member -NotePropertyName literalDebtBaseline -NotePropertyValue $existingLiteralDebtBaseline
}
Assert-Manifest -Manifest $manifest
$json = ($manifest | ConvertTo-Json -Depth 12 -Compress).Replace("`r`n", "`n") + "`n"
$parent = [System.IO.Path]::GetDirectoryName($outputFullPath)
if (-not (Test-Path -LiteralPath $parent)) { [System.IO.Directory]::CreateDirectory($parent) | Out-Null }
[System.IO.File]::WriteAllText($outputFullPath, $json, (New-Object System.Text.UTF8Encoding($false)))
Write-Output ("Wrote {0}: {1} entries ({2} keyed table, {3} keyed calls, {4} literals, {5} image reviews)." -f `
    (Get-RepoRelativePath $outputFullPath), $manifest.entries.Count, $manifest.summary.keyedEntry,
    $manifest.summary.keyedCall, $manifest.summary.literalCandidate, $manifest.summary.imageTextCandidate)
