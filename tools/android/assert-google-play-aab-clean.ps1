<#
    Standalone Google Play AAB cleanliness scanner (WO-1255, widened by WO-1364).

    THE TOKEN POLICY IS NOT DUPLICATED HERE ANY MORE.
    Assets/Editor/Regression/GooglePlayPackagingGate.cs owns ForbiddenTokens,
    ShortTokensRequiringTextContext, FalsePositiveAllowlist, ExactIdentifierAllowlist
    and MinPrintableRunForShortTokens; this script PARSES them out of that file at run
    time. The two copies used to be maintained by hand and had already drifted once,
    which is the same duplicated-state failure CLAUDE.md sec.2/sec.5/sec.16 all record.
    The compiled gate keeps its literals (a gate that can lose its policy to a missing
    data file is a hollow gate); this script fails CLOSED - it throws - if it cannot
    read and parse them.

    WO-1754 (2026-09-15) mirrors two gate changes, and the MATCHING RULES have to move
    with the arrays or the drift comes back through the algorithm instead of the data:

      1. ExactIdentifierAllowlist - owner-ruled identifiers suppressed by WHOLE-IDENTITY
         match only (bounded by a non-alphanumeric character on BOTH sides), so an
         identifier that merely starts with one of them keeps firing.

      2. The CHUNK SEAM. This script reads a payload in 1 MiB reads and used to carry a
         small tail forward, then judge each buffer whole. Both the allowlist window and
         the printable run read context on BOTH sides of a hit, so a read edge truncated
         the very evidence that suppresses a false positive - and the retained tail was
         then RE-JUDGED at the head of the next buffer with its left context gone. A
         buffer now DECIDES only the positions it holds full context for and defers the
         rest, so every byte is judged exactly once, by the buffer that can see it.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AabPath,
    [string]$GateSourcePath
)

$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $AabPath).Path
if ([IO.Path]::GetExtension($resolved) -ne '.aab') {
    throw "PLAY_ARTIFACT_FAIL: expected an .aab file, got '$resolved'"
}

if (-not $GateSourcePath) {
    $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $GateSourcePath = Join-Path $repoRoot 'Assets/Editor/Regression/GooglePlayPackagingGate.cs'
}
if (-not (Test-Path -LiteralPath $GateSourcePath)) {
    throw "PLAY_ARTIFACT_FAIL: token policy source not found at '$GateSourcePath'; refusing to scan with an empty policy"
}
$gateSource = [IO.File]::ReadAllText($GateSourcePath)

function Get-GateTokenArray {
    param([string]$Source, [string]$Name)

    # Pull one `private static readonly string[] Name = { ... };` block out of the C#
    # gate and return its string literals. Comments are stripped first; the gate's own
    # doc comment requires them to stay free of double quotes so this stays simple.
    $anchor = $Source.IndexOf("string[] $Name", [StringComparison]::Ordinal)
    if ($anchor -lt 0) { throw "PLAY_ARTIFACT_FAIL: $Name not found in $GateSourcePath" }
    $open = $Source.IndexOf('{', $anchor)
    $close = $Source.IndexOf('};', $open)
    if ($open -lt 0 -or $close -lt 0) { throw "PLAY_ARTIFACT_FAIL: $Name block is unreadable in $GateSourcePath" }
    $block = $Source.Substring($open + 1, $close - $open - 1)

    $tokens = [Collections.Generic.List[string]]::new()
    foreach ($line in $block -split "`n") {
        $text = $line
        $comment = $text.IndexOf('//')
        if ($comment -ge 0) {
            $quotesBefore = ([regex]::Matches($text.Substring(0, $comment), '"')).Count
            if ($quotesBefore % 2 -eq 0) { $text = $text.Substring(0, $comment) }
        }
        foreach ($match in [regex]::Matches($text, '"([^"]*)"')) {
            $tokens.Add($match.Groups[1].Value)
        }
    }
    if ($tokens.Count -eq 0) { throw "PLAY_ARTIFACT_FAIL: $Name parsed empty from $GateSourcePath" }
    return $tokens.ToArray()
}

