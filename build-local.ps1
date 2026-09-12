$ErrorActionPreference = "Stop"
$ProgressPreference = "Continue"

Write-Host "PowerShell started successfully." -ForegroundColor Green
Write-Host "Preparing PrismCast build environment..." -ForegroundColor Cyan

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Tools = Join-Path $Root ".tools"
$PrivateDotnetDir = Join-Path $Tools "dotnet"
$PrivateDotnet = Join-Path $PrivateDotnetDir "dotnet.exe"
$NugetPackages = Join-Path $Tools "nuget-packages"
$BootstrapDir = Join-Path $Tools "sdk-bootstrap"
$Dist = Join-Path $Root "dist"
$Dev = Join-Path $Dist "PrismCast-Dev"
$Project = Join-Path $Root "PrismCast\PrismCast.csproj"
$NugetConfig = Join-Path $Root "NuGet.Config"
$Log = Join-Path $Root "build.log"

New-Item -ItemType Directory -Force -Path $Tools, $NugetPackages, $Dist | Out-Null
$env:NUGET_PACKAGES = $NugetPackages
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

function Write-Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Find-Dotnet10 {
    # Prefer an existing system .NET 10 SDK. A runtime-only installation does not
    # count; --list-sdks must report an actual 10.x SDK before it is selected.
    $systemDotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($systemDotnet) {
        $sdkList = & $systemDotnet.Source --list-sdks 2>$null
        if ($sdkList -match '(?m)^10\.') {
            Write-Step "Using installed .NET 10 SDK"
            return $systemDotnet.Source
        }
    }

    $sdkRoot = Join-Path $PrivateDotnetDir "sdk"
    $hasPrivateSdk10 = $false
    if ((Test-Path $PrivateDotnet) -and (Test-Path $sdkRoot)) {
        $sdk10 = Get-ChildItem $sdkRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^10\.' } |
            Select-Object -First 1
        $hasPrivateSdk10 = ($null -ne $sdk10)
    }

    if ($hasPrivateSdk10) {
        return $PrivateDotnet
    }

    # If an interrupted install left a partial tool directory behind, clear only
    # PrismCast's private .tools\dotnet directory and install a clean SDK.
    if (Test-Path $PrivateDotnetDir) {
        Write-Step "Repairing private .NET 10 SDK"
        Remove-Item $PrivateDotnetDir -Recurse -Force
    }
    else {
        Write-Step "Installing private .NET 10 SDK"
    }

    New-Item -ItemType Directory -Force -Path $PrivateDotnetDir | Out-Null
    $installer = Join-Path $Tools "dotnet-install.ps1"
    Write-Host "Downloading the Microsoft .NET installer..." -ForegroundColor Yellow
    Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer -UseBasicParsing

    # Installer output goes straight to the console. We validate installation by
    # checking the actual executable and SDK directory instead of trusting
    # LASTEXITCODE, which is unreliable here when dotnet-install reports that an
    # SDK is already present.
    Write-Host "Downloading and installing .NET SDK 10.0.401. This is the longest first-build step." -ForegroundColor Yellow
    & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $installer -Version 10.0.401 -InstallDir $PrivateDotnetDir | Out-Host

    $sdkRoot = Join-Path $PrivateDotnetDir "sdk"
    $sdk10 = if (Test-Path $sdkRoot) {
        Get-ChildItem $sdkRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^10\.' } |
            Select-Object -First 1
    } else { $null }

    if (-not (Test-Path $PrivateDotnet) -or $null -eq $sdk10) {
        throw "Private .NET 10 installation failed."
    }

    return $PrivateDotnet
}

function Find-Dalamud {
    $candidates = @()

    if ($env:DALAMUD_HOME) {
        $candidates += $env:DALAMUD_HOME
    }

    if ($env:APPDATA) {
        $candidates += (Join-Path $env:APPDATA "XIVLauncher\addon\Hooks\dev")
    }

    foreach ($candidate in $candidates | Select-Object -Unique) {
        if (-not $candidate) { continue }
        if (Test-Path (Join-Path $candidate "Dalamud.dll")) {
            return (Resolve-Path $candidate).Path
        }
    }

    throw @"
Could not locate the Dalamud development binaries.
Launch FFXIV through XIVLauncher at least once, then try again.
Expected location is normally:
  %APPDATA%\XIVLauncher\addon\Hooks\dev
"@
}

