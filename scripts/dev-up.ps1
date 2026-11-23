#Requires -Version 5.1
<#
.SYNOPSIS
  EssSimulator 开发态一键启动（Windows 原生版，逻辑等价于 scripts/dev-up.sh）。

.DESCRIPTION
  同时启动后端 (dotnet run) 与前端 (Vite HMR)，Ctrl+C 一起退出。
  支持 -Backend / -Frontend / -NoOpen，以及 HTTP_PORT / VITE_PORT / VITE_BACKEND 环境变量。
  与 bash 版的行为差异：
    - 端口占用与进程树清理使用 Windows 的 Get-NetTCPConnection / taskkill；
    - npm 通过 cmd.exe 调用，避开 PowerShell 对 npm.ps1 的执行策略限制。

.EXAMPLE
  .\scripts\dev-up.cmd
  .\scripts\dev-up.cmd -Backend
  .\scripts\dev-up.cmd -Frontend -NoOpen
  $env:HTTP_PORT=5050; .\scripts\dev-up.cmd
#>
[CmdletBinding()]
param(
    [switch]$Backend,
    [switch]$Frontend,
    [switch]$NoOpen,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Show-Help {
    Write-Host @'
EssSimulator 开发态一键启动（Windows 原生版）

用法:
  .\scripts\dev-up.cmd                 # 启动前后端，Ctrl+C 一起退出
  .\scripts\dev-up.cmd -Backend        # 仅后端
  .\scripts\dev-up.cmd -Frontend       # 仅前端（需后端已在跑）
  .\scripts\dev-up.cmd -NoOpen         # 不自动打开浏览器
  $env:HTTP_PORT=5050; $env:VITE_PORT=5173; .\scripts\dev-up.cmd

直接运行 PowerShell 脚本（执行策略为 Restricted 时需要 Bypass）:
  powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\dev-up.ps1
'@
}

if ($Help) {
    Show-Help
    exit 0
}

if ($Backend -and $Frontend) {
    Write-Host '错误: -Backend 与 -Frontend 只能二选一' -ForegroundColor Red
    exit 1
}

# 从旧终端/旧 Explorer 启动时，进程环境可能还是安装 Node/.NET 之前的快照。
# 把注册表里的机器/用户 PATH 前置，保证 dotnet 与 npm.cmd 能找到。
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

# 点表校验与 publish-windows.ps1 共用同一份实现（对应 pointmap-common.sh）
. (Join-Path $PSScriptRoot 'pointmap-common.ps1')

$HttpPort = 5050
if ($env:HTTP_PORT) {
    $parsed = 0
    if (-not [int]::TryParse($env:HTTP_PORT, [ref]$parsed) -or $parsed -lt 1 -or $parsed -gt 65535) {
        Write-Host "错误: HTTP_PORT 不是有效端口: $env:HTTP_PORT" -ForegroundColor Red
        exit 1
    }
    $HttpPort = $parsed
}

$VitePort = 5173
if ($env:VITE_PORT) {
    $parsed = 0
    if (-not [int]::TryParse($env:VITE_PORT, [ref]$parsed) -or $parsed -lt 1 -or $parsed -gt 65535) {
        Write-Host "错误: VITE_PORT 不是有效端口: $env:VITE_PORT" -ForegroundColor Red
        exit 1
    }
    $VitePort = $parsed
}

$BackendUrl = "http://localhost:$HttpPort"
if ($env:VITE_BACKEND) {
    $BackendUrl = $env:VITE_BACKEND
}

$WebDir = Join-Path $Root 'Web'
if (-not (Test-Path $WebDir) -and (Test-Path (Join-Path $Root 'web'))) {
    $WebDir = Join-Path $Root 'web'
}

$Mode = 'all'
if ($Backend) { $Mode = 'backend' }
elseif ($Frontend) { $Mode = 'frontend' }

$script:BackendProc = $null
$script:FrontendProc = $null
$script:StartedBackend = $false
$script:StartedFrontend = $false

function Assert-Command([string]$Name, [string]$Hint) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "找不到 $Name。$Hint"
    }
}

function Test-PortInUse([int]$Port) {
    try {
        $conn = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        return ($null -ne $conn)
    }
    catch {
        $line = & netstat.exe -ano -p tcp 2>$null |
                Select-String -Pattern (':{0}\s+.*LISTENING' -f $Port)
        return ($null -ne $line)
    }
}

