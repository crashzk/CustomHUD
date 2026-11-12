<#
.SYNOPSIS
    Syncs CustomHUD Panorama sources into the customhud addon and compiles them.

.DESCRIPTION
    resources/panorama is the source of truth. Each file is copied into the
    addon's content tree and compiled there; resourcecompiler writes the _c
    output into the matching game tree through the content -> game addon mapping,
    and the script then copies that _c into the local client tree:

        content\csgo_addons\customhud\panorama\...   raw sources (copied here)
        game\csgo_addons\customhud\panorama\...      compiled _c (-addon mounts it, a publish packs it)
        game\csgo\panorama\...                       copy of each _c for the local client

    The client copy sits on the base 'Game csgo' search path, so a local client
    finds the layouts even without the addon mounted. It never ships.

    Create the addon once in Workshop Tools (Create New Addon -> customhud) so
    both addon trees exist. Run it locally exactly as Workshop Tools' Launch does:

        cs2.exe -addon customhud -tools -insecure +sv_lan 1 +map de_dust2

    CS2 has no Panorama hot-reload. After a compile, re-trigger the layout by
    reopening the menu. F6 opens the Panorama debugger.

    Rasters (png/tga/jpg/webp) compile through a generated <name>_<ext>.vtex
    wrapper, because Panorama resolves url("...x.png") to x_png.vtex_c. Only png
    is proven in practice. SVGs compile to .vsvg_c and are only parsed by the
    client, so this script lints them for constructs CS2 rejects.

.PARAMETER Cs2Root
    CS2 install root (the folder containing game\ and content\).

.PARAMETER Addon
    Addon folder name under csgo_addons.

.PARAMETER Source
    Folder holding the panorama/ source tree.

.PARAMETER PublishedId
    Workshop id of the published addon. When set, warns if compiled files are
    newer than the downloaded VPK, which shadows them while it is mounted.

.PARAMETER ClientRoot
    Local client panorama folder that receives a copy of every compiled file.
    Defaults to <Cs2Root>\game\csgo\panorama.

.PARAMETER SkipClient
    Compile into the addon only and leave the local client tree untouched.

.PARAMETER Once
    Sync and compile everything once and exit (exit code 1 on any failure).

.PARAMETER Changed
    Compile only the sources that changed since their last compile: the addon's copy differs,
    or the compiled file is missing or older than the source. scripts/audit.ps1 runs it this way.

.EXAMPLE
    .\scripts\watch-panorama.ps1
    .\scripts\watch-panorama.ps1 -Once
    .\scripts\watch-panorama.ps1 -Once -Changed
    .\scripts\watch-panorama.ps1 -SkipClient
    .\scripts\watch-panorama.ps1 -PublishedId 1234567890
#>
[CmdletBinding()]
param(
    [string] $Cs2Root     = 'C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive',
    [string] $Addon       = 'customhud',
    [string] $Source      = $null,
    [string] $PublishedId = $null,
    [string] $ClientRoot  = $null,
    [switch] $SkipClient,
    [switch] $Once,
    [switch] $Changed
)

$ErrorActionPreference = 'Stop'

if ($Addon -notmatch '^[a-zA-Z0-9][a-zA-Z0-9_]*$') {
    throw "Addon name '$Addon' is invalid; addon names must match [a-zA-Z0-9][a-zA-Z0-9_]*."
}

$compiler = Join-Path $Cs2Root 'game\bin\win64\resourcecompiler.exe'
if (-not (Test-Path $compiler)) {
    throw "resourcecompiler.exe not found at '$compiler'. Pass -Cs2Root."
}

$addonContent = Join-Path $Cs2Root "content\csgo_addons\$Addon"
if (-not (Test-Path $addonContent)) {
    throw "Addon '$Addon' not found at '$addonContent'. Create it once in Workshop Tools (Create New Addon), then rerun."
}

# Windows PowerShell 5.1 leaves $PSScriptRoot empty inside param defaults.
if (-not $Source) { $Source = Join-Path $PSScriptRoot '..\resources\panorama' }
$Source      = (Resolve-Path $Source).Path
$contentRoot = Join-Path $addonContent 'panorama'
$gameRoot    = Join-Path $Cs2Root "game\csgo_addons\$Addon\panorama"