function Get-GateIntConst {
    param([string]$Source, [string]$Name, [int]$Fallback)
    $match = [regex]::Match($Source, "const\s+int\s+$Name\s*=\s*(\d+)")
    if ($match.Success) { return [int]$match.Groups[1].Value }
    return $Fallback
}

$forbiddenTokens = Get-GateTokenArray -Source $gateSource -Name 'ForbiddenTokens'
$shortTokens = Get-GateTokenArray -Source $gateSource -Name 'ShortTokensRequiringTextContext'
$allowlist = Get-GateTokenArray -Source $gateSource -Name 'FalsePositiveAllowlist'
$exactIdentifiers = Get-GateTokenArray -Source $gateSource -Name 'ExactIdentifierAllowlist'
$minPrintableRun = Get-GateIntConst -Source $gateSource -Name 'MinPrintableRunForShortTokens' -Fallback 12

Add-Type -AssemblyName System.IO.Compression.FileSystem
$scratch = Join-Path ([IO.Path]::GetTempPath()) ("eoa-play-aab-audit-" + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null

$hits = [Collections.Generic.List[string]]::new()

function Test-PrintableRun {
    param([string]$Text, [int]$Hit, [int]$Length, [int]$MinRun)
    $isPrintable = { param([char]$c) $code = [int]$c; ($code -ge 32 -and $code -le 126) -or $code -eq 9 }
    for ($i = $Hit; $i -lt ($Hit + $Length) -and $i -lt $Text.Length; $i++) {
        if (-not (& $isPrintable $Text[$i])) { return $false }
    }
    $left = $Hit
    while ($left -gt 0 -and (& $isPrintable $Text[$left - 1])) { $left-- }
    $right = $Hit + $Length
    while ($right -lt $Text.Length -and (& $isPrintable $Text[$right])) { $right++ }
    return (($right - $left) -ge $MinRun)
}

function Test-AllowlistedOccurrence {
    param([string]$Text, [int]$Hit, [string]$Token, [string[]]$Allowlist)
    foreach ($allow in $Allowlist) {
        if ($allow.IndexOf($Token, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
        $windowStart = [Math]::Max(0, $Hit - $allow.Length)
        $windowEnd = [Math]::Min($Text.Length, $Hit + $Token.Length + $allow.Length)
        $found = $Text.IndexOf($allow, $windowStart, $windowEnd - $windowStart, [StringComparison]::OrdinalIgnoreCase)
        while ($found -ge 0) {
            if ($found -le $Hit -and ($found + $allow.Length) -ge ($Hit + $Token.Length)) { return $true }
            $next = $found + 1
            if ($next -ge $windowEnd) { break }
            $found = $Text.IndexOf($allow, $next, $windowEnd - $next, [StringComparison]::OrdinalIgnoreCase)
        }
    }
    return $false
}

function Test-IdentifierChar {
    # Mirror of GooglePlayPackagingGate.IsIdentifierChar (WO-1754). A character that can be
    # part of a NAME here: a C# identifier, an addressable id, or a hyphen-separated
    # PlayerPrefs save key. NOT a plain alphanumeric test - a hyphen boundary would read the
    # ruled key dotr-arena-skr-balance as standing alone inside dotr-arena-skr-balance-v2 and
    # suppress that one too. The dot is deliberately NOT an identifier character, so a
    # fully-qualified reference to a ruled member is still the ruled member.
    param([char]$Char)
    return ([char]::IsLetterOrDigit($Char) -or $Char -eq '_' -or $Char -eq '-')
}

function Test-ExactIdentifierOccurrence {
    # Mirror of GooglePlayPackagingGate.IsExactIdentifierAllowlisted (WO-1754). The whole
    # identifier must be present AND bounded by a non-alphanumeric character on both sides,
    # so SolanaWalletAdapterWebGL is NOT suppressed by the SolanaWallet ruling while a
    # NUL-packed name-table entry is. An edge of the TEXT counts as a boundary only when it
    # is an edge of the STREAM - otherwise a read seam would decide what it cannot see.
    param(
        [string]$Text,
        [int]$Hit,
        [string]$Token,
        [string[]]$Identifiers,
        [bool]$TextStartIsStreamStart = $true,
        [bool]$TextEndIsStreamEnd = $true
    )
    foreach ($identifier in $Identifiers) {
        if ([string]::IsNullOrEmpty($identifier)) { continue }
        if ($identifier.IndexOf($Token, [StringComparison]::OrdinalIgnoreCase) -lt 0) { continue }
        $windowStart = [Math]::Max(0, $Hit - $identifier.Length)
        $windowEnd = [Math]::Min($Text.Length, $Hit + $Token.Length + $identifier.Length)
        $found = $Text.IndexOf($identifier, $windowStart, $windowEnd - $windowStart, [StringComparison]::OrdinalIgnoreCase)
        while ($found -ge 0) {
            if ($found -le $Hit -and ($found + $identifier.Length) -ge ($Hit + $Token.Length)) {
                $before = $found - 1
                $after = $found + $identifier.Length
                $leftClear = if ($before -lt 0) { $TextStartIsStreamStart } else { -not (Test-IdentifierChar -Char $Text[$before]) }
                $rightClear = if ($after -ge $Text.Length) { $TextEndIsStreamEnd } else { -not (Test-IdentifierChar -Char $Text[$after]) }
                if ($leftClear -and $rightClear) { return $true }
            }
            $next = $found + 1
            if ($next -ge $windowEnd) { break }
            $found = $Text.IndexOf($identifier, $next, $windowEnd - $next, [StringComparison]::OrdinalIgnoreCase)
        }
    }
    return $false
}

function Test-TokenInText {
    # Mirror of GooglePlayPackagingGate.MatchesTokenInWindow. ReadableEntry means the
    # payload is text end to end, so a short token needs no printable-run corroboration.
    # WindowStart/WindowEnd (WO-1754) say which occurrences THIS caller is responsible for
    # deciding; context is still read across the whole text, so a hit is judged with every
    # byte the caller has. WindowEnd of -1 means the whole text, which with both stream-edge
    # flags true is byte-for-byte the pre-WO-1754 behaviour.
    param(
        [string]$Text,
        [string]$Token,
        [bool]$ReadableEntry = $true,
        [string[]]$ShortTokens = @(),
        [string[]]$Allowlist = @(),
        [string[]]$ExactIdentifiers = @(),
        [int]$MinRun = 12,
        [int]$WindowStart = 0,
        [int]$WindowEnd = -1,
        [bool]$TextStartIsStreamStart = $true,
        [bool]$TextEndIsStreamEnd = $true
    )
    if ([string]::IsNullOrEmpty($Text) -or [string]::IsNullOrEmpty($Token)) { return $false }
    if ($WindowEnd -lt 0 -or $WindowEnd -gt $Text.Length) { $WindowEnd = $Text.Length }
    if ($WindowStart -lt 0) { $WindowStart = 0 }
    if ($WindowStart -ge $WindowEnd) { return $false }

    $isShort = (-not $ReadableEntry) -and ($ShortTokens -contains $Token)
    $start = $WindowStart
    while ($start -lt $WindowEnd) {
        $hit = $Text.IndexOf($Token, $start, [StringComparison]::OrdinalIgnoreCase)
        if ($hit -lt 0 -or $hit -ge $WindowEnd) { return $false }
        $start = $hit + 1

        $needsBoundary = [char]::IsLetterOrDigit($Token[0])
        if ($needsBoundary -and $hit -ne 0 -and [char]::IsLetterOrDigit($Text[$hit - 1])) { continue }
        if ($needsBoundary -and $hit -eq 0 -and -not $TextStartIsStreamStart) { continue }

        if ($isShort) {
            $after = $hit + $Token.Length
            if ($after -lt $Text.Length -and [char]::IsLetterOrDigit($Text[$after])) { continue }
            # A read edge is NOT a word end.
            if ($after -ge $Text.Length -and -not $TextEndIsStreamEnd) { continue }
            if (-not (Test-PrintableRun -Text $Text -Hit $hit -Length $Token.Length -MinRun $MinRun)) { continue }
        }

        if (Test-AllowlistedOccurrence -Text $Text -Hit $hit -Token $Token -Allowlist $Allowlist) { continue }
        if (Test-ExactIdentifierOccurrence -Text $Text -Hit $hit -Token $Token -Identifiers $ExactIdentifiers `
                -TextStartIsStreamStart $TextStartIsStreamStart -TextEndIsStreamEnd $TextEndIsStreamEnd) { continue }

        return $true
    }
    return $false
}

function Get-ScanMarginChars {
    # Mirror of GooglePlayPackagingGate.ScanMarginChars (WO-1754). Characters of context a
    # single hit can need on EITHER side before it can be judged at all: the longest token,
    # the longest allowlist phrase or ruled identifier (the allowlist window searches
    # hit +/- phrase length, which the old tail never accounted for), the printable-run
    # reach, and headroom. CHARACTERS, not bytes - both views are indexed in characters.
    param([string[]]$Tokens, [string[]]$Allowlist, [string[]]$ExactIdentifiers, [int]$MinRun)
    $longestToken = 0
    foreach ($t in $Tokens) { if ($t -and $t.Length -gt $longestToken) { $longestToken = $t.Length } }
    $longestPhrase = 0
    foreach ($a in $Allowlist) { if ($a -and $a.Length -gt $longestPhrase) { $longestPhrase = $a.Length } }
    foreach ($a in $ExactIdentifiers) { if ($a -and $a.Length -gt $longestPhrase) { $longestPhrase = $a.Length } }
    return $longestToken + $longestPhrase + (4 * $MinRun) + 128
}

function Read-StreamFully {
    # Stream.Read may return fewer bytes than asked for without being at the end, and the
    # seam rule below reads a short read as END OF STREAM. Fill the buffer or prove EOF.
    param([IO.Stream]$Stream, [byte[]]$Buffer, [int]$Offset, [int]$Count)
    $total = 0
    while ($total -lt $Count) {
        $read = $Stream.Read($Buffer, $Offset + $total, $Count - $total)
        if ($read -le 0) { break }
        $total += $read
    }
    return $total
}

function Find-StreamTokens {
    param(
        [string]$Path,
        [string[]]$Tokens,
        [bool]$ReadableEntry = $true,
        [string[]]$ShortTokens = @(),
        [string[]]$Allowlist = @(),
        [string[]]$ExactIdentifiers = @(),
        [int]$MinRun = 12
    )

    # One pass over the payload for the WHOLE vocabulary. Test-StreamToken below keeps the
    # single-token contract, but calling it per token re-read a 500 MB artifact 30 times.
    #
    # WO-1754 SEAM RULE. A buffer SEARCHES the whole text it holds, so context is read at
    # full width, but only REPORTS hits inside its decision range - and the ranges tile, so
    # every absolute position is judged exactly once, by the buffer that holds its context.
    # Retaining six margins is what makes the ranges tile in the UTF-16 view too, where a
    # buffer holds half as many characters per byte. See the gate's ScanRetainBytes.
    $found = [Collections.Generic.List[string]]::new()
    $pending = [Collections.Generic.List[string]]::new()
    $pending.AddRange($Tokens)

    $marginChars = Get-ScanMarginChars -Tokens $Tokens -Allowlist $Allowlist -ExactIdentifiers $ExactIdentifiers -MinRun $MinRun
    $retainBytes = 6 * $marginChars
    if (($retainBytes % 2) -ne 0) { $retainBytes++ }   # keep the UTF-16 view on one parity
    $readSize = 1024 * 1024

    $stream = [IO.File]::OpenRead($Path)
    try {
        $buffer = New-Object byte[] ($readSize + $retainBytes)
        $latin1 = [Text.Encoding]::GetEncoding(28591)
        $retained = 0
        $firstChunk = $true

        while ($true) {
            if ($pending.Count -eq 0) { break }
            $read = Read-StreamFully -Stream $stream -Buffer $buffer -Offset $retained -Count $readSize
            if ($read -le 0) {
                # The previous buffer deferred its tail and there is no next buffer to judge
                # it. Without this pass a payload whose length is an exact multiple of the
                # read size silently drops its last margin of bytes.
                if (-not $firstChunk -and $retained -gt 0) {
                    $ascii = $latin1.GetString($buffer, 0, $retained)
                    $utf16 = [Text.Encoding]::Unicode.GetString($buffer, 0, $retained - ($retained % 2))
                    foreach ($token in @($pending)) {
                        $lo = [Math]::Max(0, $retained - $marginChars - $token.Length)
                        $lo16 = [Math]::Max(0, [int]($retained / 2) - $marginChars - $token.Length)
                        if ((Test-TokenInText -Text $ascii -Token $token -ReadableEntry $ReadableEntry -ShortTokens $ShortTokens -Allowlist $Allowlist -ExactIdentifiers $ExactIdentifiers -MinRun $MinRun -WindowStart $lo -WindowEnd -1 -TextStartIsStreamStart $false -TextEndIsStreamEnd $true) -or
                            (Test-TokenInText -Text $utf16 -Token $token -ReadableEntry $ReadableEntry -ShortTokens $ShortTokens -Allowlist $Allowlist -ExactIdentifiers $ExactIdentifiers -MinRun $MinRun -WindowStart $lo16 -WindowEnd -1 -TextStartIsStreamStart $false -TextEndIsStreamEnd $true)) {
                            $found.Add($token) | Out-Null
                            $pending.Remove($token) | Out-Null
                        }
                    }
                }
                break
            }

            $count = $retained + $read
            $finalChunk = $read -lt $readSize
            $ascii = $latin1.GetString($buffer, 0, $count)
            $utf16 = [Text.Encoding]::Unicode.GetString($buffer, 0, $count - ($count % 2))

            foreach ($token in @($pending)) {
                $lo = if ($firstChunk) { 0 } else { [Math]::Max(0, $retained - $marginChars - $token.Length) }
                $hi = if ($finalChunk) { -1 } else { [Math]::Max(0, $ascii.Length - $marginChars - $token.Length) }
                $lo16 = if ($firstChunk) { 0 } else { [Math]::Max(0, [int]($retained / 2) - $marginChars - $token.Length) }
                $hi16 = if ($finalChunk) { -1 } else { [Math]::Max(0, $utf16.Length - $marginChars - $token.Length) }
                if ((Test-TokenInText -Text $ascii -Token $token -ReadableEntry $ReadableEntry -ShortTokens $ShortTokens -Allowlist $Allowlist -ExactIdentifiers $ExactIdentifiers -MinRun $MinRun -WindowStart $lo -WindowEnd $hi -TextStartIsStreamStart $firstChunk -TextEndIsStreamEnd $finalChunk) -or
                    (Test-TokenInText -Text $utf16 -Token $token -ReadableEntry $ReadableEntry -ShortTokens $ShortTokens -Allowlist $Allowlist -ExactIdentifiers $ExactIdentifiers -MinRun $MinRun -WindowStart $lo16 -WindowEnd $hi16 -TextStartIsStreamStart $firstChunk -TextEndIsStreamEnd $finalChunk)) {
                    $found.Add($token) | Out-Null
                    $pending.Remove($token) | Out-Null
                }
            }

            if ($finalChunk) { break }
            $retained = [Math]::Min($retainBytes, $count)
            [Buffer]::BlockCopy($buffer, $count - $retained, $buffer, 0, $retained)
            $firstChunk = $false
        }
    }
    finally { $stream.Dispose() }
    return $found.ToArray()
}

function Test-StreamToken {
    param(
        [string]$Path,
        [string]$Token,
        [bool]$ReadableEntry = $true,
        [string[]]$ShortTokens = @(),
        [string[]]$Allowlist = @(),
        [string[]]$ExactIdentifiers = @(),
        [int]$MinRun = 12
    )

    # The single-token contract, kept for callers and for the source oracle that pins it.
    # It has NO caller inside this script: the scan loop below calls Find-StreamTokens,
    # which sweeps the whole vocabulary in one pass.
    #
    # WO-1754: this used to be a SECOND, hand-written copy of the chunked scan - its own
    # buffer, its own tail arithmetic, its own seam. That is the duplicated-state failure
    # the header paragraph is about, one level down in the algorithm rather than in the
    # data, and it had already diverged from Find-StreamTokens (a per-token $keep instead
    # of a per-vocabulary one). It now DELEGATES, so there is exactly one seam rule in this
    # file and a fix can never land in only half of it. Latin-1 (28591) decoding, the
    # NOT-ASCII decision that keeps the printable-run rule meaningful, lives there too.
    $hitTokens = Find-StreamTokens -Path $Path -Tokens @($Token) -ReadableEntry $ReadableEntry `
        -ShortTokens $ShortTokens -Allowlist $Allowlist -ExactIdentifiers $ExactIdentifiers -MinRun $MinRun
    return ($hitTokens.Count -gt 0)
}

try {
    [IO.Compression.ZipFile]::ExtractToDirectory($resolved, $scratch)
    foreach ($file in Get-ChildItem -LiteralPath $scratch -File -Recurse) {
        $relative = $file.FullName.Substring($scratch.Length + 1).Replace('\', '/')
        if ($relative.Equals('BUNDLE-METADATA/com.unity/dependencies.pb', [StringComparison]::OrdinalIgnoreCase)) {
            continue # provenance receipt; actual executable leakage is scanned elsewhere
        }
        $isUserFacing = $relative.StartsWith('base/assets/Data/Canonical/', [StringComparison]::OrdinalIgnoreCase) -or
            $relative.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase) -or
            $relative.EndsWith('.txt', [StringComparison]::OrdinalIgnoreCase) -or
            $relative.EndsWith('.html', [StringComparison]::OrdinalIgnoreCase) -or
            $relative.EndsWith('.xml', [StringComparison]::OrdinalIgnoreCase) -or
            $relative.EndsWith('.uxml', [StringComparison]::OrdinalIgnoreCase)
        # WO-1364: one vocabulary for every entry. The entry class only decides how much
        # corroboration a SHORT token needs, never which tokens are enforced.
        $tokens = $forbiddenTokens
        # Mirror of GooglePlayPackagingGate.IsSignatureDigestEntry: base64 SHA digests are
        # long printable runs of arbitrary characters, so short tokens cannot be judged there.
        $isDigestListing = $relative.StartsWith('META-INF/', [StringComparison]::OrdinalIgnoreCase) -and
            ($relative.EndsWith('/MANIFEST.MF', [StringComparison]::OrdinalIgnoreCase) -or
             $relative.EndsWith('.SF', [StringComparison]::OrdinalIgnoreCase))
        if ($isDigestListing) { $tokens = @($forbiddenTokens | Where-Object { $shortTokens -notcontains $_ }) }
        foreach ($token in $tokens) {
            # An entry NAME is always text, so no printable-run rule applies to it.
            if (Test-TokenInText -Text $relative -Token $token -ReadableEntry $true -ShortTokens $shortTokens -Allowlist $allowlist -ExactIdentifiers $exactIdentifiers -MinRun $minPrintableRun) {
                $hits.Add("path:$relative token:$token")
            }
        }
        foreach ($token in (Find-StreamTokens -Path $file.FullName -Tokens $tokens -ReadableEntry $isUserFacing -ShortTokens $shortTokens -Allowlist $allowlist -ExactIdentifiers $exactIdentifiers -MinRun $minPrintableRun)) {
            $hits.Add("content:$relative token:$token")
        }
    }
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}

if ($hits.Count -gt 0) {
    # Write-Output, not Write-Error: with $ErrorActionPreference = 'Stop' the first
    # Write-Error terminated the script, so a dirty artifact reported ONE hit and hid
    # the rest. The gate has to name every entry it found.
    $hits | Sort-Object -Unique | Select-Object -First 100 | ForEach-Object { Write-Output "PLAY_ARTIFACT_DIRTY_HIT $_" }
    throw "PLAY_ARTIFACT_DIRTY: forbidden wallet/crypto material found in $resolved"
}

Write-Output "PLAY_ARTIFACT_CLEAN_OK $resolved"