try {
    try { Start-Transcript -Path $Log -Force | Out-Null } catch {
        Write-Host "Warning: build logging could not start, but the build will continue." -ForegroundColor Yellow
    }

    Write-Host "PrismCast local build" -ForegroundColor White
    Write-Host "Folder: $Root" -ForegroundColor Gray

    # Validate the complete source package before downloading an SDK or compiling.
    # These files are copied into the public plugin package after the build.
    $requiredInputs = @(
        $Project,
        $NugetConfig,
        (Join-Path $Root "LICENSE.md"),
        (Join-Path $Root "THIRD_PARTY_NOTICES.md"),
        (Join-Path $Root "NOTICE"),
        (Join-Path $Root "licenses")
    )
    $missingInputs = @($requiredInputs | Where-Object { -not (Test-Path $_) })
    if ($missingInputs.Count -gt 0) {
        throw "Source package is incomplete. Missing required path(s):`n  $($missingInputs -join "`n  ")`nDownload and extract the complete PrismCast source package again."
    }

    $Dotnet = Find-Dotnet10
    if ($Dotnet -eq $PrivateDotnet) {
        $env:DOTNET_ROOT = $PrivateDotnetDir
        $env:DOTNET_ROOT_X64 = $PrivateDotnetDir
        $env:PATH = $PrivateDotnetDir + ";" + $env:PATH
    }
    else {
        $systemDotnetDir = Split-Path -Parent $Dotnet
        $env:PATH = $systemDotnetDir + ";" + $env:PATH
    }
    Write-Host "Using .NET: $Dotnet"

    Write-Step "Bootstrapping Dalamud.NET.Sdk 15.0.0"
    New-Item -ItemType Directory -Force -Path $BootstrapDir | Out-Null
    $bootstrapProject = Join-Path $BootstrapDir "SdkBootstrap.csproj"
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Dalamud.NET.Sdk" Version="15.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 $bootstrapProject

    & $Dotnet restore $bootstrapProject --configfile $NugetConfig
    if ($LASTEXITCODE -ne 0) { throw "Dalamud.NET.Sdk bootstrap restore failed." }

    $Dalamud = Find-Dalamud
    $DalamudLibPath = $Dalamud.TrimEnd('\') + '\'
    $env:DALAMUD_HOME = $Dalamud
    Write-Host "Using Dalamud: $Dalamud"

    Write-Step "Restoring PrismCast"
    & $Dotnet restore $Project --configfile $NugetConfig "-p:DalamudLibPath=$DalamudLibPath"
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }

    Write-Step "Building PrismCast Release/x64"
    & $Dotnet build $Project -c Release -p:Platform=x64 "-p:DalamudLibPath=$DalamudLibPath" --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed." }

    $outputCandidates = @(
        (Join-Path $Root "PrismCast\bin\x64\Release"),
        (Join-Path $Root "PrismCast\bin\Release")
    )
    $Output = $outputCandidates | Where-Object { Test-Path (Join-Path $_ "PrismCast.dll") } | Select-Object -First 1
    if (-not $Output) { throw "Could not find PrismCast.dll in the build output." }

    Write-Step "Packaging"
    Remove-Item $Dev -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $Dev | Out-Null
    Get-ChildItem $Output -File | Where-Object {
        $_.Extension -in '.dll', '.json', '.pdb'
    } | Copy-Item -Destination $Dev -Force
    Copy-Item (Join-Path $Root "LICENSE.md") $Dev -Force
    Copy-Item (Join-Path $Root "THIRD_PARTY_NOTICES.md") $Dev -Force
    Copy-Item (Join-Path $Root "NOTICE") $Dev -Force
    Copy-Item (Join-Path $Root "licenses") (Join-Path $Dev "licenses") -Recurse -Force

    $Zip = Join-Path $Dist "PrismCast.zip"
    Remove-Item $Zip -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $Dev "*") -DestinationPath $Zip -Force

    Write-Host ""
    Write-Host "BUILD COMPLETE" -ForegroundColor Green
    Write-Host "Dev DLL: $Dev\PrismCast.dll" -ForegroundColor White
    Write-Host "Package: $Zip" -ForegroundColor White
    Write-Host ""
    Write-Host "Dalamud Dev Plugin Location must point to the DLL above, not the folder."
}
catch {
    Write-Host ""
    Write-Host "================ BUILD ERROR ================" -ForegroundColor Red
    Write-Host $_ -ForegroundColor Red
    Write-Host "=============================================" -ForegroundColor Red
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
