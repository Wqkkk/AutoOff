param([switch]$Desktop)
# 打包 AutoOff.exe：像素图标 -> csc 编译 -> --dry-run 自检（电源动作只落日志，绝不真关机）。零下载，只用系统自带 csc.exe + node。
$ErrorActionPreference = 'Stop'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { throw '找不到系统自带 csc.exe' }
Push-Location $PSScriptRoot
try {
  Write-Host '== 1/3 icon (像素按功能生成)'
  node make-icon.mjs
  if ($LASTEXITCODE -ne 0) { throw 'icon FAIL' }
  Write-Host '== 2/3 compile (csc /target:winexe /win32icon)'
  Remove-Item .\AutoOff.exe -ErrorAction SilentlyContinue
  & $csc /nologo /codepage:65001 /target:winexe /win32icon:icon.ico /out:AutoOff.exe AutoOff.cs 2>&1 | ForEach-Object { "$_" }
  if (-not (Test-Path .\AutoOff.exe)) { throw 'csc 没产出 exe' }
  Write-Host ('   built: ' + (Get-Item .\AutoOff.exe).Length + ' bytes')
  Write-Host '== 3/3 dry-run selftest'
  $log = Join-Path $env:TEMP 'autooff-selftest.log'
  Remove-Item $log -ErrorAction SilentlyContinue
  $p = Start-Process -FilePath (Resolve-Path .\AutoOff.exe) -ArgumentList '--dry-run' -PassThru -Wait
  Write-Host ('   exit=' + $p.ExitCode + ' ' + [string](Get-Content $log -Raw))
  if ($p.ExitCode -ne 0) { throw 'dry-run FAIL' }
  if ($Desktop) {
    Copy-Item .\AutoOff.exe ([Environment]::GetFolderPath('Desktop')) -Force
    Write-Host ('COPIED -> ' + (Join-Path ([Environment]::GetFolderPath('Desktop')) 'AutoOff.exe'))
  }
  Write-Host ('DONE ' + (Join-Path $PSScriptRoot 'AutoOff.exe'))
}
finally { Pop-Location }
