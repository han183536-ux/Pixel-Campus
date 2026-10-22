param([string]$FixtureDirectory = "")
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$projectFile = Join-Path $projectRoot 'Assembly-CSharp.csproj'
if (!(Test-Path -LiteralPath $projectFile)) { throw '먼저 Unity에서 C# 프로젝트 파일을 생성하세요.' }
[xml]$unityProject = Get-Content -LiteralPath $projectFile -Raw
$engineCore = ($unityProject.Project.ItemGroup.Reference | Where-Object { $_.Include -eq 'UnityEngine.CoreModule' } | Select-Object -First 1).HintPath
if (!$engineCore) { throw 'UnityEngine.CoreModule 경로를 확인하지 못했습니다.' }
$managedDirectory = Split-Path $engineCore -Parent
$testRoot = Join-Path $projectRoot 'Temp/UniversityNoticeChecks'
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$includeFile = Join-Path $testRoot 'UniversityInclude.targets'
@'
<Project><ItemGroup Condition="'$(MSBuildProjectName)' == 'Assembly-CSharp'">
<Compile Remove="Assets\Notifications\Runtime\UniversityNoticeFeed.cs" />
<Compile Include="$(MSBuildProjectDirectory)\Assets\Notifications\Runtime\UniversityNoticeFeed.cs" />
<Compile Remove="Assets\Notifications\Runtime\UniversityNoticeDeadline.cs" />
<Compile Include="$(MSBuildProjectDirectory)\Assets\Notifications\Runtime\UniversityNoticeDeadline.cs" />
<Compile Remove="Assets\Notifications\Runtime\NotificationRequestScope.cs;$(MSBuildProjectDirectory)\Assets\Notifications\Runtime\NotificationRequestScope.cs" />
<Compile Include="$(MSBuildProjectDirectory)\Assets\Notifications\Runtime\NotificationRequestScope.cs" />
</ItemGroup></Project>
'@ | Set-Content -LiteralPath $includeFile
& dotnet build $projectFile --verbosity quiet --ignore-failed-sources -p:WarningLevel=0 "-p:CustomAfterMicrosoftCommonTargets=$includeFile"
if ($LASTEXITCODE -ne 0) { throw 'Unity 소스 컴파일 실패' }
$escapeXml = { param($value) [System.Security.SecurityElement]::Escape($value) }
$assembly = & $escapeXml (Join-Path $projectRoot 'Temp/bin/Debug/Assembly-CSharp.dll')
$source = & $escapeXml (Join-Path $PSScriptRoot 'UniversityNoticeChecks.cs')
$references = foreach ($module in @('UnityEngine.CoreModule', 'UnityEngine.IMGUIModule', 'UnityEngine.UnityWebRequestModule')) {
    $hint = & $escapeXml (Join-Path $managedDirectory "$module.dll")
    "<Reference Include='$module'><HintPath>$hint</HintPath></Reference>"
}
@"
<Project Sdk='Microsoft.NET.Sdk'><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>
<Compile Include='$source' /><Reference Include='Assembly-CSharp'><HintPath>$assembly</HintPath></Reference>
$($references -join "`n")
</ItemGroup></Project>
"@ | Set-Content -LiteralPath (Join-Path $testRoot 'Checks.csproj')
if (!(Test-Path -LiteralPath (Join-Path $testRoot 'obj/project.assets.json'))) {
    & dotnet restore (Join-Path $testRoot 'Checks.csproj') --ignore-failed-sources -p:NuGetAudit=false --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw '검증 프로젝트 복원 실패' }
}
$arguments = @('run', '--project', (Join-Path $testRoot 'Checks.csproj'), '--no-restore', '--verbosity', 'quiet')
if ($FixtureDirectory) { $arguments += @('--', (Resolve-Path -LiteralPath $FixtureDirectory).Path) }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw '학교 공지 검증 실패' }
