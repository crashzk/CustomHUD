<#
.SYNOPSIS
    Audits CustomHUD the way the game never does. Run it before reporting any change done.

.DESCRIPTION
    CS2 drops an unknown Panorama property without a word, loses a whole sheet to one selector it
    cannot parse, and resourcecompiler passes both (docs/PanoramaAuthoring.md, "Why Panorama
    silently eats mistakes"). Generated files drift from their generators, a translation drifts
    between languages, and a glyph with no icon class draws nothing. Each finding names its check;
    the summary names the doc rule behind each check that failed.

    -Quick runs the static checks, in seconds. Without it the audit also builds the contract,
    publishes the plugin as CI does, runs SoftDependencyCheck and docs/verify-wiki.ps1, and
    compiles the changed Panorama sources into the customhud addon (noted and skipped when CS2 or
    the addon is missing). C# unchanged since the last full pass skips the build; -Force rebuilds.

    A pass writes build/audit/receipt.json, which scripts/audit-hook.ps1 reads.

.PARAMETER Quick
    Static checks only.

.PARAMETER Force
    Rebuild even when the C# is unchanged since the last full pass.

.PARAMETER JsonPath
    Also write the result as JSON to this file.

.PARAMETER Fingerprint
    Write only the repository's fingerprints to -JsonPath (or the pipeline) and stop.

.EXAMPLE
    pwsh -File scripts/audit.ps1
    pwsh -File scripts/audit.ps1 -Quick
#>
#Requires -Version 7.0
# Windows PowerShell 5.1 reads the BOM-less generators as ANSI and writes mojibake into the layouts.
[CmdletBinding()]
param(
    [switch] $Quick,
    [switch] $Force,
    [switch] $Fingerprint,
    [string] $JsonPath = $null,
    [string] $Cs2Root  = 'C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive'
)

$ErrorActionPreference = 'Stop'
$started = Get-Date
$clock = [Diagnostics.Stopwatch]::StartNew()
function Write-Stage([string] $Name) { Write-Verbose ('{0,-14} {1,5:N1}s' -f $Name, $clock.Elapsed.TotalSeconds) }

$Repo         = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Panorama     = Join-Path $Repo 'resources\panorama'
$Styles       = Join-Path $Panorama 'styles\custom_game\customhud'
$Layouts      = Join-Path $Panorama 'layout\custom_game\customhud'
$Translations = Join-Path $Repo 'resources\translations'
$ReceiptPath  = Join-Path $Repo 'build\audit\receipt.json'

# The hook asks for a full pass when cs, panorama or i18n moved since the last one.
$Sets = [ordered]@{
    cs       = @('src', 'CustomHUD.Contract', 'CustomHUD.csproj', 'lib', 'tools')
    panorama = @('resources\panorama')
    i18n     = @('resources\translations')
    docs     = @('docs', 'README.md', 'config.example.jsonc')
    scripts  = @('scripts', '.github')
}

function Get-Rel([string] $Path) { $Path.Substring($Repo.Length).TrimStart('\', '/').Replace('\', '/') }

function Get-Hex([byte[]] $Bytes) { ([BitConverter]::ToString($Bytes) -replace '-', '').ToLowerInvariant() }

function Get-Fingerprints {
    $sha = [Security.Cryptography.SHA256]::Create()
    $result = [ordered]@{}
    foreach ($name in $Sets.Keys) {
        $lines = New-Object 'System.Collections.Generic.List[string]'
        foreach ($entry in $Sets[$name]) {
            $full = Join-Path $Repo $entry
            if (Test-Path -LiteralPath $full -PathType Leaf) { $items = @(Get-Item -LiteralPath $full) }
            elseif (Test-Path -LiteralPath $full) {
                $items = @(Get-ChildItem -LiteralPath $full -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
            }
            else { continue }
            foreach ($item in $items) { $lines.Add(('{0}|{1}|{2}' -f (Get-Rel $item.FullName), $item.Length, $item.LastWriteTimeUtc.Ticks)) }
        }
        $lines.Sort([StringComparer]::Ordinal)
        $result[$name] = Get-Hex $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))
    }
    $result['all'] = Get-Hex $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes((@($result.Values) -join '|')))
    return $result
}

function Write-JsonFile([string] $Path, $Value) {
    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 6), (New-Object System.Text.UTF8Encoding $false))
}

if ($Fingerprint) {
    $fingerprints = Get-Fingerprints
    if ($JsonPath) { Write-JsonFile $JsonPath $fingerprints } else { $fingerprints }
    return
}

