param([string]$GatewayUrl = 'http://localhost:8080')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$composeFile = Join-Path $repoRoot 'deploy/docker-compose.yaml'

function Invoke-Compose([string[]]$CommandArgs) {
    & docker compose -f $composeFile @CommandArgs
    if ($LASTEXITCODE -ne 0) { throw "Falha no Docker Compose: $CommandArgs" }
}

function Invoke-Api([string]$Method, [string]$Path, [int]$Expected, $Body = $null, [hashtable]$Headers = @{}) {
    $parameters = @{ Uri = "$GatewayUrl$Path"; Method = $Method; Headers = $Headers; UseBasicParsing = $true; TimeoutSec = 30 }
    if ($null -ne $Body) { $parameters.Body = $Body | ConvertTo-Json -Compress; $parameters.ContentType = 'application/json' }
    try {
        $response = Invoke-WebRequest @parameters
        $status = [int]$response.StatusCode
        $content = $response.Content
    } catch {
        if (-not $_.Exception.Response) { throw }
        $status = [int]$_.Exception.Response.StatusCode
        $content = $null
    }
    if ($status -ne $Expected) { throw "$Method $Path retornou $status; esperado $Expected." }
    if ($content) { return $content | ConvertFrom-Json }
}

function Wait-Api([string]$Path, [hashtable]$Headers = @{}) {
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        try { return Invoke-Api GET $Path 200 -Headers $Headers } catch { Start-Sleep -Seconds 1 }
    }
    throw "Endpoint não voltou a responder: $Path"
}

$run = [Guid]::NewGuid().ToString('N')
$email = "security-lifecycle-$run@teste.local"
$null = Invoke-Api POST /api/identity/users 201 @{ name = 'Security lifecycle'; email = $email; password = 'senha123' }
$tokens = Invoke-Api POST /api/auth/login 200 @{ email = $email; password = 'senha123' }
$headers = @{ Authorization = 'Bearer ' + $tokens.accessToken }
$jwks = Invoke-Api GET /api/auth/.well-known/jwks.json 200
$null = Invoke-Api GET /api/identity/users/me 200 -Headers $headers
$null = Invoke-Api GET /api/download/progress 200 -Headers $headers

try {
    Write-Host '1/5 Reiniciando Security: chave e sessão precisam sobreviver...'
    Invoke-Compose @('restart', 'securityservice')
    $after = Wait-Api /api/auth/.well-known/jwks.json
    if ($after.keys[0].kid -ne $jwks.keys[0].kid) { throw 'Chave mudou após o reinício.' }
    $tokens = Invoke-Api POST /api/auth/refresh 200 @{ refreshToken = $tokens.refreshToken }
    Write-Host 'OK: mesma chave e refresh persistente após reinício.'

    Write-Host '2/5 Parando Security: consumidores precisam usar o JWKS em cache...'
    Invoke-Compose @('stop', 'securityservice')
    $null = Invoke-Api GET /api/identity/users/me 200 -Headers $headers
    $null = Invoke-Api GET /api/download/progress 200 -Headers $headers
    Write-Host 'OK: Identity e Download validam JWT com Security indisponível.'
    Invoke-Compose @('start', 'securityservice')
    $null = Wait-Api /api/auth/.well-known/jwks.json

    Write-Host '3/5 Parando Identity: refresh precisa falhar sem consumir o token...'
    Invoke-Compose @('stop', 'identityservice')
    $null = Invoke-Api POST /api/auth/refresh 503 @{ refreshToken = $tokens.refreshToken }
    $null = Invoke-Api POST /api/auth/login 503 @{ email = $email; password = 'senha123' }
    Invoke-Compose @('start', 'identityservice')
    $null = Wait-Api /api/identity/users/me $headers
    $tokens = Invoke-Api POST /api/auth/refresh 200 @{ refreshToken = $tokens.refreshToken }
    Write-Host 'OK: dependência recuperada, mesmo refresh aceito após falha.'

    Write-Host '4/5 Enviando 12 renovações simultâneas...'
    $client = New-Object System.Net.Http.HttpClient
    try {
        $body = @{ refreshToken = $tokens.refreshToken } | ConvertTo-Json -Compress
        $requests = @(1..12 | ForEach-Object {
            $content = New-Object System.Net.Http.StringContent($body, [Text.Encoding]::UTF8, 'application/json')
            $client.PostAsync("$GatewayUrl/api/auth/refresh", $content)
        })
        [Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]$requests)
        $statuses = @($requests | ForEach-Object { [int]$_.Result.StatusCode })
        if (@($statuses | Where-Object { $_ -eq 200 }).Count -ne 1 -or @($statuses | Where-Object { $_ -ne 200 -and $_ -ne 401 }).Count -gt 0) {
            throw "Resultado incorreto das renovações concorrentes: $statuses"
        }
        $winner = $requests | Where-Object { [int]$_.Result.StatusCode -eq 200 } | Select-Object -First 1
        $next = $winner.Result.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
        $null = Invoke-Api POST /api/auth/refresh 401 @{ refreshToken = $next.refreshToken }
    } finally { $client.Dispose() }
    Write-Host 'OK: uma renovação aceita; reutilização revogou a sessão.'

    Write-Host '5/5 Confirmando que os serviços recuperaram...'
    $null = Wait-Api /api/identity/users/me $headers
    $null = Wait-Api /api/download/progress $headers
    Write-Host 'RESULTADO: OK - reinício, cache JWKS, falha/recuperação e concorrência pelo gateway.'
} finally {
    Invoke-Compose @('start', 'securityservice', 'identityservice')
}