function Stop-Tree([int]$TargetPid) {
    if ($TargetPid -le 0) { return }
    try {
        & taskkill.exe /PID $TargetPid /T /F 2>$null | Out-Null
    }
    catch { }
}

function Stop-Port([int]$Port) {
    try {
        $conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        foreach ($c in $conns) {
            Stop-Tree ([int]$c.OwningProcess)
        }
        return
    }
    catch { }

    $lines = & netstat.exe -ano -p tcp 2>$null |
             Select-String -Pattern (':{0}\s+.*LISTENING' -f $Port)
    foreach ($line in $lines) {
        $parts = @($line.Line -split '\s+' | Where-Object { $_ -ne '' })
        if ($parts.Count -ge 5) {
            Stop-Tree ([int]$parts[-1])
        }
    }
}

function Start-Backend {
    if (Test-PortInUse $HttpPort) {
        throw "端口 $HttpPort 已被占用，请先释放或设置 HTTP_PORT=..."
    }
    if (-not (Test-DeviceModels -Root $Root)) {
        throw '点表校验失败，已停止启动。'
    }

    Write-Host "==> 启动后端: dotnet run (http://localhost:$HttpPort)"
    $env:Simulator__Web__HttpPort = "$HttpPort"
    $script:BackendProc = Start-Process -FilePath 'dotnet.exe' `
        -ArgumentList @('run', '--project', 'EssSimulator.csproj', '--no-launch-profile') `
        -WorkingDirectory $Root -NoNewWindow -PassThru
    $script:StartedBackend = $true
}

function Start-Frontend {
    if (Test-PortInUse $VitePort) {
        throw "端口 $VitePort 已被占用，请先释放或设置 VITE_PORT=..."
    }
    if (-not (Test-Path $WebDir)) {
        throw '找不到前端目录 Web/（或 web/）'
    }

    $nodeModules = Join-Path $WebDir 'node_modules'
    if (-not (Test-Path $nodeModules)) {
        Write-Host '==> 安装前端依赖 (npm install)...'
        Push-Location $WebDir
        try {
            & $env:ComSpec /c npm install
            if ($LASTEXITCODE -ne 0) {
                throw "npm install 失败（退出码 $LASTEXITCODE）"
            }
        }
        finally {
            Pop-Location
        }
    }

    Write-Host "==> 启动前端: Vite (http://localhost:$VitePort → $BackendUrl)"
    $env:VITE_BACKEND = $BackendUrl
    $script:FrontendProc = Start-Process -FilePath $env:ComSpec `
        -ArgumentList @('/c', "npm run dev -- --port $VitePort --host 0.0.0.0") `
        -WorkingDirectory $WebDir -NoNewWindow -PassThru
    $script:StartedFrontend = $true
}

function Wait-Http([string]$Url, [string]$Name, [int]$Retries = 60) {
    for ($i = 1; $i -le $Retries; $i++) {
        try {
            $resp = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
            if ($resp.StatusCode -ge 200 -and $resp.StatusCode -lt 400) {
                Write-Host "    $Name 就绪: $Url"
                return $true
            }
        }
        catch { }
        Start-Sleep -Milliseconds 500
    }

    Write-Host "    警告: 等待 $Name 超时 ($Url)" -ForegroundColor Yellow
    return $false
}

function Open-Ui([string]$Url) {
    if ($NoOpen) { return }
    try {
        Start-Process $Url | Out-Null
    }
    catch {
        Write-Host "    提示: 无法自动打开浏览器，请手动访问 $Url" -ForegroundColor Yellow
    }
}

function Stop-All {
    if (-not $script:StartedBackend -and -not $script:StartedFrontend) {
        return
    }

    Write-Host ''
    Write-Host '==> 正在停止开发服务...'

    if ($script:StartedFrontend -and $script:FrontendProc) {
        if (-not $script:FrontendProc.HasExited) {
            Stop-Tree $script:FrontendProc.Id
        }
    }
    if ($script:StartedBackend -and $script:BackendProc) {
        if (-not $script:BackendProc.HasExited) {
            Stop-Tree $script:BackendProc.Id
        }
    }

    # 兜底：dotnet run / cmd 的子进程有时不会跟着父进程退出
    if ($script:StartedFrontend) { Stop-Port $VitePort }
    if ($script:StartedBackend) { Stop-Port $HttpPort }

    Write-Host '==> 已退出'
}

