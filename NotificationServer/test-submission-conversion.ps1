param([switch]$Integration, [string]$Fixture)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$testRoot = Join-Path $projectRoot 'Temp/SubmissionConversion/Checks'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$source = [System.Security.SecurityElement]::Escape((Join-Path $projectRoot 'Assets/Notifications/Runtime/SubmissionDocumentConverter.cs'))
$checks = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'SubmissionConversionChecks.cs'))
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><DefineConstants>UNITY_STANDALONE_WIN</DefineConstants><NuGetAudit>false</NuGetAudit><NoWarn>CA1416</NoWarn></PropertyGroup><ItemGroup><Compile Include='$source' /><Compile Include='$checks' /></ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'Checks.csproj')
& dotnet restore (Join-Path $testRoot 'Checks.csproj') --source $testRoot --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Conversion checks restore failed' }
$testArguments = @()
if ($Integration) { $testArguments = @('--integration',(Join-Path $testRoot 'fixture'),(Join-Path $projectRoot 'Assets/Notifications/Resources/SubmissionHancomWorker.txt')) }
if ($Integration) {
    if (-not $Fixture) { $Fixture = Join-Path $PSScriptRoot 'fixtures/submission-conversion.docx' }
    $testArguments += [System.IO.Path]::GetFullPath($Fixture)
}
& dotnet run --project (Join-Path $testRoot 'Checks.csproj') --no-restore --verbosity quiet -- @testArguments
if ($LASTEXITCODE -ne 0) { throw 'Submission conversion checks failed' }
