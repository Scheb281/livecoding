param()

$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$deployment = Join-Path $project '.deploy'
if (-not (Test-Path -LiteralPath (Join-Path $deployment '.initialized'))) {
    throw 'Сначала создайте .deploy командой docker compose --profile tools run --rm setup (после сборки setup).'
}
$importedKeys = Join-Path $deployment 'imported-keys'
if ((Test-Path -LiteralPath $importedKeys) -and @(Get-ChildItem -LiteralPath $importedKeys -Force).Count -gt 0) {
    throw 'Ключи уже экспортированы. Существующий экспорт не заменяется; повторная подготовка не нужна.'
}

# Пароль читается из уже настроенного приложения и не печатается в терминал.
$secretsPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\visual-interview-api-development\secrets.json'
$secrets = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json
$connection = [string]$secrets.'ConnectionStrings:Database'
$builder = [System.Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString($connection)
if ([string]$builder['Host'] -notmatch '\.mdb\.yandexcloud\.net$') { throw 'В user-secrets API не настроен хост Yandex Managed PostgreSQL.' }
$builder['SSL Mode'] = 'VerifyFull'
$builder['Root Certificate'] = '/run/interview/database-ca.crt'
$pendingConnection = $builder.get_ConnectionString()
$builder = $null
$secrets = $null
$connection = $null

# Экспорт выполняется на этой Windows под тем же пользователем, который запускал API.
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
try {
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    & dotnet run --project (Join-Path $project 'backend\Interview.Api') --configuration Release --no-launch-profile -- `
        --export-keyring "--Export:Directory=$importedKeys" `
        "--Export:CertificatePath=$(Join-Path $deployment 'keyring.pfx')" `
        "--Export:CertificatePasswordFile=$(Join-Path $deployment 'keyring_password')"
    if ($LASTEXITCODE -ne 0) { throw 'Экспорт не завершён. Не запускайте контейнеры с этой БД до успешного экспорта ключей.' }
}
finally { $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment }

# Переключаем подключение только после успешного экспорта и проверки ключей.
Copy-Item -LiteralPath (Join-Path $project 'root.crt') -Destination (Join-Path $deployment 'database_ca.crt') -Force
[IO.File]::WriteAllText((Join-Path $deployment 'database_connection'), $pendingConnection, [Text.UTF8Encoding]::new($false))
$pendingConnection = $null

$environmentFile = Join-Path $project '.env'
if (-not (Test-Path -LiteralPath $environmentFile)) { Copy-Item -LiteralPath (Join-Path $project '.env.example') -Destination $environmentFile }
$content = Get-Content -LiteralPath $environmentFile -Raw
$content = [regex]::Replace($content, '(?m)^COMPOSE_FILE=.*$', 'COMPOSE_FILE=compose.yml')
[IO.File]::WriteAllText($environmentFile, $content, [Text.UTF8Encoding]::new($false))
Write-Host 'Подключение Yandex и переносимые ключи подготовлены. .env, .deploy и исходники перенесите на Linux по SSH.'
Write-Host 'Рабочая база не изменялась. При переключении остановите старый API: одновременно работает один экземпляр.'
