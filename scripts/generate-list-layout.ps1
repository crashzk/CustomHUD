<#
.SYNOPSIS
    Generates the list layouts: menu_list.vxml (Wasd, Buttons, Numbers),
    menu_list_mouse.vxml (Mouse) and menu_list_skins.vxml (WeaponSkins in the
    keyboard modes), the first designs of ListProfiles.Keys(), Mouse() and Skins(),
    and their _v2 files, the defaults.

.DESCRIPTION
    The files draw one row anatomy, which ListProfiles.Row names part by part: a
    slot drawn here without its name there (or the other way round) either never
    fills or shows its {s:...} placeholder. The rows reuse the page locker's part
    classes (lk-text, lk-tags, lk-mark ...), so locker.vcss styles their content and
    list.vcss only places it.

    The keyboard file adds a number per row and the legend of keycaps; the mouse
    file adds the ‹ › steppers and track segments beside a slider's or selector's
    value, a scrolling body, and the < and × buttons. menu_list_skins.vxml is the
    keyboard file plus econ_stickers.vcss: sticker art is most of all econ classes,
    so only the list that draws stickers loads it. Slot counts follow
    ListProfiles.KeySlots, MouseSlots, Segments and FootSlots.

    A _v2 file draws the same rows and ids in CS2's store look: a band over the
    title, Back and Close at the top in Mouse mode, and the v2 copies of the sheets
    (generate-v2-sheets.ps1) with menu_list_v2.vcss last.

    Rerun after changing a count or the anatomy, then rerun watch-panorama.ps1.
#>
[CmdletBinding()]
param([string] $OutputDir = $null)

$ErrorActionPreference = 'Stop'
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot '..\resources\panorama\layout\custom_game\customhud' }
$OutputDir = [IO.Path]::GetFullPath($OutputDir)

$KeySlots = 20
$MouseSlots = 24
$Segments = 21
$FootSlots = 3
$Pips = 20
$Chips = 3
$Legend = 4
$Dots = 8
$n = 'hittest="false"'

