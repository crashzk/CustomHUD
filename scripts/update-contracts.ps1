# Builds each provider plugin's public contract and copies it into lib/, where CustomHUD.csproj
# references it with Private=false. The provider ships the assembly in its own resources/exports,
# so CustomHUD must never ship a copy: a second one is a second type identity and the shared
# interface would never resolve.
#
# Run after a provider's contract changes. Providers are expected next to this repository:
#   ..\Ranks, ..\WeaponSkins, ..\ShopCore, ..\MapChooser, ..\VIPCore, ..\Admins
# MixScrims is rebuilt only when -MixScrims names the root of its repository.
param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path,
    [string]$MixScrims
)

$ErrorActionPreference = 'Stop'
$lib = Join-Path $PSScriptRoot '..\lib'

$contracts = @(
    @{ Project = 'Ranks\Ranks.Contract\Ranks.Contract.csproj'; License = 'Ranks\LICENSE' },
    @{ Project = 'Ranks\Modules\Ranks_WeaponStats\Ranks_WeaponStats.Contract\Ranks_WeaponStats.Contract.csproj'; License = 'Ranks\LICENSE' },
    @{ Project = 'Ranks\Modules\Ranks_CustomCombos\Ranks_CustomCombos.Contract\Ranks_CustomCombos.Contract.csproj'; License = 'Ranks\LICENSE' },
    @{ Project = 'Ranks\Modules\Ranks_Prestige\Ranks_Prestige.Contract\Ranks_Prestige.Contract.csproj'; License = 'Ranks\LICENSE' },
    @{ Project = 'WeaponSkins\WeaponSkins.Shared\WeaponSkins.Shared.csproj'; License = 'WeaponSkins\LICENSE' },
    @{ Project = 'ShopCore\ShopCore.Contract\ShopCore.Contract.csproj'; License = 'ShopCore\LICENSE' },
    @{ Project = 'MapChooser\MapChooser.Contract\MapChooser.Contract.csproj'; License = 'MapChooser\LICENSE' },
    @{ Project = 'VIPCore\VIPCore.Contract\VIPCore.Contract.csproj'; License = 'VIPCore\LICENSE.MIT' },
    @{ Project = 'Admins\Admins.Contract\Admins.Contract.csproj'; License = 'Admins\LICENSE' }
)
if ($MixScrims) {
    $mix = (Resolve-Path $MixScrims).Path
    $contracts += @{ Project = (Join-Path $mix 'MixScrims.Contract\MixScrims.Contract.csproj'); License = (Join-Path $mix 'LICENSE') }
}

foreach ($contract in $contracts) {
    # Combine keeps a rooted path (-MixScrims) as it is.
    $project = [IO.Path]::Combine($Root, $contract.Project)
    if (-not (Test-Path $project)) {
        Write-Warning "Skipped: $($contract.Project) not found."
        continue
    }

    dotnet build $project -c Release --nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($contract.Project)" }

    $target = (dotnet msbuild $project -getProperty:TargetPath -p:Configuration=Release).Trim()
    Copy-Item $target $lib -Force
    $name = [IO.Path]::GetFileNameWithoutExtension($target)

    $license = [IO.Path]::Combine($Root, $contract.License)
    if (Test-Path $license) { Copy-Item $license (Join-Path $lib "$name.LICENSE.txt") -Force }

    Write-Host "lib/$name.dll <- $($contract.Project)"
}
