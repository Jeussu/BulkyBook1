param(
    [string]$ProjectPath = "BulkyBookWeb\BulkyBookWeb.csproj",
    [string]$PublishDir = "",
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = (Get-Location).Path
$SolutionPath = Join-Path $ProjectRoot "BulkyBook.sln"
$WebProjectPath = Join-Path $ProjectRoot $ProjectPath

if (-not (Test-Path $SolutionPath) -or -not (Test-Path $WebProjectPath)) {
    throw "Run this script from the BulkyBook repository root."
}

if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    $PublishDir = Join-Path ([System.IO.Path]::GetTempPath()) ("BulkyBookPublishCheck-" + [guid]::NewGuid().ToString("N"))
}
elseif (-not [System.IO.Path]::IsPathRooted($PublishDir)) {
    $PublishDir = Join-Path $ProjectRoot $PublishDir
}

$script:Results = [System.Collections.Generic.List[object]]::new()

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "== $Title ==" -ForegroundColor Cyan
}

function Add-Result {
    param(
        [string]$Name,
        [bool]$Passed,
        [string]$Details
    )

    $script:Results.Add([pscustomobject]@{
        Name = $Name
        Passed = $Passed
        Details = $Details
    }) | Out-Null

    if ($Passed) {
        Write-Host "[PASS] $Name - $Details" -ForegroundColor Green
    }
    else {
        Write-Host "[FAIL] $Name - $Details" -ForegroundColor Red
    }
}

function Test-PublishFile {
    param(
        [string]$RelativePath,
        [string]$Label
    )

    $path = Join-Path $PublishDir $RelativePath
    Add-Result -Name $Label -Passed (Test-Path -LiteralPath $path) -Details $RelativePath
}

function Read-JsonFile {
    param([string]$RelativePath)

    $path = Join-Path $PublishDir $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        return $null
    }

    return Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
}

function Test-ConfigValue {
    param(
        [string]$Name,
        [object]$Actual,
        [object]$Expected
    )

    Add-Result -Name $Name -Passed ($Actual -eq $Expected) -Details "Expected '$Expected', actual '$Actual'"
}

Write-Section "Project root"
Write-Host $ProjectRoot
Write-Host "Publish directory: $PublishDir"

if (-not $SkipPublish) {
    Write-Section "Publish"
    $publishArgs = @("publish", $WebProjectPath, "-c", $Configuration, "-o", $PublishDir)
    if ($VerboseOutput) {
        Write-Host ("dotnet " + ($publishArgs -join " "))
    }

    & dotnet @publishArgs
    Add-Result -Name "dotnet publish" -Passed ($LASTEXITCODE -eq 0) -Details "Configuration=$Configuration"
}
else {
    Write-Section "Publish"
    Add-Result -Name "Publish skipped" -Passed (Test-Path -LiteralPath $PublishDir) -Details $PublishDir
}

if (-not (Test-Path -LiteralPath $PublishDir)) {
    Add-Result -Name "Publish directory exists" -Passed $false -Details $PublishDir
}

Write-Section "IIS / package files"
Test-PublishFile -RelativePath "BulkyBookWeb.dll" -Label "Web app assembly"
Test-PublishFile -RelativePath "BulkyBookWeb.runtimeconfig.json" -Label "Runtime config"
Test-PublishFile -RelativePath "web.config" -Label "IIS web.config"
Test-PublishFile -RelativePath "appsettings.json" -Label "Base appsettings"
Test-PublishFile -RelativePath "appsettings.Production.json" -Label "Production appsettings"
Add-Result -Name "Development appsettings excluded" -Passed (-not (Test-Path -LiteralPath (Join-Path $PublishDir "appsettings.Development.json"))) -Details "appsettings.Development.json"

$webConfigPath = Join-Path $PublishDir "web.config"
if (Test-Path -LiteralPath $webConfigPath) {
    $webConfig = Get-Content -Raw -LiteralPath $webConfigPath
    Add-Result -Name "web.config uses AspNetCoreModuleV2" -Passed ($webConfig -match "AspNetCoreModuleV2") -Details "IIS/ANCM handler"
    Add-Result -Name "web.config starts BulkyBookWeb.dll" -Passed ($webConfig -match "BulkyBookWeb\.dll") -Details "process arguments"
    Add-Result -Name "stdout logging disabled" -Passed ($webConfig -match 'stdoutLogEnabled="false"') -Details "Production default"
}

