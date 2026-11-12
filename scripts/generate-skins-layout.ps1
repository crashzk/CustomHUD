<#
.SYNOPSIS
    Generates menu_skins_mouse_v2.vxml, the WeaponSkins locker in CS2's store look, which
    MenuLayoutProfile.WeaponSkinsLockerV2() points at.

.DESCRIPTION
    The counts below must match the profile: RowSlots (tiles), Sidebar.Slots, Tabs.Slots,
    Card.Slots with Card.Segments per slider track and Card.SlotTiles per field, and
    Spot.FactSlots. A slot drawn here that the profile does not name never fills; one the
    profile names that is missing here shows its {s:...} placeholder.

    The first three tiles sit in panels of their own so the categories view can draw them
    as a mosaic, one large tile beside a column of two wide ones; every other view flows
    them in line with the rest. A tile's rarity layer (icon_N) holds everything its rarity
    colours, the picture and caption included. Styles are menu_skins_mouse_v2.vcss.

    Rerun after changing a count or the anatomy, then rerun watch-panorama.ps1.
#>
[CmdletBinding()]
param([string] $OutputDir = $null)

$ErrorActionPreference = 'Stop'
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot '..\resources\panorama\layout\custom_game\customhud' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)

$Tiles = 128
$Categories = 12
$Tabs = 8
$Fields = 8
$Segments = 51
$SlotTiles = 6
$Facts = 4
$Bands = 'fn', 'mw', 'ft', 'ww', 'bs'
$n = 'hittest="false"'

$sb = New-Object System.Text.StringBuilder
function Line([int] $Depth, [string] $Text) { [void] $sb.Append("`t" * $Depth).AppendLine($Text) }

function Tile([int] $Depth, [int] $Index, [string] $Shape) {
    $id = "option_$Index"
    Line $Depth "<Button id=""$id"" class=""sk-tile$Shape hidden"">"
    $d = $Depth + 1
    Line $d "<Panel id=""icon_$Index"" class=""sk-tile__art hidden"" $n>"
    Line ($d + 1) "<Panel class=""sk-tile__glow"" $n />"
    Line ($d + 1) "<Panel id=""thumb_$Index"" class=""sk-tile__thumb hidden"" $n />"
    Line ($d + 1) "<Panel class=""sk-tile__line"" $n />"
    Line ($d + 1) "<Panel class=""sk-tile__cap"" $n><Panel class=""sk-tile__dot"" $n /><Label class=""sk-tile__type"" text=""{s:${id}_type}"" $n /></Panel>"
    Line $d '</Panel>'
    Line $d "<Label class=""sk-tile__name"" text=""{s:$id}"" $n />"
    Line $d "<Label class=""sk-tile__value"" text=""{s:${id}_right}"" $n />"
    Line $d "<Panel class=""sk-tile__check"" $n><Panel class=""chud-ico ico-check sk-tile__tick"" $n /></Panel>"
    Line $Depth '</Button>'
}

function BandSet([int] $Depth, [string] $Class) {
    Line $Depth "<Panel class=""$Class"" $n>"
    foreach ($band in $Bands) { Line ($Depth + 1) "<Panel class=""sk-band sk-band--$band"" $n />" }
    Line $Depth '</Panel>'
}

