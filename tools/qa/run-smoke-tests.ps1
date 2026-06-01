param(
    [string]$BaseUrl = "https://localhost:7206",
    [switch]$SkipBuild,
    [switch]$StartApp,
    [switch]$StopApp,
    [switch]$VerboseOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ProjectRoot = (Get-Location).Path
$SolutionPath = Join-Path $ProjectRoot "BulkyBook.sln"
$WebProjectPath = Join-Path $ProjectRoot "BulkyBookWeb\BulkyBookWeb.csproj"

if (-not (Test-Path $SolutionPath) -or -not (Test-Path $WebProjectPath)) {
    throw "Run this script from the BulkyBook repository root."
}

$BaseUrl = $BaseUrl.TrimEnd("/")
$BaseUri = [Uri]$BaseUrl
$script:Results = New-Object System.Collections.Generic.List[object]
$script:StartedProcess = $null
$script:OriginalCertificateCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback

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

function Enable-LocalhostCertificateBypass {
    if (-not $BaseUri.IsLoopback -or $BaseUri.Scheme -ne "https") {
        return
    }

    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {
        param($Sender, $Certificate, $Chain, $SslPolicyErrors)

        if ($SslPolicyErrors -eq [System.Net.Security.SslPolicyErrors]::None) {
            return $true
        }

        try {
            return ($Sender -and $Sender.RequestUri -and $Sender.RequestUri.IsLoopback)
        }
        catch {
            return $false
        }
    }
}

function Invoke-SmokeRequest {
    param(
        [string]$Path,
        [int]$TimeoutMs = 15000
    )

    $url = if ($Path.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Path
    }
    else {
        "$BaseUrl$Path"
    }

    $request = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($url)
    $request.Method = "GET"
    $request.AllowAutoRedirect = $false
    $request.Timeout = $TimeoutMs
    $request.ReadWriteTimeout = $TimeoutMs
    $request.UserAgent = "BulkyBookSmoke/1.0"

    $response = $null
    try {
        $response = [System.Net.HttpWebResponse]$request.GetResponse()
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response -eq $null) {
            return [pscustomobject]@{
                Url = $url
                StatusCode = 0
                Location = $null
                Body = ""
                Error = $_.Exception.Message
            }
        }

        $response = [System.Net.HttpWebResponse]$_.Exception.Response
    }

    $body = ""
    try {
        $stream = $response.GetResponseStream()
        if ($stream -ne $null) {
            $reader = New-Object System.IO.StreamReader($stream)
            $body = $reader.ReadToEnd()
            $reader.Dispose()
        }
    }
    finally {
        $location = $response.Headers["Location"]
        $statusCode = [int]$response.StatusCode
        $response.Dispose()
    }

    return [pscustomobject]@{
        Url = $url
        StatusCode = $statusCode
        Location = $location
        Body = $body
        Error = $null
    }
}

function Test-AppResponding {
    try {
        $result = Invoke-SmokeRequest -Path "/Customer/Home/Index" -TimeoutMs 3000
        return ($result.StatusCode -gt 0)
    }
    catch {
        return $false
    }
}

function Wait-ForApp {
    param([int]$TimeoutSeconds = 60)

    for ($i = 1; $i -le $TimeoutSeconds; $i++) {
        if (Test-AppResponding) {
            return $true
        }

        if ($script:StartedProcess -ne $null -and $script:StartedProcess.HasExited) {
            return $false
        }

        Start-Sleep -Seconds 1
    }

    return $false
}

function Start-AppIfNeeded {
    if (-not $StartApp) {
        return
    }

    if (Test-AppResponding) {
        Write-Host "App already responds at $BaseUrl; using existing process."
        return
    }

    Write-Section "Starting app"
    $previousEnv = @{
        ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
        Database__AutoMigrate = $env:Database__AutoMigrate
        SeedData__EnableDemoData = $env:SeedData__EnableDemoData
        SeedAdmin__Email = $env:SeedAdmin__Email
        SeedAdmin__Password = $env:SeedAdmin__Password
    }

    try {
        $env:ASPNETCORE_ENVIRONMENT = "Development"
        $env:Database__AutoMigrate = "false"
        $env:SeedData__EnableDemoData = "false"
        $env:SeedAdmin__Email = ""
        $env:SeedAdmin__Password = ""

        $stdout = Join-Path $env:TEMP ("bulkybook-smoke-" + [Guid]::NewGuid().ToString("N") + ".out.log")
        $stderr = Join-Path $env:TEMP ("bulkybook-smoke-" + [Guid]::NewGuid().ToString("N") + ".err.log")
        $arguments = @("run", "--project", "BulkyBookWeb\BulkyBookWeb.csproj", "--no-build", "--urls", $BaseUrl)
        $script:StartedProcess = Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $ProjectRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

        if (-not (Wait-ForApp -TimeoutSeconds 75)) {
            $outText = if (Test-Path $stdout) { Get-Content -LiteralPath $stdout -Raw } else { "" }
            $errText = if (Test-Path $stderr) { Get-Content -LiteralPath $stderr -Raw } else { "" }
            throw "App did not respond at $BaseUrl. STDOUT: $outText STDERR: $errText"
        }

        Write-Host "App started at $BaseUrl with PID $($script:StartedProcess.Id)."
    }
    finally {
        foreach ($key in $previousEnv.Keys) {
            Set-Item -Path "Env:$key" -Value $previousEnv[$key]
        }
    }
}

