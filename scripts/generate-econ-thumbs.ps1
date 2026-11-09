<#
.SYNOPSIS
    Generates econ_thumbs.vcss and econ_stickers.vcss: one CSS class per official CS2 econ thumbnail.

.DESCRIPTION
    A custom HUD layout can only show images through CSS classes, so a menu row
    cannot point at an arbitrary image at runtime. This script turns every
    thumbnail CS2 already ships in pak01_dir.vpk into a class:

        .econ-3f2a9c41d0b7 { background-image: url( "s2r://panorama/images/econ/default_generated/weapon_ak47_cu_ak47_asiimov_light_png.vtex" ); }

    The URL names the compiled resource (_png.vtex), the same form CS2's own
    styles use: resourcecompiler does not rewrite s2r:// URLs, and the game has
    no .png source to resolve.

    A consumer emits the key (MenuRawNode "customhud.panorama", "image=<key>")
    and CustomHUD toggles the matching class on the row's thumb panel. Only the
    paths are written; the images themselves stay in the player's own CS2
    install and are never redistributed.

    Class names: 'econ-' + the first 12 hex digits of the SHA-256 of the
    lower-cased key. Every distinct class name is networked once per HUD
    entity, so a short token beats the 70-character key. FrameComposer.ThumbToken
    must match.

    Families: weapon paintkits (the _light render, closest to Factory New),
    base weapons, agents, music kits, charms, patches, cases and capsules
    (weapon_cases, plus tournaments for major capsules), collection icons
    (set_icons) and collectible pins (status_icons/collectible_pin_*) go to
    econ_thumbs.vcss.
    Stickers go to econ_stickers.vcss: they are roughly 80% of the classes, so
    a layout that shows none leaves that file out. Alternate renders (_large,
    _small, _square, the sticker _1355_37) are skipped.

    Rerun after a CS2 update that adds items, then rerun watch-panorama.ps1.

.PARAMETER Listing
    A Source2Viewer-CLI --vpk_list output covering panorama/images/econ/.
    When omitted, -Source2Viewer is run against the VPK to produce one.