function Field([int] $Depth, [int] $Index) {
    $id = "field_$Index"
    Line $Depth "<Panel id=""$id"" class=""sk-field hidden"">"
    $d = $Depth + 1
    Line $d '<Panel class="sk-field__row">'
    Line ($d + 1) "<Button id=""${id}_hit"" class=""sk-field__hit"">"
    Line ($d + 2) "<Label class=""sk-field__label"" text=""{s:$id}"" $n />"
    Line ($d + 2) "<Panel class=""sk-field__box"" $n><Label class=""sk-field__value"" text=""{s:${id}_value}"" $n /></Panel>"
    Line ($d + 2) "<Panel class=""sk-switch"" $n><Panel class=""sk-switch__knob"" $n /></Panel>"
    Line ($d + 2) "<Label class=""sk-field__chevron"" text=""›"" $n />"
    Line ($d + 1) '</Button>'
    Line ($d + 1) "<Button id=""${id}_aside"" class=""sk-field__aside"">"
    Line ($d + 2) "<Panel class=""sk-switch"" $n><Panel class=""sk-switch__knob"" $n /></Panel>"
    Line ($d + 2) "<Panel class=""chud-ico ico-cancel sk-field__clear"" $n />"
    Line ($d + 1) '</Button>'
    Line $d '</Panel>'
    # With slots the row collapses to this heading: only the tiles open it.
    Line $d "<Panel class=""sk-field__head"" $n>"
    Line ($d + 1) "<Label class=""sk-field__heading"" text=""{s:$id}"" $n />"
    Line ($d + 1) "<Panel class=""sk-field__rule"" $n />"
    Line $d '</Panel>'
    # A row's slots (a weapon's stickers, its charm): a tile each, the picture of what is on it.
    Line $d '<Panel class="sk-slots">'
    for ($k = 0; $k -lt $SlotTiles; $k++) {
        Line ($d + 1) "<Button id=""${id}_slot$k"" class=""sk-slot hidden"">"
        Line ($d + 2) "<Panel class=""chud-ico ico-add sk-slot__add"" $n />"
        Line ($d + 2) "<Panel id=""${id}_slot${k}_thumb"" class=""sk-slot__thumb hidden"" $n />"
        Line ($d + 2) "<Label class=""sk-slot__name"" text=""{s:${id}_slot$k}"" $n />"
        Line ($d + 1) '</Button>'
    }
    Line $d '</Panel>'
    Line $d '<Panel class="sk-track">'
    BandSet ($d + 1) 'sk-track__base'
    Line ($d + 1) "<Panel class=""sk-track__fill"" $n>"
    Line ($d + 2) "<Panel class=""sk-track__clip"" $n>"
    BandSet ($d + 3) 'sk-track__lit'
    Line ($d + 2) '</Panel>'
    Line ($d + 2) "<Panel class=""sk-track__knob"" $n />"
    Line ($d + 1) '</Panel>'
    # Widest first: a later sibling sits on top, so the narrowest segment under the pointer wins.
    Line ($d + 1) '<Panel class="sk-track__hits">'
    for ($s = $Segments - 1; $s -ge 0; $s--) { Line ($d + 2) "<Button id=""${id}_seg$s"" class=""sk-seg sk-seg-$s"" />" }
    Line ($d + 1) '</Panel>'
    Line $d '</Panel>'
    Line $d "<Panel class=""sk-track__legend"" $n>"
    foreach ($band in $Bands) { Line ($d + 1) "<Label class=""sk-legend sk-legend--$band"" text=""$($band.ToUpperInvariant())"" $n />" }
    Line $d '</Panel>'
    Line $Depth '</Panel>'
}

Line 0 '<root>'
Line 1 '<styles>'
foreach ($sheet in 'customhud_tokens', 'v2/menu', 'v2/menu_mouse', 'econ_thumbs', 'econ_stickers', 'customhud_icons', 'menu_skins_mouse_v2') {
    Line 2 "<include src=""s2r://panorama/styles/custom_game/customhud/$sheet.vcss_c"" />"
}
Line 1 '</styles>'
Line 1 "<Panel class=""chud-root"" $n>"
Line 2 '<Panel id="shell" class="chud-locker sk hidden">'

Line 3 '<Panel class="sk-side">'
Line 4 "<Panel class=""sk-brand"" $n><Label class=""sk-brand__text"" text=""WEAPONSKINS"" $n /></Panel>"
Line 4 '<Panel class="sk-nav">'
for ($i = 0; $i -lt $Categories; $i++) {
    Line 5 "<Button id=""category_$i"" class=""sk-nav__item hidden"">"
    Line 6 "<Panel class=""sk-nav__edge"" $n />"
    Line 6 "<Panel id=""category_${i}_icon"" class=""chud-ico sk-nav__icon"" $n><Panel class=""chud-ico__badge"" $n /></Panel>"
    Line 6 "<Label class=""sk-nav__label"" text=""{s:category}"" $n />"
    Line 5 '</Button>'
}
Line 4 '</Panel>'
Line 3 '</Panel>'

Line 3 '<Panel class="sk-main">'
Line 4 '<Panel class="sk-bar">'
Line 5 '<Button id="nav_back" class="sk-back hidden">'
Line 6 "<Panel class=""chud-ico ico-back sk-back__icon"" $n />"
Line 6 "<Label class=""sk-back__label"" text=""{s:crumb}"" $n />"
Line 6 "<Panel class=""sk-back__rule"" $n />"
Line 5 '</Button>'
Line 5 "<Panel id=""title"" class=""sk-title"" $n><Label class=""sk-title__text"" text=""{s:title}"" $n /></Panel>"
Line 5 "<Label id=""info"" class=""sk-info"" text=""{s:info}"" $n />"
Line 5 "<Button id=""nav_close"" class=""sk-close hidden""><Panel class=""chud-ico ico-exit sk-close__icon"" $n /></Button>"
Line 4 '</Panel>'

