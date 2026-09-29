param([switch]$Parar, [int]$Puerto = 5149, [switch]$Publicado)

$ErrorActionPreference = "Stop"
$raiz = $PSScriptRoot
$log = "$env:TEMP\blzpg.log"
$err = "$env:TEMP\blzpg.err"

function Parar-Aplicacion {
    $procesos = Get-CimInstance Win32_Process |
        Where-Object { $_.CommandLine -match "gestor_archivos_blzpg" -and $_.Name -ne "powershell.exe" }
    foreach ($p in $procesos) {
        Write-Host "  parando PID $($p.ProcessId) ($($p.Name))"
        Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 3
}

Write-Host "=== 1. parar lo anterior ==="
Parar-Aplicacion
if ($Parar) { Write-Host "Parado."; exit 0 }

$exe = "C:\Program Files\dotnet\dotnet.exe"

if ($Publicado) {
    # Se ejecuta la version publicada. Es la unica forma de tener el manifiesto
    # de arranque: en Debug, dotnet run no lo genera y el navegador no sabe que
    # ensamblados descargar, con lo que la pagina se sirve pero no arranca.
    Write-Host ""
    Write-Host "=== 2. ejecutar la version publicada ==="
    $dll = "$raiz\publicado\gestor_archivos_blzpg.dll"
    if (-not (Test-Path $dll)) {
        Write-Host "  FALLO: no esta publicado. Ejecuta primero:"
        Write-Host "    dotnet publish gestor_archivos_blzpg -c Release -o publicado"
        exit 1
    }

    # El contenido base se pone en la carpeta de la aplicacion: ahi es donde
    # busca appsettings.Local.json, y las rutas de los archivos de usuario.
    Copy-Item "$raiz\gestor_archivos_blzpg\appsettings.Local.json" "$raiz\publicado\appsettings.Local.json" -Force -ErrorAction SilentlyContinue

    Remove-Item $log, $err -ErrorAction SilentlyContinue
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    Start-Process -FilePath $exe -ArgumentList "`"$dll`" --urls http://localhost:$Puerto" `
        -WorkingDirectory "$raiz\publicado" -RedirectStandardOutput $log -RedirectStandardError $err `
        -WindowStyle Hidden
}
else {
    Write-Host ""
    Write-Host "=== 2. compilar y ejecutar en modo desarrollo ==="
    Push-Location $raiz
    $salida = & dotnet build 2>&1
    $codigo = $LASTEXITCODE
    Pop-Location

    $errores = $salida | Select-String "error" | Where-Object { $_ -notmatch "0 Errores" } | Select-Object -Unique -First 8
    if ($errores) {
        Write-Host "  FALLO:"
        $errores | ForEach-Object { Write-Host "    $($_.Line.Trim())" }
        exit 1
    }
    Write-Host "  compilacion correcta"

    Remove-Item $log, $err -ErrorAction SilentlyContinue
    $env:ASPNETCORE_ENVIRONMENT = "Development"
    Start-Process -FilePath $exe `
        -ArgumentList "run --project gestor_archivos_blzpg\gestor_archivos_blzpg.csproj --no-build --no-launch-profile --urls http://localhost:$Puerto" `
        -WorkingDirectory $raiz -RedirectStandardOutput $log -RedirectStandardError $err `
        -WindowStyle Hidden
}

Write-Host ""
Write-Host "=== 3. esperar a que responda ==="
$arriba = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 2
    $codigo = & curl.exe -s -o NUL -w "%{http_code}" --max-time 5 "http://localhost:$Puerto/auth" 2>$null
    if ($codigo -eq "200") { $arriba = $true; break }
}

if (-not $arriba) {
    Write-Host "  NO ARRANCO. Ultimas lineas:"
    Get-Content $log -ErrorAction SilentlyContinue | Select-Object -Last 12 | ForEach-Object { Write-Host "    $_" }
    exit 1
}

Write-Host "  http://localhost:$Puerto/auth"
Get-Content $log -ErrorAction SilentlyContinue | Select-Object -First 4 | ForEach-Object { Write-Host "  $_" }
