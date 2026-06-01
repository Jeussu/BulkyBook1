param(
    [string]$OutputPath = "migrate-somee.sql"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = (Get-Location).Path
$SolutionPath = Join-Path $ProjectRoot "BulkyBook.sln"
$DataProjectPath = Join-Path $ProjectRoot "BulkyBook.DataAccess"
$StartupProjectPath = Join-Path $ProjectRoot "BulkyBookWeb"

if (-not (Test-Path $SolutionPath) -or -not (Test-Path $DataProjectPath) -or -not (Test-Path $StartupProjectPath)) {
    throw "Run this script from the BulkyBook repository root."
}

if (-not [System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath = Join-Path $ProjectRoot $OutputPath
}

Write-Host "Generating idempotent SQL migration script for review only..." -ForegroundColor Cyan
Write-Host "Output: $OutputPath"
Write-Host "This script does not apply SQL and does not run update-database." -ForegroundColor Yellow

& dotnet ef migrations script `
    --project "BulkyBook.DataAccess" `
    --startup-project "BulkyBookWeb" `
    --context "ApplicationDbContext" `
    --idempotent `
    -o $OutputPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "dotnet ef failed. If EF CLI is unavailable, run this command manually after installing/repairing dotnet-ef:" -ForegroundColor Yellow
    Write-Host "dotnet ef migrations script --project BulkyBook.DataAccess --startup-project BulkyBookWeb --context ApplicationDbContext --idempotent -o migrate-somee.sql"
    exit $LASTEXITCODE
}

if (Test-Path -LiteralPath $OutputPath) {
    Write-Host "Generated: $OutputPath" -ForegroundColor Green
    Write-Host "Review the SQL before applying it through Somee SQL tools or SSMS." -ForegroundColor Yellow
    Write-Host "Do not commit migrate-somee.sql unless explicitly requested." -ForegroundColor Yellow
}
else {
    throw "Expected SQL output was not created: $OutputPath"
}