Line 4 '<Panel class="sk-body">'
Line 5 '<Panel id="tabs" class="sk-tabs hidden">'
for ($i = 0; $i -lt $Tabs; $i++) {
    Line 6 "<Button id=""tab_$i"" class=""sk-tab hidden""><Label class=""sk-tab__label"" text=""{s:tab}"" $n /><Label class=""sk-tab__count"" text=""{s:tab_count}"" $n /></Button>"
}
Line 5 '</Panel>'

Line 5 "<Panel id=""spot"" class=""sk-spot hidden"" $n>"
Line 6 "<Panel class=""sk-spot__stage"" $n>"
Line 7 "<Panel class=""sk-spot__glow"" $n />"
Line 7 "<Panel id=""spot_thumb"" class=""sk-spot__thumb hidden"" $n />"
Line 7 "<Panel class=""sk-status"" $n><Panel class=""chud-ico ico-check sk-status__icon"" $n /><Label class=""sk-status__text"" text=""{s:spot_note}"" $n /></Panel>"
Line 6 '</Panel>'
Line 6 "<Panel class=""sk-spot__info"" $n>"
Line 7 "<Label class=""sk-spot__sub"" text=""{s:spot_sub}"" $n />"
Line 7 "<Label class=""sk-spot__name"" text=""{s:spot_name}"" $n />"
Line 7 "<Label class=""sk-spot__type"" text=""{s:spot_type}"" $n />"
Line 7 "<Panel class=""sk-spot__bar"" $n />"
Line 7 "<Panel class=""sk-facts"" $n>"
for ($i = 0; $i -lt $Facts; $i++) {
    Line 8 "<Panel id=""spot_fact_$i"" class=""sk-fact hidden"" $n><Label class=""sk-fact__label"" text=""{s:spot_fact_$i}"" $n /><Label class=""sk-fact__value"" text=""{s:spot_fact_${i}_value}"" $n /></Panel>"
}
Line 7 '</Panel>'
Line 7 "<Panel class=""sk-spot__room"" $n />"
Line 7 "<Button id=""spot_link"" class=""sk-link hidden""><Panel class=""chud-ico ico-settings sk-link__icon"" $n /><Label class=""sk-link__label"" text=""{s:spot_link}"" $n /></Button>"
Line 6 '</Panel>'
Line 5 '</Panel>'

Line 5 '<Panel id="options" class="sk-grid chud-scroll">'
Line 6 '<Panel class="sk-items">'
Line 7 '<Panel class="sk-lead">'
Tile 8 0 ' sk-tile--lead'
Line 8 '<Panel class="sk-lead__col">'
Tile 9 1 ' sk-tile--wide'
Tile 9 2 ' sk-tile--wide'
Line 8 '</Panel>'
Line 7 '</Panel>'
for ($i = 3; $i -lt $Tiles; $i++) { Tile 7 $i '' }
Line 6 '</Panel>'
Line 5 '</Panel>'

Line 5 '<Panel id="card" class="sk-card">'
Line 6 "<Panel id=""hero"" class=""sk-hero hidden"" $n>"
Line 7 "<Panel class=""sk-hero__glow"" $n />"
Line 7 "<Panel id=""hero_thumb"" class=""sk-hero__thumb hidden"" $n />"
Line 7 "<Panel class=""sk-hero__text"" $n>"
Line 8 "<Label class=""sk-hero__type"" text=""{s:hero_type}"" $n />"
Line 8 "<Label class=""sk-hero__name"" text=""{s:hero_name}"" $n />"
Line 8 "<Panel class=""sk-hero__bar"" $n />"
Line 7 '</Panel>'
Line 6 '</Panel>'
Line 6 '<Panel class="sk-fields">'
for ($i = 0; $i -lt $Fields; $i++) { Field 7 $i }
Line 6 '</Panel>'
Line 5 '</Panel>'
Line 4 '</Panel>'
# Every row layout's mouse hint lands here; this design has no footer to show it in.
Line 4 "<Label id=""footer"" class=""sk-hint"" text=""{s:footer}"" $n />"
Line 3 '</Panel>'
Line 2 '</Panel>'
Line 1 '</Panel>'
Line 0 '</root>'

$text = $sb.ToString()
$path = Join-Path $OutputDir 'menu_skins_mouse_v2.vxml'
[IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))
$panels = ([regex]::Matches($text, '<(Panel|Button|Label)\b')).Count
Write-Host ("{0} panels -> {1}" -f $panels, $path)
