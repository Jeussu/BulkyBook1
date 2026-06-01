param(
    [string]$BaseUrl = "https://localhost:7206",
    [switch]$SkipBuild,
    [switch]$StartApp,
    [switch]$StopApp,
    [string]$CustomerEmail,
    [string]$CustomerPassword,
    [int]$ProductId = 1,
    [switch]$PlaceOrder,
    [switch]$CleanupTestData = $true,
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
$script:CookieContainer = [System.Net.CookieContainer]::new()
$script:OriginalCertificateCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
$script:CreatedOrderId = $null
$script:CartId = $null
$script:ProductTitle = $null
$script:InitialQuantity = 0
$script:CurrentQuantity = 0

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

function Invoke-Get {
    param([string]$PathOrUrl)

    $url = Resolve-Url $PathOrUrl
    $response = $null
    try {
        $request = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($url)
        $request.Method = "GET"
        $request.AllowAutoRedirect = $false
        $request.CookieContainer = $script:CookieContainer
        $request.Timeout = 30000
        $request.ReadWriteTimeout = 30000
        $request.UserAgent = "BulkyBookCustomerFlow/1.0"
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

function Invoke-PostForm {
    param(
        [string]$PathOrUrl,
        [hashtable]$Fields
    )

    $url = Resolve-Url $PathOrUrl
    $pairs = New-Object System.Collections.Generic.List[string]
    foreach ($key in $Fields.Keys) {
        $value = if ($null -eq $Fields[$key]) { "" } else { [string]$Fields[$key] }
        $pairs.Add(("{0}={1}" -f [System.Net.WebUtility]::UrlEncode([string]$key), [System.Net.WebUtility]::UrlEncode($value)))
    }

    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes(($pairs -join "&"))
    $response = $null
    try {
        $request = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($url)
        $request.Method = "POST"
        $request.AllowAutoRedirect = $false
        $request.CookieContainer = $script:CookieContainer
        $request.Timeout = 30000
        $request.ReadWriteTimeout = 30000
        $request.UserAgent = "BulkyBookCustomerFlow/1.0"
        $request.ContentType = "application/x-www-form-urlencoded"
        $request.ContentLength = $bodyBytes.Length
        $requestStream = $request.GetRequestStream()
        $requestStream.Write($bodyBytes, 0, $bodyBytes.Length)
        $requestStream.Dispose()
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

function Follow-Redirect {
    param([object]$Response)

    if ($Response.StatusCode -in @(301, 302, 303, 307, 308) -and $Response.Location) {
        return Invoke-Get -PathOrUrl $Response.Location
    }

    return $Response
}

function Get-InputValue {
    param(
        [string]$Html,
        [string]$Name
    )

    $escaped = [regex]::Escape($Name)
    $pattern = "<input\b(?=[^>]*\bname\s*=\s*[""']$escaped[""'])(?=[^>]*\bvalue\s*=\s*[""']([^""']*)[""'])[^>]*>"
    $match = [regex]::Match($Html, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        return $null
    }

    return [System.Net.WebUtility]::HtmlDecode($match.Groups[1].Value)
}

function Get-AntiForgeryToken {
    param([string]$Html)
    return Get-InputValue -Html $Html -Name "__RequestVerificationToken"
}

function Strip-Html {
    param([string]$Html)
    $withoutTags = [regex]::Replace($Html, "<[^>]+>", " ")
    $normalized = [regex]::Replace($withoutTags, "\s+", " ").Trim()
    return [System.Net.WebUtility]::HtmlDecode($normalized)
}

function Get-ProductTitleFromDetail {
    param([string]$Html)

    $match = [regex]::Match($Html, "<h1[^>]*class=[""'][^""']*book-detail-title[^""']*[""'][^>]*>(.*?)</h1>", "IgnoreCase,Singleline")
    if ($match.Success) {
        return (Strip-Html $match.Groups[1].Value)
    }

    return $null
}

function Get-CartItemForTitle {
    param(
        [string]$Html,
        [string]$Title
    )

    $articles = [regex]::Matches($Html, "<article\b[^>]*class=[""'][^""']*cart-item[^""']*[""'][^>]*>.*?</article>", "IgnoreCase,Singleline")
    foreach ($article in $articles) {
        $block = $article.Value
        $text = Strip-Html $block
        if ($text -notlike "*$Title*") {
            continue
        }

        $count = 0
        $countMatch = [regex]::Match($block, "<span\b[^>]*class=[""'][^""']*qty-count[^""']*[""'][^>]*>\s*(\d+)\s*</span>", "IgnoreCase")
        if ($countMatch.Success) {
            $count = [int]$countMatch.Groups[1].Value
        }

        $plusAction = $null
        $minusAction = $null
        $removeAction = $null
        $plusMatch = [regex]::Match($block, "<form\b(?=[^>]*\baction=[""']([^""']*Plus[^""']*)[""'])[^>]*>", "IgnoreCase")
        $minusMatch = [regex]::Match($block, "<form\b(?=[^>]*\baction=[""']([^""']*Minus[^""']*)[""'])[^>]*>", "IgnoreCase")
        $removeMatch = [regex]::Match($block, "<form\b(?=[^>]*\baction=[""']([^""']*Remove[^""']*)[""'])[^>]*>", "IgnoreCase")
        if ($plusMatch.Success) { $plusAction = [System.Net.WebUtility]::HtmlDecode($plusMatch.Groups[1].Value) }
        if ($minusMatch.Success) { $minusAction = [System.Net.WebUtility]::HtmlDecode($minusMatch.Groups[1].Value) }
        if ($removeMatch.Success) { $removeAction = [System.Net.WebUtility]::HtmlDecode($removeMatch.Groups[1].Value) }

        $cartId = $null
        foreach ($action in @($plusAction, $minusAction, $removeAction)) {
            if ($action -and $action -match "cartId=(\d+)") {
                $cartId = [int]$Matches[1]
                break
            }
        }

        return [pscustomobject]@{
            Title = $Title
            Count = $count
            CartId = $cartId
            PlusAction = $plusAction
            MinusAction = $minusAction
            RemoveAction = $removeAction
            Block = $block
        }
    }

    return $null
}

function Get-FieldValueOrDefault {
    param(
        [string]$Html,
        [string]$Name,
        [string]$DefaultValue
    )

    $value = Get-InputValue -Html $Html -Name $Name
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }

    return $value
}

function Test-AppResponding {
    $response = Invoke-Get -PathOrUrl "/Customer/Home/Index"
    return ($response.StatusCode -gt 0)
}

function Wait-ForApp {
    param([int]$TimeoutSeconds = 75)

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

        $stdout = Join-Path $env:TEMP ("bulkybook-customer-flow-" + [Guid]::NewGuid().ToString("N") + ".out.log")
        $stderr = Join-Path $env:TEMP ("bulkybook-customer-flow-" + [Guid]::NewGuid().ToString("N") + ".err.log")
        $arguments = @("run", "--project", "BulkyBookWeb\BulkyBookWeb.csproj", "--no-build", "--urls", $BaseUrl)
        $script:StartedProcess = Start-Process -FilePath "dotnet" -ArgumentList $arguments -WorkingDirectory $ProjectRoot -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr -PassThru

        if (-not (Wait-ForApp)) {
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
        Write-Host "Stopped customer flow app process $($script:StartedProcess.Id)."
    }
    catch {
        Write-Warning "Unable to stop app process cleanly: $($_.Exception.Message)"
    }
}

function Resolve-CustomerCredentials {
    if (-not [string]::IsNullOrWhiteSpace($CustomerEmail) -and -not [string]::IsNullOrWhiteSpace($CustomerPassword)) {
        return
    }

    $seedPath = Join-Path $ProjectRoot "BulkyBook.DataAccess\DbInitializer\DemoDataSeeder.cs"
    if (-not (Test-Path $seedPath)) {
        throw "Customer credentials were not provided. Pass -CustomerEmail and -CustomerPassword for a local-safe customer account."
    }

    $seedText = Get-Content -LiteralPath $seedPath -Raw
    $matches = [regex]::Matches($seedText, 'new DemoUser\("([^"]+)",\s*"([^"]+)",\s*SD\.Role_User_Indi', "IgnoreCase")
    if ($matches.Count -eq 0) {
        throw "No local customer seed credentials were found. Pass -CustomerEmail and -CustomerPassword."
    }

    $selected = $matches | Where-Object { $_.Groups[1].Value -like "customer2@*" } | Select-Object -First 1
    if ($null -eq $selected) {
        $selected = $matches[0]
    }

    if ([string]::IsNullOrWhiteSpace($CustomerEmail)) {
        $script:CustomerEmail = $selected.Groups[1].Value
    }
    if ([string]::IsNullOrWhiteSpace($CustomerPassword)) {
        $script:CustomerPassword = $selected.Groups[2].Value
    }

    Write-Host "Using local seed customer account $script:CustomerEmail. Override with -CustomerEmail/-CustomerPassword if needed."
}

function Post-CartAction {
    param(
        [string]$Name,
        [string]$Action,
        [string]$CartHtml
    )

    $token = Get-AntiForgeryToken $CartHtml
    if ([string]::IsNullOrWhiteSpace($token)) {
        Add-Result -Name "$Name anti-forgery token" -Passed $false -Details "No token found on cart page."
        return $null
    }

    $response = Invoke-PostForm -PathOrUrl $Action -Fields @{ "__RequestVerificationToken" = $token }
    $ok = $response.StatusCode -in @(200, 302)
    Add-Result -Name $Name -Passed $ok -Details "HTTP $($response.StatusCode)"
    return (Follow-Redirect $response)
}

function Cleanup-CartItem {
    if (-not $CleanupTestData -or [string]::IsNullOrWhiteSpace($script:ProductTitle)) {
        return
    }

    Write-Section "Cleanup"
    $cartResponse = Invoke-Get "/Customer/Cart/Index"
    $item = Get-CartItemForTitle -Html $cartResponse.Body -Title $script:ProductTitle
    if ($null -eq $item) {
        Add-Result -Name "Cleanup cart item" -Passed $true -Details "No test cart item remains."
        return
    }

    if ($script:InitialQuantity -eq 0) {
        if ([string]::IsNullOrWhiteSpace($item.RemoveAction)) {
            Add-Result -Name "Cleanup remove test-created item" -Passed $false -Details "Remove action not found for cart item $($item.CartId)."
            return
        }

        $afterRemove = Post-CartAction -Name "Cleanup remove test-created cart item" -Action $item.RemoveAction -CartHtml $cartResponse.Body
        if ($afterRemove -ne $null) {
            $remaining = Get-CartItemForTitle -Html $afterRemove.Body -Title $script:ProductTitle
            Add-Result -Name "Cleanup item removed" -Passed ($null -eq $remaining) -Details "Product '$script:ProductTitle'"
        }
        return
    }

    $guard = 0
    while ($item -ne $null -and $item.Count -gt $script:InitialQuantity -and $guard -lt 10) {
        if ([string]::IsNullOrWhiteSpace($item.MinusAction)) {
            Add-Result -Name "Cleanup restore initial quantity" -Passed $false -Details "Minus action not found for cart item $($item.CartId)."
            return
        }

        $cartResponse = Post-CartAction -Name "Cleanup decrement added quantity" -Action $item.MinusAction -CartHtml $cartResponse.Body
        $item = Get-CartItemForTitle -Html $cartResponse.Body -Title $script:ProductTitle
        $guard++
    }

    $restored = ($item -ne $null -and $item.Count -eq $script:InitialQuantity)
    Add-Result -Name "Cleanup restored existing cart quantity" -Passed $restored -Details "Initial=$script:InitialQuantity Current=$(if ($item) { $item.Count } else { 'missing' })"
}

try {
    Initialize-WebRequestClient
    Resolve-CustomerCredentials

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

    Write-Section "A. Anonymous baseline"
    $detailPath = "/Customer/Home/Details?productId=$ProductId"
    $detailAnonymous = Invoke-Get $detailPath
    Add-Result -Name "Anonymous detail page" -Passed ($detailAnonymous.StatusCode -eq 200) -Details "HTTP $($detailAnonymous.StatusCode) $detailPath"
    $script:ProductTitle = Get-ProductTitleFromDetail $detailAnonymous.Body
    Add-Result -Name "Product title detected" -Passed (-not [string]::IsNullOrWhiteSpace($script:ProductTitle)) -Details "$script:ProductTitle"
    Add-Result -Name "Add to Cart form exists" -Passed ($detailAnonymous.Body -match "ShoppingCart\.ProductId" -and $detailAnonymous.Body -match "ShoppingCart\.Count") -Details $detailPath
    Add-Result -Name "Detail anti-forgery token exists" -Passed (-not [string]::IsNullOrWhiteSpace((Get-AntiForgeryToken $detailAnonymous.Body))) -Details $detailPath
    Add-Result -Name "Detail uses modern PNG cover" -Passed ($detailAnonymous.Body -match "/images/products/book-covers-modern/[^`"']+\.png") -Details $detailPath
    Add-Result -Name "Detail has no old SVG cover path" -Passed (-not ($detailAnonymous.Body -match "/images/products/book-covers/[^`"']+\.svg")) -Details $detailPath

    Write-Section "B. Customer login"
    $loginPage = Invoke-Get "/Identity/Account/Login"
    Add-Result -Name "Login page loads" -Passed ($loginPage.StatusCode -eq 200) -Details "HTTP $($loginPage.StatusCode)"
    $loginToken = Get-AntiForgeryToken $loginPage.Body
    Add-Result -Name "Login anti-forgery token exists" -Passed (-not [string]::IsNullOrWhiteSpace($loginToken)) -Details "/Identity/Account/Login"
    if ([string]::IsNullOrWhiteSpace($loginToken)) {
        throw "Cannot continue without login anti-forgery token."
    }

    $loginResponse = Invoke-PostForm -PathOrUrl "/Identity/Account/Login" -Fields @{
        "__RequestVerificationToken" = $loginToken
        "Input.Email" = $script:CustomerEmail
        "Input.Password" = $script:CustomerPassword
        "Input.RememberMe" = "false"
    }
    Add-Result -Name "Login POST accepted" -Passed ($loginResponse.StatusCode -in @(200, 302)) -Details "HTTP $($loginResponse.StatusCode)"
    $homeAfterLogin = Follow-Redirect $loginResponse
    if ($homeAfterLogin.StatusCode -ne 200) {
        $homeAfterLogin = Invoke-Get "/Customer/Home/Index"
    }
    $loggedIn = $homeAfterLogin.Body -match "/Identity/Account/Logout" -or $homeAfterLogin.Body -match "logoutForm"
    Add-Result -Name "Customer login succeeded" -Passed $loggedIn -Details "Account $script:CustomerEmail"
    Add-Result -Name "Navbar avoids local/demo copy" -Passed (-not ($homeAfterLogin.Body -match "(?i)local demos|local testing|demo accounts|bulky\.local")) -Details "/Customer/Home/Index"
    Add-Result -Name "Cart count area exists" -Passed ($homeAfterLogin.Body -match "bb-cart-count|bb-cart-indicator") -Details "ShoppingCart view component"
    if (-not $loggedIn) {
        throw "Login failed for $script:CustomerEmail. Pass valid -CustomerEmail and -CustomerPassword for a local-safe customer account."
    }

    Write-Section "C. Add to cart"
    $detailAuthenticated = Invoke-Get $detailPath
    $detailToken = Get-AntiForgeryToken $detailAuthenticated.Body
    Add-Result -Name "Authenticated detail page" -Passed ($detailAuthenticated.StatusCode -eq 200) -Details "HTTP $($detailAuthenticated.StatusCode)"
    Add-Result -Name "Authenticated detail anti-forgery token" -Passed (-not [string]::IsNullOrWhiteSpace($detailToken)) -Details $detailPath

    $cartBefore = Invoke-Get "/Customer/Cart/Index"
    $existingItem = Get-CartItemForTitle -Html $cartBefore.Body -Title $script:ProductTitle
    if ($existingItem -ne $null) {
        $script:InitialQuantity = $existingItem.Count
    }
    Add-Result -Name "Initial cart state captured" -Passed $true -Details "Product='$script:ProductTitle' InitialQuantity=$script:InitialQuantity"

    $addResponse = Invoke-PostForm -PathOrUrl $detailPath -Fields @{
        "__RequestVerificationToken" = $detailToken
        "ShoppingCart.ProductId" = "$ProductId"
        "ShoppingCart.Count" = "1"
    }
    Add-Result -Name "Add to Cart POST" -Passed ($addResponse.StatusCode -in @(200, 302)) -Details "HTTP $($addResponse.StatusCode)"
    $cartAfterAdd = Invoke-Get "/Customer/Cart/Index"
    Add-Result -Name "Cart index loads after add" -Passed ($cartAfterAdd.StatusCode -eq 200) -Details "HTTP $($cartAfterAdd.StatusCode)"
    $itemAfterAdd = Get-CartItemForTitle -Html $cartAfterAdd.Body -Title $script:ProductTitle
    $script:CartId = if ($itemAfterAdd) { $itemAfterAdd.CartId } else { $null }
    $script:CurrentQuantity = if ($itemAfterAdd) { $itemAfterAdd.Count } else { 0 }
    Add-Result -Name "Cart item exists after add" -Passed ($itemAfterAdd -ne $null) -Details "CartId=$script:CartId Quantity=$script:CurrentQuantity"
    Add-Result -Name "Cart quantity increased by add" -Passed ($script:CurrentQuantity -ge ($script:InitialQuantity + 1)) -Details "Initial=$script:InitialQuantity Current=$script:CurrentQuantity"
    Add-Result -Name "Cart controls exist" -Passed ($itemAfterAdd -ne $null -and $itemAfterAdd.PlusAction -and $itemAfterAdd.MinusAction -and $itemAfterAdd.RemoveAction) -Details "Plus/Minus/Remove form actions"
    Add-Result -Name "Cart totals display" -Passed ($cartAfterAdd.Body -match "Line total" -and $cartAfterAdd.Body -match "Order total") -Details "/Customer/Cart/Index"

    Write-Section "D. Cart operations"
    if ($itemAfterAdd -eq $null -or [string]::IsNullOrWhiteSpace($itemAfterAdd.PlusAction) -or [string]::IsNullOrWhiteSpace($itemAfterAdd.MinusAction)) {
        Add-Result -Name "Plus/Minus execution" -Passed $false -Details "Cannot identify cart item actions."
    }
    else {
        $cartAfterPlus = Post-CartAction -Name "Plus cart item" -Action $itemAfterAdd.PlusAction -CartHtml $cartAfterAdd.Body
        $itemAfterPlus = if ($cartAfterPlus) { Get-CartItemForTitle -Html $cartAfterPlus.Body -Title $script:ProductTitle } else { $null }
        Add-Result -Name "Plus increments quantity" -Passed ($itemAfterPlus -ne $null -and $itemAfterPlus.Count -eq ($script:CurrentQuantity + 1)) -Details "Before=$script:CurrentQuantity After=$(if ($itemAfterPlus) { $itemAfterPlus.Count } else { 'missing' })"

        if ($itemAfterPlus -ne $null) {
            $cartAfterMinus = Post-CartAction -Name "Minus cart item" -Action $itemAfterPlus.MinusAction -CartHtml $cartAfterPlus.Body
            $itemAfterMinus = if ($cartAfterMinus) { Get-CartItemForTitle -Html $cartAfterMinus.Body -Title $script:ProductTitle } else { $null }
            Add-Result -Name "Minus restores quantity" -Passed ($itemAfterMinus -ne $null -and $itemAfterMinus.Count -eq $script:CurrentQuantity) -Details "Expected=$script:CurrentQuantity Actual=$(if ($itemAfterMinus) { $itemAfterMinus.Count } else { 'missing' })"
            if ($itemAfterMinus -ne $null) {
                $script:CurrentQuantity = $itemAfterMinus.Count
                $cartAfterAdd = $cartAfterMinus
            }
        }
    }

    if ($script:InitialQuantity -eq 0) {
        Add-Result -Name "Remove will be tested during cleanup" -Passed $true -Details "Cart item was created by this script."
    }
    else {
        Add-Skip -Name "Remove execution" -Details "Product already existed in cart; destructive remove skipped to preserve user data."
    }

    Write-Section "E. Summary validation"
    $summary = Invoke-Get "/Customer/Cart/Summary"
    Add-Result -Name "Summary page loads" -Passed ($summary.StatusCode -eq 200) -Details "HTTP $($summary.StatusCode)"
    foreach ($field in @(
        "OrderHeader.Name",
        "OrderHeader.PhoneNumber",
        "OrderHeader.StreetAddress",
        "OrderHeader.City",
        "OrderHeader.State",
        "OrderHeader.PostalCode"
    )) {
        Add-Result -Name "Summary field $field exists" -Passed ($summary.Body -match [regex]::Escape("name=`"$field`"") -or $summary.Body -match [regex]::Escape("name='$field'")) -Details "/Customer/Cart/Summary"
    }
    Add-Result -Name "Summary anti-forgery token exists" -Passed (-not [string]::IsNullOrWhiteSpace((Get-AntiForgeryToken $summary.Body))) -Details "/Customer/Cart/Summary"
    Add-Result -Name "Summary total displays" -Passed ($summary.Body -match "Total \(USD\)" -or $summary.Body -match "Order summary") -Details "/Customer/Cart/Summary"

    $summaryToken = Get-AntiForgeryToken $summary.Body
    if (-not [string]::IsNullOrWhiteSpace($summaryToken)) {
        $invalidSummary = Invoke-PostForm -PathOrUrl "/Customer/Cart/Summary" -Fields @{
            "__RequestVerificationToken" = $summaryToken
            "OrderHeader.Name" = ""
            "OrderHeader.PhoneNumber" = ""
            "OrderHeader.PostalCode" = ""
            "OrderHeader.StreetAddress" = ""
            "OrderHeader.City" = ""
            "OrderHeader.State" = ""
        }
        $validationStayedOnPage = ($invalidSummary.StatusCode -eq 200 -and ($invalidSummary.Body -match "field-validation-error|required|The .* field is required"))
        Add-Result -Name "Invalid summary stays on checkout" -Passed $validationStayedOnPage -Details "HTTP $($invalidSummary.StatusCode)"
        $cartAfterInvalid = Invoke-Get "/Customer/Cart/Index"
        $itemAfterInvalid = Get-CartItemForTitle -Html $cartAfterInvalid.Body -Title $script:ProductTitle
        Add-Result -Name "Invalid summary does not clear cart" -Passed ($itemAfterInvalid -ne $null) -Details "Cart item still present"
    }

    Write-Section "F. Optional place order"
    if ($PlaceOrder) {
        $summaryForOrder = Invoke-Get "/Customer/Cart/Summary"
        $orderToken = Get-AntiForgeryToken $summaryForOrder.Body
        $validFields = @{
            "__RequestVerificationToken" = $orderToken
            "OrderHeader.Name" = (Get-FieldValueOrDefault -Html $summaryForOrder.Body -Name "OrderHeader.Name" -DefaultValue "QA Customer")
            "OrderHeader.PhoneNumber" = (Get-FieldValueOrDefault -Html $summaryForOrder.Body -Name "OrderHeader.PhoneNumber" -DefaultValue "0900000000")
            "OrderHeader.PostalCode" = (Get-FieldValueOrDefault -Html $summaryForOrder.Body -Name "OrderHeader.PostalCode" -DefaultValue "700000")
            "OrderHeader.StreetAddress" = (Get-FieldValueOrDefault -Html $summaryForOrder.Body -Name "OrderHeader.StreetAddress" -DefaultValue "1 QA Street")
            "OrderHeader.City" = (Get-FieldValueOrDefault -Html $summaryForOrder.Body -Name "OrderHeader.City" -DefaultValue "Ho Chi Minh City")
            "OrderHeader.State" = (Get-FieldValueOrDefault -Html $summaryForOrder.Body -Name "OrderHeader.State" -DefaultValue "HCMC")
        }

        $orderResponse = Invoke-PostForm -PathOrUrl "/Customer/Cart/Summary" -Fields $validFields
        $orderAccepted = $orderResponse.StatusCode -in @(200, 302, 303)
        Add-Result -Name "Place order POST" -Passed $orderAccepted -Details "HTTP $($orderResponse.StatusCode) Location=$($orderResponse.Location)"
        if ($orderResponse.Location -match "OrderConfirmation\?id=(\d+)") {
            $script:CreatedOrderId = [int]$Matches[1]
            $confirmation = Follow-Redirect $orderResponse
            Add-Result -Name "Order confirmation loads" -Passed ($confirmation.StatusCode -eq 200 -and $confirmation.Body -match [regex]::Escape([string]$script:CreatedOrderId)) -Details "OrderId=$script:CreatedOrderId"
        }
        elseif ($orderResponse.Location -match "stripe|checkout" -or $orderResponse.StatusCode -eq 303) {
            Add-Result -Name "Payment redirect/fallback path" -Passed $true -Details "Location=$($orderResponse.Location)"
        }
        else {
            $followedOrder = Follow-Redirect $orderResponse
            $professionalFallback = $followedOrder.Body -match "Payment processing is temporarily unavailable|Online card payment is currently unavailable|Order placed successfully"
            Add-Result -Name "Professional payment/order feedback" -Passed $professionalFallback -Details "HTTP $($followedOrder.StatusCode)"
        }
    }
    else {
        Add-Skip -Name "Place order" -Details "Use -PlaceOrder only with local-safe data/config."
    }
}
finally {
    try {
        if ($script:CookieContainer -ne $null -and -not $PlaceOrder) {
            Cleanup-CartItem
        }
    }
    finally {
        Stop-StartedApp
        [System.Net.ServicePointManager]::ServerCertificateValidationCallback = $script:OriginalCertificateCallback
    }
}

Write-Section "Summary"
$failed = @($script:Results | Where-Object { -not $_.Passed })
$passedCount = $script:Results.Count - $failed.Count
Write-Host "Passed: $passedCount"
Write-Host "Failed: $($failed.Count)"
if ($script:CartId) {
    Write-Host "CartId touched: $script:CartId"
}
if ($script:CreatedOrderId) {
    Write-Host "Created order id: $script:CreatedOrderId"
}

if ($failed.Count -gt 0) {
    Write-Host ""
    Write-Host "Failures:" -ForegroundColor Red
    foreach ($failure in $failed) {
        Write-Host "- $($failure.Name): $($failure.Details)" -ForegroundColor Red
    }
    exit 1
}

exit 0
