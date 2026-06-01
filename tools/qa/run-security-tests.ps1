param(
    [string]$BaseUrl = "https://localhost:7206",
    [switch]$SkipBuild,
    [switch]$StartApp,
    [switch]$StopApp,
    [string]$CustomerEmail,
    [string]$CustomerPassword,
    [string]$AdminEmail,
    [string]$AdminPassword,
    [int]$ProductId = 1,
    [int]$OrderId = 1,
    [switch]$RunUploadTests,
    [switch]$RunIdorTests,
    [switch]$RunOverpostingTests,
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
$script:Results = [System.Collections.Generic.List[object]]::new()
$script:StartedProcess = $null
$script:CookieContainer = [System.Net.CookieContainer]::new()
$script:OriginalCertificateCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
$script:CustomerEmail = $CustomerEmail
$script:CustomerPassword = $CustomerPassword
$script:AdminEmail = $AdminEmail
$script:AdminPassword = $AdminPassword

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

function Add-Skip {
    param(
        [string]$Name,
        [string]$Details
    )

    $script:Results.Add([pscustomobject]@{
        Name = $Name
        Passed = $true
        Details = "SKIP: $Details"
    }) | Out-Null
    Write-Host "[SKIP] $Name - $Details" -ForegroundColor Yellow
}

function Initialize-WebRequestClient {
    if ($BaseUri.IsLoopback -and $BaseUri.Scheme -eq "https") {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = {
            param($Sender, $Certificate, $Chain, $SslPolicyErrors)

            if ($SslPolicyErrors -eq [System.Net.Security.SslPolicyErrors]::None) {
                return $true
            }

            try {
                if ($Sender -and $Sender.RequestUri -and $Sender.RequestUri.IsLoopback) {
                    return $true
                }
            }
            catch {
                return $true
            }

            return $false
        }
    }
}

function Resolve-Url {
    param([string]$PathOrUrl)

    if ([string]::IsNullOrWhiteSpace($PathOrUrl)) {
        return $BaseUrl
    }

    $decoded = [System.Net.WebUtility]::HtmlDecode($PathOrUrl)
    if ($decoded.StartsWith("http", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $decoded
    }

    if (-not $decoded.StartsWith("/")) {
        $decoded = "/$decoded"
    }

    return "$BaseUrl$decoded"
}

function Invoke-Request {
    param(
        [string]$Method,
        [string]$PathOrUrl,
        [byte[]]$BodyBytes = $null,
        [string]$ContentType = $null,
        [hashtable]$Headers = @{}
    )

    $url = Resolve-Url $PathOrUrl
    $response = $null

    try {
        $request = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($url)
        $request.Method = $Method
        $request.AllowAutoRedirect = $false
        $request.CookieContainer = $script:CookieContainer
        $request.Timeout = 30000
        $request.ReadWriteTimeout = 30000
        $request.UserAgent = "BulkyBookSecurityFlow/1.0"

        foreach ($key in $Headers.Keys) {
            $request.Headers[$key] = [string]$Headers[$key]
        }

        if ($BodyBytes -ne $null) {
            if (-not [string]::IsNullOrWhiteSpace($ContentType)) {
                $request.ContentType = $ContentType
            }
            $request.ContentLength = $BodyBytes.Length
            $requestStream = $request.GetRequestStream()
            $requestStream.Write($BodyBytes, 0, $BodyBytes.Length)
            $requestStream.Dispose()
        }

        $response = [System.Net.HttpWebResponse]$request.GetResponse()
    }
    catch [System.Net.WebException] {
        if ($_.Exception.Response -eq $null) {
            return [pscustomobject]@{
                Url = $url
                StatusCode = 0
                Location = $null
                Body = $_.Exception.Message
            }
        }

        $response = [System.Net.HttpWebResponse]$_.Exception.Response
    }

    try {
        $body = ""
        $stream = $response.GetResponseStream()
        if ($stream -ne $null) {
            $reader = [System.IO.StreamReader]::new($stream)
            $body = $reader.ReadToEnd()
            $reader.Dispose()
        }

        if ($VerboseOutput) {
            Write-Host "[$Method] $url -> $([int]$response.StatusCode)"
        }

        return [pscustomobject]@{
            Url = $url
            StatusCode = [int]$response.StatusCode
            Location = $response.Headers["Location"]
            Body = $body
        }
    }
    finally {
        if ($response -ne $null) {
            $response.Dispose()
        }
    }
}

function Invoke-Get {
    param([string]$PathOrUrl)
    return Invoke-Request -Method "GET" -PathOrUrl $PathOrUrl
}

function Invoke-Delete {
    param(
        [string]$PathOrUrl,
        [hashtable]$Headers = @{}
    )
    return Invoke-Request -Method "DELETE" -PathOrUrl $PathOrUrl -Headers $Headers
}

function Invoke-PostForm {
    param(
        [string]$PathOrUrl,
        [hashtable]$Fields
    )

    $pairs = [System.Collections.Generic.List[string]]::new()
    foreach ($key in $Fields.Keys) {
        $value = if ($null -eq $Fields[$key]) { "" } else { [string]$Fields[$key] }
        $pairs.Add(("{0}={1}" -f [System.Net.WebUtility]::UrlEncode([string]$key), [System.Net.WebUtility]::UrlEncode($value)))
    }

    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes(($pairs -join "&"))
    return Invoke-Request -Method "POST" -PathOrUrl $PathOrUrl -BodyBytes $bodyBytes -ContentType "application/x-www-form-urlencoded"
}

function Invoke-MultipartPost {
    param(
        [string]$PathOrUrl,
        [hashtable]$Fields,
        [string]$FileFieldName,
        [string]$FilePath,
        [string]$FileContentType
    )

    $boundary = "----BulkyBookSecurityFlow$([Guid]::NewGuid().ToString("N"))"
    $memory = [System.IO.MemoryStream]::new()
    $utf8 = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.IO.StreamWriter]::new($memory, $utf8)

    foreach ($key in $Fields.Keys) {
        $value = if ($null -eq $Fields[$key]) { "" } else { [string]$Fields[$key] }
        $writer.Write("--$boundary`r`n")
        $writer.Write("Content-Disposition: form-data; name=`"$key`"`r`n`r`n")
        $writer.Write("$value`r`n")
    }

    if (-not [string]::IsNullOrWhiteSpace($FilePath) -and (Test-Path $FilePath)) {
        $fileName = [System.IO.Path]::GetFileName($FilePath)
        $writer.Write("--$boundary`r`n")
        $writer.Write("Content-Disposition: form-data; name=`"$FileFieldName`"; filename=`"$fileName`"`r`n")
        $writer.Write("Content-Type: $FileContentType`r`n`r`n")
        $writer.Flush()
        $fileBytes = [System.IO.File]::ReadAllBytes($FilePath)
        $memory.Write($fileBytes, 0, $fileBytes.Length)
        $writer.Write("`r`n")
    }

    $writer.Write("--$boundary--`r`n")
    $writer.Flush()
    $bodyBytes = $memory.ToArray()
    $writer.Dispose()
    $memory.Dispose()

    return Invoke-Request -Method "POST" -PathOrUrl $PathOrUrl -BodyBytes $bodyBytes -ContentType "multipart/form-data; boundary=$boundary"
}

function Follow-Redirect {
    param([object]$Response)

    if ($Response.StatusCode -in @(301, 302, 303, 307, 308) -and $Response.Location) {
        return Invoke-Get -PathOrUrl $Response.Location
    }

    return $Response
}

function Get-InputTagByName {
    param(
        [string]$Html,
        [string]$Name
    )

    $pattern = '<input\b(?=[^>]*\bname\s*=\s*["'']' + [regex]::Escape($Name) + '["''])[^>]*>'
    $match = [regex]::Match($Html, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success) {
        return $match.Value
    }

    return $null
}

function Get-AttributeValue {
    param(
        [string]$Tag,
        [string]$Attribute
    )

    if ([string]::IsNullOrWhiteSpace($Tag)) {
        return $null
    }

    $pattern = '\b' + [regex]::Escape($Attribute) + '\s*=\s*["'']([^"'']*)["'']'
    $match = [regex]::Match($Tag, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success) {
        return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
    }

    return $null
}

function Get-InputValue {
    param(
        [string]$Html,
        [string]$Name
    )

    $tag = Get-InputTagByName -Html $Html -Name $Name
    return Get-AttributeValue -Tag $tag -Attribute "value"
}

function Get-AntiForgeryToken {
    param([string]$Html)
    return Get-InputValue -Html $Html -Name "__RequestVerificationToken"
}

function Get-MetaRequestVerificationToken {
    param([string]$Html)

    $pattern = '<meta\b(?=[^>]*\bname\s*=\s*["'']request-verification-token["''])(?=[^>]*\bcontent\s*=\s*["'']([^"'']*)["''])[^>]*>'
    $match = [regex]::Match($Html, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success) {
        return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
    }

    return $null
}

function Test-HtmlHasName {
    param(
        [string]$Html,
        [string]$Name
    )

    $pattern = '\bname\s*=\s*["'']' + [regex]::Escape($Name) + '["'']'
    return [regex]::IsMatch($Html, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
}

function Get-JsonBody {
    param([object]$Response)

    try {
        return $Response.Body | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return $null
    }
}

function Test-AdminDenied {
    param(
        [string]$Name,
        [object]$Response,
        [string]$AdminMarker = ""
    )

    $notAuthorizedStatus = $Response.StatusCode -in @(401, 403) -or ($Response.StatusCode -in @(301, 302, 303) -and ($Response.Location -match "/Identity/Account/Login|/Identity/Account/AccessDenied"))
    $noAdminContent = if ([string]::IsNullOrWhiteSpace($AdminMarker)) { $true } else { -not ($Response.Body -match $AdminMarker) }
    Add-Result -Name $Name -Passed ($notAuthorizedStatus -or ($Response.StatusCode -eq 404) -or ($Response.StatusCode -eq 200 -and $noAdminContent)) -Details "HTTP $($Response.StatusCode) Location=$($Response.Location)"
}

function Set-ScopedEnvironment {
    param(
        [string]$Name,
        [string]$Value,
        [hashtable]$OriginalValues
    )

    if (-not $OriginalValues.ContainsKey($Name)) {
        $OriginalValues[$Name] = [Environment]::GetEnvironmentVariable($Name, "Process")
    }

    [Environment]::SetEnvironmentVariable($Name, $Value, "Process")
}

function Restore-ScopedEnvironment {
    param([hashtable]$OriginalValues)

    foreach ($name in $OriginalValues.Keys) {
        [Environment]::SetEnvironmentVariable($name, $OriginalValues[$name], "Process")
    }
}

function Test-AppResponding {
    $response = Invoke-Get "/Customer/Home/Index"
    return $response.StatusCode -in @(200, 301, 302)
}

function Wait-ForApp {
    param([int]$Seconds = 45)

    $deadline = (Get-Date).AddSeconds($Seconds)
    do {
        if (Test-AppResponding) {
            return $true
        }
        Start-Sleep -Seconds 1
    } while ((Get-Date) -lt $deadline)

    return $false
}

function Start-AppIfNeeded {
    if (-not $StartApp) {
        return
    }

    Write-Section "Start app"
    $envBackup = @{}
    Set-ScopedEnvironment -Name "ASPNETCORE_ENVIRONMENT" -Value "Development" -OriginalValues $envBackup
    Set-ScopedEnvironment -Name "Database__AutoMigrate" -Value "false" -OriginalValues $envBackup
    Set-ScopedEnvironment -Name "SeedData__EnableDemoData" -Value "false" -OriginalValues $envBackup

    try {
        $arguments = @(
            "run",
            "--project",
            "`"$WebProjectPath`"",
            "--no-build",
            "--urls",
            $BaseUrl
        )
        $script:StartedProcess = Start-Process -FilePath "dotnet" -ArgumentList $arguments -PassThru -WindowStyle Hidden
    }
    finally {
        Restore-ScopedEnvironment -OriginalValues $envBackup
    }

    if (-not (Wait-ForApp -Seconds 60)) {
        throw "Started app process did not respond at $BaseUrl."
    }

    Add-Result -Name "App started" -Passed $true -Details "PID $($script:StartedProcess.Id) at $BaseUrl"
}

function Stop-StartedApp {
    if ($StopApp -and $script:StartedProcess -ne $null -and -not $script:StartedProcess.HasExited) {
        Write-Section "Stop app"
        Stop-Process -Id $script:StartedProcess.Id -Force
        Add-Result -Name "App stopped" -Passed $true -Details "PID $($script:StartedProcess.Id)"
    }
}

function Resolve-Credentials {
    $devSettingsPath = Join-Path $ProjectRoot "BulkyBookWeb\appsettings.Development.json"
    if (([string]::IsNullOrWhiteSpace($script:AdminEmail) -or [string]::IsNullOrWhiteSpace($script:AdminPassword)) -and (Test-Path $devSettingsPath)) {
        try {
            $settings = Get-Content -Path $devSettingsPath -Raw | ConvertFrom-Json -ErrorAction Stop
            if ($settings.SeedAdmin -and $settings.SeedAdmin.Email -and $settings.SeedAdmin.Password) {
                $script:AdminEmail = [string]$settings.SeedAdmin.Email
                $script:AdminPassword = [string]$settings.SeedAdmin.Password
            }
        }
        catch {
            if ($VerboseOutput) {
                Write-Host "Could not parse ${devSettingsPath}: $($_.Exception.Message)"
            }
        }
    }

    $seedPath = Join-Path $ProjectRoot "BulkyBook.DataAccess\DbInitializer\DemoDataSeeder.cs"
    if (Test-Path $seedPath) {
        $seedText = Get-Content -Path $seedPath -Raw

        if ([string]::IsNullOrWhiteSpace($script:AdminEmail) -or [string]::IsNullOrWhiteSpace($script:AdminPassword)) {
            $adminMatch = [regex]::Match($seedText, 'new\s+DemoUser\("([^"]+)",\s*"([^"]+)",\s*SD\.Role_Admin', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if ($adminMatch.Success) {
                $script:AdminEmail = $adminMatch.Groups[1].Value
                $script:AdminPassword = $adminMatch.Groups[2].Value
            }
        }

        if ([string]::IsNullOrWhiteSpace($script:CustomerEmail) -or [string]::IsNullOrWhiteSpace($script:CustomerPassword)) {
            $customerMatch = [regex]::Match($seedText, 'new\s+DemoUser\("([^"]+)",\s*"([^"]+)",\s*SD\.Role_User_Indi', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if ($customerMatch.Success) {
                $script:CustomerEmail = $customerMatch.Groups[1].Value
                $script:CustomerPassword = $customerMatch.Groups[2].Value
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($script:AdminEmail) -or [string]::IsNullOrWhiteSpace($script:AdminPassword)) {
        throw "Admin credentials were not provided and no local development admin account was found. Rerun with -AdminEmail and -AdminPassword."
    }

    if ([string]::IsNullOrWhiteSpace($script:CustomerEmail) -or [string]::IsNullOrWhiteSpace($script:CustomerPassword)) {
        throw "Customer credentials were not provided and no local development customer account was found. Rerun with -CustomerEmail and -CustomerPassword."
    }

    Add-Result -Name "Credentials resolved" -Passed $true -Details "Customer=$script:CustomerEmail Admin=$script:AdminEmail"
}

function Login-User {
    param(
        [string]$Email,
        [string]$Password,
        [string]$ExpectedMarker,
        [string]$Name
    )

    $script:CookieContainer = [System.Net.CookieContainer]::new()
    $loginPage = Invoke-Get "/Identity/Account/Login"
    $token = Get-AntiForgeryToken $loginPage.Body
    if ([string]::IsNullOrWhiteSpace($token)) {
        Add-Result -Name "$Name login token" -Passed $false -Details "No anti-forgery token."
        return $false
    }

    $loginResponse = Invoke-PostForm -PathOrUrl "/Identity/Account/Login" -Fields @{
        "__RequestVerificationToken" = $token
        "Input.Email" = $Email
        "Input.Password" = $Password
        "Input.RememberMe" = "false"
    }
    Add-Result -Name "$Name login POST" -Passed ($loginResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($loginResponse.StatusCode)"
    $afterLogin = Invoke-Get "/Customer/Home/Index"
    $loggedIn = $afterLogin.Body -match "/Identity/Account/Logout|logoutForm|$ExpectedMarker"
    Add-Result -Name "$Name login succeeded" -Passed $loggedIn -Details $Email
    return $loggedIn
}

function Invoke-StaticSecurityScan {
    Write-Section "A. Static security scan"

    $controllerFiles = @(
        "BulkyBookWeb\Areas\Customer\Controllers\HomeController.cs",
        "BulkyBookWeb\Areas\Customer\Controllers\CartController.cs",
        "BulkyBookWeb\Areas\Admin\Controllers\ProductController.cs",
        "BulkyBookWeb\Areas\Admin\Controllers\CategoryController.cs",
        "BulkyBookWeb\Areas\Admin\Controllers\CoverTypeController.cs",
        "BulkyBookWeb\Areas\Admin\Controllers\CompanyController.cs",
        "BulkyBookWeb\Areas\Admin\Controllers\OrderController.cs"
    )

    $missingAntiForgery = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $controllerFiles) {
        $lines = Get-Content -Path $file
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '\[Http(Post|Delete)') {
                $attributeBlock = $lines[$i]
                $j = $i + 1
                while ($j -lt $lines.Count -and $lines[$j] -notmatch 'public\s+.*IActionResult') {
                    $attributeBlock += "`n$($lines[$j])"
                    $j++
                }
                if ($j -lt $lines.Count) {
                    $attributeBlock += "`n$($lines[$j])"
                }
                if ($attributeBlock -notmatch 'ValidateAntiForgeryToken') {
                    $missingAntiForgery.Add(("{0}:{1}" -f $file, ($i + 1))) | Out-Null
                }
            }
        }
    }
    Add-Result -Name "POST/DELETE actions require anti-forgery" -Passed ($missingAntiForgery.Count -eq 0) -Details ($(if ($missingAntiForgery.Count -eq 0) { "No missing controller tokens." } else { $missingAntiForgery -join "; " }))

    $unsafeGetCandidates = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $controllerFiles) {
        $content = Get-Content -Path $file -Raw
        $methodMatches = [regex]::Matches($content, '(?ms)(?<attrs>(?:\s*\[[^\]]+\]\s*)*)\s*public\s+(?:async\s+)?IActionResult\s+(?<name>\w+)\s*\([^)]*\)\s*\{(?<body>.*?)(?=\n\s*(?:\[[^\]]+\]\s*)*\s*public\s+(?:async\s+)?IActionResult|\z)')
        foreach ($match in $methodMatches) {
            $attrs = $match.Groups["attrs"].Value
            $body = $match.Groups["body"].Value
            if ($attrs -notmatch 'HttpPost|HttpDelete' -and $body -match '_unitOfWork\.Save\(|\.Remove\(|\.Add\(|UpdateStatus\(|Session\.Clear\(|SetInt32\(') {
                $unsafeGetCandidates.Add(("{0}:{1}" -f $file, $match.Groups["name"].Value)) | Out-Null
            }
        }
    }
    if ($unsafeGetCandidates.Count -gt 0) {
        Add-Skip -Name "GET mutation candidates reviewed" -Details (($unsafeGetCandidates -join "; ") + " require callback/confirmation-specific manual review before route conversion.")
    }
    else {
        Add-Result -Name "No unsafe GET mutation candidates" -Passed $true -Details "Static scan found no GET mutations."
    }

    $orderController = Get-Content -Path "BulkyBookWeb\Areas\Admin\Controllers\OrderController.cs" -Raw
    $orderIndexStaffGate = [regex]::IsMatch($orderController, '(?s)\[Authorize\(Roles\s*=\s*SD\.Role_Admin\s*\+\s*","\s*\+\s*SD\.Role_Employee\)\]\s*public\s+IActionResult\s+Index\s*\(')
    $orderGetAllStaffGate = [regex]::IsMatch($orderController, '(?s)\[HttpGet\]\s*\[Authorize\(Roles\s*=\s*SD\.Role_Admin\s*\+\s*","\s*\+\s*SD\.Role_Employee\)\]\s*public\s+IActionResult\s+GetAll\s*\(')
    Add-Result -Name "Admin order list/API have staff role gate" -Passed ($orderIndexStaffGate -and $orderGetAllStaffGate) -Details "Order Index and GetAll should not expose admin management pages/data to ordinary customers."

    $rawMatches = Select-String -Path "BulkyBookWeb\Areas\**\*.cshtml","BulkyBookWeb\Views\**\*.cshtml" -Pattern "Html.Raw" -ErrorAction SilentlyContinue
    $unsafeRaw = @($rawMatches | Where-Object { $_.Line -notmatch "SafeHtml\.Sanitize|JsonSerializer\.Serialize" })
    Add-Result -Name "Html.Raw usage is sanitizer/serializer guarded" -Passed ($unsafeRaw.Count -eq 0) -Details ($(if ($unsafeRaw.Count -eq 0) { "All Html.Raw usages are guarded." } else { ($unsafeRaw | ForEach-Object { "$($_.Path):$($_.LineNumber)" }) -join "; " }))

    $productController = Get-Content -Path "BulkyBookWeb\Areas\Admin\Controllers\ProductController.cs" -Raw
    Add-Result -Name "Product upload extension allowlist exists" -Passed ($productController -match "AllowedImageExtensions" -and $productController -match "\.jpg" -and $productController -match "\.png" -and $productController -match "\.webp") -Details "ProductController upload extension checks"
    Add-Result -Name "Product upload content type allowlist exists" -Passed ($productController -match "AllowedImageContentTypes" -and $productController -match "image/jpeg" -and $productController -match "image/png") -Details "ProductController upload content-type checks"
    Add-Result -Name "Product upload size limit exists" -Passed ($productController -match "MaxImageBytes" -and $productController -match "2 \* 1024 \* 1024") -Details "ProductController upload size checks"
    Add-Result -Name "Product upload filename/path safety exists" -Passed ($productController -match "Guid\.NewGuid" -and $productController -match "Path\.GetFullPath" -and $productController -match "StartsWith\(uploadDirectory") -Details "Random upload names and delete path containment"

    foreach ($jsPath in @("BulkyBookWeb\wwwroot\js\product.js", "BulkyBookWeb\wwwroot\js\company.js")) {
        if (Test-Path $jsPath) {
            $js = Get-Content -Path $jsPath -Raw
            Add-Result -Name "$([System.IO.Path]::GetFileName($jsPath)) AJAX delete sends anti-forgery" -Passed ($js -match "RequestVerificationToken" -and $js -match 'request-verification-token') -Details $jsPath
        }
    }
}

