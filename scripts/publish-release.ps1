param(
    [string]$Version = '0.5.0',
    [string]$Scratch = $env:MKP_SCRATCH,
    [switch]$SkipTests,
    [switch]$SkipPush,
    [switch]$SkipTag
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if ([string]::IsNullOrWhiteSpace($Scratch)) {
    $Scratch = Join-Path $env:TEMP "mkp-release-$Version"
}
New-Item -ItemType Directory -Force -Path $Scratch | Out-Null

$receipt = Join-Path $repoRoot 'docs' 'receipts-release-v0.5.0.txt'
"=== MouseKeyProxy release v$Version ===" | Out-File -FilePath $receipt -Encoding utf8
"Date: $(Get-Date -Format o)" | Out-File -FilePath $receipt -Append -Encoding utf8
"Scratch: $Scratch" | Out-File -FilePath $receipt -Append -Encoding utf8

Write-Host "=== RELEASE: v$Version ==="

if (-not $SkipTests) {
    Write-Host '=== TEST MATRIX ==='
    dotnet test MouseKeyProxy.slnx -c Release 2>&1 | Tee-Object -FilePath (Join-Path $Scratch 'release-test.log') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Test matrix failed with exit $LASTEXITCODE" }
    "Tests: PASS (exit 0)" | Out-File -FilePath $receipt -Append -Encoding utf8
}

Write-Host '=== PACK REPL ==='
$nupkg = Join-Path $Scratch "MouseKeyProxy.Repl.$Version.nupkg"
dotnet pack src/MouseKeyProxy.Repl/MouseKeyProxy.Repl.csproj -c Release -o $Scratch /p:PackageVersion=$Version 2>&1 |
    Tee-Object -FilePath (Join-Path $Scratch 'release-pack.log') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit $LASTEXITCODE" }
if (-not (Test-Path $nupkg)) {
    throw "Expected nupkg not found: $nupkg"
}
"Pack: $nupkg" | Out-File -FilePath $receipt -Append -Encoding utf8

if (-not $SkipPush) {
    $apiKey = $env:NUGET_API_KEY
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        "NuGet push: SKIPPED (NUGET_API_KEY not set)" | Out-File -FilePath $receipt -Append -Encoding utf8
        Write-Warning 'NUGET_API_KEY not set; skipping nuget push'
    }
    else {
        Write-Host '=== NUGET PUSH ==='
        dotnet nuget push $nupkg --api-key $apiKey --source https://api.nuget.org/v3/index.json --skip-duplicate 2>&1 |
            Tee-Object -FilePath (Join-Path $Scratch 'release-push.log') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "nuget push failed with exit $LASTEXITCODE" }
        "NuGet push: PASS (nuget.org)" | Out-File -FilePath $receipt -Append -Encoding utf8
    }
}

if (-not $SkipTag) {
    $tag = "v$Version"
    Write-Host "=== GIT TAG $tag ==="
    $existing = git tag -l $tag 2>&1
    if ($existing -match [regex]::Escape($tag)) {
        "Git tag: EXISTS $tag" | Out-File -FilePath $receipt -Append -Encoding utf8
    }
    else {
        git tag -a $tag -m "MouseKeyProxy v$Version" 2>&1 | Out-File -FilePath $receipt -Append -Encoding utf8
        if ($LASTEXITCODE -ne 0) { throw "git tag failed with exit $LASTEXITCODE" }
        "Git tag: CREATED $tag" | Out-File -FilePath $receipt -Append -Encoding utf8
    }

    Write-Host '=== GIT PUSH TAG ==='
    git push origin $tag 2>&1 | Out-File -FilePath $receipt -Append -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        "Git push tag: FAILED exit $LASTEXITCODE (may need auth or committed tree)" | Out-File -FilePath $receipt -Append -Encoding utf8
        Write-Warning "git push origin $tag failed with exit $LASTEXITCODE"
    }
    else {
        "Git push tag: PASS" | Out-File -FilePath $receipt -Append -Encoding utf8
    }
}

Write-Host '=== RELEASE COMPLETE ==='
exit 0