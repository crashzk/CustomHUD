<#
.SYNOPSIS
    Copilot hook: once a session has changed CustomHUD, its agent cannot finish a turn until
    scripts/audit.ps1 passes on exactly what it leaves behind.

.DESCRIPTION
    Register it user-wide, so it fires whichever folder a session opened: a repository's
    .github/hooks only load when the session starts inside that repository. In
    %USERPROFILE%\.copilot\hooks\customhud-audit.json, with <repo> this repository's path:

        { "version": 1, "hooks": {
            "sessionStart": [ { "type": "command", "timeoutSec": 30,
                "powershell": "& '<repo>\\scripts\\audit-hook.ps1' -Event sessionStart" } ],
            "agentStop":    [ { "type": "command", "timeoutSec": 120,
                "powershell": "& '<repo>\\scripts\\audit-hook.ps1' -Event agentStop" } ] } }

    sessionStart records the repository's fingerprint. At the end of each agent turn:
      - nothing changed since the session started: the agent finishes;
      - build/audit/receipt.json holds a full pass of exactly this state: the agent finishes;
      - otherwise the quick audit runs, and a failure becomes the agent's next prompt;
      - C#, Panorama or translations changed since the last full pass: one more prompt asks
        for the full audit (build, dependency check, compile).
    An identical prompt repeats at most twice. Any error in this hook lets the agent finish,
    because a broken hook must never trap a session. State and log live in
    %LOCALAPPDATA%\CustomHUD\audit-hook.

.PARAMETER Event
    sessionStart, agentStop, or subagentStop for a custom agent's own scoped Stop hook.
#>
param([ValidateSet('sessionStart', 'agentStop', 'subagentStop')] [string] $Event = 'agentStop')

# The hook's shell may be Windows PowerShell 5.1, under which the audit's generators write mojibake.
if ($PSVersionTable.PSVersion.Major -lt 7 -and (Get-Command pwsh -ErrorAction SilentlyContinue)) {
    $stdin = ''
    if ([Console]::IsInputRedirected) { $stdin = [Console]::In.ReadToEnd() }
    $answer = $stdin | pwsh -NoProfile -NonInteractive -File $PSCommandPath -Event $Event
    [Console]::Out.Write((@($answer) -join "`n"))
    return
}

$ErrorActionPreference = 'Stop'
$started  = Get-Date
$audit    = Join-Path $PSScriptRoot 'audit.ps1'
$repo     = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$stateDir = Join-Path $env:LOCALAPPDATA 'CustomHUD\audit-hook'
$log      = Join-Path $stateDir 'hook.log'

function Write-Log([string] $Text) {
    try {
        if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).Length -gt 1MB) { Remove-Item -LiteralPath $log -Force }
        Add-Content -LiteralPath $log -Value ('{0:u} {1,-12} {2,-10} {3} ({4:N1}s)' -f (Get-Date), $Event, $script:session, $Text, ((Get-Date) - $started).TotalSeconds)
    }
    catch { }
}

# stdout carries exactly one JSON object: the runtime parses all of it as the hook's answer.
function Send($Output, [string] $Why) {
    Write-Log $Why
    [Console]::Out.Write((ConvertTo-Json -InputObject $Output -Compress -Depth 4))
}

function Save-State {
    [IO.File]::WriteAllText($script:statePath, (ConvertTo-Json -InputObject $script:state -Compress -Depth 4), (New-Object System.Text.UTF8Encoding $false))
}

function Read-State {
    if (-not (Test-Path -LiteralPath $script:statePath)) { return $null }
    $state = @{}
    foreach ($p in (Get-Content -LiteralPath $script:statePath -Raw | ConvertFrom-Json).PSObject.Properties) { $state[$p.Name] = $p.Value }
    return $state
}

function Get-Key([string] $Text) {
    $sha = [Security.Cryptography.SHA256]::Create()
    return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))) -replace '-', '').Substring(0, 16)
}

# The user-wide hook and a custom agent's own Stop hook can fire for one stop: the second
# reuses the first's answer instead of auditing again and spending the block budget twice.
function Answer($Output, [string] $Why) {
    $script:state['last'] = @{ at = (Get-Date).ToUniversalTime().Ticks; all = $script:fingerprints['all']; output = (ConvertTo-Json -InputObject $Output -Compress -Depth 4) }
    Save-State
    Send $Output $Why
}

function Block([string] $Reason, [string] $Why) {
    $key = Get-Key $Reason
    $count = 1
    if ($script:state['blockKey'] -eq $key) { $count = [int] $script:state['blockCount'] + 1 }
    $script:state['blockKey'] = $key
    $script:state['blockCount'] = $count
    if ($count -gt 2) { Answer @{} "released after two identical blocks: $Why"; return }
    Answer @{ decision = 'block'; reason = $Reason } "blocked ($count): $Why"
}

function Pass([string] $Why) {
    $script:state['blockKey'] = ''
    $script:state['blockCount'] = 0
    Answer @{} $Why
}

