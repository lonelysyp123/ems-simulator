$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path))
$Edition = if ($env:EDITION) { $env:EDITION } else { "社区版" }
$Rid = "win-x64"
$Out = Join-Path $Root "dist\$Edition\$Rid"
$Zip = Join-Path $Root "dist\EssSimulator-$Edition-$Rid.zip"

Push-Location $Root
try {
    if (-not (Test-Path (Join-Path $Root "configs\$Edition.appsettings.json"))) {
        throw "未知版本: $Edition（可选: 社区版, 充值版, 定制版）"
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $Root "dist\$Edition") | Out-Null

    Write-Host "==> Publishing EssSimulator [$Edition] for Windows x64 (self-contained)..."
    dotnet publish EssSimulator.csproj `
        -c Release `
        -r $Rid `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -o $Out

    & bash "$Root/scripts/commercial/sync-runtime.sh" $Edition $Rid

    $native = Join-Path $Root "lib61850\native\win-x64\iec61850.dll"
    if (Test-Path $native) {
        Copy-Item $native (Join-Path $Out "iec61850.dll") -Force
        $ridNative = Join-Path $Out "runtimes\win-x64\native"
        New-Item -ItemType Directory -Force -Path $ridNative | Out-Null
        Copy-Item $native (Join-Path $ridNative "iec61850.dll") -Force
    }

    if (Test-Path $Zip) { Remove-Item $Zip -Force }
    Compress-Archive -Path (Join-Path $Out "*") -DestinationPath $Zip -Force

    Write-Host "Done."
    Write-Host "  Edition: $Edition"
    Write-Host "  Folder:  $Out"
    Write-Host "  Zip:     $Zip"
}
finally {
    Pop-Location
}
