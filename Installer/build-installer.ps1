param(
 [Parameter(Mandatory=$true)][string]$CompilerPath,
 [string]$AppDirectory=(Join-Path (Split-Path $PSScriptRoot) 'App'),
 [string]$OutputDirectory=(Join-Path (Split-Path $PSScriptRoot) '.artifacts'),
 [switch]$TestBuild
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path $PSScriptRoot
$project=[xml](Get-Content -LiteralPath (Join-Path $repoRoot 'Source/Faemyr.csproj') -Raw)
$version=[string]$project.Project.PropertyGroup.Version
$appRoot=(Resolve-Path -LiteralPath $AppDirectory).Path
$binaryVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $appRoot 'Faemyr.dll')).FileVersion
if($binaryVersion-ne ($version+'.0')){throw 'The application version does not match the source version.'}
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Path $outputRoot -Force)
$payload=Join-Path $outputRoot ('payload-'+$version)
if(Test-Path -LiteralPath $payload){throw 'Payload directory already exists; choose a new output directory.'}
[void](New-Item -ItemType Directory -Path $payload)
foreach($file in (Get-ChildItem -LiteralPath $appRoot -Recurse -File)){
 $relative=[IO.Path]::GetRelativePath($appRoot,$file.FullName)
 if($relative-match '(?i)^Lyra\.(exe|dll|deps\.json|runtimeconfig\.json|pdb)$'){continue}
 if($relative-match '(?i)(^|[\\/])(data|cache|artist-photos|waveforms|lyrics)([\\/]|$)|\.(pdb|log|tmp|db|db-wal|db-shm)$'){continue}
 $target=Join-Path $payload $relative
 [void](New-Item -ItemType Directory -Path (Split-Path $target) -Force)
 Copy-Item -LiteralPath $file.FullName -Destination $target
}
foreach($required in @('Faemyr.exe','Faemyr.dll','Faemyr-brand-v2.ico','coreclr.dll','PresentationFramework.dll','tools/ffmpeg.exe','tools/ffprobe.exe','tools/wvunpack.exe')){
 if(-not(Test-Path -LiteralPath (Join-Path $payload $required))){throw ('Missing required payload file: '+$required)}
}
$notice=Get-Content -LiteralPath (Join-Path $repoRoot 'THIRD-PARTY-NOTICES.md') -Raw
[IO.File]::WriteAllText((Join-Path $payload 'THIRD-PARTY-NOTICES.md'),$notice.Replace('App\licenses','licenses'),[Text.UTF8Encoding]::new($false))
$readme=@"
FAEMYR $version - Windows x64

KURULUM / INSTALLATION
Setup.exe dosyasını aç, kurulum klasörünü seç ve kısayol seçeneklerini belirle.
Başlat menüsü ve masaüstü kısayolları başlangıçta seçilidir. Ayrı .NET kurulumu gerekmez.
Open Setup.exe, choose the installation folder and shortcut options. No separate .NET installation is required.

PORTABLE ZIP
ZIP'in tümünü ayıkla ve Faemyr klasöründeki Faemyr.exe dosyasını aç.
Extract the whole ZIP and open Faemyr/Faemyr.exe. Keep the other files and folders beside it.

MÜZİK / MUSIC
Ayarlar > Müzik arşivi > Klasör ekle.
Settings > Music library > Add folder.

VERİLER / DATA
Arşiv, favoriler ve ayarlar Faemyr.exe yanındaki data klasöründe saklanır.
Güncellemeden önce uygulamayı kapat ve data klasörünü yedekle.
Kurulum güncellemeleri ve kaldırma işlemi data klasörünü silmez.
Portable sürümden geçerken Faemyr kapalıyken eski data klasörünü yeni Faemyr klasörüne kopyalayabilirsin.
Library, favorites and preferences are stored in the data folder beside Faemyr.exe.
Close Faemyr and back up data before updating. Updates and uninstall preserve this folder.
When switching from portable, copy your old data folder into the new installation with Faemyr closed.

Windows 10 1809+ / Windows 11, x64. Windows 10 üzerinde doğrudan test yapılmadı.
Uygulama ve kurulum paketi dijital olarak imzalanmamıştır.
Third-party licenses and source references are in licenses and THIRD-PARTY-NOTICES.md.
"@
[IO.File]::WriteAllText((Join-Path $payload 'readme.txt'),$readme,[Text.UTF8Encoding]::new($false))
$files=@(Get-ChildItem -LiteralPath $payload -Recurse -File)
if($files | Where-Object { [IO.Path]::GetRelativePath($payload,$_.FullName)-match '(?i)(^|[\\/])(data|Source|bin|obj)([\\/]|$)|\.(pdb|db|db-wal|db-shm)$' }){throw 'Forbidden private or development files in payload.'}
$compileArgs=@("/DAppVersion=$version","/DPayloadDir=$payload","/DReleaseDir=$outputRoot")
if($TestBuild){$compileArgs+='/DTestBuild'}
& $CompilerPath @compileArgs (Join-Path $PSScriptRoot 'Faemyr.iss')
if($LASTEXITCODE-ne 0){throw 'Installer compilation failed.'}
if(-not $TestBuild){
 $zip=Join-Path $outputRoot ('Faemyr-'+$version+'-Windows-x64-Portable.zip')
 if(Test-Path -LiteralPath $zip){throw 'Portable archive already exists.'}
 $archive=[IO.Compression.ZipFile]::Open($zip,[IO.Compression.ZipArchiveMode]::Create)
 try{foreach($file in $files){$relative=[IO.Path]::GetRelativePath($payload,$file.FullName).Replace('\','/');[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$file.FullName,('Faemyr/'+$relative),[IO.Compression.CompressionLevel]::Optimal)}}finally{$archive.Dispose()}
 $assets=@(Get-ChildItem -LiteralPath $outputRoot -File | Where-Object {$_.Name-match '\.(exe|zip)$'})
 $hashes=$assets | ForEach-Object {(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name}
 [IO.File]::WriteAllLines((Join-Path $outputRoot 'SHA256SUMS.txt'),[string[]]$hashes,[Text.UTF8Encoding]::new($false))
}
[ordered]@{Version=$version;Payload=$payload;Files=$files.Count;UserDataIncluded=$false;BinarySha256=(Get-FileHash -LiteralPath (Join-Path $payload 'Faemyr.dll')).Hash;TestBuild=[bool]$TestBuild}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $outputRoot 'package-checks.json')