function Stop-StartedApp {
    if (-not $StopApp -or $script:StartedProcess -eq $null) {
        return
    }

    Write-Section "Stopping app"
    try {
        $children = @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$($script:StartedProcess.Id)" -ErrorAction SilentlyContinue)
        foreach ($child in $children) {
            Stop-Process -Id $child.ProcessId -Force -ErrorAction SilentlyContinue
        }

        Stop-Process -Id $script:StartedProcess.Id -Force -ErrorAction SilentlyContinue
        Write-Host "Stopped smoke app process $($script:StartedProcess.Id)."
    }
    catch {
        Write-Warning "Unable to stop app process cleanly: $($_.Exception.Message)"
    }
}

function Assert-Status {
    param(
        [string]$Name,
        [string]$Path,
        [int[]]$AllowedStatusCodes,
        [switch]$RequireLoginRedirectIf302
    )

    $response = Invoke-SmokeRequest -Path $Path
    $passed = $AllowedStatusCodes -contains $response.StatusCode

    if ($passed -and $RequireLoginRedirectIf302 -and $response.StatusCode -eq 302) {
        $passed = ($response.Location -match "/Identity/Account/Login")
    }

    $details = "HTTP $($response.StatusCode) $Path"
    if ($response.Location) {
        $details += " -> $($response.Location)"
    }
    if ($response.Error) {
        $details += " Error: $($response.Error)"
    }

    Add-Result -Name $Name -Passed $passed -Details $details
    if ($VerboseOutput -and $response.Body) {
        Write-Host ($response.Body.Substring(0, [Math]::Min(300, $response.Body.Length)))
    }

    return $response
}

