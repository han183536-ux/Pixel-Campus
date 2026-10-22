$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $projectRoot 'Temp/RequestScopeChecks'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$escapeXml = { param($value) [System.Security.SecurityElement]::Escape($value) }
$scopeSource = & $escapeXml (Join-Path $projectRoot 'Assets/Notifications/Runtime/NotificationRequestScope.cs')
$checksSource = & $escapeXml (Join-Path $PSScriptRoot 'RequestScopeChecks.cs')
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include='$scopeSource' /><Compile Include='$checksSource' /></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'Checks.csproj')
& dotnet restore (Join-Path $testRoot 'Checks.csproj') --source $testRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Request lifecycle test restore failed' }
& dotnet run --project (Join-Path $testRoot 'Checks.csproj') --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Request lifecycle test failed' }
