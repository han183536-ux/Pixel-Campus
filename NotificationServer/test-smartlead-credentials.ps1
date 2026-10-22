$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $projectRoot 'Temp/SmartleadCredentialChecks'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$storePath = Join-Path $projectRoot 'Assets/Notifications/Runtime/SmartleadCredentialStore.cs'
$testTarget = 'PixelCampus/Test/' + [guid]::NewGuid().ToString('N')
(Get-Content -LiteralPath $storePath -Raw).Replace('"PixelCampus/Smartlead"', ('"' + $testTarget + '"')) |
    Set-Content -LiteralPath (Join-Path $testRoot 'Store.cs')
$escapeXml = { param($value) [System.Security.SecurityElement]::Escape($value) }
$checks = & $escapeXml (Join-Path $PSScriptRoot 'CredentialStoreChecks.cs')
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><DefineConstants>UNITY_STANDALONE_WIN</DefineConstants><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include='Store.cs' /><Compile Include='$checks' /></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'Checks.csproj')
& dotnet restore (Join-Path $testRoot 'Checks.csproj') --source $testRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Credential test restore failed' }
& dotnet run --project (Join-Path $testRoot 'Checks.csproj') --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Credential store test failed' }

[xml]$unityProject = Get-Content -LiteralPath (Join-Path $projectRoot 'Assembly-CSharp.csproj') -Raw
$engineCore = ($unityProject.Project.ItemGroup.Reference | Where-Object { $_.Include -eq 'UnityEngine.CoreModule' } | Select-Object -First 1).HintPath
$managedDirectory = Split-Path $engineCore -Parent
$references = foreach ($module in @('UnityEngine.CoreModule', 'UnityEngine.AndroidJNIModule', 'UnityEngine.JSONSerializeModule')) {
    $hint = & $escapeXml (Join-Path $managedDirectory "$module.dll")
    "<Reference Include='$module'><HintPath>$hint</HintPath></Reference>"
}
$androidRoot = Join-Path $testRoot 'Android'
New-Item -ItemType Directory -Force -Path $androidRoot | Out-Null
$source = & $escapeXml $storePath
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><DefineConstants>UNITY_ANDROID</DefineConstants><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include='$source' />$($references -join "`n")</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $androidRoot 'Checks.csproj')
& dotnet restore (Join-Path $androidRoot 'Checks.csproj') --source $testRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Android credential compile restore failed' }
& dotnet build (Join-Path $androidRoot 'Checks.csproj') --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Android credential source compile failed' }
