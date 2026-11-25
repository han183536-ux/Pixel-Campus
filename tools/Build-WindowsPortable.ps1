#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PlayerExe,
    [string]$CacheDirectory = (Join-Path $env:LOCALAPPDATA 'PixelCampus\BuildCache'),
    [string]$OutputZip
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression

function Get-PinnedPackage([string]$Name, [string]$Url, [string]$Sha256) {
    $file = Join-Path $cache $Name
    if ((Test-Path -LiteralPath $file -PathType Leaf) -and (Get-FileHash -LiteralPath $file).Hash -eq $Sha256) {
        return $file
    }
    $partial = "$file.$job.partial"
    Write-Host "Downloading $Name"
    $client = New-Object System.Net.WebClient
    try {
        $client.DownloadFile($Url, $partial)
        if ((Get-FileHash -LiteralPath $partial).Hash -ne $Sha256) { throw "SHA-256 mismatch for $Name" }
        if ([IO.File]::Exists($file)) {
            [IO.File]::Replace($partial, $file, [NullString]::Value)
        } else {
            try { [IO.File]::Move($partial, $file) }
            catch [IO.IOException] {
                if (!(Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file).Hash -ne $Sha256) { throw }
            }
        }
    } finally {
        $client.Dispose()
        [IO.File]::Delete($partial)
    }
    return $file
}

function Assert-X64PE([string]$File) {
    $reader = [IO.BinaryReader]::new([IO.File]::OpenRead($File))
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Invalid executable: $File" }
        $reader.BaseStream.Position = 0x3C
        $offset = $reader.ReadUInt32()
        $reader.BaseStream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw "Expected an x64 Windows binary: $File" }
    } finally { $reader.Dispose() }
}

function Add-ZipFile([string]$Source, [string]$Name) {
    [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $Source, $Name.Replace('\','/'), [IO.Compression.CompressionLevel]::Fastest)
}

function Add-ZipFolder([string]$Source, [string]$Name) {
    foreach ($file in [IO.Directory]::EnumerateFiles($Source, '*', [IO.SearchOption]::AllDirectories)) {
        Add-ZipFile $file ($Name + '/' + $file.Substring($Source.Length + 1))
    }
}

$player = Get-Item -LiteralPath $PlayerExe
if ($player.Extension -ine '.exe') { throw 'Supply the .exe from a fresh Windows x64 Unity player build.' }
Assert-X64PE $player.FullName
$build = $player.DirectoryName
$dataName = $player.BaseName + '_Data'
$data = Join-Path $build $dataName
Assert-X64PE (Join-Path $data 'Plugins\x86_64\WebView.dll')
if (!(Test-Path -LiteralPath (Join-Path $data 'globalgamemanagers') -PathType Leaf) -or
    !(Test-Path -LiteralPath (Join-Path $build 'UnityPlayer.dll') -PathType Leaf)) { throw "Incomplete Unity build: $build" }
if (!(Test-Path -LiteralPath (Join-Path $build 'MonoBleedingEdge') -PathType Container) -and
    !(Test-Path -LiteralPath (Join-Path $build 'GameAssembly.dll') -PathType Leaf)) { throw 'Missing Unity scripting backend.' }

if (!$OutputZip) { $OutputZip = Join-Path (Split-Path -Parent $build) 'PixelCampus-Windows-x64.zip' }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputZip)
if (Test-Path -LiteralPath $output) { throw "Output already exists: $output" }
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($output))
$cache = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CacheDirectory)
[void][IO.Directory]::CreateDirectory($cache)
$job = [Guid]::NewGuid().ToString('N')
$stage = Join-Path ([IO.Path]::GetTempPath()) "PixelCampus-$job"
[void][IO.Directory]::CreateDirectory($stage)
Write-Host "Staging: $stage"