$Rules = [ordered]@{
    'css.parse'          = 'docs/PanoramaAuthoring.md, "Why Panorama silently eats mistakes": one selector CS2 cannot parse loses the whole sheet'
    'css.property'       = 'docs/PanoramaAuthoring.md, "Why Panorama silently eats mistakes"; the list is scripts/panorama-properties.txt'
    'css.selector'       = 'docs/PanoramaAuthoring.md, "Panorama vs web CSS, concretely": its pseudo-class list and "Not supported" row'
    'css.value'          = 'docs/PanoramaAuthoring.md, "Panorama vs web CSS, concretely": no calc(), var() or hsl()'
    'css.at-rule'        = 'docs/PanoramaAuthoring.md, "Panorama vs web CSS, concretely": only @define and @keyframes exist'
    'css.selector-limit' = 'docs/DesignLanguage.md, "Stylesheet rules": at most 30 selectors, about 1600 characters, per rule'
    'css.token'          = 'docs/DesignLanguage.md, "Stylesheet rules": a token comes from customhud_tokens.vcss or its own sheet'
    'css.define-prefix'  = 'docs/DesignLanguage.md, "Stylesheet rules": no define name may be a prefix of another'
    'css.colour-literal' = 'docs/DesignLanguage.md, "Stylesheet rules": no colour literal outside customhud_tokens.vcss'
    'css.keyframes'      = 'docs/DesignLanguage.md, "Stylesheet rules": keyframes stay in the sheet that uses them'
    'css.border-colour'  = 'docs/DesignLanguage.md, "Stylesheet rules": one border colour per panel'
    'css.thin-shadow'    = 'docs/DesignLanguage.md, "Stylesheet rules": thin bars take no box-shadow'
    'css.image'          = 'docs/PanoramaAuthoring.md, "Images": an addon image needs its source in resources/panorama/images'
    'css.comment'        = 'Valve''s sheets use /* */ only: a // comment joins the next selector, and one unreadable selector loses the whole sheet'
    'css.shorthand'      = 'Valve''s own sheets use the animation-* and transition-* longhands only; the shorthands are unproven in CS2'
    'css.sizing'         = 'docs/PanoramaAuthoring.md, "Page layouts": a fixed-column line stays at least 6px short of its container, and its budget comment must add up'
    'vxml.allowlist'     = 'docs/PanoramaAuthoring.md, "What a HUD layout may contain": the client validator''s elements and attributes'
    'vxml.xml'           = 'a layout that is not well-formed XML does not load'
    'vxml.include'       = 'docs/DesignLanguage.md, "Files": every addon include names a source in resources/panorama'
    'vxml.tokens-first'  = 'docs/DesignLanguage.md, "Stylesheet rules": tokens come first in every layout'
    'vxml.csgostyles'    = 'docs/DesignLanguage.md, "Stylesheet rules": csgostyles is not included'
    'vxml.duplicate-id'  = 'FrameComposer writes by panel id, so an id names one panel'
    'gen.v2-sheets'      = 'docs/PanoramaAuthoring.md, "The v2 sheet copies": edit the source sheet, rerun scripts/generate-v2-sheets.ps1'
    'gen.layouts'        = 'scripts/generate-*-layout.ps1 headers: change the generator, then rerun it'
    'cs.glyph'           = 'MenuPageProfile.Glyph is "ico-{0}": a glyph needs its .ico- class in customhud_icons.vcss'
    'i18n.parse'         = 'resources/translations/*.jsonc must parse'
    'i18n.duplicate'     = 'a duplicated key silently keeps one of its values'
    'i18n.parity'        = 'every string ships in English and Lithuanian'
    'i18n.orphan'        = 'a translated key with no English original is never shown'
    'i18n.placeholders'  = 'a {N} the caller does not pass fails at format time'
    'i18n.key'           = 'a key the code asks for must exist in en.jsonc'
    'build.contract'     = '.github/workflows/build-and-release.yml'
    'build.publish'      = '.github/workflows/build-and-release.yml: the build stays warning-free'
    'build.soft-deps'    = 'tools/SoftDependencyCheck: the plugin loads with no provider plugin installed'
    'panorama.compile'   = 'docs/PanoramaAuthoring.md, "The authoring loop"'
    'docs.wiki'          = 'docs/verify-wiki.ps1'
}

$Findings = New-Object 'System.Collections.Generic.List[object]'
function Add-Finding([string] $Level, [string] $Check, [string] $File, [int] $Line, [string] $Message) {
    $Findings.Add([pscustomobject]@{ Level = $Level; Check = $Check; File = $File; Line = $Line; Message = $Message })
}

function Limit([string] $Text, [int] $Max = 70) {
    $t = ($Text -replace '\s+', ' ').Trim()
    if ($t.Length -gt $Max) { return $t.Substring(0, $Max) + '...' }
    return $t
}

function Get-LineStarts([string] $Text) {
    $starts = New-Object 'System.Collections.Generic.List[int]'
    $starts.Add(0)
    $i = $Text.IndexOf("`n")
    while ($i -ge 0) { $starts.Add($i + 1); $i = $Text.IndexOf("`n", $i + 1) }
    return , $starts.ToArray()
}

function Get-Line([int[]] $Starts, [int] $Offset) {
    $i = [Array]::BinarySearch($Starts, $Offset)
    if ($i -lt 0) { $i = (-bnot $i) - 1 }
    return $i + 1
}

function Remove-Comments([string] $Text) {
    # Blanked rather than removed, so every offset and line number stays true.
    [regex]::Replace($Text, '/\*[\s\S]*?\*/', { param($m) [regex]::Replace($m.Value, '[^\n]', ' ') })
}

# ---------------------------------------------------------------- stylesheets

# Findings carry an offset until they are reported: a line number per declaration is most of the cost.
function Add-At([string] $Level, [string] $Check, $Sheet, [int] $At, [string] $Message) {
    Add-Finding $Level $Check $Sheet.Rel (Get-Line $Sheet.Starts $At) $Message
}

function New-Rule($Sheet, [string] $Header, [int] $HeaderAt, [int] $From, [int] $To, [bool] $Keyframe) {
    $rule = [pscustomobject]@{
        Header = $Header; At = $HeaderAt; Keyframe = $Keyframe; Border = $false; Shadow = $false
        Decls  = New-Object 'System.Collections.Generic.List[object]'
    }
    $offset = $From
    foreach ($segment in $Sheet.Text.Substring($From, $To - $From).Split(';')) {
        $at = $offset
        $offset += $segment.Length + 1
        $trimmed = $segment.Trim()
        if (-not $trimmed) { continue }
        $at += $segment.Length - $segment.TrimStart().Length
        $colon = $trimmed.IndexOf(':')
        if ($colon -lt 0) { Add-At error css.parse $Sheet $at "a declaration with no colon: '$(Limit $trimmed)'"; continue }
        $prop = $trimmed.Substring(0, $colon).Trim().ToLowerInvariant()
        if ($prop.StartsWith('border')) { $rule.Border = $true } elseif ($prop -eq 'box-shadow') { $rule.Shadow = $true }
        $rule.Decls.Add([pscustomobject]@{ Prop = $prop; Value = $trimmed.Substring($colon + 1).Trim(); At = $at })
    }
    return $rule
}

