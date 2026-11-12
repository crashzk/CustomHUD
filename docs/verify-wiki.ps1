# Verifies the wiki against the source it documents.
#
# Five checks, adapted from Ranks/docs/verify-wiki.ps1:
#   1. Links      - every internal wiki link resolves to a page that exists
#   2. Anchors    - every "#section" target is a heading that exists on that page
#   3. Keys       - every JSONC key shown in the wiki is declared in the source
#   4. Navigation - README's wiki links resolve, and Home reaches every page
#   5. Defaults   - a Setting/Field table's Default agrees with the property initialiser
#
# Run from the repo root:  pwsh -File docs/verify-wiki.ps1
# Point -WikiPath at the wiki clone if it is not the sibling default.

[CmdletBinding()]
param(
    [string]$RepoPath = (Split-Path $PSScriptRoot -Parent),
    [string]$WikiPath = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'CustomHUD.wiki')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $WikiPath)) {
    Write-Host "Wiki not found at $WikiPath — clone it or pass -WikiPath." -ForegroundColor Yellow
    exit 2
}

$readme = Join-Path $RepoPath 'README.md'
$pages = Get-ChildItem $WikiPath -Filter *.md | ForEach-Object { $_.BaseName }
$failures = @()

function Add-Failure([string]$check, [string]$detail) {
    $script:failures += [pscustomobject]@{ Check = $check; Detail = $detail }
}

# GitHub's heading -> anchor slug.
function ConvertTo-Anchor([string]$heading) {
    $h = [regex]::Replace($heading, '`|\*|_|<[^>]+>', '')
    $h = $h.Trim().ToLowerInvariant()
    $h = [regex]::Replace($h, '[^\w\s-]', '')
    return ($h -replace '\s', '-')
}

# Fenced code is not prose: a "# comment" inside it is not a heading, and a link inside it is not a link.
function Remove-Fences([string]$text) { [regex]::Replace($text, '(?s)```.*?```', '') }

# ---- 1 + 2: links and anchors ------------------------------------------------

$anchors = @{}
foreach ($f in Get-ChildItem $WikiPath -Filter *.md) {
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($line in (Remove-Fences (Get-Content $f.FullName -Raw)) -split "`r?`n") {
        if ($line -match '^#{1,6}\s+(.*)$') { [void]$set.Add((ConvertTo-Anchor $Matches[1])) }
    }
    $anchors[$f.BaseName] = $set
}

foreach ($f in Get-ChildItem $WikiPath -Filter *.md) {
    $text = Remove-Fences (Get-Content $f.FullName -Raw)
    foreach ($m in [regex]::Matches($text, '\]\(([^)\s]+)\)')) {
        $t = $m.Groups[1].Value
        if ($t -match '^(https?:|mailto:)') { continue }

        $parts = $t -split '#', 2
        $page = if ($parts[0] -eq '') { $f.BaseName } else { $parts[0] }

        # Images are committed beside the pages, so check the file rather than the page list.
        if ($page -match '\.(png|jpg|jpeg|gif|svg|webp)$') {
            if (-not (Test-Path (Join-Path $WikiPath $page))) {
                Add-Failure 'Assets' "$($f.Name) -> $t  (image not committed to the wiki)"
            }
            continue
        }
        if ($page -match '\.md$') {
            Add-Failure 'Links' "$($f.Name) -> $t  (file-style link; wiki links are bare page names)"
            continue
        }
        if ($pages -notcontains $page) {
            Add-Failure 'Links' "$($f.Name) -> $t  (no such page)"
            continue
        }
        if ($parts.Count -eq 2 -and -not $anchors[$page].Contains($parts[1])) {
            Add-Failure 'Anchors' "$($f.Name) -> $t  (no such heading)"
        }
    }
}

# ---- 3: JSONC keys exist in the source ---------------------------------------
#
# Keys the operator names (eyebrow examples, a layout of their own) and keys of other plugins' files
# (SwiftlyS2's menus.jsonc) are exempt. Translation keys are checked against the shipped en.jsonc.

$exempt = @('Menus', 'Renderer', 'customhud.eyebrow.default', 'customhud.eyebrow.missions', 'customhud.eyebrow.arenas', 'my-list',
            'menu_list_myserver', 'hud_text_myserver')