$runtime = 'Microsoft.WebView2.FixedVersionRuntime.158.0.4370.33.x64'
$cab = Get-PinnedPackage "$runtime.cab" "https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/ecc2761e-1380-46ee-9552-995b039e4e47/$runtime.cab" '835DA387DF67797F710881F553BB6934D98F24D08E370456F48D38ED4E072B62'
$crtHash = '4aaf54db0bfc9435f7c3660e1a00237a4b556042bfeea64bde44c2e0194e6ee5'
$vsix = Get-PinnedPackage 'MSVC-14.44.35211-x64.vsix' "https://download.visualstudio.microsoft.com/download/pr/45d3b8dd-bced-4b37-9974-142f748d710c/$crtHash/Microsoft.VC.14.44.17.14.CRT.Redist.X64.base.vsix" $crtHash
& "$env:SystemRoot\System32\expand.exe" '-F:*' $cab $stage | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Failed to expand WebView2 CAB in $stage" }
$browser = Join-Path $stage $runtime
Assert-X64PE (Join-Path $browser 'msedgewebview2.exe')

$crt = Join-Path $stage 'CRT'
[void][IO.Directory]::CreateDirectory($crt)
$package = [IO.Compression.ZipFile]::OpenRead($vsix)
try {
    foreach ($entry in $package.Entries) {
        if ($entry.FullName -match '^Contents/VC/Redist/MSVC/14\.44\.35112/x64/Microsoft\.VC143\.CRT/([^/]+\.dll)$') {
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $crt $Matches[1]))
        }
    }
} finally { $package.Dispose() }
foreach ($dep in 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll') { Assert-X64PE (Join-Path $crt $dep) }

# Only ship player output. Unity can also create debug symbols and source backups beside the executable.
$pending = "$output.$job.partial"
$archive = [IO.Compression.ZipFile]::Open($pending, [IO.Compression.ZipArchiveMode]::Create)
try {
    Add-ZipFile $player.FullName $player.Name
    Add-ZipFolder $data $dataName
    foreach ($dir in 'MonoBleedingEdge', 'D3D12', 'DirectML') {
        $path = Join-Path $build $dir
        if ([IO.Directory]::Exists($path)) { Add-ZipFolder $path $dir }
    }
    foreach ($name in 'UnityPlayer.dll', 'UnityCrashHandler64.exe', 'GameAssembly.dll', 'baselib.dll', 'WinPixEventRuntime.dll', 'AMDUnityPlugin.dll', 'NVUnityPlugin.dll', 'nvngx_dlss.dll') {
        $path = Join-Path $build $name
        if ([IO.File]::Exists($path)) { Add-ZipFile $path $name }
    }
    foreach ($file in [IO.Directory]::EnumerateFiles($crt, '*.dll')) { Add-ZipFile $file ([IO.Path]::GetFileName($file)) }
    Add-ZipFolder $browser 'WebView2Runtime'
    $repo = Split-Path -Parent $PSScriptRoot
    $docs = Join-Path $repo 'docs\windows-portable'
    foreach ($name in ([regex]::Unescape('\uC0AC\uC6A9\uC548\uB0B4.txt')), 'WebView2-Fixed-License.ko.txt', 'WebView2-Fixed-License.en.txt') {
        Add-ZipFile (Join-Path $docs $name) $name
    }
    Add-ZipFile (Join-Path $repo 'Assets\Plugins\UnityWebView\LICENSE.txt') 'Licenses/UnityWebView.txt'
    Add-ZipFile (Join-Path $repo 'Assets\Notifications\Resources\CampusFonts\Pretendard-OFL.txt') 'Licenses/Pretendard-OFL.txt'
} finally { $archive.Dispose() }

[IO.File]::Move($pending, $output)
$expectedStage = [IO.Path]::Combine([IO.Path]::GetTempPath(), "PixelCampus-$job")
if ((Get-Item -LiteralPath $stage).FullName -eq [IO.Path]::GetFullPath($expectedStage)) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
Write-Output $output