function Read-Sheet([string] $Path) {
    $raw = [IO.File]::ReadAllText($Path)
    $text = Remove-Comments $raw
    $sheet = [pscustomobject]@{
        Name      = [IO.Path]::GetFileName($Path)
        Rel       = Get-Rel $Path
        Raw       = $raw
        Text      = $text
        Starts    = (Get-LineStarts $text)
        Rules     = New-Object 'System.Collections.Generic.List[object]'
        Defines   = New-Object 'System.Collections.Generic.List[object]'
        Keyframes = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    }
    $depth = 0; $start = 0; $kind = ''; $header = ''; $headerAt = 0; $bodyAt = 0
    foreach ($token in [regex]::Matches($text, '[{};]')) {
        $i = $token.Index
        if ($token.Value -eq ';') {
            if ($depth -eq 0) {
                $statement = $text.Substring($start, $i - $start)
                $at = $start + ($statement.Length - $statement.TrimStart().Length)
                if ($statement.Trim() -match '^@define\s+([A-Za-z_]\w*)\s*:\s*([\s\S]*)$') {
                    $sheet.Defines.Add([pscustomobject]@{ Name = $Matches[1]; Value = $Matches[2].Trim(); At = $at })
                }
                elseif ($statement.Trim()) { Add-At error css.parse $sheet $at "text outside any rule: '$(Limit $statement)'" }
                $start = $i + 1
            }
            continue
        }
        if ($token.Value -eq '{') {
            if ($depth -eq 0) {
                $segment = $text.Substring($start, $i - $start)
                $header = $segment.Trim()
                $headerAt = $start + ($segment.Length - $segment.TrimStart().Length)
                $bodyAt = $i + 1
                if ($header -match '^@keyframes\s+[''"]?([\w-]+)') { $kind = 'keyframes'; [void] $sheet.Keyframes.Add($Matches[1]) }
                elseif ($header.StartsWith('@')) {
                    $kind = 'at'
                    Add-At error css.at-rule $sheet $headerAt "'$(($header -split '\s+')[0])' does not exist in Panorama, so CS2 ignores the block"
                }
                else {
                    $kind = 'rule'
                    if (-not $header) { Add-At error css.parse $sheet $i 'a rule with no selector' }
                }
            }
            elseif ($depth -eq 1 -and $kind -eq 'keyframes') { $bodyAt = $i + 1 }
            elseif ($kind -ne 'at') { Add-At error css.parse $sheet $i "a block inside a rule (a missing '}' above?)" }
            $depth++
            continue
        }
        if ($depth -eq 0) {
            Add-At error css.parse $sheet $i "a '}' with no '{' before it"
            $start = $i + 1
            continue
        }
        $depth--
        if ($kind -eq 'keyframes') {
            if ($depth -eq 1) { $sheet.Rules.Add((New-Rule $sheet '' $bodyAt $bodyAt $i $true)) }
            elseif ($depth -eq 0) { $kind = ''; $start = $i + 1 }
        }
        elseif ($depth -eq 0) {
            if ($kind -eq 'rule' -and $header) { $sheet.Rules.Add((New-Rule $sheet $header $headerAt $bodyAt $i $false)) }
            $kind = ''; $start = $i + 1
        }
    }
    if ($depth -gt 0) { Add-At error css.parse $sheet $headerAt 'a block opened here is never closed' }
    elseif ($text.Substring($start).Trim()) { Add-At error css.parse $sheet $start "text after the last rule: '$(Limit $text.Substring($start))'" }
    return $sheet
}

function Get-BorderSide([string] $Value) {
    # A shorthand's width and colour; rgba() is one token despite its spaces.
    $v = [regex]::Replace($Value, 'rgba?\s*\([^)]*\)', { param($m) $m.Value -replace '\s', '' })
    $side = @{ Width = $null; Colour = $null }
    foreach ($part in ($v.Trim() -split '\s+')) {
        if ($part -match '^\d+(\.\d+)?(px)?$') { $side.Width = [double] ($part -replace 'px$', '') }
        elseif ($part -ieq 'none') { $side.Width = 0 }
        elseif ($part -match '^(chud\w+|#[0-9a-fA-F]{3,8}|rgba?\(.*\)|transparent)$') { $side.Colour = $part }
    }
    return $side
}

function Test-BorderColours($Sheet, $Rule) {
    $order = 'top', 'right', 'bottom', 'left'
    $sides = @{}
    foreach ($name in $order) { $sides[$name] = @{ Width = $null; Colour = $null } }
    foreach ($d in $Rule.Decls) {
        if (-not $d.Prop.StartsWith('border')) { continue }
        if ($d.Prop -eq 'border') {
            $s = Get-BorderSide $d.Value
            foreach ($name in $order) { $sides[$name] = @{ Width = $s.Width; Colour = $s.Colour } }
        }
        elseif ($d.Prop -match '^border-(top|right|bottom|left)$') { $sides[$Matches[1]] = Get-BorderSide $d.Value }
        elseif ($d.Prop -match '^border-(top|right|bottom|left)-color$') { $sides[$Matches[1]].Colour = $d.Value }
        elseif ($d.Prop -match '^border-(top|right|bottom|left)-width$') { $sides[$Matches[1]].Width = [double] ($d.Value -replace 'px$', '') }
        elseif ($d.Prop -eq 'border-color' -or $d.Prop -eq 'border-width') {
            $parts = $d.Value.Trim() -split '\s+'
            $map = switch ($parts.Count) {
                1 { @($parts[0], $parts[0], $parts[0], $parts[0]) }
                2 { @($parts[0], $parts[1], $parts[0], $parts[1]) }
                3 { @($parts[0], $parts[1], $parts[2], $parts[1]) }
                default { @($parts[0], $parts[1], $parts[2], $parts[3]) }
            }
            for ($k = 0; $k -lt 4; $k++) {
                if ($d.Prop -eq 'border-color') { $sides[$order[$k]].Colour = $map[$k] }
                else { $sides[$order[$k]].Width = [double] ($map[$k] -replace 'px$', '') }
            }
        }
    }
    $colours = New-Object 'System.Collections.Generic.List[string]'
    foreach ($name in $order) {
        $c = $sides[$name].Colour
        if ($c -and $sides[$name].Width -ne 0 -and -not $colours.Contains($c)) { $colours.Add($c) }
    }
    if ($colours.Count -gt 1) {
        Add-At error css.border-colour $Sheet $Rule.At "'$(Limit $Rule.Header 50)' draws its sides in $($colours -join ' and '); CS2 draws every side in one colour (the top's), so a second colour needs a child panel"
    }
}

$Properties = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($line in [IO.File]::ReadAllLines((Join-Path $PSScriptRoot 'panorama-properties.txt'))) {
    $p = $line.Trim()
    if ($p -and -not $p.StartsWith('#')) { [void] $Properties.Add($p) }
}
$escaped = foreach ($p in $Properties) { [regex]::Escape($p) }
$UnknownProperty = New-Object regex ('[{;]\s*(?!(?:' + ($escaped -join '|') + ')\s*:)([A-Za-z-]+)\s*:'), 'IgnoreCase'

$PseudoClasses = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($p in 'hover', 'focus', 'active', 'disabled', 'enabled', 'inspect', 'selected', 'descendantfocus', 'parentdisabled',
               'layoutloading', 'layoutfailed', 'activationdisabled', 'not', 'first-child', 'last-child') { [void] $PseudoClasses.Add($p) }

