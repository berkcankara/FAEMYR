$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'Ahenk.csproj'
$destination = Join-Path (Split-Path $PSScriptRoot) 'App'
dotnet publish $project -c Release -r win-x64 --self-contained true -o $destination --configfile (Join-Path $PSScriptRoot 'NuGet.Config')
if ($LASTEXITCODE -ne 0) { throw 'Derleme başarısız.' }
Write-Output "Hazır: $destination\Lyra.exe"
