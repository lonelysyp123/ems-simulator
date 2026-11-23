#Requires -Version 5.1
<#
.SYNOPSIS
  点表资产校验（Windows 原生版，等价于 scripts/pointmap-common.sh 的 validate_device_models）。

.DESCRIPTION
  选型型（bms / emu / em / pv）：检查 pointmaps/models/<type>/standard/ 下的点表文件是否存在。
  拼装型（lc）：检查 type.json，且每个片段目录都含 lc.csv。
  仅做存在性校验，不解析 CSV 内容。
#>

function Test-DeviceModels {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $pointmapRoot = Join-Path $Root 'pointmaps'
    $types = @(
        @{ Type = 'bms'; Files = @('bms_bank.csv', 'bms_rack.csv') },
        @{ Type = 'emu'; Files = @('emu.csv') },
        @{ Type = 'em';  Files = @('em.csv') },
        @{ Type = 'lc';  Files = @('lc.csv'); Compose = $true },
        @{ Type = 'pv';  Files = @('pv_logger.csv', 'pv_apm810.csv') }
    )

    foreach ($t in $types) {
        $typeDir = Join-Path $pointmapRoot (Join-Path 'models' $t.Type)
        if (-not (Test-Path $typeDir)) {
            Write-Host "设备类型点表目录不存在: $typeDir" -ForegroundColor Red
            return $false
        }

        if ($t.Compose) {
            $typeJson = Join-Path $typeDir 'type.json'
            if (-not (Test-Path $typeJson)) {
                Write-Host "拼装类型缺少 type.json: $typeJson" -ForegroundColor Red
                return $false
            }

            $fragments = @(Get-ChildItem -Path $typeDir -Directory -Force)
            if ($fragments.Count -eq 0) {
                Write-Host "拼装类型 [$($t.Type)] 没有任何片段目录（期望如 group / system / unit_10MW）" -ForegroundColor Red
                return $false
            }

            foreach ($fragment in $fragments) {
                foreach ($file in $t.Files) {
                    $path = Join-Path $fragment.FullName $file
                    if (-not (Test-Path $path)) {
                        Write-Host "拼装片段 [$($t.Type)/$($fragment.Name)] 缺少文件: $file" -ForegroundColor Red
                        return $false
                    }
                }
            }
            continue
        }

        $standard = Join-Path $typeDir 'standard'
        if (-not (Test-Path $standard)) {
            Write-Host "设备型号点表目录不存在: $standard" -ForegroundColor Red
            return $false
        }
        foreach ($file in $t.Files) {
            $path = Join-Path $standard $file
            if (-not (Test-Path $path)) {
                Write-Host "设备型号点表 [$($t.Type)/standard] 缺少文件: $file" -ForegroundColor Red
                return $false
            }
        }
    }

    return $true
}