$AnimationWords = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($w in 'none', 'linear', 'ease', 'ease-in', 'ease-out', 'ease-in-out', 'step-start', 'step-end', 'infinite', 'normal', 'reverse',
               'alternate', 'alternate-reverse', 'forwards', 'backwards', 'both', 'running', 'paused') { [void] $AnimationWords.Add($w) }

$GeneratedSheets = 'econ_thumbs.vcss', 'econ_stickers.vcss', 'locker_art.vcss'
$TokenSheet = 'customhud_tokens.vcss'
$ImageRef = 's2r://panorama/(images/custom_game/customhud/[^"''\s)]+)'

# The client's HUD layout validator, disassembled from client.dll (docs/PanoramaAuthoring.md).
$LayoutAllowlist = New-Object 'System.Collections.Generic.Dictionary[string,string[]]' ([StringComparer]::Ordinal)
$LayoutAllowlist['root']    = @()
$LayoutAllowlist['styles']  = @()
$LayoutAllowlist['include'] = @('src')
$LayoutAllowlist['Panel']   = @('id', 'class', 'hittest')
$LayoutAllowlist['Label']   = @('id', 'class', 'hittest', 'text')
$LayoutAllowlist['Image']   = @('id', 'class', 'hittest', 'src', 'languages')
$LayoutAllowlist['Button']  = @('id', 'class')