function Assert-HtmlChecks {
    param(
        [string]$Name,
        [object]$Response,
        [switch]$RequireModernCover
    )

    $body = [string]$Response.Body
    $checks = @(
        @{ Label = "$Name no exception page"; Passed = -not ($body -match "Developer Exception Page|An unhandled exception occurred|System\.InvalidOperationException|Microsoft\.AspNetCore\.Diagnostics") },
        @{ Label = "$Name no stale SVG cover path"; Passed = -not ($body -match "/images/products/book-covers/[^`"']+\.svg") },
        @{ Label = "$Name no local demo copy"; Passed = -not ($body -match "(?i)local demos|local testing|demo accounts|Stripe is not configured|test secret key") }
    )

    if ($RequireModernCover) {
        $checks += @{ Label = "$Name has modern cover path"; Passed = ($body -match "/images/products/book-covers-modern/") }
    }

    foreach ($check in $checks) {
        Add-Result -Name $check.Label -Passed ([bool]$check.Passed) -Details $Response.Url
    }
}

try {
    Enable-LocalhostCertificateBypass

    Write-Section "Project root"
    Write-Host $ProjectRoot

    if (-not $SkipBuild) {
        Write-Section "Build"
        dotnet build
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    Start-AppIfNeeded

    if (-not (Test-AppResponding)) {
        throw "App is not responding at $BaseUrl. Start the app first or rerun with -StartApp."
    }

    Write-Section "Anonymous routes"
    $homeResponse = Assert-Status -Name "Home catalog" -Path "/Customer/Home/Index" -AllowedStatusCodes @(200)
    $homePage1Response = Assert-Status -Name "Home page 1" -Path "/Customer/Home/Index?page=1" -AllowedStatusCodes @(200)
    $homePage2Response = Assert-Status -Name "Home page 2 size 8" -Path "/Customer/Home/Index?page=2&pageSize=8" -AllowedStatusCodes @(200)
    $homeSearchResponse = Assert-Status -Name "Home search core" -Path "/Customer/Home/Index?searchTerm=core" -AllowedStatusCodes @(200)
    $homePriceResponse = Assert-Status -Name "Home price filter" -Path "/Customer/Home/Index?minPrice=10&maxPrice=80" -AllowedStatusCodes @(200)
    $detailResponse = Assert-Status -Name "Product detail 1" -Path "/Customer/Home/Details?productId=1" -AllowedStatusCodes @(200)
    $loginResponse = Assert-Status -Name "Login" -Path "/Identity/Account/Login" -AllowedStatusCodes @(200)
    $registerResponse = Assert-Status -Name "Register" -Path "/Identity/Account/Register" -AllowedStatusCodes @(200)

    Write-Section "Protected routes"
    Assert-Status -Name "Cart index protected" -Path "/Customer/Cart/Index" -AllowedStatusCodes @(200, 302) -RequireLoginRedirectIf302 | Out-Null
    Assert-Status -Name "Cart summary protected" -Path "/Customer/Cart/Summary" -AllowedStatusCodes @(200, 302) -RequireLoginRedirectIf302 | Out-Null
    Assert-Status -Name "Admin product protected" -Path "/Admin/Product/Index" -AllowedStatusCodes @(200, 302) -RequireLoginRedirectIf302 | Out-Null
    Assert-Status -Name "Admin order protected" -Path "/Admin/Order/Index" -AllowedStatusCodes @(200, 302) -RequireLoginRedirectIf302 | Out-Null
    Assert-Status -Name "Admin order details protected" -Path "/Admin/Order/Details?orderId=1" -AllowedStatusCodes @(200, 302) -RequireLoginRedirectIf302 | Out-Null

    Write-Section "Rendered HTML checks"
    Assert-HtmlChecks -Name "Home" -Response $homeResponse -RequireModernCover
    Assert-HtmlChecks -Name "Home page 1" -Response $homePage1Response -RequireModernCover
    Assert-HtmlChecks -Name "Home page 2" -Response $homePage2Response -RequireModernCover
    Assert-HtmlChecks -Name "Home search" -Response $homeSearchResponse -RequireModernCover
    Assert-HtmlChecks -Name "Home price" -Response $homePriceResponse -RequireModernCover
    Assert-HtmlChecks -Name "Detail" -Response $detailResponse -RequireModernCover
    Assert-HtmlChecks -Name "Login" -Response $loginResponse
    Assert-HtmlChecks -Name "Register" -Response $registerResponse

    Write-Section "Modern cover assets"
    $covers = @(
        "aspnet-core-mvc-fundamentals.png",
        "azure-app-service-deployment.png",
        "clean-architecture-with-dotnet.png",
        "cloud-native-dotnet.png",
        "data-structures-in-csharp.png",
        "debugging-production-apps.png",
        "docker-for-dotnet-developers.png",
        "domain-driven-design-basics.png",
        "e-commerce-patterns.png",
        "ef-core-in-action.png",
        "git-and-github-workflow.png",
        "identity-and-security-aspnet-core.png",
        "javascript-for-razor-developers.png",
        "microservices-with-aspnet-core.png",
        "practical-csharp-12.png",
        "practical-devops-for-developers.png",
        "refactoring-legacy-code.png",
        "sql-server-for-developers.png",
        "stripe-payments-for-dotnet.png",
        "system-design-notes.png"
    )

    foreach ($cover in $covers) {
        Assert-Status -Name "Cover asset $cover" -Path "/images/products/book-covers-modern/$cover" -AllowedStatusCodes @(200) | Out-Null
    }

    Write-Section "Static assets"
    Assert-Status -Name "site.css" -Path "/css/site.css" -AllowedStatusCodes @(200) | Out-Null
    Assert-Status -Name "product.js" -Path "/js/product.js" -AllowedStatusCodes @(200) | Out-Null
    Assert-Status -Name "order.js" -Path "/js/order.js" -AllowedStatusCodes @(200) | Out-Null

    Write-Section "Summary"
    $failed = @($script:Results | Where-Object { -not $_.Passed })
    $passedCount = ($script:Results.Count - $failed.Count)
    Write-Host "Passed: $passedCount"
    Write-Host "Failed: $($failed.Count)"

    if ($failed.Count -gt 0) {
        Write-Host ""
        Write-Host "Failures:" -ForegroundColor Red
        foreach ($failure in $failed) {
            Write-Host "- $($failure.Name): $($failure.Details)" -ForegroundColor Red
        }
        exit 1
    }

    exit 0
}
finally {
    Stop-StartedApp
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $script:OriginalCertificateCallback
}
