param(
    [string]$BaseUrl = "https://localhost:7206",
    [switch]$SkipBuild,
    [switch]$StartApp,
    [switch]$StopApp,
    [string]$AdminEmail,
    [string]$AdminPassword,
    [int]$ProductId = 1,
    [int]$OrderId = 1,
    [switch]$RunCrud,
    [switch]$RunDestructive,
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
$script:Results = [System.Collections.Generic.List[object]]::new()
$script:StartedProcess = $null
$script:CookieContainer = [System.Net.CookieContainer]::new()
$script:OriginalCertificateCallback = [System.Net.ServicePointManager]::ServerCertificateValidationCallback
$script:ResolvedProductId = $ProductId
$script:ResolvedOrderId = $OrderId
$script:CreatedRecords = [System.Collections.Generic.List[string]]::new()
$script:CreatedProductId = $null
$script:CreatedProductTitle = $null

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
        $request.UserAgent = "BulkyBookAdminFlow/1.0"

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

function Invoke-Delete {
    param(
        [string]$PathOrUrl,
        [hashtable]$Headers = @{}
    )

    return Invoke-Request -Method "DELETE" -PathOrUrl $PathOrUrl -Headers $Headers
}

function Invoke-MultipartPost {
    param(
        [string]$PathOrUrl,
        [hashtable]$Fields,
        [string]$FileFieldName,
        [string]$FilePath,
        [string]$FileContentType = "image/png"
    )

    $boundary = "----BulkyBookAdminFlow$([Guid]::NewGuid().ToString("N"))"
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

function Test-HtmlHasId {
    param(
        [string]$Html,
        [string]$Id
    )

    $pattern = '\bid\s*=\s*["'']' + [regex]::Escape($Id) + '["'']'
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

function Test-Property {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $false
    }

    return @($Object.PSObject.Properties.Name) -contains $Name
}

function Get-FirstDataItem {
    param([object]$Json)

    if ($null -eq $Json -or -not (Test-Property -Object $Json -Name "data")) {
        return $null
    }

    $items = @($Json.data)
    if ($items.Count -eq 0) {
        return $null
    }

    return $items[0]
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

function Get-FormAction {
    param(
        [string]$Html,
        [string]$FallbackPath
    )

    $match = [regex]::Match($Html, '<form\b(?=[^>]*\bmethod\s*=\s*["'']post["''])[^>]*>', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) {
        return $FallbackPath
    }

    $action = Get-AttributeValue -Tag $match.Value -Attribute "action"
    if ([string]::IsNullOrWhiteSpace($action)) {
        return $FallbackPath
    }

    return $action
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

function Resolve-AdminCredentials {
    if (-not [string]::IsNullOrWhiteSpace($AdminEmail) -and -not [string]::IsNullOrWhiteSpace($AdminPassword)) {
        $script:AdminEmail = $AdminEmail
        $script:AdminPassword = $AdminPassword
        return
    }

    $devSettingsPath = Join-Path $ProjectRoot "BulkyBookWeb\appsettings.Development.json"
    if (Test-Path $devSettingsPath) {
        try {
            $settings = Get-Content -Path $devSettingsPath -Raw | ConvertFrom-Json -ErrorAction Stop
            if ($settings.SeedAdmin -and $settings.SeedAdmin.Email -and $settings.SeedAdmin.Password) {
                $script:AdminEmail = [string]$settings.SeedAdmin.Email
                $script:AdminPassword = [string]$settings.SeedAdmin.Password
                Add-Result -Name "Admin credentials resolved" -Passed $true -Details "Using local development SeedAdmin account $script:AdminEmail"
                return
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
        $match = [regex]::Match($seedText, 'new\s+DemoUser\("([^"]+)",\s*"([^"]+)",\s*SD\.Role_Admin', [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($match.Success) {
            $script:AdminEmail = $match.Groups[1].Value
            $script:AdminPassword = $match.Groups[2].Value
            Add-Result -Name "Admin credentials resolved" -Passed $true -Details "Using local demo seed admin account $script:AdminEmail"
            return
        }
    }

    throw "Admin credentials were not provided and no local development seed admin account was found. Rerun with -AdminEmail and -AdminPassword."
}

function Assert-ProtectedResponse {
    param(
        [string]$Name,
        [object]$Response
    )

    $protected = $Response.StatusCode -eq 401 -or $Response.StatusCode -eq 403 -or ($Response.StatusCode -in @(301, 302, 303) -and $Response.Location -match "/Identity/Account/Login")
    Add-Result -Name $Name -Passed $protected -Details "HTTP $($Response.StatusCode) Location=$($Response.Location)"
}

function Test-FieldSet {
    param(
        [string]$Html,
        [string[]]$Names,
        [string]$Scope
    )

    foreach ($name in $Names) {
        Add-Result -Name "$Scope field $name exists" -Passed (Test-HtmlHasName -Html $Html -Name $name) -Details $Scope
    }
}

function New-TempPngFile {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("bulkybook-qa-admin-{0}.png" -f [Guid]::NewGuid().ToString("N"))
    $bytes = [Convert]::FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/p9sAAAAASUVORK5CYII=")
    [System.IO.File]::WriteAllBytes($path, $bytes)
    return $path
}

function Run-ProductCrudIfRequested {
    if (-not $RunCrud) {
        Add-Skip -Name "Product CRUD" -Details "Use -RunCrud only with disposable local data."
        return
    }

    Write-Section "D. Product CRUD optional"
    $createPage = Invoke-Get "/Admin/Product/Upsert"
    $token = Get-AntiForgeryToken $createPage.Body
    $categoryId = Get-FirstSelectOptionValue -Html $createPage.Body -SelectName "Product.CategoryId"
    $coverTypeId = Get-FirstSelectOptionValue -Html $createPage.Body -SelectName "Product.CoverTypeId"
    $imagePath = New-TempPngFile

    try {
        if ([string]::IsNullOrWhiteSpace($token) -or [string]::IsNullOrWhiteSpace($categoryId) -or [string]::IsNullOrWhiteSpace($coverTypeId)) {
            Add-Result -Name "Product CRUD prerequisites" -Passed $false -Details "Missing token/category/cover option."
            return
        }

        $title = "QA-D-Product-$((Get-Date).ToString("yyyyMMddHHmmss"))"
        $fields = @{
            "__RequestVerificationToken" = $token
            "Product.Id" = "0"
            "Product.ImageUrl" = ""
            "Product.Title" = $title
            "Product.Author" = "QA Automation"
            "Product.ISBN" = "QA-$((Get-Date).ToString("HHmmss"))"
            "Product.Description" = "QA admin flow product. Safe to remove."
            "Product.ListPrice" = "49.99"
            "Product.Price" = "39.99"
            "Product.Price50" = "34.99"
            "Product.Price100" = "29.99"
            "Product.StockQuantity" = "7"
            "Product.IsActive" = "true"
            "Product.CategoryId" = $categoryId
            "Product.CoverTypeId" = $coverTypeId
        }

        $createResponse = Invoke-MultipartPost -PathOrUrl "/Admin/Product/Upsert" -Fields $fields -FileFieldName "file" -FilePath $imagePath
        Add-Result -Name "Create QA product POST" -Passed ($createResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($createResponse.StatusCode)"

        $productJson = Get-JsonBody (Invoke-Get "/Admin/Product/GetAll")
        $created = @($productJson.data | Where-Object { $_.title -eq $title }) | Select-Object -First 1
        Add-Result -Name "Created QA product found" -Passed ($null -ne $created) -Details $title
        if ($null -eq $created) {
            return
        }

        $script:CreatedProductId = [int]$created.id
        $script:CreatedProductTitle = $title
        $script:CreatedRecords.Add(("Product:{0}:{1}" -f $script:CreatedProductId, $title)) | Out-Null

        $editPage = Invoke-Get "/Admin/Product/Upsert?id=$script:CreatedProductId"
        $editToken = Get-AntiForgeryToken $editPage.Body
        $imageUrl = Get-InputValue -Html $editPage.Body -Name "Product.ImageUrl"
        $editFields = $fields.Clone()
        $editFields["__RequestVerificationToken"] = $editToken
        $editFields["Product.Id"] = [string]$script:CreatedProductId
        $editFields["Product.ImageUrl"] = $imageUrl
        $editFields["Product.Title"] = "$title Updated"
        $editResponse = Invoke-PostForm -PathOrUrl "/Admin/Product/Upsert" -Fields $editFields
        Add-Result -Name "Edit QA product POST" -Passed ($editResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($editResponse.StatusCode)"

        $afterEditJson = Get-JsonBody (Invoke-Get "/Admin/Product/GetAll")
        $edited = @($afterEditJson.data | Where-Object { $_.id -eq $script:CreatedProductId -and $_.title -eq "$title Updated" }) | Select-Object -First 1
        Add-Result -Name "Edit QA product persisted" -Passed ($null -ne $edited) -Details "ProductId=$script:CreatedProductId"

        if ($RunDestructive -and $CleanupTestData) {
            $indexPage = Invoke-Get "/Admin/Product/Index"
            $metaToken = Get-MetaRequestVerificationToken $indexPage.Body
            $deleteHeaders = @{}
            if (-not [string]::IsNullOrWhiteSpace($metaToken)) {
                $deleteHeaders["RequestVerificationToken"] = $metaToken
            }

            $deleteResponse = Invoke-Delete -PathOrUrl "/Admin/Product/Delete/$script:CreatedProductId" -Headers $deleteHeaders
            $deleteJson = Get-JsonBody $deleteResponse
            $deleted = $deleteResponse.StatusCode -eq 200 -and $deleteJson -ne $null -and $deleteJson.success -eq $true
            Add-Result -Name "Delete QA product" -Passed $deleted -Details "HTTP $($deleteResponse.StatusCode) ProductId=$script:CreatedProductId"
        }
        else {
            Add-Skip -Name "Delete QA product" -Details "Use -RunDestructive -CleanupTestData to delete the test-created product. Manual cleanup may be needed for ProductId=$script:CreatedProductId."
        }
    }
    finally {
        if (Test-Path $imagePath) {
            Remove-Item -LiteralPath $imagePath -Force
        }
    }
}

function Run-SimpleCrudIfRequested {
    param(
        [string]$AreaName,
        [string]$CreatePath,
        [string]$EditPathTemplate,
        [string]$DeletePathTemplate,
        [hashtable]$CreateFields,
        [hashtable]$EditFields,
        [string]$UniqueName
    )

    if (-not $RunCrud) {
        Add-Skip -Name "$AreaName CRUD" -Details "Use -RunCrud only with disposable local data."
        return
    }

    $createPage = Invoke-Get $CreatePath
    $token = Get-AntiForgeryToken $createPage.Body
    $createFieldsWithToken = $CreateFields.Clone()
    $createFieldsWithToken["__RequestVerificationToken"] = $token
    $createResponse = Invoke-PostForm -PathOrUrl $CreatePath -Fields $createFieldsWithToken
    Add-Result -Name "$AreaName create POST" -Passed ($createResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($createResponse.StatusCode)"

    $index = Invoke-Get "/Admin/$AreaName/Index"
    $escapedName = [regex]::Escape($UniqueName)
    $rowMatch = [regex]::Match($index.Body, "<tr\b[^>]*>(?:(?!</tr>).)*$escapedName(?:(?!</tr>).)*</tr>", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase -bor [System.Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $rowMatch.Success) {
        Add-Result -Name "$AreaName created id discovered" -Passed $false -Details $UniqueName
        return
    }

    $idMatch = [regex]::Match($rowMatch.Value, "/Admin/$AreaName/Edit/(\d+)", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $idMatch.Success) {
        Add-Result -Name "$AreaName created id discovered" -Passed $false -Details "QA row found but edit id not found for $UniqueName"
        return
    }

    $id = $idMatch.Groups[1].Value
    $script:CreatedRecords.Add(("{0}:{1}:{2}" -f $AreaName, $id, $UniqueName)) | Out-Null
    Add-Result -Name "$AreaName created id discovered" -Passed $true -Details "Id=$id"

    $editPath = $EditPathTemplate -replace "\{id\}", $id
    $editPage = Invoke-Get $editPath
    $editToken = Get-AntiForgeryToken $editPage.Body
    $editFieldsWithToken = $EditFields.Clone()
    $editFieldsWithToken["__RequestVerificationToken"] = $editToken
    $editResponse = Invoke-PostForm -PathOrUrl $editPath -Fields $editFieldsWithToken
    Add-Result -Name "$AreaName edit POST" -Passed ($editResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($editResponse.StatusCode)"

    if ($CleanupTestData) {
        $deletePath = $DeletePathTemplate -replace "\{id\}", $id
        $deletePage = Invoke-Get $deletePath
        $deleteToken = Get-AntiForgeryToken $deletePage.Body
        $deleteResponse = Invoke-PostForm -PathOrUrl $deletePath -Fields @{
            "__RequestVerificationToken" = $deleteToken
            "Id" = $id
        }
        Add-Result -Name "$AreaName cleanup delete" -Passed ($deleteResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($deleteResponse.StatusCode) Id=$id"
    }
}

function Run-CompanyCrudIfRequested {
    if (-not $RunCrud) {
        Add-Skip -Name "Company CRUD" -Details "Use -RunCrud only with disposable local data."
        return
    }

    $timestamp = (Get-Date).ToString("yyyyMMddHHmmss")
    $companyName = "QA-D-Company-$timestamp"
    $createPage = Invoke-Get "/Admin/Company/Upsert"
    $token = Get-AntiForgeryToken $createPage.Body
    $createResponse = Invoke-PostForm -PathOrUrl "/Admin/Company/Upsert" -Fields @{
        "__RequestVerificationToken" = $token
        "Id" = "0"
        "Name" = $companyName
        "PhoneNumber" = "0900000000"
        "StreetAddress" = "1 QA Admin Street"
        "City" = "Ho Chi Minh City"
        "State" = "HCMC"
        "PostalCode" = "700000"
    }
    Add-Result -Name "Company create POST" -Passed ($createResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($createResponse.StatusCode)"

    $companyJson = Get-JsonBody (Invoke-Get "/Admin/Company/GetAll")
    $company = @($companyJson.data | Where-Object { $_.name -eq $companyName }) | Select-Object -First 1
    Add-Result -Name "Company created id discovered" -Passed ($null -ne $company) -Details $companyName
    if ($null -eq $company) {
        return
    }

    $id = [int]$company.id
    $script:CreatedRecords.Add(("Company:{0}:{1}" -f $id, $companyName)) | Out-Null

    $editPage = Invoke-Get "/Admin/Company/Upsert?id=$id"
    $editToken = Get-AntiForgeryToken $editPage.Body
    $editResponse = Invoke-PostForm -PathOrUrl "/Admin/Company/Upsert" -Fields @{
        "__RequestVerificationToken" = $editToken
        "Id" = [string]$id
        "Name" = "$companyName Updated"
        "PhoneNumber" = "0911111111"
        "StreetAddress" = "2 QA Admin Street"
        "City" = "Ha Noi"
        "State" = "HN"
        "PostalCode" = "100000"
    }
    Add-Result -Name "Company edit POST" -Passed ($editResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($editResponse.StatusCode)"

    $afterEditJson = Get-JsonBody (Invoke-Get "/Admin/Company/GetAll")
    $edited = @($afterEditJson.data | Where-Object { $_.id -eq $id -and $_.name -eq "$companyName Updated" }) | Select-Object -First 1
    Add-Result -Name "Company edit persisted" -Passed ($null -ne $edited) -Details "CompanyId=$id"

    if ($CleanupTestData) {
        $indexPage = Invoke-Get "/Admin/Company/Index"
        $metaToken = Get-MetaRequestVerificationToken $indexPage.Body
        $headers = @{}
        if (-not [string]::IsNullOrWhiteSpace($metaToken)) {
            $headers["RequestVerificationToken"] = $metaToken
        }

        $deleteResponse = Invoke-Delete -PathOrUrl "/Admin/Company/Delete/$id" -Headers $headers
        $deleteJson = Get-JsonBody $deleteResponse
        $deleted = $deleteResponse.StatusCode -eq 200 -and $deleteJson -ne $null -and $deleteJson.success -eq $true
        Add-Result -Name "Company cleanup delete" -Passed $deleted -Details "HTTP $($deleteResponse.StatusCode) CompanyId=$id"
    }
}

try {
    Initialize-WebRequestClient
    Resolve-AdminCredentials

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

    Write-Section "A. Anonymous/protected baseline"
    Assert-ProtectedResponse -Name "Anonymous Product admin protected" -Response (Invoke-Get "/Admin/Product/Index")
    Assert-ProtectedResponse -Name "Anonymous Order admin protected" -Response (Invoke-Get "/Admin/Order/Index")

    $script:CookieContainer = [System.Net.CookieContainer]::new()

    Write-Section "B. Admin login"
    $loginPage = Invoke-Get "/Identity/Account/Login"
    Add-Result -Name "Login page loads" -Passed ($loginPage.StatusCode -eq 200) -Details "HTTP $($loginPage.StatusCode)"
    $loginToken = Get-AntiForgeryToken $loginPage.Body
    Add-Result -Name "Login anti-forgery token exists" -Passed (-not [string]::IsNullOrWhiteSpace($loginToken)) -Details "/Identity/Account/Login"
    if ([string]::IsNullOrWhiteSpace($loginToken)) {
        throw "Cannot continue without login anti-forgery token."
    }

    $loginResponse = Invoke-PostForm -PathOrUrl "/Identity/Account/Login" -Fields @{
        "__RequestVerificationToken" = $loginToken
        "Input.Email" = $script:AdminEmail
        "Input.Password" = $script:AdminPassword
        "Input.RememberMe" = "false"
    }
    Add-Result -Name "Login POST accepted" -Passed ($loginResponse.StatusCode -in @(200, 302, 303)) -Details "HTTP $($loginResponse.StatusCode)"
    $productAfterLogin = Invoke-Get "/Admin/Product/Index"
    $loggedIn = $productAfterLogin.StatusCode -eq 200 -and ($productAfterLogin.Body -match "Product List|Product Inventory|Create New Product")
    Add-Result -Name "Admin login succeeded" -Passed $loggedIn -Details "Account $script:AdminEmail"
    Add-Result -Name "Admin nav links exist" -Passed ($productAfterLogin.Body -match "Content Management" -and $productAfterLogin.Body -match "Manage Orders") -Details "/Admin/Product/Index"
    Add-Result -Name "Navbar avoids local/demo copy" -Passed (-not ($productAfterLogin.Body -match "(?i)local demos|local testing|demo accounts|bulky\.local")) -Details "/Admin/Product/Index"
    if (-not $loggedIn) {
        throw "Login failed for $script:AdminEmail. Pass valid -AdminEmail and -AdminPassword for a local-safe admin account."
    }

    Write-Section "C. Product admin smoke"
    Add-Result -Name "Product index loads" -Passed ($productAfterLogin.StatusCode -eq 200) -Details "HTTP $($productAfterLogin.StatusCode)"
    Add-Result -Name "Product DataTable id exists" -Passed (Test-HtmlHasId -Html $productAfterLogin.Body -Id "tblData") -Details "/Admin/Product/Index"
    Add-Result -Name "Product JS reference exists" -Passed ($productAfterLogin.Body -match "/js/product\.js") -Details "/Admin/Product/Index"

    $productApi = Invoke-Get "/Admin/Product/GetAll"
    $productJson = Get-JsonBody $productApi
    Add-Result -Name "Product GetAll returns 200" -Passed ($productApi.StatusCode -eq 200) -Details "HTTP $($productApi.StatusCode)"
    Add-Result -Name "Product GetAll contains data" -Passed ($productJson -ne $null -and (Test-Property -Object $productJson -Name "data")) -Details "/Admin/Product/GetAll"
    $firstProduct = Get-FirstDataItem $productJson
    if ($firstProduct -ne $null) {
        Add-Result -Name "Product GetAll has rows" -Passed $true -Details "First ProductId=$($firstProduct.id)"
        $requiredProductProps = @("title", "isbn", "author", "category", "stockQuantity", "isActive", "id")
        foreach ($prop in $requiredProductProps) {
            Add-Result -Name "Product row field $prop" -Passed (Test-Property -Object $firstProduct -Name $prop) -Details "/Admin/Product/GetAll"
        }
        Add-Result -Name "Product row field price/listPrice" -Passed ((Test-Property -Object $firstProduct -Name "price") -or (Test-Property -Object $firstProduct -Name "listPrice")) -Details "/Admin/Product/GetAll"
        $script:ResolvedProductId = [int]$firstProduct.id
    }
    else {
        Add-Skip -Name "Product GetAll has rows" -Details "No products returned by local data."
    }

    $productCreate = Invoke-Get "/Admin/Product/Upsert"
    Add-Result -Name "Product create form loads" -Passed ($productCreate.StatusCode -eq 200) -Details "HTTP $($productCreate.StatusCode)"
    Test-FieldSet -Html $productCreate.Body -Names @(
        "Product.Id",
        "Product.ImageUrl",
        "Product.Title",
        "Product.Description",
        "Product.ISBN",
        "Product.Author",
        "Product.ListPrice",
        "Product.Price",
        "Product.Price50",
        "Product.Price100",
        "Product.StockQuantity",
        "Product.IsActive",
        "Product.CategoryId",
        "Product.CoverTypeId",
        "file"
    ) -Scope "Product create"
    Add-Result -Name "Product uploadBox id exists" -Passed (Test-HtmlHasId -Html $productCreate.Body -Id "uploadBox") -Details "/Admin/Product/Upsert"

    $productEdit = Invoke-Get "/Admin/Product/Upsert?id=$ProductId"
    if ($productEdit.StatusCode -ne 200 -and $script:ResolvedProductId -ne $ProductId) {
        $productEdit = Invoke-Get "/Admin/Product/Upsert?id=$script:ResolvedProductId"
    }
    Add-Result -Name "Product edit form loads" -Passed ($productEdit.StatusCode -eq 200) -Details "HTTP $($productEdit.StatusCode) ProductId=$script:ResolvedProductId"
    Add-Result -Name "Product edit hidden Id exists" -Passed (Test-HtmlHasName -Html $productEdit.Body -Name "Product.Id") -Details "/Admin/Product/Upsert?id=$script:ResolvedProductId"
    Add-Result -Name "Product edit hidden ImageUrl exists" -Passed (Test-HtmlHasName -Html $productEdit.Body -Name "Product.ImageUrl") -Details "/Admin/Product/Upsert?id=$script:ResolvedProductId"
    Add-Result -Name "Product edit values render" -Passed ($productEdit.Body -match "Product.Title|name=""Product.Title""|name='Product.Title'") -Details "/Admin/Product/Upsert?id=$script:ResolvedProductId"
    Add-Result -Name "Product edit uses modern PNG cover" -Passed ($productEdit.Body -match "/images/products/book-covers-modern/[^`"']+\.png") -Details "/Admin/Product/Upsert?id=$script:ResolvedProductId"

    Run-ProductCrudIfRequested

    Write-Section "E. Category/CoverType/Company admin smoke"
    foreach ($area in @("Category", "CoverType", "Company")) {
        $indexResponse = Invoke-Get "/Admin/$area/Index"
        if ($indexResponse.StatusCode -eq 404) {
            Add-Skip -Name "$area index" -Details "Route not present."
            continue
        }

        Add-Result -Name "$area index loads" -Passed ($indexResponse.StatusCode -eq 200) -Details "HTTP $($indexResponse.StatusCode)"
        Add-Result -Name "$area list actions render" -Passed ($indexResponse.Body -match "Create|Edit|Delete|Upsert|tblData|table") -Details "/Admin/$area/Index"
        if ($area -eq "Company") {
            Add-Result -Name "Company DataTable id exists" -Passed (Test-HtmlHasId -Html $indexResponse.Body -Id "tblData") -Details "/Admin/Company/Index"
            Add-Result -Name "Company JS reference exists" -Passed ($indexResponse.Body -match "/js/company\.js") -Details "/Admin/Company/Index"
            $companyApi = Invoke-Get "/Admin/Company/GetAll"
            $companyJson = Get-JsonBody $companyApi
            Add-Result -Name "Company GetAll contains data" -Passed ($companyApi.StatusCode -eq 200 -and $companyJson -ne $null -and (Test-Property -Object $companyJson -Name "data")) -Details "/Admin/Company/GetAll"
        }
    }

    $timestamp = (Get-Date).ToString("yyyyMMddHHmmss")
    Run-SimpleCrudIfRequested -AreaName "Category" -CreatePath "/Admin/Category/Create" -EditPathTemplate "/Admin/Category/Edit/{id}" -DeletePathTemplate "/Admin/Category/Delete/{id}" -UniqueName "QA-D-Category-$timestamp" -CreateFields @{
        "Name" = "QA-D-Category-$timestamp"
        "DisplayOrder" = "99"
    } -EditFields @{
        "Name" = "QA-D-Category-$timestamp-Updated"
        "DisplayOrder" = "98"
    }
    Run-SimpleCrudIfRequested -AreaName "CoverType" -CreatePath "/Admin/CoverType/Create" -EditPathTemplate "/Admin/CoverType/Edit/{id}" -DeletePathTemplate "/Admin/CoverType/Delete/{id}" -UniqueName "QA-D-Cover-$timestamp" -CreateFields @{
        "Name" = "QA-D-Cover-$timestamp"
    } -EditFields @{
        "Name" = "QA-D-Cover-$timestamp-Updated"
    }
    Run-CompanyCrudIfRequested

    Write-Section "F. Order admin smoke"
    $orderIndex = Invoke-Get "/Admin/Order/Index"
    Add-Result -Name "Order index loads" -Passed ($orderIndex.StatusCode -eq 200) -Details "HTTP $($orderIndex.StatusCode)"
    Add-Result -Name "Order DataTable id exists" -Passed (Test-HtmlHasId -Html $orderIndex.Body -Id "tblData") -Details "/Admin/Order/Index"
    Add-Result -Name "Order JS reference exists" -Passed ($orderIndex.Body -match "/js/order\.js") -Details "/Admin/Order/Index"

    foreach ($path in @(
        "/Admin/Order/Index",
        "/Admin/Order/Index?status=inprocess",
        "/Admin/Order/Index?status=pending",
        "/Admin/Order/Index?status=completed",
        "/Admin/Order/Index?status=approved",
        "/Admin/Order/Index?status=cancelled"
    )) {
        $statusPage = Invoke-Get $path
        Add-Result -Name "Order status URL $path" -Passed ($statusPage.StatusCode -eq 200 -and $statusPage.Body -match "admin-status-tab|status|tblData") -Details "HTTP $($statusPage.StatusCode)"
    }

    $orderApi = Invoke-Get "/Admin/Order/GetAll?status=all"
    $orderJson = Get-JsonBody $orderApi
    Add-Result -Name "Order GetAll returns 200" -Passed ($orderApi.StatusCode -eq 200) -Details "HTTP $($orderApi.StatusCode)"
    Add-Result -Name "Order GetAll contains data" -Passed ($orderJson -ne $null -and (Test-Property -Object $orderJson -Name "data")) -Details "/Admin/Order/GetAll?status=all"
    $firstOrder = Get-FirstDataItem $orderJson
    if ($firstOrder -ne $null) {
        Add-Result -Name "Order GetAll has rows" -Passed $true -Details "First OrderId=$($firstOrder.id)"
        foreach ($prop in @("id", "name", "phoneNumber", "applicationUser", "orderStatus", "orderTotal")) {
            Add-Result -Name "Order row field $prop" -Passed (Test-Property -Object $firstOrder -Name $prop) -Details "/Admin/Order/GetAll?status=all"
        }
    }
    else {
        Add-Skip -Name "Order GetAll has rows" -Details "No orders returned by local data."
    }

    $orderDetails = Invoke-Get "/Admin/Order/Details?orderId=$OrderId"
    if ($orderDetails.StatusCode -ne 200 -and $firstOrder -ne $null) {
        $script:ResolvedOrderId = [int]$firstOrder.id
        $orderDetails = Invoke-Get "/Admin/Order/Details?orderId=$script:ResolvedOrderId"
    }
    Add-Result -Name "Order details loads" -Passed ($orderDetails.StatusCode -eq 200) -Details "HTTP $($orderDetails.StatusCode) OrderId=$script:ResolvedOrderId"
    if ($orderDetails.StatusCode -eq 200) {
        Test-FieldSet -Html $orderDetails.Body -Names @(
            "OrderHeader.Id",
            "OrderHeader.Name",
            "OrderHeader.PhoneNumber",
            "OrderHeader.StreetAddress",
            "OrderHeader.City",
            "OrderHeader.State",
            "OrderHeader.PostalCode",
            "OrderHeader.Carrier",
            "OrderHeader.TrackingNumber"
        ) -Scope "Order details"
        Add-Result -Name "Order carrier id exists" -Passed (Test-HtmlHasId -Html $orderDetails.Body -Id "carrier") -Details "/Admin/Order/Details?orderId=$script:ResolvedOrderId"
        Add-Result -Name "Order trackingNumber id exists" -Passed (Test-HtmlHasId -Html $orderDetails.Body -Id "trackingNumber") -Details "/Admin/Order/Details?orderId=$script:ResolvedOrderId"
        Add-Result -Name "Order summary renders" -Passed ($orderDetails.Body -match "Order summary|Books in order|Total") -Details "/Admin/Order/Details?orderId=$script:ResolvedOrderId"
        Add-Result -Name "Order action area renders" -Passed ($orderDetails.Body -match "Update Order Details|Start Processing|Ship Order|Cancel Order|Pay Now") -Details "/Admin/Order/Details?orderId=$script:ResolvedOrderId"
        Add-Result -Name "Ship client validation present" -Passed ($orderDetails.Body -match "function validateInput" -and $orderDetails.Body -match "trackingNumber" -and $orderDetails.Body -match "carrier") -Details "/Admin/Order/Details?orderId=$script:ResolvedOrderId"
    }

    Write-Section "G. Order actions optional"
    if ($RunDestructive) {
        Add-Skip -Name "Order destructive actions" -Details "No disposable order creator is available in this harness. Use a manually created disposable order before testing StartProcessing/Ship/Cancel."
    }
    else {
        Add-Skip -Name "Order destructive actions" -Details "Use -RunDestructive only with a disposable local order."
    }

    Write-Section "H. DataTables/JS static checks"
    $productJs = Invoke-Get "/js/product.js"
    Add-Result -Name "product.js loads" -Passed ($productJs.StatusCode -eq 200) -Details "HTTP $($productJs.StatusCode)"
    Add-Result -Name "product.js references #tblData" -Passed ($productJs.Body -match "#tblData") -Details "/js/product.js"
    Add-Result -Name "product.js endpoint stable" -Passed ($productJs.Body -match "/Admin/Product/GetAll") -Details "/js/product.js"
    Add-Result -Name "product.js delete endpoint stable" -Passed ($productJs.Body -match "/Admin/Product/Delete") -Details "/js/product.js"
    Add-Result -Name "product.js uses SweetAlert/Toastr" -Passed ($productJs.Body -match "Swal\.fire" -and $productJs.Body -match "toastr") -Details "/js/product.js"

    $orderJs = Invoke-Get "/js/order.js"
    Add-Result -Name "order.js loads" -Passed ($orderJs.StatusCode -eq 200) -Details "HTTP $($orderJs.StatusCode)"
    Add-Result -Name "order.js references #tblData" -Passed ($orderJs.Body -match "#tblData") -Details "/js/order.js"
    Add-Result -Name "order.js endpoint stable" -Passed ($orderJs.Body -match "/Admin/Order/GetAll\?status=") -Details "/js/order.js"
    Add-Result -Name "order.js details route stable" -Passed ($orderJs.Body -match "/Admin/Order/Details\\?orderId=" -or $orderJs.Body -match "/Admin/Order/Details\?orderId=") -Details "/js/order.js"
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
Write-Host "Resolved ProductId: $script:ResolvedProductId"
Write-Host "Resolved OrderId: $script:ResolvedOrderId"
if ($script:CreatedRecords.Count -gt 0) {
    Write-Host "Created records:"
    foreach ($record in $script:CreatedRecords) {
        Write-Host "- $record"
    }
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