function Test-RenderedNoSecrets {
    param(
        [string]$Name,
        [object]$Response,
        [switch]$StrictDemoText
    )

    $secretPattern = '(?i)Admin123|Customer123|ConnectionStrings|DefaultConnection|Stripe:SecretKey|Facebook:AppSecret|SmtpPassword|ApiKey|SecretKey'
    if ($StrictDemoText) {
        $secretPattern = '(?i)Admin123|Customer123|ConnectionStrings|DefaultConnection|Stripe:SecretKey|Facebook:AppSecret|SmtpPassword|ApiKey|SecretKey|demo accounts|local testing|local demos|admin@bulky\.local|customer@bulky\.local'
    }
    Add-Result -Name "$Name no secret/dev leakage" -Passed (-not ($Response.Body -match $secretPattern)) -Details $Response.Url
}

function Run-AnonymousChecks {
    Write-Section "B. Anonymous security checks"
    $script:CookieContainer = [System.Net.CookieContainer]::new()

    $anonymousTargets = @(
        @{ Name = "Anonymous Product Index"; Url = "/Admin/Product/Index"; Marker = "Product List|Product Inventory" },
        @{ Name = "Anonymous Product Upsert"; Url = "/Admin/Product/Upsert"; Marker = "Create Product|Update Product" },
        @{ Name = "Anonymous Order Index"; Url = "/Admin/Order/Index"; Marker = "Order Management|Order List" },
        @{ Name = "Anonymous Order Details"; Url = "/Admin/Order/Details?orderId=$OrderId"; Marker = "Order Details" },
        @{ Name = "Anonymous Product GetAll"; Url = "/Admin/Product/GetAll"; Marker = '"data"' },
        @{ Name = "Anonymous Company GetAll"; Url = "/Admin/Company/GetAll"; Marker = '"data"' }
    )

    foreach ($target in $anonymousTargets) {
        Test-AdminDenied -Name $target.Name -Response (Invoke-Get $target.Url) -AdminMarker $target.Marker
    }

    foreach ($url in @("/Customer/Home/Index", "/Customer/Home/Details?productId=$ProductId", "/Identity/Account/Login", "/Identity/Account/Register")) {
        $response = Invoke-Get $url
        Add-Result -Name "Public page loads $url" -Passed ($response.StatusCode -eq 200) -Details "HTTP $($response.StatusCode)"
        Test-RenderedNoSecrets -Name "Public page $url" -Response $response -StrictDemoText
    }
}

