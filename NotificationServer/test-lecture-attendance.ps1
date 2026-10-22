$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $projectRoot 'Temp/LectureAttendanceChecks'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$source = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'Assets/Notifications/Runtime/LectureAttendancePeriod.cs'))
$checks = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'LectureAttendanceChecks.cs'))
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><Compile Include='$source' /><Compile Include='$checks' /></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'Checks.csproj')
& dotnet restore (Join-Path $testRoot 'Checks.csproj') --source $testRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Lecture attendance restore failed' }
& dotnet run --project (Join-Path $testRoot 'Checks.csproj') --no-restore --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Lecture attendance checks failed' }
