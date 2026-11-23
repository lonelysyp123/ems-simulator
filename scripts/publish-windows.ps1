#Requires -Version 5.1
<#
.SYNOPSIS
  EssSimulator Windows x64 发布打包（Windows 原生版，逻辑等价于 scripts/publish-windows.sh）。

.DESCRIPTION
  1. 构建 Vue3 + Vite 前端到 wwwroot/；
  2. dotnet publish 自包含单文件到 dist/win-x64；
  3. 拷贝运行期配置、点表、组态种子、文档与 iec61850 原生库；
  4. 生成 dist/EssSimulator-win-x64.zip。

.EXAMPLE
  .\scripts\publish-windows.cmd
#>
[CmdletBinding()]
param(
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Show-Help {
    Write-Host @'
EssSimulator Windows x64 发布打包（Windows 原生版）

用法:
  .\scripts\publish-windows.cmd

输出:
  dist\win-x64\                  解压目录
  dist\EssSimulator-win-x64.zip  发布包

前置条件:
  .NET 8 SDK、Node.js LTS（含 npm），以及已安装的前端依赖
  （缺 node_modules 时脚本会自动执行 npm install）。

直接运行 PowerShell 脚本（执行策略为 Restricted 时需要 Bypass）:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-windows.ps1
'@
}

if ($Help) {
    Show-Help
    exit 0
}

# 从旧终端/旧 Explorer 启动时，进程环境可能还是安装 Node/.NET 之前的快照。
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ($machinePath -or $userPath) {
    $env:Path = (@($machinePath, $userPath, $env:Path) | Where-Object { $_ }) -join ';'
}

$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root 'EssSimulator.csproj'))) {
    Write-Host "错误: 找不到仓库根目录（$Root）" -ForegroundColor Red
    exit 1
}

# 点表校验与 dev-up.ps1 共用同一份实现（对应 pointmap-common.sh）
. (Join-Path $PSScriptRoot 'pointmap-common.ps1')

$DistDir = Join-Path $Root 'dist'
$Out = Join-Path $DistDir 'win-x64'
$Zip = Join-Path $DistDir 'EssSimulator-win-x64.zip'

$WebDir = Join-Path $Root 'Web'
if (-not (Test-Path $WebDir) -and (Test-Path (Join-Path $Root 'web'))) {
    $WebDir = Join-Path $Root 'web'
}

function Assert-Command([string]$Name, [string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "找不到 $Name。$Hint"
    }
}

function Assert-UnderRoot([string]$Path) {
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    $full = [System.IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝操作仓库外路径: $full"
    }
}