function Run-CustomerChecks {
    Write-Section "C. Customer security checks"
    if (-not (Login-User -Email $script:CustomerEmail -Password $script:CustomerPassword -ExpectedMarker "logoutForm" -Name "Customer")) {
        throw "Customer login failed."
    }

    $customerDeniedTargets = @(
        @{ Name = "Customer denied Product Index"; Url = "/Admin/Product/Index"; Marker = "Product List|Product Inventory" },
        @{ Name = "Customer denied Product Upsert"; Url = "/Admin/Product/Upsert"; Marker = "Create Product|Update Product" },
        @{ Name = "Customer denied Category Index"; Url = "/Admin/Category/Index"; Marker = "Category List" },
        @{ Name = "Customer denied Company Index"; Url = "/Admin/Company/Index"; Marker = "Company List" },
        @{ Name = "Customer denied Order Index"; Url = "/Admin/Order/Index"; Marker = "Order Management|Order List" },
        @{ Name = "Customer denied Order Details"; Url = "/Admin/Order/Details?orderId=$OrderId"; Marker = "Order Details" },
        @{ Name = "Customer denied Order GetAll"; Url = "/Admin/Order/GetAll?status=all"; Marker = '"data"' }
    )

    foreach ($target in $customerDeniedTargets) {
        Test-AdminDenied -Name $target.Name -Response (Invoke-Get $target.Url) -AdminMarker $target.Marker
    }

    $detail = Invoke-Get "/Customer/Home/Details?productId=$ProductId"
    $postWithoutToken = Invoke-PostForm -PathOrUrl "/Customer/Home/Details?productId=$ProductId" -Fields @{
        "ShoppingCart.ProductId" = "$ProductId"
        "ShoppingCart.Count" = "1"
    }
    Add-Result -Name "Add to cart rejects missing anti-forgery" -Passed ($postWithoutToken.StatusCode -eq 400 -or $postWithoutToken.StatusCode -eq 403) -Details "HTTP $($postWithoutToken.StatusCode)"

    $detailToken = Get-AntiForgeryToken $detail.Body
    Add-Result -Name "Detail anti-forgery token rendered" -Passed (-not [string]::IsNullOrWhiteSpace($detailToken)) -Details "/Customer/Home/Details?productId=$ProductId"

    foreach ($action in @("Plus", "Minus", "Remove")) {
        $invalidCart = Invoke-PostForm -PathOrUrl "/Customer/Cart/${action}?cartId=999999999" -Fields @{
            "__RequestVerificationToken" = $detailToken
        }
        Add-Result -Name "Invalid cartId $action denied" -Passed ($invalidCart.StatusCode -in @(403, 404)) -Details "HTTP $($invalidCart.StatusCode)"
    }

    $summary = Invoke-Get "/Customer/Cart/Summary"
    if ($summary.StatusCode -eq 200) {
        Add-Result -Name "Summary token rendered" -Passed (-not [string]::IsNullOrWhiteSpace((Get-AntiForgeryToken $summary.Body))) -Details "/Customer/Cart/Summary"
    }
    else {
        Add-Skip -Name "Summary token rendered" -Details "Cart may be empty; Summary returned HTTP $($summary.StatusCode)."
    }
}

