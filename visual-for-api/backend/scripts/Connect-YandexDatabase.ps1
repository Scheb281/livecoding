param(
    [Parameter(Mandatory = $true)]
    [string]$HostName,
    [string]$DatabaseName = 'db1',
    [string]$UserName = 'user1',
    [int]$Port = 6432,
    [switch]$ApplyMigrations
)

$ErrorActionPreference = 'Stop'
$backendDirectory = Split-Path -Parent $PSScriptRoot
$apiProject = Join-Path $backendDirectory 'Interview.Api\Interview.Api.csproj'
$probeProject = Join-Path $backendDirectory 'Interview.DatabaseSetup\Interview.DatabaseSetup.csproj'
$probeAssembly = Join-Path $backendDirectory 'Interview.DatabaseSetup\bin\Release\net10.0\Interview.DatabaseSetup.dll'

# Проверяем адрес до ввода пароля и изменения локальных настроек.
if ($HostName -match '[\s;/]' -or [string]::IsNullOrWhiteSpace($HostName)) {
    throw 'Укажите только DNS-имя хоста из раздела подключения PostgreSQL.'
}
try { $null = [System.Net.Dns]::GetHostAddresses($HostName) }
catch { throw 'Хост не найден в DNS. Скопируйте актуальное имя из настроек кластера Yandex Cloud.' }

$socket = [System.Net.Sockets.TcpClient]::new()
try {
    $pending = $socket.ConnectAsync($HostName, $Port)
    if (-not $pending.Wait(10000) -or -not $socket.Connected) {
        throw 'Порт PostgreSQL недоступен. Проверьте публичный доступ/VPN и группу безопасности.'
    }
}
finally { $socket.Dispose() }

# Используем CA из корня проекта и передаём Npgsql его полный путь.
$projectDirectory = Split-Path -Parent $backendDirectory
$certificatePath = Join-Path $projectDirectory 'root.crt'
if (-not (Test-Path -LiteralPath $certificatePath)) {
    Invoke-WebRequest -Uri 'https://storage.yandexcloud.net/cloud-certs/CA.pem' -OutFile $certificatePath
}

# Вспомогательная программа проверяет TLS, пароль и SELECT VERSION() без изменения БД.
& dotnet build $probeProject --configuration Release --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Не удалось собрать проверку подключения. Нужны .NET SDK 10 и пакет Npgsql.' }

$securePassword = Read-Host 'Пароль пользователя PostgreSQL (ввод скрыт)' -AsSecureString
$passwordPointer = [IntPtr]::Zero
try {
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    if ([string]::IsNullOrEmpty($plainPassword)) { throw 'Пароль не введён.' }
    $payload = @{
        Host = $HostName; Port = $Port; Database = $DatabaseName
        Username = $UserName; Password = $plainPassword; Certificate = $certificatePath
    } | ConvertTo-Json -Compress
    $previousOutputEncoding = $OutputEncoding
    try {
        # Сохраняем Unicode в пароле при запуске из Windows PowerShell.
        $OutputEncoding = [System.Text.UTF8Encoding]::new($false)
        $probeOutput = $payload | & dotnet $probeAssembly
        $probeExitCode = $LASTEXITCODE
    }
    finally { $OutputEncoding = $previousOutputEncoding }
    $result = $probeOutput | ConvertFrom-Json
    if ($probeExitCode -ne 0 -or -not $result.success) {
        if ($result.sqlState -in @('28P01', '28000')) { throw 'PostgreSQL отклонил пароль. Прежнее подключение сохранено.' }
        if ($result.sqlState -eq '3D000') { throw 'Такой базы данных нет. Прежнее подключение сохранено.' }
        throw 'Проверка подключения не прошла. Проверьте имя БД, пользователя, сертификат и доступ к серверу. Прежнее подключение сохранено.'
    }

    # DbConnectionStringBuilder корректно экранирует специальные символы пароля.
    $connectionBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connectionBuilder['Host'] = $HostName
    $connectionBuilder['Port'] = $Port
    $connectionBuilder['Database'] = $DatabaseName
    $connectionBuilder['Username'] = $UserName
    $connectionBuilder['Password'] = $plainPassword
    $connectionBuilder['SSL Mode'] = 'VerifyFull'
    # Join-Path возвращает обёртку PSObject; индексатор .NET требует обычную строку.
    $connectionBuilder['Root Certificate'] = [string]$certificatePath
    $connectionBuilder['Timeout'] = 15

    # Записываем только после успешной проверки, сохраняя остальные user-secrets.
    [xml]$projectXml = Get-Content -LiteralPath $apiProject -Raw
    $secretsId = [string]$projectXml.Project.PropertyGroup.UserSecretsId
    $secretsDirectory = Join-Path $env:APPDATA ('Microsoft\UserSecrets\' + $secretsId)
    $secretsPath = Join-Path $secretsDirectory 'secrets.json'
    $secrets = @{}
    if (Test-Path -LiteralPath $secretsPath) {
        $existing = Get-Content -LiteralPath $secretsPath -Raw | ConvertFrom-Json
        foreach ($property in $existing.PSObject.Properties) { $secrets[$property.Name] = $property.Value }
    }
    $secrets['ConnectionStrings:Database'] = $connectionBuilder.ConnectionString
    New-Item -ItemType Directory -Path $secretsDirectory -Force | Out-Null
    $secrets | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $secretsPath -Encoding UTF8
    Write-Host 'TLS-подключение проверено. Строка подключения сохранена в локальных user-secrets API.'
    Write-Host ('Сервер: ' + $result.version)
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer) }
    $plainPassword = $null
    $payload = $null
    $connectionBuilder = $null
    $secrets = $null
    $securePassword.Dispose()
}

# Структуру БД изменяем только при явно указанном переключателе.
if ($ApplyMigrations) {
    & dotnet run --project $apiProject -- --migrate
    if ($LASTEXITCODE -ne 0) { throw 'Подключение настроено, но миграции завершились ошибкой. Проверьте права пользователя на создание таблиц.' }
    Write-Host 'Миграции применены; роли созданы.'
}


