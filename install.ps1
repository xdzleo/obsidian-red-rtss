<#
.SYNOPSIS
  Instala o layout "Obsidian Red II" no OverlayEditor do RivaTuner Statistics Server.

.DESCRIPTION
  - copia overlay\ObsidianRed2.ovl e overlay\ObsidianRed2\ para Plugins\Client\Overlays
  - detecta o numero de serie do Aqua Computer high flow NEXT (USB 0C70:F012) e grava no layout
  - liga o grupo "controller" do LibreHardwareMonitor embutido no RTSS (onde fica o Aqua Computer)
  - define o layout ativo do OverlayEditor
  Faz backup (*.bak) de todo arquivo que altera, so na primeira vez.

.PARAMETER RtssDir
  Pasta do RTSS. Padrao: C:\Program Files (x86)\RivaTuner Statistics Server

.PARAMETER Restart
  Reinicia o RTSS no final para carregar o layout.

.PARAMETER DryRun
  So mostra o que seria feito.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File .\install.ps1 -Restart
#>
param(
  [string]$RtssDir = "${env:ProgramFiles(x86)}\RivaTuner Statistics Server",
  [switch]$Restart,
  [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$latin1 = [Text.Encoding]::GetEncoding(28591)   # .ovl files are ANSI; keeps the degree sign intact

function Step($msg) { Write-Host ("[{0}] {1}" -f $(if ($DryRun) { 'dry-run' } else { 'ok' }), $msg) }
function Backup($file) {
  if ((Test-Path $file) -and -not (Test-Path "$file.bak")) {
    if (-not $DryRun) { Copy-Item $file "$file.bak" }
    Step "backup $file.bak"
  }
}

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and -not $DryRun) { throw 'Rode como administrador (o RTSS fica em Program Files).' }

$client = Join-Path $RtssDir 'Plugins\Client'
if (-not (Test-Path (Join-Path $client 'OverlayEditor.dll'))) { throw "OverlayEditor nao encontrado em $client (RTSS 7.3.5+ com o plugin OverlayEditor)." }
$overlays = Join-Path $client 'Overlays'
$src = Join-Path $PSScriptRoot 'overlay'

# 1. high flow NEXT serial (LibreHardwareMonitor identifies it as /usbhid/0C70/F012/<serial>)
$hfn = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
  Where-Object { $_.InstanceId -match '^USB\\VID_0C70&PID_F012\\([^\\]+)$' } |
  ForEach-Object { $Matches[1] }
$serial = @($hfn)[0]
if ($serial) {
  Step "high flow NEXT encontrado, serial $serial"
  if (@($hfn).Count -gt 1) { Write-Warning "Mais de um high flow NEXT; usando $serial" }
} else {
  Write-Warning 'high flow NEXT nao encontrado: a secao de agua vai mostrar "--".'
}

# 2. layout + art
$ovl = [IO.File]::ReadAllText((Join-Path $src 'ObsidianRed2.ovl'), $latin1)
if ($serial) { $ovl = $ovl.Replace('HFN-SERIAL', $serial) }
if (-not $DryRun) {
  New-Item -ItemType Directory -Force (Join-Path $overlays 'ObsidianRed2') | Out-Null
  Copy-Item (Join-Path $src 'ObsidianRed2\*') (Join-Path $overlays 'ObsidianRed2') -Force
  [IO.File]::WriteAllText((Join-Path $overlays 'ObsidianRed2.ovl'), $ovl, $latin1)
}
Step "layout copiado para $overlays"

# 3. LibreHardwareMonitor provider: enable the controller group (Aqua Computer devices)
$exp = Join-Path $client 'LHMDataProvider\Exporter.config'
if (Test-Path $exp) {
  $xml = Get-Content $exp -Raw
  if ($xml -match '<controller>\s*true\s*</controller>') { Step 'LHM: controller ja estava ligado' }
  else {
    Backup $exp
    if (-not $DryRun) { Set-Content $exp ($xml -replace '<controller>\s*false\s*</controller>', '<controller>true</controller>') -NoNewline -Encoding ASCII }
    Step 'LHM: controller ligado'
  }
} else { Write-Warning "LHMDataProvider nao encontrado ($exp): a agua vai mostrar `"--`"." }

# 4. active layout
$cfg = Join-Path $client 'OverlayEditor.cfg'
Backup $cfg
$text = if (Test-Path $cfg) { Get-Content $cfg -Raw } else { "[Settings]`r`n" }
if ($text -match '(?m)^Layout=') { $text = $text -replace '(?m)^Layout=.*$', 'Layout=ObsidianRed2.ovl' }
else { $text = $text -replace '(?m)^\[Settings\]\s*$', "[Settings]`r`nLayout=ObsidianRed2.ovl" }
if (-not $DryRun) { Set-Content $cfg $text -NoNewline -Encoding ASCII }
Step 'OverlayEditor: layout ativo = ObsidianRed2.ovl'

# 5. restart RTSS so OverlayEditor reloads (helpers must go too, or the new RTSS exits)
if ($Restart) {
  $names = 'RTSS', 'RTSSHooksLoader', 'RTSSHooksLoader64', 'LHMDataProvider'
  if (-not $DryRun) {
    Get-Process -Name $names -ErrorAction SilentlyContinue | Stop-Process -Force
    $t = (Get-Date).AddSeconds(15)
    while ((Get-Process -Name $names -ErrorAction SilentlyContinue) -and (Get-Date) -lt $t) { Start-Sleep -Milliseconds 250 }
    Start-Process (Join-Path $RtssDir 'RTSS.exe') -WorkingDirectory $RtssDir
  }
  Step 'RTSS reiniciado'
} else {
  Write-Host 'Reinicie o RTSS (ou rode com -Restart) para carregar o layout.'
}
