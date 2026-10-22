$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $projectRoot 'Temp/AssignmentUpload/Checks'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$source = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'Assets/Notifications/Editor/AndroidCampusSetup.cs'))
$checks = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'AssignmentUploadChecks.cs'))
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include='$source' /><Compile Include='$checks' /></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'Checks.csproj')
& dotnet restore (Join-Path $testRoot 'Checks.csproj') --source $testRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Upload setup check restore failed' }
& dotnet run --project (Join-Path $testRoot 'Checks.csproj') --no-restore --verbosity quiet -- (Join-Path $testRoot 'fixture')
if ($LASTEXITCODE -ne 0) { throw 'Upload setup checks failed' }

# Compile the complete runtime and file-picker wrapper with Android branches enabled.
$androidRoot = Join-Path $testRoot 'AndroidCompile'
New-Item -ItemType Directory -Force -Path $androidRoot | Out-Null
[xml]$unityProject = Get-Content -LiteralPath (Join-Path $projectRoot 'Assembly-CSharp.csproj') -Raw
$references = foreach ($reference in $unityProject.Project.ItemGroup.Reference) {
    if ($reference.Include -like 'UnityEngine*' -and (Test-Path -LiteralPath $reference.HintPath)) {
        $hint = [System.Security.SecurityElement]::Escape([System.IO.Path]::GetFullPath($reference.HintPath))
        "<Reference Include='$($reference.Include)'><HintPath>$hint</HintPath></Reference>"
    }
}
$runtime = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'Assets/Notifications/Runtime/*.cs'))
$plugin = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'Assets/Plugins/UnityWebView/WebViewObject.cs'))
$player = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'Assets/PixelPlayer/PixelPlayerMovement.cs'))
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><DefineConstants>UNITY_ANDROID;UNITY_2020_2_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2022_2_OR_NEWER</DefineConstants><NuGetAudit>false</NuGetAudit><NoWarn>0649;0618</NoWarn></PropertyGroup><ItemGroup><Compile Include='$runtime' /><Compile Include='$plugin' /><Compile Include='$player' />$($references -join "`n")</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $androidRoot 'Checks.csproj')
& dotnet restore (Join-Path $androidRoot 'Checks.csproj') --source $testRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Android runtime compile restore failed' }
& dotnet build (Join-Path $androidRoot 'Checks.csproj') --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Android file picker/runtime compile failed' }