try {
    Write-Host 'EssSimulator 开发启动'
    Write-Host "  根目录: $Root"
    Write-Host "  模式:   $Mode"
    Write-Host ''

    switch ($Mode) {
        'backend' {
            Assert-Command 'dotnet.exe' '请安装 .NET 8 SDK（https://dotnet.microsoft.com/download）。'
            Start-Backend
            [void](Wait-Http "$BackendUrl/api/health" '后端')
            if ($script:BackendProc.HasExited) {
                throw "后端启动失败（退出码 $($script:BackendProc.ExitCode)），请查看上方日志。"
            }
            Write-Host ''
            Write-Host '后端运行中。Ctrl+C 退出。'
            Write-Host "  UI(发布态同源): $BackendUrl/"
            while (-not $script:BackendProc.HasExited) {
                Start-Sleep -Milliseconds 500
            }
        }
        'frontend' {
            Assert-Command 'npm.cmd' '请安装 Node.js LTS（winget install OpenJS.NodeJS.LTS）。'
            Start-Frontend
            [void](Wait-Http "http://127.0.0.1:$VitePort/" '前端')
            if ($script:FrontendProc.HasExited) {
                throw "前端启动失败（退出码 $($script:FrontendProc.ExitCode)），请查看上方日志。"
            }
            Open-Ui "http://localhost:$VitePort/"
            Write-Host ''
            Write-Host '前端运行中。Ctrl+C 退出。'
            Write-Host "  开发 UI: http://localhost:$VitePort/"
            while (-not $script:FrontendProc.HasExited) {
                Start-Sleep -Milliseconds 500
            }
        }
        'all' {
            Assert-Command 'dotnet.exe' '请安装 .NET 8 SDK（https://dotnet.microsoft.com/download）。'
            Assert-Command 'npm.cmd' '请安装 Node.js LTS（winget install OpenJS.NodeJS.LTS）。'
            Start-Backend
            [void](Wait-Http "$BackendUrl/api/health" '后端')
            Start-Frontend
            [void](Wait-Http "http://127.0.0.1:$VitePort/" '前端')
            if ($script:FrontendProc.HasExited) {
                throw "前端启动失败（退出码 $($script:FrontendProc.ExitCode)），请查看上方日志。"
            }
            Open-Ui "http://localhost:$VitePort/"
            Write-Host ''
            Write-Host '开发服务已启动。Ctrl+C 同时停止前后端。'
            Write-Host "  前端(HMR): http://localhost:$VitePort/"
            Write-Host "  后端 API:  $BackendUrl/api/health"
            Write-Host "  后端同源:  $BackendUrl/  (需已有 wwwroot 构建产物)"
            Write-Host '  提示: 系统配置「应用到仿真」会重启后端；本脚本会自动拉起。'

            while ($true) {
                if ($script:FrontendProc.HasExited) {
                    Write-Host '前端已退出' -ForegroundColor Red
                    exit 1
                }

                if ($script:BackendProc.HasExited) {
                    Write-Host '==> 后端已退出，正在按当前配置重启...' -ForegroundColor Yellow
                    Start-Sleep -Seconds 1
                    for ($i = 0; $i -lt 10 -and (Test-PortInUse $HttpPort); $i++) {
                        if ($i -eq 0) {
                            Write-Host "    等待端口 $HttpPort 释放..."
                        }
                        Start-Sleep -Milliseconds 500
                    }
                    Start-Backend
                    [void](Wait-Http "$BackendUrl/api/health" '后端' 90)
                    if ($script:BackendProc.HasExited) {
                        Write-Host '后端重启失败，已停止自动重启。' -ForegroundColor Red
                        exit 1
                    }
                }

                Start-Sleep -Seconds 1
            }
        }
    }
}
catch [System.Management.Automation.PipelineStoppedException] {
    # Ctrl+C：交给 finally 清理
}
catch {
    Write-Host ''
    Write-Host "错误: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    Stop-All
}
