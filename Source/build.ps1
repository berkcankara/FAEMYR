$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Faemyr.csproj'
$destination = Join-Path (Split-Path $PSScriptRoot) 'App'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $destination --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
Write-Output "Hazır: $destination\Faemyr.exe"