# One native scan per rule: walking every tag in PowerShell costs seconds on the generated layouts.
$LayoutChecks = New-Object 'System.Collections.Generic.List[object]'
$LayoutChecks.Add(@{
    Regex = [regex] ('<(?!(?:' + (@($LayoutAllowlist.Keys) -join '|') + ')[\s/>])([A-Za-z][\w-]*)')
    Say   = { param($m) "<$($m.Groups[1].Value)> is not an element the validator accepts" }
})
foreach ($tag in $LayoutAllowlist.Keys) {
    $allowed = $LayoutAllowlist[$tag]
    $other = if ($allowed.Count) { '(?!(?:' + ($allowed -join '|') + ')\s*=)' } else { '' }
    $LayoutChecks.Add(@{
        Regex = [regex] ('<' + $tag + '(?=[\s/>])(?:[^>"]|"[^"]*")*?\s' + $other + '([A-Za-z][\w-]*)\s*=')
        Say   = [scriptblock]::Create("param(`$m) ""<$tag `$(`$m.Groups[1].Value)=...>: $tag takes only $(if ($allowed.Count) { $allowed -join ', ' } else { 'no attributes' })""")
    })
}
$LayoutChecks.Add(@{
    Regex = [regex] '<include(?=[\s/>])(?:[^>"]|"[^"]*")*?\ssrc="(?![^"]*\.vcss_c")([^"]*)"'
    Say   = { param($m) "<include src=""$($m.Groups[1].Value)"">: a layout may reference .vcss resources only" }
})
function Test-ImageRefs([string] $Text, [string] $Rel, [int[]] $Starts) {
    foreach ($m in [regex]::Matches($Text, $ImageRef)) {
        $path = [regex]::Replace($m.Groups[1].Value, '_(png|tga|jpg|jpeg|webp)\.vtex$', '.$1') -replace '\.vsvg$', '.svg'
        if (-not (Test-Path -LiteralPath (Join-Path $Panorama ($path -replace '/', '\')))) {
            Add-Finding error css.image $Rel (Get-Line $Starts $m.Index) "'$($m.Value)' has no source at resources/panorama/$path"
        }
    }
}

# A width budget written as "3 x 146 + 2 x 6 = 450 of the list's 452" is checked: the sum, then the slack.
function Test-Sizing($Sheet) {
    foreach ($c in [regex]::Matches($Sheet.Raw, '/\*[\s\S]*?\*/')) {
        if ($c.Value.IndexOf('=') -lt 0) { continue }
        $comment = $c.Value -replace '\s+', ' '
        foreach ($m in [regex]::Matches($comment, '(?<![\d.])(\d+(?:\s*[x\u00D7+]\s*\d+)+)\s*=\s*(\d+)(?:\s*px)?(?:\s+of\s+(?:the\s+)?(?:[A-Za-z'']+\s+)?(\d+))?')) {
            $sum = 0
            foreach ($term in ($m.Groups[1].Value -split '\+')) {
                $product = 1
                foreach ($factor in ($term -split '[x\u00D7]')) { $product *= [int] $factor.Trim() }
                $sum += $product
            }
            $total = [int] $m.Groups[2].Value
            if ($sum -ne $total) { Add-At error css.sizing $Sheet $c.Index "'$($m.Value)': the terms add up to $sum, not $total"; continue }
            if (-not $m.Groups[3].Success) { continue }
            $room = [int] $m.Groups[3].Value
            if ($total -gt $room) { Add-At error css.sizing $Sheet $c.Index "'$($m.Value)': $total overflows $room" }
            elseif ($room - $total -lt 6) { Add-At warn css.sizing $Sheet $c.Index "'$($m.Value)': $($room - $total)px of slack where a fixed-column line needs 6px, ui-scale rounding included" }
        }
    }
}

$sheets = New-Object 'System.Collections.Generic.List[object]'
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Panorama 'styles') -Recurse -File -Filter *.vcss) {
    $rel = Get-Rel $file.FullName
    if ($rel -match '/v2/') { continue }   # generated copies: gen.v2-sheets holds them to their sources
    if ($GeneratedSheets -contains $file.Name) {
        $text = Remove-Comments ([IO.File]::ReadAllText($file.FullName))
        if (([regex]::Matches($text, '\{')).Count -ne ([regex]::Matches($text, '\}')).Count) {
            Add-Finding error css.parse $rel 0 "unbalanced braces: rerun the script that generates $($file.Name)"
        }
        $unknown = $UnknownProperty.Match($text)
        if ($unknown.Success) { Add-Finding error css.property $rel 0 "'$($unknown.Groups[1].Value)' is not a Panorama property" }
        continue
    }
    $sheets.Add((Read-Sheet $file.FullName))
}

$tokenDefines = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
foreach ($sheet in $sheets) { if ($sheet.Name -eq $TokenSheet) { foreach ($d in $sheet.Defines) { [void] $tokenDefines.Add($d.Name) } } }

$Icons = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
$defineAt = @{}
foreach ($sheet in $sheets) {
    $isTokens = $sheet.Name -eq $TokenSheet
    $defined = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($name in $tokenDefines) { [void] $defined.Add($name) }
    foreach ($d in $sheet.Defines) {
        [void] $defined.Add($d.Name)
        if (-not $defineAt.ContainsKey($d.Name)) { $defineAt[$d.Name] = @($sheet, $d.At) }
    }
    foreach ($m in [regex]::Matches($sheet.Text, '\.ico-([a-z0-9][a-z0-9-]*)')) { [void] $Icons.Add($m.Groups[1].Value) }
    Test-ImageRefs $sheet.Text $sheet.Rel $sheet.Starts
    $unquoted = [regex]::Replace($sheet.Text, '"[^"\n]*"|''[^''\n]*''', { param($m) ' ' * $m.Length })
    $slash = [regex]::Match($unquoted, '//')
    if ($slash.Success) { Add-At error css.comment $sheet $slash.Index 'a // comment: write it as /* */' }
    Test-Sizing $sheet

    $values = New-Object 'System.Collections.Generic.List[object]'
    foreach ($d in $sheet.Defines) { $values.Add($d) }
    foreach ($rule in $sheet.Rules) {
        if (-not $rule.Keyframe) {
            $selectors = New-Object 'System.Collections.Generic.List[string]'
            foreach ($s in $rule.Header.Split(',')) { $s = $s.Trim(); if ($s) { $selectors.Add($s) } }
            $length = $rule.Header.Length
            if ($length -gt 1600) { $length = ($rule.Header -replace '\s+', ' ').Trim().Length }
            if ($selectors.Count -gt 30 -or $length -gt 1600) {
                Add-At error css.selector-limit $sheet $rule.At "$($selectors.Count) selectors, $length characters in one rule: CS2 drops the whole sheet past about 30 or 1600; split it into rules with the same body"
            }
            if ($rule.Header.IndexOf(':') -ge 0 -or $rule.Header.IndexOf('[') -ge 0) {
                foreach ($selector in $selectors) {
                    if ($selector.Contains('::')) { Add-At error css.selector $sheet $rule.At "'$(Limit $selector 50)': pseudo-elements do not exist in Panorama"; continue }
                    if ($selector.Contains('[')) { Add-At error css.selector $sheet $rule.At "'$(Limit $selector 50)': attribute selectors do not exist in Panorama"; continue }
                    foreach ($m in [regex]::Matches($selector, ':([A-Za-z][\w-]*)')) {
                        if (-not $PseudoClasses.Contains($m.Groups[1].Value)) {
                            Add-At error css.selector $sheet $rule.At "':$($m.Groups[1].Value)' is not a Panorama pseudo-class (verify it in the F6 debugger before adding it to this script)"
                        }
                    }
                }
            }
            if ($rule.Border) { Test-BorderColours $sheet $rule }
            if ($rule.Shadow) {
                $thin = $null; $shadow = $false
                foreach ($d in $rule.Decls) {
                    if ($d.Prop -eq 'height' -and $d.Value -match '^(\d+(\.\d+)?)px$' -and [double] $Matches[1] -le 4) { $thin = $d.Value }
                    elseif ($d.Prop -eq 'box-shadow' -and $d.Value -ne 'none') { $shadow = $true }
                }
                if ($thin -and $shadow) { Add-At warn css.thin-shadow $sheet $rule.At "'$(Limit $rule.Header 50)' is a bar $thin tall with a box-shadow, which draws as a thick halo in game" }
            }
        }
        foreach ($d in $rule.Decls) {
            $prop = $d.Prop
            $value = $d.Value
            if (-not $Properties.Contains($prop)) { Add-At error css.property $sheet $d.At "'$prop' is not a Panorama property, so CS2 drops it silently" }
            if ($value.IndexOf('(') -ge 0) {
                if ($value -match '\b(calc|var|hsla?)\s*\(') { Add-At error css.value $sheet $d.At "$($Matches[1])() does not exist in Panorama" }
                if ($prop.StartsWith('margin') -and $value.Contains('fill-parent-flow')) { Add-At error css.value $sheet $d.At 'fill-parent-flow() is not valid in a margin' }
            }
            if ($prop -eq 'animation' -or $prop -eq 'transition') { Add-At error css.shorthand $sheet $d.At "write '${prop}:' as its $prop-* longhands" }
            if ($prop -eq 'animation-name' -or $prop -eq 'animation') {
                foreach ($word in ($value -split '[\s,]+')) {
                    $name = $word.Trim('''', '"')
                    if (-not $name -or $name -match '^[\d.]' -or $AnimationWords.Contains($name) -or $name.StartsWith('cubic-bezier')) { continue }
                    if (-not $sheet.Keyframes.Contains($name)) { Add-At error css.keyframes $sheet $d.At "animation '$name' has no @keyframes in this sheet" }
                }
            }
            if ($value.IndexOf('#') -ge 0 -or $value.IndexOf('rgb') -ge 0 -or $value.IndexOf('chud') -ge 0) { $values.Add($d) }
        }
    }
    foreach ($v in $values) {
        $value = $v.Value
        if ($value.Contains('url(')) { $value = $value -replace 'url\([^)]*\)', 'url()' }
        if (-not $isTokens -and $value -match '(#[0-9a-fA-F]{3,8}\b|\brgba?\s*\()') {
            Add-At error css.colour-literal $sheet $v.At "colour literal '$($Matches[1])': name a token from customhud_tokens.vcss instead"
        }
        if ($value.IndexOf('chud') -ge 0) {
            foreach ($m in [regex]::Matches($value, '\bchud[A-Z]\w*')) {
                if (-not $defined.Contains($m.Value)) { Add-At error css.token $sheet $v.At "'$($m.Value)' is not defined in $TokenSheet or this sheet" }
            }
        }
    }
}
# Sorted ordinally, a name and every name it prefixes sit side by side.
$names = [string[]] @($defineAt.Keys)
[Array]::Sort($names, [StringComparer]::Ordinal)
for ($k = 0; $k -lt $names.Count - 1; $k++) {
    if ($names[$k + 1].StartsWith($names[$k], [StringComparison]::Ordinal)) {
        $where = $defineAt[$names[$k]]
        Add-At error css.define-prefix $where[0] $where[1] "'$($names[$k])' is a prefix of '$($names[$k + 1])': Panorama substitutes defines as text, so the longer name breaks"
    }
}

Write-Stage 'stylesheets'

# ---------------------------------------------------------------- layouts

foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Panorama 'layout') -Recurse -File -Filter *.vxml) {
    $rel = Get-Rel $file.FullName
    $raw = [IO.File]::ReadAllText($file.FullName)
    $starts = Get-LineStarts $raw
    try { [void] ([xml] $raw) }
    catch { Add-Finding error vxml.xml $rel 0 (Limit $_.Exception.Message 160) }

    $firstSheet = $null
    foreach ($m in [regex]::Matches($raw, '<include\s+src="([^"]+)"')) {
        $src = $m.Groups[1].Value
        $line = Get-Line $starts $m.Index
        if ($src -match 'csgostyles') { Add-Finding error vxml.csgostyles $rel $line "includes $src" }
        if ($src -notmatch '^s2r://panorama/(\w+/custom_game/customhud/.+)_c$') { continue }
        $source = Join-Path $Panorama ($Matches[1] -replace '/', '\')
        if (-not (Test-Path -LiteralPath $source)) { Add-Finding error vxml.include $rel $line "$src has no source at resources/panorama/$($Matches[1])" }
        if ($src.EndsWith('.vcss_c') -and -not $firstSheet) {
            $firstSheet = $src
            if (-not $src.EndsWith('/customhud_tokens.vcss_c')) { Add-Finding error vxml.tokens-first $rel $line "the first sheet is $src; a @define is visible only to sheets included after it" }
        }
    }

    $ids = @{}
    foreach ($m in [regex]::Matches($raw, '\sid="([^"]+)"')) {
        if ($ids.ContainsKey($m.Groups[1].Value)) { $ids[$m.Groups[1].Value]++ } else { $ids[$m.Groups[1].Value] = 1 }
    }
    $dupes = @($ids.Keys | Where-Object { $ids[$_] -gt 1 } | Sort-Object)
    if ($dupes.Count) { Add-Finding error vxml.duplicate-id $rel 0 "$($dupes.Count) ids name more than one panel: $((@($dupes | Select-Object -First 5)) -join ', ')" }

    $markup = [regex]::Replace($raw, '<!--[\s\S]*?-->', { param($m) [regex]::Replace($m.Value, '[^\n]', ' ') })
    foreach ($check in $LayoutChecks) {
        $hits = $check.Regex.Matches($markup)
        if (-not $hits.Count) { continue }
        $what = & $check.Say $hits[0]
        if ($hits.Count -gt 1) { $what += " ($($hits.Count) times)" }
        Add-Finding error vxml.allowlist $rel (Get-Line $starts $hits[0].Index) $what
    }
    Test-ImageRefs $raw $rel $starts
}

Write-Stage 'layouts'

# ---------------------------------------------------------------- generated files

function Compare-Generated([string] $Produced, [string] $Committed, [string] $Check, [string] $Generator, [bool] $Orphans) {
    foreach ($f in Get-ChildItem -LiteralPath $Produced -File) {
        $mine = Join-Path $Committed $f.Name
        if (-not (Test-Path -LiteralPath $mine)) { Add-Finding error $Check (Get-Rel $mine) 0 "missing: rerun $Generator"; continue }
        if ([IO.File]::ReadAllText($f.FullName) -cne [IO.File]::ReadAllText($mine)) {
            Add-Finding error $Check (Get-Rel $mine) 0 "differs from what $Generator writes. Never edit it by hand: change the source or the generator, then rerun $Generator"
        }
    }
    if ($Orphans) {
        foreach ($f in Get-ChildItem -LiteralPath $Committed -File) {
            if (-not (Test-Path -LiteralPath (Join-Path $Produced $f.Name))) { Add-Finding error $Check (Get-Rel $f.FullName) 0 "no source sheet generates it: delete it" }
        }
    }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('customhud-audit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
try {
    $copies = Join-Path $work 'styles'
    New-Item -ItemType Directory -Path $copies | Out-Null
    Copy-Item -Path (Join-Path $Styles '*.vcss') -Destination $copies
    try {
        & (Join-Path $PSScriptRoot 'generate-v2-sheets.ps1') -StylesDir $copies *> $null
        Compare-Generated (Join-Path $copies 'v2') (Join-Path $Styles 'v2') 'gen.v2-sheets' 'scripts/generate-v2-sheets.ps1' $true
    }
    catch { Add-Finding error gen.v2-sheets 'scripts/generate-v2-sheets.ps1' 0 "the generator failed: $(Limit $_.Exception.Message 160)" }

    foreach ($generator in 'generate-locker-layout.ps1', 'generate-list-layout.ps1', 'generate-skins-layout.ps1') {
        $out = Join-Path $work ([IO.Path]::GetFileNameWithoutExtension($generator))
        New-Item -ItemType Directory -Path $out | Out-Null
        try {
            & (Join-Path $PSScriptRoot $generator) -OutputDir $out *> $null
            Compare-Generated $out $Layouts 'gen.layouts' "scripts/$generator" $false
        }
        catch { Add-Finding error gen.layouts "scripts/$generator" 0 "the generator failed: $(Limit $_.Exception.Message 160)" }
    }
}
finally { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue }

Write-Stage 'generators'

# ---------------------------------------------------------------- translations

function Read-Jsonc([string] $Path) {
    $raw = [IO.File]::ReadAllText($Path)
    $rel = Get-Rel $Path
    $starts = Get-LineStarts $raw
    $keys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($m in [regex]::Matches($raw, '(?m)^\s*"((?:[^"\\]|\\.)+)"\s*:')) {
        if (-not $keys.Add($m.Groups[1].Value)) { Add-Finding error i18n.duplicate $rel (Get-Line $starts $m.Index) "'$($m.Groups[1].Value)' is defined twice" }
    }
    $json = [regex]::Replace($raw, '("(?:[^"\\]|\\.)*")|//[^\n]*|/\*[\s\S]*?\*/', { param($m) $m.Groups[1].Value })
    $json = [regex]::Replace($json, ',(\s*[}\]])', '$1')
    $values = @{}
    try {
        $object = ConvertFrom-Json $json
        foreach ($p in $object.PSObject.Properties) { $values[$p.Name] = [string] $p.Value }
    }
    catch { Add-Finding error i18n.parse $rel 0 (Limit $_.Exception.Message 160) }
    return [pscustomobject]@{ Name = [IO.Path]::GetFileNameWithoutExtension($Path); Rel = $rel; Keys = $keys; Values = $values }
}

function Get-Placeholders([string] $Text) {
    $set = New-Object 'System.Collections.Generic.SortedSet[int]'
    foreach ($m in [regex]::Matches($Text, '\{(\d+)(?:[,:][^}]*)?\}')) { [void] $set.Add([int] $m.Groups[1].Value) }
    return , $set
}

$languages = @{}
foreach ($file in Get-ChildItem -LiteralPath $Translations -File -Filter *.jsonc) { $languages[$file.BaseName] = Read-Jsonc $file.FullName }
$en = $languages['en']
if (-not $en) { Add-Finding error i18n.parity 'resources/translations' 0 'en.jsonc is missing' }
elseif (-not $languages['lt']) { Add-Finding error i18n.parity 'resources/translations' 0 'lt.jsonc is missing' }
if ($en) {
    foreach ($name in $languages.Keys) {
        if ($name -eq 'en') { continue }
        $lang = $languages[$name]
        $orphans = @($lang.Keys | Where-Object { -not $en.Keys.Contains($_) })
        foreach ($k in $orphans) { Add-Finding error i18n.orphan $lang.Rel 0 "'$k' has no English original" }
        $missing = @($en.Keys | Where-Object { -not $lang.Keys.Contains($_) })
        if ($missing.Count) {
            if ($name -eq 'lt') { foreach ($k in $missing) { Add-Finding error i18n.parity $lang.Rel 0 "'$k' is in en.jsonc but not lt.jsonc" } }
            else { Add-Finding note i18n.parity $lang.Rel 0 "$($missing.Count) keys fall back to English" }
        }
        foreach ($k in $lang.Keys) {
            if (-not $en.Values.ContainsKey($k) -or -not $lang.Values.ContainsKey($k)) { continue }
            $want = Get-Placeholders $en.Values[$k]
            $have = Get-Placeholders $lang.Values[$k]
            foreach ($n in $have) { if (-not $want.Contains($n)) { Add-Finding error i18n.placeholders $lang.Rel 0 "'$k' uses {$n}, which the English string never receives" } }
            foreach ($n in $want) { if (-not $have.Contains($n)) { Add-Finding warn i18n.placeholders $lang.Rel 0 "'$k' drops {$n}" } }
        }
    }
}

Write-Stage 'translations'

# ---------------------------------------------------------------- C# against the sheets and strings

$CsTokens = '@"(?:[^"]|"")*"|\$?"(?:[^"\\\r\n]|\\.)*"|''(?:[^''\\\r\n]|\\.)*''|//[^\r\n]*|/\*[\s\S]*?\*/'
$prefixes = @{}
$sources = @()
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $Repo 'src') -Recurse -File -Filter *.cs) {
    $code = [regex]::Replace([IO.File]::ReadAllText($file.FullName), $CsTokens, {
        param($m)
        if ($m.Value.StartsWith('//') -or $m.Value.StartsWith('/*')) { return [regex]::Replace($m.Value, '[^\n]', ' ') }
        return $m.Value
    })
    $class = [regex]::Match($code, '\bclass\s+(\w+)').Groups[1].Value
    $t = [regex]::Match($code, 'string\s+T\(\s*PageDraw\s+\w+\s*,\s*string\s+\w+[^)]*\)\s*=>\s*\w+\.Text\(\s*"(customhud\.[a-z0-9_.]+\.)"\s*\+')
    if ($class -and $t.Success) { $prefixes[$class] = $t.Groups[1].Value }
    $sources += [pscustomobject]@{ Rel = Get-Rel $file.FullName; Class = $class; Code = $code; Starts = (Get-LineStarts $code) }
}

foreach ($src in $sources) {
    foreach ($m in [regex]::Matches($src.Code, '(?<![\w-])(?:glyph|tagglyph|vglyph)=([A-Za-z0-9][\w-]*)')) {
        if (-not $Icons.Contains($m.Groups[1].Value)) {
            Add-Finding error cs.glyph $src.Rel (Get-Line $src.Starts $m.Index) "glyph '$($m.Groups[1].Value)' has no .ico-$($m.Groups[1].Value) class, so it draws nothing"
        }
    }
    if (-not $en) { continue }
    foreach ($m in [regex]::Matches($src.Code, '"(customhud\.[a-z0-9_]+(?:\.[a-z0-9_]+)+)"')) {
        if (-not $en.Keys.Contains($m.Groups[1].Value)) { Add-Finding error i18n.key $src.Rel (Get-Line $src.Starts $m.Index) "'$($m.Groups[1].Value)' is not in en.jsonc" }
    }
    if ($src.Class -and $prefixes.ContainsKey($src.Class)) {
        foreach ($m in [regex]::Matches($src.Code, '\bT\(\s*\w+\s*,\s*"([a-z0-9_]+(?:\.[a-z0-9_]+)*)"\s*[,)]')) {
            $key = $prefixes[$src.Class] + $m.Groups[1].Value
            if (-not $en.Keys.Contains($key)) { Add-Finding error i18n.key $src.Rel (Get-Line $src.Starts $m.Index) "'$key' is not in en.jsonc" }
        }
    }
}

Write-Stage 'csharp'

# ---------------------------------------------------------------- build, compile, docs

$fingerprints = Get-Fingerprints
$previous = $null
if (Test-Path -LiteralPath $ReceiptPath) { try { $previous = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json } catch { $previous = $null } }

function Invoke-Native([scriptblock] $Command) {
    # Native stderr under 'Stop' throws on Windows PowerShell 5.1.
    $ErrorActionPreference = 'Continue'
    $global:LASTEXITCODE = 0
    $output = @(& $Command 2>&1 | ForEach-Object { "$_" })
    return [pscustomobject]@{ Code = $LASTEXITCODE; Output = $output }
}

if (-not $Quick) {
    $staticErrors = @($Findings | Where-Object { $_.Level -eq 'error' }).Count
    $built = $previous -and $previous.full -and $previous.full.cs -eq $fingerprints['cs']
    if ($staticErrors) {
        Add-Finding note build.publish '' 0 'build and compile skipped until the static checks pass'
    }
    elseif ($built -and -not $Force) {
        Add-Finding note build.publish '' 0 'C# unchanged since the last full pass, so the build was skipped (-Force rebuilds)'
    }
    else {
        Push-Location $Repo
        try {
            $r = Invoke-Native { dotnet build CustomHUD.Contract/CustomHUD.Contract.csproj -c Release --nologo -v q }
            if ($r.Code -ne 0) { Add-Finding error build.contract 'CustomHUD.Contract' 0 ("build failed: " + (Limit (($r.Output | Where-Object { $_ -match 'error' } | Select-Object -First 3) -join ' | ') 300)) }
            else {
                $r = Invoke-Native { dotnet publish CustomHUD.csproj -c Release --nologo }
                $errors = @($r.Output | Where-Object { $_ -match ': error [A-Z]+\d+' } | Sort-Object -Unique)
                $warnings = @($r.Output | Where-Object { $_ -match ': warning ([A-Z]+\d+)' } | Sort-Object -Unique)
                foreach ($e in ($errors | Select-Object -First 10)) { Add-Finding error build.publish '' 0 (Limit $e 300) }
                if ($r.Code -ne 0 -and -not $errors.Count) { Add-Finding error build.publish '' 0 ("publish failed: " + (Limit (($r.Output | Select-Object -Last 3) -join ' | ') 300)) }
                foreach ($w in ($warnings | Select-Object -First 10)) {
                    $level = if ($w -match ': warning NU1\d{3}') { 'warn' } else { 'error' }
                    Add-Finding $level build.publish '' 0 (Limit $w 300)
                }
                if ($r.Code -eq 0) {
                    $r = Invoke-Native { dotnet run --project tools/SoftDependencyCheck -c Release -- build/publish/CustomHUD/CustomHUD.dll }
                    if ($r.Code -ne 0) { Add-Finding error build.soft-deps 'tools/SoftDependencyCheck' 0 (Limit (($r.Output | Select-Object -Last 4) -join ' | ') 400) }
                }
            }
        }
        finally { Pop-Location }
    }

    # Combine, not Join-Path: Join-Path throws on a drive this machine lacks, and CS2 may not be here.
    $compiler = [IO.Path]::Combine($Cs2Root, 'game\bin\win64\resourcecompiler.exe')
    $addon = [IO.Path]::Combine($Cs2Root, 'content\csgo_addons\customhud')
    if (-not $staticErrors -and (-not (Test-Path -LiteralPath $compiler) -or -not (Test-Path -LiteralPath $addon))) {
        Add-Finding note panorama.compile '' 0 'CS2 or its customhud addon is not installed here, so nothing was compiled'
    }
    elseif (-not $staticErrors) {
        $global:LASTEXITCODE = 0
        $output = @(& (Join-Path $PSScriptRoot 'watch-panorama.ps1') -Cs2Root $Cs2Root -Once -Changed *>&1 | ForEach-Object { "$_" })
        $failed = @($output | Where-Object { $_ -match '^FAIL\s' })
        foreach ($f in $failed) { Add-Finding error panorama.compile '' 0 (Limit $f 300) }
        $summary = @($output | Where-Object { $_ -match '^compiled \d+, failed \d+|^nothing changed' }) | Select-Object -Last 1
        if (-not $failed.Count -and $summary) { Add-Finding note panorama.compile '' 0 $summary }
        foreach ($w in @($output | Where-Object { $_ -match '^warn\s' })) { Add-Finding warn panorama.compile '' 0 (Limit $w 300) }
    }

    $wiki = Join-Path (Split-Path -Parent $Repo) 'CustomHUD.wiki'
    if (Test-Path -LiteralPath $wiki) {
        $global:LASTEXITCODE = 0
        $output = @(& (Join-Path $Repo 'docs\verify-wiki.ps1') *>&1 | ForEach-Object { "$_" })
        if ($LASTEXITCODE -ne 0) { Add-Finding error docs.wiki 'docs/verify-wiki.ps1' 0 (Limit (($output | Select-Object -Last 6) -join ' | ') 500) }
    }
}

# ---------------------------------------------------------------- report

$errorCount = @($Findings | Where-Object { $_.Level -eq 'error' }).Count
$warnCount = @($Findings | Where-Object { $_.Level -eq 'warn' }).Count
$pass = $errorCount -eq 0
$tier = if ($Quick) { 'quick' } else { 'full' }

if ($pass) {
    $receipt = [ordered]@{ schema = 1; quick = $null; full = $null }
    if ($previous) { $receipt.quick = $previous.quick; $receipt.full = $previous.full }
    $stamp = [ordered]@{ time = (Get-Date).ToUniversalTime().ToString('o') }
    foreach ($k in $fingerprints.Keys) { $stamp[$k] = $fingerprints[$k] }
    $receipt.quick = $stamp
    if (-not $Quick) { $receipt.full = $stamp }
    Write-JsonFile $ReceiptPath $receipt
}

$result = [ordered]@{
    pass     = $pass
    tier     = $tier
    errors   = $errorCount
    warnings = $warnCount
    seconds  = [Math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    findings = $Findings.ToArray()
    rules    = [ordered]@{}
}
foreach ($check in @($Findings | Where-Object { $_.Level -ne 'note' } | ForEach-Object { $_.Check } | Sort-Object -Unique)) { $result.rules[$check] = $Rules[$check] }
if ($JsonPath) { Write-JsonFile $JsonPath $result }

$colour = @{ error = 'Red'; warn = 'Yellow'; note = 'DarkGray' }
foreach ($f in $Findings | Sort-Object @{ e = { @{ error = 0; warn = 1; note = 2 }[$_.Level] } }, Check, File, Line) {
    $where = if ($f.File) { if ($f.Line) { "$($f.File):$($f.Line)" } else { $f.File } } else { '' }
    Write-Host ('{0,-5} {1,-18} {2} {3}' -f $f.Level.ToUpperInvariant(), $f.Check, $where, $f.Message).TrimEnd() -ForegroundColor $colour[$f.Level]
}
if ($result.rules.Count) {
    Write-Host ''
    foreach ($check in $result.rules.Keys) { Write-Host ('{0,-18} {1}' -f $check, $result.rules[$check]) -ForegroundColor DarkGray }
}
Write-Host ''
$verdict = if ($pass) { 'PASS' } else { 'FAIL' }
Write-Host ("{0} ({1}): {2} errors, {3} warnings, {4}s" -f $verdict, $tier, $errorCount, $warnCount, $result.seconds) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
if (-not $pass) { exit 1 }