Write-Section "Static assets"
Test-PublishFile -RelativePath "wwwroot\css\site.css" -Label "site.css"
Test-PublishFile -RelativePath "wwwroot\js\product.js" -Label "product.js"
Test-PublishFile -RelativePath "wwwroot\js\order.js" -Label "order.js"
Test-PublishFile -RelativePath "wwwroot\js\company.js" -Label "company.js"

$modernCoverDir = Join-Path $PublishDir "wwwroot\images\products\book-covers-modern"
$modernCoverCount = 0
if (Test-Path -LiteralPath $modernCoverDir) {
    $modernCoverCount = (Get-ChildItem -LiteralPath $modernCoverDir -Filter *.png -File | Measure-Object).Count
}
Add-Result -Name "Modern PNG covers included" -Passed ($modernCoverCount -ge 20) -Details "PNG count=$modernCoverCount"
Test-PublishFile -RelativePath "wwwroot\images\products\book-covers-modern\aspnet-core-mvc-fundamentals.png" -Label "Known modern cover 1"
Test-PublishFile -RelativePath "wwwroot\images\products\book-covers-modern\system-design-notes.png" -Label "Known modern cover 2"

Write-Section "Production config defaults"
$productionSettings = Read-JsonFile -RelativePath "appsettings.Production.json"
if ($null -eq $productionSettings) {
    Add-Result -Name "Production config parse" -Passed $false -Details "appsettings.Production.json missing"
}
else {
    Test-ConfigValue -Name "Production AutoMigrate disabled" -Actual $productionSettings.Database.AutoMigrate -Expected $false
    Test-ConfigValue -Name "Production demo seed disabled" -Actual $productionSettings.SeedData.EnableDemoData -Expected $false
    Test-ConfigValue -Name "Production local Stripe fallback disabled" -Actual $productionSettings.Stripe.EnableLocalCheckoutFallback -Expected $false
    Test-ConfigValue -Name "Production connection string left for host override" -Actual $productionSettings.ConnectionStrings.DefaultConnection -Expected ""
    Test-ConfigValue -Name "Production Stripe secret not committed" -Actual $productionSettings.Stripe.SecretKey -Expected ""
    Test-ConfigValue -Name "Production Facebook secret not committed" -Actual $productionSettings.Authentication.Facebook.AppSecret -Expected ""
    Test-ConfigValue -Name "Production SMTP password not committed" -Actual $productionSettings.Email.Smtp.Password -Expected ""
}

Write-Section "Publish leakage scan"
$textExtensions = @(".json", ".config", ".js", ".css", ".html", ".txt", ".xml", ".md")
$blockedPatterns = @(
    "Admin123!",
    "Customer123!",
    "Employee123!",
    "Company123!",
    "admin@bulky\.local",
    "customer@bulky\.local",
    "\(localdb\)",
    "MSSQLLocalDB",
    '"AutoMigrate"\s*:\s*true',
    '"EnableDemoData"\s*:\s*true',
    '"EnableLocalCheckoutFallback"\s*:\s*true',
    "no-reply@localhost",
    "Local Admin",
    "Local Street",
    "sk_live_",
    "sk_test_",
    "pk_live_"
)

$matches = [System.Collections.Generic.List[string]]::new()
Get-ChildItem -LiteralPath $PublishDir -Recurse -File |
    Where-Object { $textExtensions -contains $_.Extension.ToLowerInvariant() } |
    ForEach-Object {
        $content = Get-Content -Raw -LiteralPath $_.FullName
        foreach ($pattern in $blockedPatterns) {
            if ($content -match $pattern) {
                $relative = [System.IO.Path]::GetRelativePath($PublishDir, $_.FullName)
                $matches.Add("$relative :: $pattern") | Out-Null
            }
        }
    }

if ($matches.Count -gt 0 -and $VerboseOutput) {
    $matches | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}
Add-Result -Name "No development/demo secrets in publish text files" -Passed ($matches.Count -eq 0) -Details "Matches=$($matches.Count)"

$sourceExtensions = @(".cs", ".pubxml", ".csproj")
$sourceFiles = @(Get-ChildItem -LiteralPath $PublishDir -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $sourceExtensions -contains $_.Extension.ToLowerInvariant() })
Add-Result -Name "No source/publish profile files in output" -Passed ($sourceFiles.Count -eq 0) -Details "Matches=$($sourceFiles.Count)"

Write-Section "Summary"
$passed = @($script:Results | Where-Object { $_.Passed }).Count
$failed = @($script:Results | Where-Object { -not $_.Passed }).Count
Write-Host "Passed: $passed"
Write-Host "Failed: $failed"
Write-Host "PublishDir: $PublishDir"

if ($failed -gt 0) {
    exit 1
}