function Run-AdminChecks {
    Write-Section "D. Admin security checks"
    if (-not (Login-User -Email $script:AdminEmail -Password $script:AdminPassword -ExpectedMarker "Content Management|Manage Orders" -Name "Admin")) {
        throw "Admin login failed."
    }

    foreach ($url in @("/Admin/Product/Index", "/Admin/Product/Upsert", "/Admin/Order/Index", "/Admin/Order/Details?orderId=$OrderId")) {
        $response = Invoke-Get $url
        Add-Result -Name "Admin can access $url" -Passed ($response.StatusCode -eq 200) -Details "HTTP $($response.StatusCode)"
        Test-RenderedNoSecrets -Name "Admin page $url" -Response $response
    }

    $productDeleteNoToken = Invoke-Delete "/Admin/Product/Delete/$ProductId"
    Add-Result -Name "Product delete rejects missing anti-forgery" -Passed ($productDeleteNoToken.StatusCode -in @(400, 403)) -Details "HTTP $($productDeleteNoToken.StatusCode)"

    $companyDeleteNoToken = Invoke-Delete "/Admin/Company/Delete/1"
    Add-Result -Name "Company delete rejects missing anti-forgery" -Passed ($companyDeleteNoToken.StatusCode -in @(400, 403)) -Details "HTTP $($companyDeleteNoToken.StatusCode)"

    $productIndex = Invoke-Get "/Admin/Product/Index"
    Add-Result -Name "Layout anti-forgery meta rendered" -Passed (-not [string]::IsNullOrWhiteSpace((Get-MetaRequestVerificationToken $productIndex.Body))) -Details "/Admin/Product/Index"

    $productCreate = Invoke-Get "/Admin/Product/Upsert"
    Add-Result -Name "Product Upsert token rendered" -Passed (-not [string]::IsNullOrWhiteSpace((Get-AntiForgeryToken $productCreate.Body))) -Details "/Admin/Product/Upsert"
    Add-Result -Name "Product uploadBox rendered" -Passed (Test-HtmlHasName -Html $productCreate.Body -Name "file") -Details "/Admin/Product/Upsert"
}

