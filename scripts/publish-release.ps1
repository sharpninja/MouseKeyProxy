param(
    [string]$Scratch = $env:MKP_SCRATCH,
    [switch]$SkipTests,
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

Write-Host '=== RESTORE LOCAL TOOLS ==='
dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit $LASTEXITCODE" }

$version = dotnet tool run dotnet-gitversion /showvariable SemVer
if ($LASTEXITCODE -ne 0) { throw "GitVersion failed with exit $LASTEXITCODE" }
$version = $version.Trim()
if ([string]::IsNullOrWhiteSpace($version)) { throw 'GitVersion returned an empty SemVer.' }

if ([string]::IsNullOrWhiteSpace($Scratch)) {
    $Scratch = Join-Path $env:TEMP "mkp-release-$version"
}
New-Item -ItemType Directory -Force -Path $Scratch | Out-Null

$receipt = Join-Path $repoRoot 'docs' "receipts-release-v$version.txt"
"=== MouseKeyProxy release v$version ===" | Out-File -FilePath $receipt -Encoding utf8
"Date: $(Get-Date -Format o)" | Out-File -FilePath $receipt -Append -Encoding utf8
"Scratch: $Scratch" | Out-File -FilePath $receipt -Append -Encoding utf8

Write-Host "=== RELEASE: v$version ==="

if (-not $SkipTests) {
    Write-Host '=== TEST MATRIX ==='
    dotnet test MouseKeyProxy.slnx -c Release 2>&1 | Tee-Object -FilePath (Join-Path $Scratch 'release-test.log') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Test matrix failed with exit $LASTEXITCODE" }
    "Tests: PASS (exit 0)" | Out-File -FilePath $receipt -Append -Encoding utf8
}

Write-Host '=== NUKE PACK REPL ==='
dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PackRepl --configuration Release 2>&1 |
    Tee-Object -FilePath (Join-Path $Scratch 'release-pack.log') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Nuke PackRepl failed with exit $LASTEXITCODE" }
"Pack: PASS (GitVersion $version)" | Out-File -FilePath $receipt -Append -Encoding utf8

if (-not $SkipPublish) {
    if ([string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
        "NuGet publish: SKIPPED (NUGET_API_KEY not set)" | Out-File -FilePath $receipt -Append -Encoding utf8
        Write-Warning 'NUGET_API_KEY not set; skipping NuGet publish'
    }
    else {
        Write-Host '=== NUKE PUBLISH TOOL TO NUGET ==='
        dotnet run --project build/MouseKeyProxy.Build.csproj -- --target PublishToolToNuGet --configuration Release 2>&1 |
            Tee-Object -FilePath (Join-Path $Scratch 'release-publish.log') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Nuke PublishToolToNuGet failed with exit $LASTEXITCODE" }
        "NuGet publish: PASS (nuget.org)" | Out-File -FilePath $receipt -Append -Encoding utf8
    }
}

Write-Host '=== RELEASE COMPLETE ==='
exit 0
