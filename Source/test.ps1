param([string]$TestDirectory = (Join-Path (Split-Path $PSScriptRoot) ('TestAudio-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))), [ValidateSet('Core','Audio','Experience','Safety','Desktop','Player','Upgrade','UpgradeUI','Visualizer','VisualizerUI')][string]$Suite='Core')
$ErrorActionPreference = 'Stop'
$application = Join-Path (Split-Path $PSScriptRoot) 'App'
$resolvedTest = [IO.Path]::GetFullPath($TestDirectory)
New-Item -ItemType Directory -Force $resolvedTest | Out-Null
Copy-Item -LiteralPath (Join-Path $application 'tools\wavpack.exe') -Destination $resolvedTest -Force
$mode = switch($Suite){ 'Audio' {'--audio-features-test'} 'Experience' {'--experience-test'} 'Safety' {'--audio-safety-test'} 'Desktop' {'--desktop-playback-test'} 'Player' {'--player-state-test'} 'Upgrade' {'--upgrade-test'} 'UpgradeUI' {'--upgrade-ui-test'} 'Visualizer' {'--visualizer-streaming-test'} 'VisualizerUI' {'--visualizer-ui-test'} default {'--self-test'} }
$report = switch($Suite){ 'Audio' {'audio-feature-results.json'} 'Experience' {'experience-results.json'} 'Safety' {'audio-safety-results.json'} 'Desktop' {'desktop-playback-results.json'} 'Player' {'player-state-results.json'} 'Upgrade' {'upgrade-results.json'} 'UpgradeUI' {'upgrade-ui-results.json'} 'Visualizer' {'visualizer-streaming-results.json'} 'VisualizerUI' {'visualizer-ui-results.json'} default {'test-results.json'} }
$priorData=$env:FAEMYR_DATA_DIRECTORY
$env:FAEMYR_DATA_DIRECTORY=Join-Path $resolvedTest 'state'
New-Item -ItemType Directory -Force (Join-Path $env:FAEMYR_DATA_DIRECTORY 'cache') | Out-Null
$arguments = $mode + ' "' + $resolvedTest + '"'
$process = $null
try{$process = Start-Process -FilePath (Join-Path $application 'Faemyr.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait}finally{$env:FAEMYR_DATA_DIRECTORY=$priorData}
if ($process.ExitCode -ne 0) { Get-ChildItem -LiteralPath $resolvedTest -Filter '*error.txt' | ForEach-Object { Get-Content -LiteralPath $_.FullName }; throw 'Testler başarısız.' }
Get-Content (Join-Path $resolvedTest $report)