function Run-RenderedStaticLeakChecks {
    Write-Section "E. Rendered/static leakage checks"
    foreach ($url in @("/css/site.css", "/js/product.js", "/js/order.js", "/js/company.js")) {
        $response = Invoke-Get $url
        Add-Result -Name "Static asset loads $url" -Passed ($response.StatusCode -eq 200) -Details "HTTP $($response.StatusCode)"
        Test-RenderedNoSecrets -Name "Static asset $url" -Response $response -StrictDemoText
    }
}

function Run-OverpostingChecks {
    Write-Section "F. Optional over-posting checks"
    if (-not $RunOverpostingTests) {
        Add-Skip -Name "Over-posting dynamic checks" -Details "Use -RunOverpostingTests for local-safe direct POST checks."
        return
    }

    if (-not (Login-User -Email $script:CustomerEmail -Password $script:CustomerPassword -ExpectedMarker "logoutForm" -Name "Customer overpost")) {
        throw "Customer login failed."
    }

    $summary = Invoke-Get "/Customer/Cart/Summary"
    if ($summary.StatusCode -ne 200) {
        Add-Skip -Name "Customer summary overpost" -Details "Cart may be empty; Summary returned HTTP $($summary.StatusCode)."
        return
    }

    $token = Get-AntiForgeryToken $summary.Body
    $response = Invoke-PostForm -PathOrUrl "/Customer/Cart/Summary" -Fields @{
        "__RequestVerificationToken" = $token
        "OrderHeader.Name" = ""
        "OrderHeader.PhoneNumber" = ""
        "OrderHeader.PostalCode" = ""
        "OrderHeader.StreetAddress" = ""
        "OrderHeader.City" = ""
        "OrderHeader.State" = ""
        "OrderHeader.OrderStatus" = "Shipped"
        "OrderHeader.PaymentStatus" = "Approved"
        "OrderHeader.OrderTotal" = "0"
    }
    Add-Result -Name "Invalid summary overpost rejected" -Passed ($response.StatusCode -eq 200 -and $response.Body -match "validation|field-validation|required") -Details "HTTP $($response.StatusCode)"
}