function Invoke-Native([string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory) {
    Push-Location $WorkingDirectory
    try {
        & $FilePath @Arguments
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    if ($code -ne 0) {
        throw "命令失败（退出码 $code）: $FilePath $($Arguments -join ' ')"
    }
}

function Build-Frontend {
    Write-Host '==> Building web frontend (Vue3 + Vite)...'
    if (-not (Test-Path $WebDir)) {
        throw '找不到前端目录 Web/（或 web/）'
    }
    if (-not (Test-Path (Join-Path $WebDir 'node_modules'))) {
        Write-Host '    installing npm dependencies...'
        Invoke-Native $env:ComSpec @('/c', 'npm install') $WebDir
    }
    Invoke-Native $env:ComSpec @('/c', 'npm run build') $WebDir
    Write-Host '    wwwroot/ populated.'
}

function Copy-PointmapsTo([string]$DestinationRoot) {
    $modelsSrc = Join-Path $Root 'pointmaps\models'
    if (-not (Test-Path $modelsSrc)) {
        Write-Host "==> Skip device models (not found: $modelsSrc)" -ForegroundColor Yellow
        return
    }

    $pointmapsDst = Join-Path $DestinationRoot 'pointmaps'
    $modelsDst = Join-Path $pointmapsDst 'models'
    if (Test-Path $pointmapsDst) {
        Remove-Item -LiteralPath $pointmapsDst -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $modelsDst | Out-Null
    Get-ChildItem -Path $modelsSrc -Force | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination $modelsDst -Recurse -Force
    }
    Write-Host '    pointmaps/models/'
}

function Copy-RuntimeFiles {
    Write-Host '==> Copying runtime config...'
    foreach ($name in @('appsettings.json', 'log4net.config', 'autotest.json')) {
        $src = Join-Path $Root $name
        if (-not (Test-Path $src)) {
            throw "缺少运行期文件: $src"
        }
        Copy-Item -LiteralPath $src -Destination (Join-Path $Out $name) -Force
        Write-Host "    $name"
    }

    Copy-PointmapsTo $Out

    $outConfigs = Join-Path $Out 'configs'
    if (Test-Path $outConfigs) {
        Remove-Item -LiteralPath $outConfigs -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $outConfigs | Out-Null

    foreach ($name in @('protocol-bindings.json', 'ems-strategy.json')) {
        $src = Join-Path $Root "configs\$name"
        if (Test-Path $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $outConfigs $name) -Force
            Write-Host "    configs/$name"
        }
    }

    $topoSrc = Join-Path $Root 'configs\topology'
    if (Test-Path $topoSrc) {
        $topoDst = Join-Path $outConfigs 'topology'
        if (Test-Path $topoDst) {
            Remove-Item -LiteralPath $topoDst -Recurse -Force
        }
        Copy-Item -LiteralPath $topoSrc -Destination $topoDst -Recurse -Force

        # 运行期目录不入包：只带 seed/，首次启动由 TopologyStore 铺出预置工程与设备库
        foreach ($name in @('project.json', 'device-models.json')) {
            $path = Join-Path $topoDst $name
            if (Test-Path $path) {
                Remove-Item -LiteralPath $path -Force
            }
        }
        foreach ($name in @('projects', 'library', 'generated')) {
            $path = Join-Path $topoDst $name
            if (Test-Path $path) {
                Remove-Item -LiteralPath $path -Recurse -Force
            }
        }

        $runtimeMode = @'
{
  "engineeringMode": false,
  "activeProjectId": null,
  "activeProjectName": null,
  "updatedAtUtc": "1970-01-01T00:00:00Z"
}
'@
        [System.IO.File]::WriteAllText(
            (Join-Path $topoDst 'runtime-mode.json'),
            $runtimeMode,
            (New-Object System.Text.UTF8Encoding($false)))
        Write-Host '    configs/topology/'
    }

    $docsSrc = Join-Path $Root 'docs'
    if (Test-Path $docsSrc) {
        $docsDst = Join-Path $Out 'docs'
        if (Test-Path $docsDst) {
            Remove-Item -LiteralPath $docsDst -Recurse -Force
        }
        Copy-Item -LiteralPath $docsSrc -Destination $docsDst -Recurse -Force
        Write-Host '    docs/'
    }
}

try {
    Assert-Command 'dotnet.exe' '请安装 .NET 8 SDK（https://dotnet.microsoft.com/download）。'
    Assert-Command 'npm.cmd' '请安装 Node.js LTS（winget install OpenJS.NodeJS.LTS）。'

    if (-not (Test-DeviceModels -Root $Root)) {
        throw '点表校验失败，已停止发布。'
    }

    Build-Frontend

    Write-Host "==> Cleaning previous output: $Out"
    Assert-UnderRoot $Out
    if (Test-Path $Out) {
        Remove-Item -LiteralPath $Out -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $Out | Out-Null

    Write-Host '==> Publishing EssSimulator for Windows x64 (self-contained)...'
    Invoke-Native 'dotnet.exe' @(
        'publish', 'EssSimulator.csproj',
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:EnableCompressionInSingleFile=true',
        '-o', $Out
    ) $Root

    Copy-RuntimeFiles

    $nativeSrc = Join-Path $Root 'lib61850\native\win-x64\iec61850.dll'
    if (-not (Test-Path $nativeSrc)) {
        throw "缺少原生库: $nativeSrc"
    }
    Copy-Item -LiteralPath $nativeSrc -Destination (Join-Path $Out 'iec61850.dll') -Force
    $runtimeNativeDir = Join-Path $Out 'runtimes\win-x64\native'
    New-Item -ItemType Directory -Force -Path $runtimeNativeDir | Out-Null
    Copy-Item -LiteralPath $nativeSrc -Destination (Join-Path $runtimeNativeDir 'iec61850.dll') -Force

    foreach ($name in @('start.bat', 'README-Windows.txt')) {
        $src = Join-Path $Root "scripts\windows\$name"
        if (-not (Test-Path $src)) {
            throw "缺少发布辅助文件: $src"
        }
        Copy-Item -LiteralPath $src -Destination (Join-Path $Out $name) -Force
    }

    Write-Host "==> Creating zip: $Zip"
    if (Test-Path $Zip) {
        Remove-Item -LiteralPath $Zip -Force
    }
    Compress-Archive -Path $Out -DestinationPath $Zip -CompressionLevel Optimal -Force

    Write-Host 'Done.'
    Write-Host '  Point maps: pointmaps/models/ (选型型回退 standard；LC 片段拼装)'
    Write-Host "  Folder: $Out"
    Write-Host "  Zip:    $Zip"
    foreach ($path in @((Join-Path $Out 'EssSimulator.exe'), $Zip)) {
        $item = Get-Item -LiteralPath $path
        Write-Host ("  {0}  {1:N1} MB" -f $item.FullName, ($item.Length / 1MB))
    }
}
catch {
    Write-Host ''
    Write-Host "错误: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