function New-Layout([bool] $Mouse, [bool] $Stickers, [bool] $V2) {
    $sb = New-Object System.Text.StringBuilder
    function Line([int] $Depth, [string] $Text) { [void] $sb.Append("`t" * $Depth).AppendLine($Text) }

    function Chips([int] $Depth) {
        for ($i = 0; $i -lt $Chips; $i++) {
            Line $Depth "<Panel id=""chip_$i"" class=""lk-chip hidden"" $n>"
            Line ($Depth + 1) "<Panel id=""chip_${i}_glyph"" class=""chud-ico lk-chip__glyph hidden"" $n><Panel class=""chud-ico__badge"" $n /></Panel>"
            Line ($Depth + 1) "<Panel id=""chip_${i}_thumb"" class=""lk-thumb lk-chip__thumb hidden"" $n />"
            Line ($Depth + 1) "<Panel class=""lk-rating"" $n><Label class=""lk-rating__major"" text=""{s:chip_${i}_rating}"" $n /><Label class=""lk-rating__minor"" text=""{s:chip_${i}_rating_minor}"" $n /></Panel>"
            Line ($Depth + 1) "<Label class=""lk-chip__value"" text=""{s:chip_$i}"" $n />"
            Line ($Depth + 1) "<Label class=""lk-chip__label"" text=""{s:chip_${i}_label}"" $n />"
            Line $Depth '</Panel>'
        }
    }

    function Head([string] $Mode) {
        Line 2 "<Panel id=""shell"" class=""ls $Mode hidden"">"
        Line 3 "<Panel id=""title"" class=""ls-head"" $n>"
        Line 4 "<Panel class=""ls-top"" $n>"
        Line 5 "<Panel class=""ls-path"" $n>"
        Line 6 "<Label class=""ls-eyebrow"" text=""{s:eyebrow}"" $n />"
        Line 6 "<Label class=""ls-crumb-sep"" text=""›"" $n />"
        Line 6 "<Label class=""ls-crumb"" text=""{s:crumb}"" $n />"
        Line 5 '</Panel>'
        Line 5 "<Panel class=""lk-chips ls-chips"" $n>"
        Chips 6
        Line 5 '</Panel>'
        Line 4 '</Panel>'
        Line 4 "<Label class=""ls-title"" text=""{s:title}"" $n />"
        Line 3 '</Panel>'
        Line 3 "<Panel class=""ls-divider"" $n />"
    }

    # v2: a band (the plugin's name, the parent's title, the chips) over the title's line. In
    # Mouse mode the parent's title is Back and Close ends the title's line, so #title, whose
    # variables reach only the labels under it, holds Buttons there and stays hit-testable.
    function Head2([string] $Mode) {
        $quiet = if ($Mouse) { '' } else { " $n" }
        Line 2 "<Panel id=""shell"" class=""ls ls2 $Mode hidden"">"
        Line 3 "<Panel id=""title"" class=""ls2-head""$quiet>"
        Line 4 "<Panel class=""ls2-band""$quiet>"
        # Back leads the path, which names where it goes: the root's title, then the parent's.
        if ($Mouse) { Line 5 "<Button id=""nav_back"" class=""ls2-back hidden""><Panel class=""chud-ico ico-back ls2-back__icon"" $n /></Button>" }
        Line 5 "<Panel class=""ls2-path"" $n>"
        Line 6 "<Label class=""ls2-brand"" text=""{s:eyebrow}"" $n />"
        Line 6 "<Label class=""ls2-crumb"" text=""{s:crumb}"" $n />"
        Line 5 '</Panel>'
        Line 5 "<Panel class=""lk-chips ls-chips ls2-chips"" $n>"
        Chips 6
        Line 5 '</Panel>'
        Line 4 '</Panel>'
        Line 4 "<Panel class=""ls2-line""$quiet>"
        Line 5 "<Label class=""ls2-title"" text=""{s:title}"" $n />"
        if ($Mouse) { Line 5 "<Button id=""nav_close"" class=""ls2-close hidden""><Panel class=""chud-ico ico-exit ls2-close__icon"" $n /></Button>" }
        Line 4 '</Panel>'
        Line 3 '</Panel>'
    }

    function Text([int] $Depth, [string] $Id, [bool] $Meter) {
        Line $Depth "<Panel class=""lk-text"" $n>"
        Line ($Depth + 1) "<Label class=""lk-type"" text=""{s:${Id}_type}"" $n />"
        Line ($Depth + 1) "<Label class=""lk-name"" text=""{s:$Id}"" $n />"
        Line ($Depth + 1) "<Label class=""lk-unit"" text=""{s:${Id}_unit}"" $n />"
        Line ($Depth + 1) "<Label class=""lk-badge"" text=""{s:${Id}_badge}"" $n />"
        Line ($Depth + 1) "<Label class=""lk-sub"" text=""{s:${Id}_sub}"" $n />"
        # A ProgressBarComponent's meter, under its label.
        if ($Meter) { Line ($Depth + 1) "<Panel class=""chud-meter ls-meter"" $n><Panel class=""chud-meter__fill"" $n /></Panel>" }
        Line $Depth '</Panel>'
    }

    function Rating([int] $Depth, [string] $Id, [string] $Class) {
        Line $Depth "<Panel class=""lk-rating $Class"" $n><Label class=""lk-rating__major"" text=""{s:${Id}_rating}"" $n /><Label class=""lk-rating__minor"" text=""{s:${Id}_rating_minor}"" $n /></Panel>"
    }

    function Row([int] $Depth, [int] $Index) {
        $id = "option_$Index"
        Line $Depth "<Panel id=""$id"" class=""lk-slot ls-row hidden"">"
        # A `bar=` share fills the row behind its text.
        Line ($Depth + 1) "<Panel class=""chud-meter ls-fill"" $n><Panel class=""chud-meter__fill"" $n /></Panel>"
        Line ($Depth + 1) "<Button id=""${id}_hit"" class=""lk-hit ls-hit"">"
        $d = $Depth + 2
        Line $d "<Panel class=""ls-edge"" $n />"
        if (-not $Mouse) { Line $d "<Label class=""ls-key"" text=""{s:${id}_key}"" $n />" }
        Line $d "<Panel class=""ls-stripe"" $n />"
        Line $d "<Panel class=""ls-art"" $n>"
        Line ($d + 1) "<Panel id=""${id}_icon"" class=""lk-rarity hidden"" $n />"
        Line ($d + 1) "<Panel id=""${id}_thumb"" class=""lk-thumb hidden"" $n />"
        Line ($d + 1) "<Panel class=""lk-mark"" $n><Label class=""lk-mark__text"" text=""{s:${id}_mark}"" $n /></Panel>"
        Line ($d + 1) "<Panel id=""${id}_glyph"" class=""chud-ico lk-glyph hidden"" $n><Panel class=""chud-ico__badge"" $n /></Panel>"
        Rating ($d + 1) $id 'ls-rating-lead'
        Line $d '</Panel>'
        Text $d $id $true
        Rating $d $id 'ls-rating-end'
        Line $d "<Panel class=""lk-tags"" $n><Label class=""lk-tag"" text=""{s:${id}_tag}"" $n /><Label class=""lk-tag2"" text=""{s:${id}_tag2}"" $n /></Panel>"
        # A slider's track; in the mouse file it sits with the steppers instead.
        if (-not $Mouse) { Line $d "<Panel class=""chud-meter ls-track"" $n><Panel class=""chud-meter__fill"" $n /></Panel>" }
        Line $d "<Panel class=""ls-end"" $n>"
        if (-not $Mouse) { Line ($d + 1) "<Label class=""ls-arrow"" text=""‹"" $n />" }
        # The value with its small print (`note=`, a poll's vote count) under it.
        Line ($d + 1) "<Panel class=""ls-val"" $n>"
        Line ($d + 2) "<Panel class=""lk-end"" $n><Panel id=""${id}_vglyph"" class=""chud-ico lk-vglyph hidden"" $n><Panel class=""chud-ico__badge"" $n /></Panel><Label class=""lk-value"" text=""{s:${id}_value}"" $n /></Panel>"
        Line ($d + 2) "<Label class=""lk-note"" text=""{s:${id}_note}"" $n />"
        Line ($d + 1) '</Panel>'
        Line ($d + 1) "<Panel class=""chud-ico ico-edit ls-pen"" $n />"
        if (-not $Mouse) { Line ($d + 1) "<Label class=""ls-arrow ls-arrow--next"" text=""›"" $n />" }
        Line ($d + 1) "<Panel class=""chud-ico ico-cycle ls-cycle"" $n />"
        Line $d '</Panel>'
        Line $d "<Panel class=""lk-switch"" $n><Panel class=""lk-switch__knob"" $n /></Panel>"
        Line $d "<Label class=""lk-chev"" text=""›"" $n />"
        Line ($Depth + 1) '</Button>'
        if ($Mouse) {
            # Over the row's right end: a slider's track as click targets, then ‹ value ›.
            Line ($Depth + 1) "<Panel class=""ls-ctl"" $n>"
            Line ($Depth + 2) "<Panel class=""ls-ctl__track"" $n>"
            Line ($Depth + 3) "<Panel class=""chud-meter ls-track"" $n><Panel class=""chud-meter__fill"" $n /></Panel>"
            Line ($Depth + 3) "<Panel class=""ls-segs"" $n>"
            for ($s = 0; $s -lt $Segments; $s++) { Line ($Depth + 4) "<Button id=""${id}_seg$s"" class=""ls-seg"" />" }
            Line ($Depth + 3) '</Panel>'
            Line ($Depth + 2) '</Panel>'
            Line ($Depth + 2) "<Button id=""${id}_dec"" class=""ls-step""><Label text=""‹"" $n /></Button>"
            Line ($Depth + 2) "<Label class=""ls-ctl__value"" text=""{s:${id}_value}"" $n />"
            Line ($Depth + 2) "<Button id=""${id}_inc"" class=""ls-step""><Label text=""›"" $n /></Button>"
            Line ($Depth + 1) '</Panel>'
        }
        Line $Depth '</Panel>'
    }

    function Foot([int] $Depth, [int] $Index) {
        $id = "foot_$Index"
        Line $Depth "<Panel id=""$id"" class=""lk-slot ls-foot-row hidden"">"
        Line ($Depth + 1) "<Button id=""${id}_hit"" class=""lk-hit ls-hit"">"
        $d = $Depth + 2
        Line $d "<Panel id=""${id}_glyph"" class=""chud-ico lk-glyph hidden"" $n><Panel class=""chud-ico__badge"" $n /></Panel>"
        Line $d "<Panel class=""lk-pips"" $n>"
        for ($p = 0; $p -lt $Pips; $p++) { Line ($d + 1) "<Panel class=""lk-pip lk-pip-$p"" $n />" }
        Line $d '</Panel>'
        Text $d $id $false
        Line $d "<Panel class=""lk-tags"" $n><Label class=""lk-tag"" text=""{s:${id}_tag}"" $n /><Label class=""lk-tag2"" text=""{s:${id}_tag2}"" $n /></Panel>"
        Line $d "<Label class=""lk-note"" text=""{s:${id}_note}"" $n />"
        Line $d "<Panel class=""lk-end"" $n><Panel id=""${id}_vglyph"" class=""chud-ico lk-vglyph hidden"" $n><Panel class=""chud-ico__badge"" $n /></Panel><Label class=""lk-value"" text=""{s:${id}_value}"" $n /></Panel>"
        Line ($Depth + 1) '</Button>'
        Line $Depth '</Panel>'
    }

    $econ = if ($Stickers) { @('econ_thumbs', 'econ_stickers') } else { @('econ_thumbs') }
    $sheets = if ($V2) { @('customhud_tokens', 'v2/menu', 'v2/menu_mouse') + $econ + @('locker_art', 'customhud_icons', 'v2/locker', 'v2/list', 'menu_list_v2') }
              else { @('customhud_tokens', 'menu', 'menu_mouse') + $econ + @('locker_art', 'customhud_icons', 'locker', 'list') }
    Line 0 '<root>'
    Line 1 '<!-- Generated by scripts/generate-list-layout.ps1. Do not edit. -->'
    Line 1 '<styles>'
    foreach ($sheet in $sheets) { Line 2 "<include src=""s2r://panorama/styles/custom_game/customhud/$sheet.vcss_c"" />" }
    Line 1 '</styles>'
    Line 1 "<Panel class=""chud-root"" $n>"
    $mode = if ($Mouse) { 'ls-mouse' } else { 'ls-keys' }
    if ($V2) { Head2 $mode } else { Head $mode }

    $scroll = if ($Mouse) { ' chud-scroll' } else { '' }
    Line 3 "<Panel id=""options"" class=""ls-list$scroll"">"
    Line 4 '<Panel class="ls-items">'
    $slots = if ($Mouse) { $MouseSlots } else { $KeySlots }
    for ($i = 0; $i -lt $slots; $i++) { Row 5 $i }
    Line 4 '</Panel>'
    Line 3 '</Panel>'

    Line 3 '<Panel class="ls-foot">'
    for ($i = 0; $i -lt $FootSlots; $i++) { Foot 4 $i }
    Line 3 '</Panel>'

    if ($Mouse -and $V2) {
        Line 3 '<Panel class="ls2-bar">'
        Line 4 "<Label id=""footer"" class=""ls2-hint"" text=""{s:footer}"" $n />"
        Line 4 "<Button id=""nav_previous"" class=""ls2-pager hidden""><Label text=""‹"" $n /></Button>"
        Line 4 "<Panel id=""page"" class=""ls-page hidden"" $n><Label class=""ls-page__num"" text=""{s:page}"" $n /></Panel>"
        Line 4 "<Button id=""nav_next"" class=""ls2-pager hidden""><Label text=""›"" $n /></Button>"
        Line 3 '</Panel>'
    }
    elseif ($Mouse) {
        Line 3 '<Panel class="chud-bar ls-bar">'
        Line 4 "<Button id=""nav_back"" class=""chud-nav chud-nav--back hidden""><Label text=""&lt;"" $n /></Button>"
        Line 4 "<Label id=""footer"" class=""chud-bar__hint ls-hint"" text=""{s:footer}"" $n />"
        Line 4 "<Panel id=""page"" class=""ls-page hidden"" $n><Label class=""ls-page__num"" text=""{s:page}"" $n /></Panel>"
        Line 4 "<Button id=""nav_previous"" class=""chud-nav chud-nav--page hidden""><Label text=""‹"" $n /></Button>"
        Line 4 "<Button id=""nav_next"" class=""chud-nav chud-nav--page hidden""><Label text=""›"" $n /></Button>"
        Line 4 "<Button id=""nav_close"" class=""chud-nav chud-nav--close hidden""><Label text=""×"" $n /></Button>"
        Line 3 '</Panel>'
    }
    else {
        # The plain hint sits inside the legend: a sheet can only hide it from an ancestor's
        # class, and #legend carries has-legend while its keycaps show.
        $bar = if ($V2) { 'ls-bar ls2-bar' } else { 'ls-bar' }
        Line 3 "<Panel class=""$bar"" $n>"
        Line 4 "<Panel id=""legend"" class=""ls-legend"" $n>"
        for ($i = 0; $i -lt $Legend; $i++) {
            Line 5 "<Panel id=""legend_$i"" class=""ls-lg hidden"" $n>"
            Line 6 "<Label class=""ls-cap"" text=""{s:legend_${i}_key}"" $n />"
            Line 6 "<Label class=""ls-cap ls-cap2"" text=""{s:legend_${i}_key2}"" $n />"
            Line 6 "<Label class=""ls-verb"" text=""{s:legend_${i}_verb}"" $n />"
            Line 5 '</Panel>'
        }
        Line 5 "<Label id=""footer"" class=""ls-hint"" text=""{s:footer}"" $n />"
        Line 4 '</Panel>'
        Line 4 "<Panel id=""page"" class=""ls-page hidden"" $n>"
        Line 5 "<Label class=""ls-page__num"" text=""{s:page}"" $n />"
        Line 5 "<Panel class=""ls-dots"" $n>"
        for ($i = 1; $i -le $Dots; $i++) { Line 6 "<Panel class=""ls-dot ls-dot-$i"" $n />" }
        Line 5 '</Panel>'
        Line 4 '</Panel>'
        Line 3 '</Panel>'
    }

    Line 2 '</Panel>'
    Line 1 '</Panel>'
    Line 0 '</root>'
    return $sb.ToString()
}

foreach ($file in @(
    @{ Name = 'menu_list.vxml'; Mouse = $false; Stickers = $false; V2 = $false },
    @{ Name = 'menu_list_mouse.vxml'; Mouse = $true; Stickers = $false; V2 = $false },
    @{ Name = 'menu_list_skins.vxml'; Mouse = $false; Stickers = $true; V2 = $false },
    @{ Name = 'menu_list_v2.vxml'; Mouse = $false; Stickers = $false; V2 = $true },
    @{ Name = 'menu_list_mouse_v2.vxml'; Mouse = $true; Stickers = $false; V2 = $true },
    @{ Name = 'menu_list_skins_v2.vxml'; Mouse = $false; Stickers = $true; V2 = $true })) {
    $text = New-Layout $file.Mouse $file.Stickers $file.V2
    $path = Join-Path $OutputDir $file.Name
    [IO.File]::WriteAllText($path, $text.Replace("`r`n", "`n"), (New-Object System.Text.UTF8Encoding $false))
    $panels = ([regex]::Matches($text, '<(Panel|Button|Label)\b')).Count
    Write-Host ("{0} panels -> {1}" -f $panels, $path)
}