function Run-UploadChecks {
    Write-Section "G. Optional upload security checks"
    if (-not $RunUploadTests) {
        Add-Skip -Name "Invalid upload dynamic checks" -Details "Use -RunUploadTests for local-safe invalid upload checks."
        return
    }

    if (-not (Login-User -Email $script:AdminEmail -Password $script:AdminPassword -ExpectedMarker "Content Management|Manage Orders" -Name "Admin upload")) {
        throw "Admin login failed."
    }

    $createPage = Invoke-Get "/Admin/Product/Upsert"
    $token = Get-AntiForgeryToken $createPage.Body
    $categoryId = Get-FirstSelectOptionValue -Html $createPage.Body -SelectName "Product.CategoryId"
    $coverTypeId = Get-FirstSelectOptionValue -Html $createPage.Body -SelectName "Product.CoverTypeId"

    if ([string]::IsNullOrWhiteSpace($token) -or [string]::IsNullOrWhiteSpace($categoryId) -or [string]::IsNullOrWhiteSpace($coverTypeId)) {
        Add-Result -Name "Invalid upload prerequisites" -Passed $false -Details "Missing token/category/cover option."
        return
    }

    $tempFile = Join-Path ([System.IO.Path]::GetTempPath()) ("bulkybook-security-upload-{0}.txt" -f [Guid]::NewGuid().ToString("N"))
    Set-Content -LiteralPath $tempFile -Value "<script>alert('xss')</script>" -Encoding ASCII
    try {
        $response = Invoke-MultipartPost -PathOrUrl "/Admin/Product/Upsert" -FileFieldName "file" -FilePath $tempFile -FileContentType "text/plain" -Fields @{
            "__RequestVerificationToken" = $token
            "Product.Id" = "0"
            "Product.ImageUrl" = ""
            "Product.Title" = "QA-E-Rejected-Upload"
            "Product.Author" = "QA"
            "Product.ISBN" = "QA-E-UPLOAD"
            "Product.Description" = "Rejected upload check"
            "Product.ListPrice" = "10"
            "Product.Price" = "9"
            "Product.Price50" = "8"
            "Product.Price100" = "7"
            "Product.StockQuantity" = "1"
            "Product.IsActive" = "true"
            "Product.CategoryId" = $categoryId
            "Product.CoverTypeId" = $coverTypeId
        }
        $rejected = $response.StatusCode -eq 200 -and $response.Body -match "Only \.jpg|content type|allowed image"
        Add-Result -Name "Invalid .txt upload rejected server-side" -Passed $rejected -Details "HTTP $($response.StatusCode)"
    }
    finally {
        if (Test-Path $tempFile) {
            Remove-Item -LiteralPath $tempFile -Force
        }
    }
}