.PARAMETER Source2Viewer
    Path to Source2Viewer-CLI.exe (https://github.com/ValveResourceFormat/ValveResourceFormat).

.PARAMETER Cs2Root
    CS2 install root, used to find game\csgo\pak01_dir.vpk.

.PARAMETER Output
    Target vcss for every family but stickers. Defaults to resources\panorama\styles\custom_game\customhud\econ_thumbs.vcss.

.PARAMETER StickerOutput
    Target vcss for stickers. Defaults to econ_stickers.vcss beside the default Output.

.EXAMPLE
    .\scripts\generate-econ-thumbs.ps1 -Source2Viewer C:\tools\Source2Viewer-CLI.exe
    .\scripts\generate-econ-thumbs.ps1 -Listing .\econ_images.txt
#>
[CmdletBinding()]
param(
    [string] $Listing       = $null,
    [string] $Source2Viewer = $null,
    [string] $Cs2Root       = 'C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global Offensive',
    [string] $Output        = $null,
    [string] $StickerOutput = $null
)

$ErrorActionPreference = 'Stop'

$styles = Join-Path $PSScriptRoot '..\resources\panorama\styles\custom_game\customhud'
if (-not $Output) { $Output = Join-Path $styles 'econ_thumbs.vcss' }
if (-not $StickerOutput) { $StickerOutput = Join-Path $styles 'econ_stickers.vcss' }
$Output = [IO.Path]::GetFullPath($Output)
$StickerOutput = [IO.Path]::GetFullPath($StickerOutput)

if (-not $Listing) {
    if (-not $Source2Viewer -or -not (Test-Path $Source2Viewer)) {
        throw 'Pass -Listing, or -Source2Viewer pointing at Source2Viewer-CLI.exe.'
    }
    $vpk = Join-Path $Cs2Root 'game\csgo\pak01_dir.vpk'
    if (-not (Test-Path $vpk)) { throw "VPK not found at '$vpk'. Pass -Cs2Root." }

    $Listing = Join-Path ([IO.Path]::GetTempPath()) 'customhud_econ_listing.txt'
    & $Source2Viewer -i $vpk --vpk_list -f 'panorama/images/econ/' -e 'vtex_c' | Set-Content -Path $Listing -Encoding UTF8
    if ($LASTEXITCODE -ne 0) { throw "Source2Viewer-CLI exited with $LASTEXITCODE." }
}

$prefix = 'panorama/images/econ/'
$suffix = '_png.vtex_c'

# The first matching pattern decides: 'thumbs', 'stickers', or skip.
function Get-Target([string] $Key) {
    switch -Regex ($Key) {
        '^default_generated/.+_light$'     { return 'thumbs' }
        '^characters/.+_square$'           { return $null }
        '^characters/'                     { return 'thumbs' }
        '^(music_kits|keychains|patches)/' { return 'thumbs' }
        '^weapons/base_weapons/'           { return 'thumbs' }
        '^weapon_cases/.+_(large|small)$'  { return $null }
        '^weapon_cases/'                   { return 'thumbs' }
        '^tournaments/.+_small$'           { return $null }
        '^tournaments/'                    { return 'thumbs' }
        '^set_icons/.+_small$'             { return $null }
        '^set_icons/'                      { return 'thumbs' }
        '^status_icons/.+_small$'          { return $null }
        '^status_icons/collectible_pin_'   { return 'thumbs' }
        '^stickers/.+_1355_37$'            { return $null }
        '^stickers/'                       { return 'stickers' }
    }
    return $null
}

$sets = @{
    thumbs   = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    stickers = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
}
foreach ($line in [IO.File]::ReadLines((Resolve-Path $Listing).Path)) {
    $path = ($line -split '\s+', 2)[0].Replace('\', '/')
    if (-not $path.StartsWith($prefix) -or -not $path.EndsWith($suffix)) { continue }
    $key = $path.Substring($prefix.Length, $path.Length - $prefix.Length - $suffix.Length)
    $target = Get-Target $key
    if ($target) { [void] $sets[$target].Add($key) }
}

if ($sets.thumbs.Count -eq 0) { throw "No econ thumbnails found in '$Listing'." }
if ($sets.stickers.Count -eq 0) { throw "No sticker thumbnails found in '$Listing'." }

# One namespace across both files: a token two keys share would draw the wrong image.
$owners = @{}
function Write-Classes([System.Collections.Generic.SortedSet[string]] $Keys, [string] $Path) {
    $sb = New-Object System.Text.StringBuilder
    [void] $sb.AppendLine('/* Generated by scripts/generate-econ-thumbs.ps1. Do not edit. */')
    [void] $sb.AppendLine('/* Paths only: every image resolves from the player''s own CS2 install. */')
    [void] $sb.AppendLine('')
    foreach ($key in $Keys) {
        $lower = $key.ToLowerInvariant()
        $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($lower))
        $token = [Convert]::ToHexString($hash).Substring(0, 12).ToLowerInvariant()
        if ($owners.ContainsKey($token) -and $owners[$token] -ne $lower) {
            throw "Thumbnail token collision: '$($owners[$token])' and '$lower' both hash to $token."
        }
        $owners[$token] = $lower
        [void] $sb.Append('.econ-').Append($token).Append(' { background-image: url( "s2r://panorama/images/econ/').Append($key).AppendLine('_png.vtex" ); }')
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $Path -Parent) | Out-Null
    [IO.File]::WriteAllText($Path, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
    Write-Host ("{0} thumbnail classes -> {1}" -f $Keys.Count, $Path)
}

Write-Classes $sets.thumbs $Output
Write-Classes $sets.stickers $StickerOutput