if (-not $ClientRoot) { $ClientRoot = Join-Path $Cs2Root 'game\csgo\panorama' }
$ClientRoot = [IO.Path]::GetFullPath($ClientRoot).TrimEnd('\')
if (-not $SkipClient -and -not (Test-Path (Split-Path $ClientRoot -Parent))) {
    throw "Client folder '$(Split-Path $ClientRoot -Parent)' not found. Pass -ClientRoot, or -SkipClient to compile into the addon only."
}

$rasterExts = @('.png', '.tga', '.jpg', '.jpeg', '.webp')
$sourceExts = @('.vxml', '.vcss', '.svg') + $rasterExts

Write-Host "compiler : $compiler"
Write-Host "source   : $Source"
Write-Host "content  : $contentRoot"
Write-Host "game     : $gameRoot"
if ($SkipClient) { Write-Host 'client   : (skipped)' } else { Write-Host "client   : $ClientRoot" }
Write-Host ''

function Get-RelativePath([string] $Full) { $Full.Substring($Source.Length).TrimStart('\') }

function Get-WrapperRel([string] $Rel) { $Rel -replace '\.(png|tga|jpg|jpeg|webp)$', '_$1.vtex' }

function Get-CompiledRel([string] $Rel) {
    $ext = [IO.Path]::GetExtension($Rel).ToLowerInvariant()
    if ($rasterExts -contains $ext) { return (Get-WrapperRel $Rel) + '_c' }
    if ($ext -eq '.svg') { return $Rel -replace '\.svg$', '.vsvg_c' }
    return $Rel + '_c'
}

function Get-SourceFiles {
    Get-ChildItem -Path $Source -Recurse -File | Where-Object { $sourceExts -contains $_.Extension.ToLowerInvariant() }
}

function New-VtexWrapper([string] $Rel) {
    $wrapperRel = Get-WrapperRel $Rel
    $srcFwd = $Rel -replace '\\', '/'
    $body = @"
<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
"CDmeVtex"
{
	"m_inputTextureArray" "element_array"
	[
		"CDmeInputTexture"
		{
			"m_name" "string" "InputTexture0"
			"m_fileName" "string" "panorama/$srcFwd"
			"m_colorSpace" "string" "srgb"
			"m_typeString" "string" "2D"
			"m_imageProcessorArray" "element_array"
			[
			]
		}
	]
	"m_outputTypeString" "string" "2D"
	"m_outputFormat" "string" "RGBA8888"
	"m_nOutputMinDimension" "int" "0"
	"m_nOutputMaxDimension" "int" "0"
	"m_textureOutputChannelArray" "element_array"
	[
		"CDmeTextureOutputChannel"
		{
			"m_inputTextureArray" "string_array"
			[
				"InputTexture0"
			]
			"m_srcChannels" "string" "rgba"
			"m_dstChannels" "string" "rgba"
			"m_mipAlgorithm" "CDmeImageProcessor"
			{
				"m_algorithm" "string" ""
				"m_stringArg" "string" ""
				"m_vFloat4Arg" "vector4" "0 0 0 0"
			}
			"m_outputColorSpace" "string" "srgb"
		}
	]
	"m_vClamp" "vector3" "0 0 0"
	"m_bNoLod" "bool" "0"
}
"@
    $body = ($body -replace "`r?`n", "`r`n") + "`r`n"
    [IO.File]::WriteAllText((Join-Path $contentRoot $wrapperRel), $body, [Text.UTF8Encoding]::new($false))
    return $wrapperRel
}

# resourcecompiler embeds SVG text verbatim and always reports OK; only the
# client parses it, and a bad file fails there with an empty reason.
function Test-Svg([string] $File, [string] $Rel) {
    $text = [IO.File]::ReadAllText($File)
    $root = [regex]::Match($text, '<svg\b[^>]*>').Value
    $issues = @()
    if ($text -notmatch '^\s*<\?xml')                   { $issues += 'missing <?xml ?> prolog' }
    if ($text -match '<circle\b')                       { $issues += '<circle> is rejected, use <ellipse>' }
    if ($root -match '\sfill\s*=\s*"none"')             { $issues += 'fill="none" on the <svg> root' }
    if ($root -and ($root -notmatch '\swidth\s*=' -or $root -notmatch '\sheight\s*=')) { $issues += 'root <svg> lacks width/height' }
    if (($text -split "`n").Count -le 2 -and $text.Length -gt 200) { $issues += 'minified onto one line, use one element per line' }
    foreach ($i in $issues) { Write-Host "warn  $Rel -- $i" -ForegroundColor Yellow }
}

function Invoke-Sync([string] $File) {
    # Native stderr under 'Stop' throws on Windows PowerShell 5.1.
    $ErrorActionPreference = 'Continue'

    $rel    = Get-RelativePath $File
    $target = Join-Path $contentRoot $rel
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Copy-Item $File $target -Force

    $ext = [IO.Path]::GetExtension($rel).ToLowerInvariant()
    if ($ext -eq '.svg') { Test-Svg $File $rel }

    $compileRel  = if ($rasterExts -contains $ext) { New-VtexWrapper $rel } else { $rel }
    $compiledRel = Get-CompiledRel $rel
    $compiled    = Join-Path $gameRoot $compiledRel

    $out = @(& $compiler -f -i (Join-Path $contentRoot $compileRel) 2>&1 | ForEach-Object { "$_" })
    if (-not (($out -match 'OK: 1 compiled') -and (Test-Path $compiled))) {
        Write-Host "FAIL  $rel" -ForegroundColor Red
        $out | Select-Object -Last 15 | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
        return $false
    }

    # Copied rather than recompiled: compiled bytes are path-independent.
    if (-not $SkipClient) {
        $clientFile = Join-Path $ClientRoot $compiledRel
        try {
            New-Item -ItemType Directory -Force -Path (Split-Path $clientFile -Parent) -ErrorAction Stop | Out-Null
            Copy-Item $compiled $clientFile -Force -ErrorAction Stop
        }
        catch {
            Write-Host "FAIL  $rel -- compiled, but the client copy failed: $($_.Exception.Message)" -ForegroundColor Red
            return $false
        }
    }

    Write-Host "ok    $rel" -ForegroundColor Green
    return $true
}

function Test-Stale([IO.FileInfo] $File) {
    $rel      = Get-RelativePath $File.FullName
    $copy     = Join-Path $contentRoot $rel
    $compiled = Join-Path $gameRoot (Get-CompiledRel $rel)
    if (-not (Test-Path -LiteralPath $copy) -or -not (Test-Path -LiteralPath $compiled)) { return $true }
    if (-not $SkipClient -and -not (Test-Path -LiteralPath (Join-Path $ClientRoot (Get-CompiledRel $rel)))) { return $true }
    # A failed compile leaves the copy current but the compiled file older than the source.
    if ((Get-Item -LiteralPath $compiled).LastWriteTimeUtc -lt $File.LastWriteTimeUtc) { return $true }
    return (Get-FileHash -LiteralPath $File.FullName).Hash -ne (Get-FileHash -LiteralPath $copy).Hash
}

function Invoke-SyncAll {
    $files = @(Get-SourceFiles)
    if ($Changed) { $files = @($files | Where-Object { Test-Stale $_ }) }
    if (-not $files) {
        if ($Changed) { Write-Host 'nothing changed.' -ForegroundColor Green } else { Write-Host 'nothing to compile.' -ForegroundColor Yellow }
        return 0
    }

    $failed = 0
    foreach ($f in $files) { if (-not (Invoke-Sync $f.FullName)) { $failed++ } }
    Write-Host ''
    Write-Host ("compiled {0}, failed {1}" -f ($files.Count - $failed), $failed) -ForegroundColor Cyan
    return $failed
}

function Write-Orphans($Orphans, [string] $Root, [string] $Headline, [string] $Advice) {
    if ($Orphans.Count -eq 0) { return }
    Write-Host ''
    Write-Host "warn  $($Orphans.Count) $Headline" -ForegroundColor Yellow
    Write-Host "      $Advice" -ForegroundColor Yellow
    $Orphans | ForEach-Object { Write-Host "      $($_.FullName.Substring($Root.Length).TrimStart('\'))" -ForegroundColor Yellow }
}

# Anything left in the content tree ships with the next publish, and anything
# left in the client tree lets a dangling layout reference work locally only.
function Show-Orphans {
    $sources  = @(Get-SourceFiles)
    $expected = @{}
    $compiled = @{}
    foreach ($f in $sources) {
        $rel = Get-RelativePath $f.FullName
        $expected[$rel.ToLowerInvariant()] = $true
        if ($rasterExts -contains $f.Extension.ToLowerInvariant()) { $expected[(Get-WrapperRel $rel).ToLowerInvariant()] = $true }
        $compiled[(Get-CompiledRel $rel).ToLowerInvariant()] = $true
    }

    $orphans = @(Get-ChildItem -Path $contentRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { ($sourceExts + '.vtex') -contains $_.Extension.ToLowerInvariant() } |
        Where-Object { -not $expected.ContainsKey($_.FullName.Substring($contentRoot.Length).TrimStart('\').ToLowerInvariant()) })
    Write-Orphans $orphans $contentRoot 'file(s) in the addon have no source in resources/panorama.' `
        'Delete them from both the content and game trees or they ship with the next publish:'

    if ($SkipClient) { return }

    # Only the folders holding our sources (layout\custom_game\customhud, ...) are ours;
    # custom_game itself is shared with every other addon's client copy.
    $nsDirs = @($sources | ForEach-Object { Split-Path (Get-RelativePath $_.FullName) -Parent } | Sort-Object -Unique)
    $clientOrphans = @(foreach ($ns in $nsDirs) {
        Get-ChildItem -Path (Join-Path $ClientRoot $ns) -File -ErrorAction SilentlyContinue |
            Where-Object { -not $compiled.ContainsKey($_.FullName.Substring($ClientRoot.Length).TrimStart('\').ToLowerInvariant()) }
    })
    Write-Orphans $clientOrphans $ClientRoot 'file(s) in the local client tree have no source in resources/panorama.' `
        'They never ship, but a layout still referencing one works locally and breaks for players. Delete them:'
}

# A mounted Workshop VPK wins over the loose game tree, so an unpublished edit
# looks like it did nothing.
function Show-PublishedDrift {
    if (-not $PublishedId) { return }

    $workshopDir = [IO.Path]::GetFullPath((Join-Path $Cs2Root "..\..\workshop\content\730\$PublishedId"))
    $vpk = Get-ChildItem -Path $workshopDir -Filter '*_dir.vpk' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $vpk) {
        Write-Host "warn  no downloaded VPK for Workshop item $PublishedId under '$workshopDir'." -ForegroundColor Yellow
        return
    }

    $newer = @(Get-ChildItem -Path $gameRoot -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $vpk.LastWriteTime })
    if ($newer.Count -eq 0) { return }
    Write-Host ''
    Write-Host "warn  Workshop VPK is from $($vpk.LastWriteTime) but $($newer.Count) compiled file(s) are newer." -ForegroundColor Yellow
    Write-Host '      While the published addon is mounted its packed copy wins. Republish, or launch with' -ForegroundColor Yellow
    Write-Host "      -addon $Addon -tools and without +sv_workshop_addon_load so the loose files are used." -ForegroundColor Yellow
}

$failed = Invoke-SyncAll
Show-Orphans
Show-PublishedDrift

if ($Once) {
    if ($failed -gt 0) { exit 1 }
    return
}

Write-Host ''
Write-Host 'watching for changes -- Ctrl+C to stop.' -ForegroundColor Cyan
Write-Host 'after a compile: reopen the menu in game. F6 opens the Panorama debugger.'
Write-Host ''

$watcher = [System.IO.FileSystemWatcher]::new($Source)
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

# Editors write a file in several operations, so one save can raise several
# events. Collapsing by path within a short window keeps that to one compile.
$pending = @{}

try {
    while ($true) {
        $change = $watcher.WaitForChanged([System.IO.WatcherChangeTypes]::Changed -bor
                                          [System.IO.WatcherChangeTypes]::Created -bor
                                          [System.IO.WatcherChangeTypes]::Renamed, 1000)
        if ($change.TimedOut) {
            $now = Get-Date
            foreach ($key in @($pending.Keys)) {
                if (($now - $pending[$key]).TotalMilliseconds -lt 250) { continue }
                $pending.Remove($key)
                if (Test-Path $key) { Invoke-Sync $key | Out-Null }
            }
            continue
        }

        $full = Join-Path $Source $change.Name
        if ($sourceExts -notcontains [IO.Path]::GetExtension($full).ToLowerInvariant()) { continue }
        $pending[$full] = Get-Date
    }
}
finally {
    $watcher.EnableRaisingEvents = $false
    $watcher.Dispose()
}