$script:session = 'manual'
$mutex = $null
try {
    $payload = $null
    if ([Console]::IsInputRedirected) {
        $raw = [Console]::In.ReadToEnd()
        if ($raw.Trim()) { $payload = ConvertFrom-Json $raw }
    }
    if ($payload) {
        if ($payload.sessionId) { $script:session = [string] $payload.sessionId }
        elseif ($payload.session_id) { $script:session = [string] $payload.session_id }
    }
    $safe = $script:session -replace '[^\w-]', '_'
    $mutex = New-Object Threading.Mutex($false, "Local\CustomHUD-audit-hook-$safe")
    try { [void] $mutex.WaitOne(150000) } catch [Threading.AbandonedMutexException] { }

    $sessions = Join-Path $stateDir 'sessions'
    New-Item -ItemType Directory -Force -Path $sessions | Out-Null
    $script:statePath = Join-Path $sessions ($safe + '.json')
    $script:state = Read-State
    $script:fingerprints = & $audit -Fingerprint

    if ($Event -eq 'sessionStart') {
        if (-not $script:state) {
            $script:state = @{ baseline = $script:fingerprints['all']; blockKey = ''; blockCount = 0 }
            Save-State
        }
        Get-ChildItem -LiteralPath $sessions -File | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-14) } | Remove-Item -Force
        Send @{} 'baseline recorded'
        return
    }
    if (-not $script:state) {
        $script:state = @{ baseline = $script:fingerprints['all']; blockKey = ''; blockCount = 0 }
        Save-State
        Send @{} 'no baseline for this session yet; recorded one'
        return
    }
    $last = $script:state['last']
    if ($last -and $last.all -eq $script:fingerprints['all'] -and ((Get-Date).ToUniversalTime().Ticks - [long] $last.at) -lt 100000000) {
        Write-Log 'same state as the run a moment ago; same answer'
        [Console]::Out.Write([string] $last.output)
        return
    }
    if ($script:state['baseline'] -eq $script:fingerprints['all']) { Pass 'CustomHUD unchanged this session'; return }

    $receipt = $null
    $receiptPath = Join-Path $repo 'build\audit\receipt.json'
    if (Test-Path -LiteralPath $receiptPath) { $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json }
    if ($receipt -and $receipt.full -and $receipt.full.all -eq $script:fingerprints['all']) { Pass 'a full pass covers this state'; return }

    $json = Join-Path $stateDir ('quick-' + [guid]::NewGuid().ToString('N') + '.json')
    try {
        & $audit -Quick -JsonPath $json *> $null
        $result = Get-Content -LiteralPath $json -Raw | ConvertFrom-Json
    }
    finally { Remove-Item -LiteralPath $json -Force -ErrorAction SilentlyContinue }

    $command = "pwsh -NoProfile -File `"$audit`""
    if (-not $result.pass) {
        $errors = @($result.findings | Where-Object { $_.Level -eq 'error' })
        $lines = @(foreach ($f in ($errors | Select-Object -First 25)) {
            $where = if ($f.Line) { "$($f.File):$($f.Line)" } else { $f.File }
            "  $($f.Check)  $where  $($f.Message)"
        })
        if ($errors.Count -gt 25) { $lines += "  ...and $($errors.Count - 25) more" }
        $rules = @(foreach ($p in $result.rules.PSObject.Properties) { "  $($p.Name): $($p.Value)" })
        $reason = @(
            "[CustomHUD audit hook] This session changed CustomHUD and the quick audit fails with $($errors.Count) errors:"
            $lines
            ''
            'The rules behind them:'
            $rules
            ''
            "Fix them, rerun $command until it prints PASS, and only then finish."
            'If a finding is wrong, say so in your reply instead of working around it.'
        ) -join "`n"
        Block $reason "quick audit: $($errors.Count) errors"
        return
    }

    $stale = @('cs', 'panorama', 'i18n' | Where-Object { -not $receipt -or -not $receipt.full -or $receipt.full.$_ -ne $script:fingerprints[$_] })
    if ($stale.Count) {
        $reason = @(
            "[CustomHUD audit hook] This session changed CustomHUD ($($stale -join ', ')) since the last full audit, and the quick audit alone passes."
            "Run $command"
            'It publishes the plugin as CI does, runs SoftDependencyCheck and the wiki check, and compiles the changed Panorama files into the customhud addon.'
            'Fix whatever it reports, then finish. If it cannot run here, say so in your final message.'
        ) -join "`n"
        Block $reason ('full audit needed: ' + ($stale -join ', '))
        return
    }
    Pass 'quick audit passed and the full pass is current'
}
catch {
    Write-Log ('hook error, letting the agent finish: ' + $_.Exception.Message)
    [Console]::Out.Write('{}')
}
finally {
    if ($mutex) { try { $mutex.ReleaseMutex() } catch { }; $mutex.Dispose() }
}