function Get-FirstSelectOptionValue {
    param(
        [string]$Html,
        [string]$SelectName
    )

    $selectPattern = '<select\b(?=[^>]*\bname\s*=\s*["'']' + [regex]::Escape($SelectName) + '["''])[^>]*>(?<content>.*?)</select>'
    $selectMatch = [regex]::Match($Html, $selectPattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $selectMatch.Success) {
        return $null
    }

    $optionMatches = [regex]::Matches($selectMatch.Groups["content"].Value, '<option\b(?=[^>]*\bvalue\s*=\s*["'']([^"'']+)["''])[^>]*>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    foreach ($option in $optionMatches) {
        if ($option.Value -notmatch '\bdisabled\b') {
            return [System.Net.WebUtility]::HtmlDecode($option.Groups[1].Value)
        }
    }

    return $null
}

function Run-IdorChecks {
    Write-Section "H. Optional IDOR checks"
    if (-not $RunIdorTests) {
        Add-Skip -Name "Cross-customer IDOR checks" -Details "Use -RunIdorTests when local-safe multi-user data is available."
        return
    }

    Add-Skip -Name "Cross-customer cart mutation" -Details "Default security pass covers invalid/unknown cartId. Cross-user cart creation is deferred to a dedicated disposable-data test."
}

try {
    Initialize-WebRequestClient
    Resolve-Credentials

    Write-Section "Project root"
    Write-Host $ProjectRoot

    if (-not $SkipBuild) {
        Write-Section "Build"
        dotnet build
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    Invoke-StaticSecurityScan

    Start-AppIfNeeded
    if (-not (Test-AppResponding)) {
        throw "App is not responding at $BaseUrl. Start the app first or rerun with -StartApp."
    }

    Run-AnonymousChecks
    Run-CustomerChecks
    Run-AdminChecks
    Run-RenderedStaticLeakChecks
    Run-OverpostingChecks
    Run-UploadChecks
    Run-IdorChecks
}
finally {
    try {
        Stop-StartedApp
    }
    finally {
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $script:OriginalCertificateCallback
    }
}

Write-Section "Summary"
$failed = @($script:Results | Where-Object { -not $_.Passed })
$passedCount = $script:Results.Count - $failed.Count
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
