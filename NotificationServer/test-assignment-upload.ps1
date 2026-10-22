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

# Compile the runtime and file picker against Unity's .NET Standard 2.1 API surface.
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
foreach ($platform in @('Android', 'Windows')) {
    $compileRoot = Join-Path $testRoot ($platform + 'Compile')
    New-Item -ItemType Directory -Force -Path $compileRoot | Out-Null
    $platformSymbol = if ($platform -eq 'Android') { 'UNITY_ANDROID' } else { 'UNITY_STANDALONE_WIN' }
    @"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><DefineConstants>$platformSymbol;UNITY_2020_2_OR_NEWER;UNITY_2021_1_OR_NEWER;UNITY_2022_2_OR_NEWER</DefineConstants><NuGetAudit>false</NuGetAudit><NoWarn>0649;0618</NoWarn></PropertyGroup><ItemGroup><Compile Include='$runtime' /><Compile Include='$plugin' /><Compile Include='$player' />$($references -join "`n")</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $compileRoot 'Checks.csproj')
    & dotnet restore (Join-Path $compileRoot 'Checks.csproj') --source $testRoot --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "$platform runtime compile restore failed" }
    & dotnet build (Join-Path $compileRoot 'Checks.csproj') --no-restore --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw "$platform file picker/runtime compile failed" }
}