$declared = New-Object System.Collections.Generic.HashSet[string]
foreach ($f in Get-ChildItem (Join-Path $RepoPath 'src'), (Join-Path $RepoPath 'CustomHUD.Contract') -Recurse -Filter *.cs -File |
                Where-Object { $_.FullName -notmatch '\\(bin|obj|build)\\' }) {
    $t = Get-Content $f.FullName -Raw
    foreach ($m in [regex]::Matches($t, '(?m)^\s*(?:public|internal)\s+[\w\<\>\?\[\],\s\.]+?\s+(\w+)\s*\{\s*get')) {
        [void]$declared.Add($m.Groups[1].Value)
    }
    foreach ($m in [regex]::Matches($t, '"([A-Za-z_][\w\.\-\*]*)"')) {
        [void]$declared.Add($m.Groups[1].Value)
    }
}
$enJsonc = Join-Path $RepoPath 'resources\translations\en.jsonc'
foreach ($m in [regex]::Matches((Get-Content $enJsonc -Raw), '(?m)^\s*"(customhud\.[^"]+)"\s*:')) {
    [void]$declared.Add($m.Groups[1].Value)
}

$keysChecked = 0
foreach ($f in Get-ChildItem $WikiPath -Filter *.md) {
    $text = Get-Content $f.FullName -Raw
    foreach ($blk in [regex]::Matches($text, '(?s)```jsonc\r?\n(.*?)```')) {
        foreach ($line in ($blk.Groups[1].Value -split "`r?`n")) {
            $l = $line.Trim()
            if ($l -eq '' -or $l -match '^//') { continue }
            foreach ($m in [regex]::Matches($l, '"([^"]+)"\s*:')) {
                $key = $m.Groups[1].Value
                $keysChecked++
                if ($exempt -notcontains $key -and -not $declared.Contains($key)) {
                    Add-Failure 'Keys' "$($f.Name): `"$key`" is not declared anywhere in the source"
                }
            }
        }
    }
}

# ---- 4: README and Home navigation -------------------------------------------

$readmeText = Get-Content $readme -Raw
$readmeLinks = 0
foreach ($m in [regex]::Matches($readmeText, '\]\(\.\./\.\./wiki/([^)\s#]+)')) {
    $readmeLinks++
    if ($pages -notcontains $m.Groups[1].Value) {
        Add-Failure 'Navigation' "README -> wiki/$($m.Groups[1].Value)  (no such page)"
    }
}
foreach ($m in [regex]::Matches($readmeText, '\]\((docs/[^)\s]+|src/[^)\s]+)\)')) {
    Add-Failure 'Navigation' "README -> $($m.Groups[1].Value)  (repo path; content lives in the wiki)"
}

$homeText = Get-Content (Join-Path $WikiPath 'Home.md') -Raw
$linked = [regex]::Matches($homeText, '\]\(([^)\s#]+)\)') |
          ForEach-Object { $_.Groups[1].Value } |
          Where-Object { $_ -notmatch '^https?:' }
foreach ($p in $pages) {
    if ($p -ne 'Home' -and $linked -notcontains $p) {
        Add-Failure 'Navigation' "Home does not link to $p, so it is unreachable"
    }
}

$expected = ([regex]::Replace($homeText, '\]\((?!https?:)([A-Z][\w\-]*)', '](../../wiki/$1')).Replace("`r`n", "`n").Trim()
if ($readmeText.Replace("`r`n", "`n").Trim() -ne $expected) {
    Add-Failure 'Navigation' "README is not Home with its links rewritten to ../../wiki/"
}

# ---- 5: documented defaults match the source ---------------------------------
#
# CustomHUD's config classes share property names with different defaults (File, DurationSeconds,
# LabelVar ...), so defaults are keyed by class, and each settings table is mapped to the class it
# documents by the heading above it. A table no map entry names is checked against every class.

$configFiles = Get-ChildItem (Join-Path $RepoPath 'src\Config') -Filter *.cs -File | ForEach-Object { $_.FullName }

$sourceDefault = @{}
$consts = @{}
foreach ($f in $configFiles) {
    foreach ($m in [regex]::Matches((Get-Content $f -Raw), 'const\s+string\s+(\w+)\s*=\s*("[^"]*")\s*;')) {
        $consts[$m.Groups[1].Value] = $m.Groups[2].Value
    }
}
foreach ($f in $configFiles) {
    $class = ''
    foreach ($line in Get-Content $f) {
        if ($line -match '^\s*public\s+(?:sealed\s+|static\s+)*class\s+(\w+)') { $class = $Matches[1]; continue }
        if (-not $class) { continue }
        if ($line -match '^\s*public\s+[\w\<\>\?\[\],\s\.]+?\s+(\w+)\s*\{\s*get;\s*(?:set|init);\s*\}\s*=\s*(.+?);') {
            $name = $Matches[1]
            $v = $Matches[2].Trim()
            if ($v -match '^(new\b|\[)' -or $v -match '\(') { continue }
            if ($consts.ContainsKey($v)) { $v = $consts[$v] }
            elseif ($v -match '^\w+\.(\w+)$' -and $consts.ContainsKey($Matches[1])) { $v = $consts[$Matches[1]] }
            if ($v -eq 'string.Empty') { $v = '""' }
            $sourceDefault["$class|$name"] = $v
        }
    }
}

$tableOwner = @{
    'Configuration|CustomHUD'                 = 'CustomHudConfig'
    'Configuration|Settings you add yourself' = 'CustomHudConfig'
    'Configuration|Messages'                  = 'MessageChoices'
    'Configuration|Panels'                    = 'PanelChoices'
}

function Normalise([string]$v) {
    if ($null -eq $v) { return '' }
    $v = ($v.Trim() -replace '`', '' -replace '"', '' -replace "'", '').Trim()
    if ($v -match '^-?\d+(\.\d+)?[fdmFDM]$') { $v = $v.Substring(0, $v.Length - 1) }
    return $v.ToLowerInvariant()
}

$defaultsChecked = 0
foreach ($f in Get-ChildItem $WikiPath -Filter *.md) {
    $lines = Get-Content $f.FullName
    $heading = ''
    for ($i = 0; $i -lt $lines.Count - 1; $i++) {
        if ($lines[$i] -match '^#{2,3}\s+(.*)$') { $heading = $Matches[1].Trim(); continue }
        if ($lines[$i] -notmatch '^\|' -or $lines[$i + 1] -notmatch '^\|[\s\-:|]+\|$') { continue }

        $cols = ($lines[$i].Trim('|') -split '\|') | ForEach-Object { $_.Trim() }
        $keyIdx = [array]::FindIndex($cols, [Predicate[string]] { param($c) $c -in @('Setting', 'Field') })
        $defIdx = [array]::FindIndex($cols, [Predicate[string]] { param($c) $c -eq 'Default' })
        if ($keyIdx -lt 0 -or $defIdx -lt 0) { continue }

        $owner = $tableOwner["$($f.BaseName)|$heading"]

        for ($j = $i + 2; $j -lt $lines.Count -and $lines[$j] -match '^\|'; $j++) {
            $cells = ($lines[$j].Trim('|') -split '\|') | ForEach-Object { $_.Trim() }
            if ($cells.Count -le [Math]::Max($keyIdx, $defIdx)) { continue }

            $doc = Normalise $cells[$defIdx]
            if ($doc -in @('', '-', '—', 'n/a')) { continue }

            # "`Wasd`, `Buttons`, `Numbers`" documents several settings with one default.
            foreach ($rawKey in ($cells[$keyIdx] -split ',')) {
                $key = ((($rawKey -replace '`', '') -split '\.')[-1]).Trim()
                $candidates = if ($owner) { @("$owner|$key") } else { @($sourceDefault.Keys | Where-Object { $_ -like "*|$key" }) }
                $candidates = @($candidates | Where-Object { $sourceDefault.ContainsKey($_) })
                if ($candidates.Count -eq 0) { continue }

                $defaultsChecked++
                $accept = $candidates | ForEach-Object { Normalise $sourceDefault[$_] }
                if ($accept -notcontains $doc) {
                    Add-Failure 'Defaults' "$($f.Name) [$heading]: ``$key`` is documented as '$($cells[$defIdx])' but the source default is '$(($candidates | ForEach-Object { $sourceDefault[$_] }) -join "' or '")'"
                }
            }
        }
    }
}

# ---- Report ------------------------------------------------------------------

Write-Host ""
Write-Host "Pages: $($pages.Count)    README wiki links: $readmeLinks    JSONC keys: $keysChecked    Defaults: $defaultsChecked"
Write-Host ""

if ($failures.Count -eq 0) {
    Write-Host "All wiki checks passed." -ForegroundColor Green
    exit 0
}

foreach ($g in $failures | Group-Object Check) {
    Write-Host "$($g.Name) ($($g.Count))" -ForegroundColor Red
    $g.Group | ForEach-Object { Write-Host "  $($_.Detail)" }
    Write-Host ""
}
exit 1
