#Requires -Version 5.1
<#
Ticker de datos para la pagina "Data from APIs" ({ext.text}, FullScreen).
Sin dependencias externas: solo PowerShell (viene con Windows) e Invoke-RestMethod/
Invoke-WebRequest, que ya estan en el sistema. Uso:

    powershell -File api_ticker.ps1 | zkc --watch

Pensado para el tab Auto-start de zmk-companion (se relanza solo al iniciar la app).

Cada linea se limita a ~10 caracteres: el modo FullScreen renderiza con una fuente de
ancho fijo que en 68px solo entran ~9-10 caracteres por linea antes de partirse en una
"pagina" adicional (mismo problema que ya diagnosticamos con reloj_unicode.py). Por eso
el diseno usa muchas lineas cortas (encabezado + valor) en vez de pocas lineas largas.

NO VERIFICADO EN VIVO: este entorno no tiene salida a internet para probar los 3
endpoints (stooq, frankfurter, kworb), toca probarlo en una maquina Windows real. La
parte mas fragil casi seguro es el regex de kworb.net (scraping de HTML de terceros,
sin API oficial), revisa esa funcion primero si algo no aparece.
#>

$ErrorActionPreference = 'SilentlyContinue'

# Guarda contra instancias duplicadas: este script corre en un bucle infinito, y
# "Ejecutar ahora" en el tab Auto-start (o abrirlo dos veces sin querer) lanzaria
# una segunda copia mandando datos en paralelo con la primera, sin ningun aviso.
# Un Mutex con nombre es la forma estandar de Windows para esto: si otra instancia
# ya lo tiene, esta simplemente termina de inmediato, sin tocar zkc ni imprimir nada.
$mutex = New-Object System.Threading.Mutex($false, 'Global\ZmkCompanion_ApiTicker')
if (-not $mutex.WaitOne(0)) {
    exit
}

try {

function Get-DowJones {
    try {
        # stooq.com/q/l/ ya no existe ("the page you requested does not exist or
        # has been moved", confirmado en vivo), reemplazado por el endpoint de
        # grafico de Yahoo Finance, sin API key. Confirmado en vivo:
        # regularMarketPrice devolvio 52658.64.
        $r = Invoke-RestMethod -Uri 'https://query1.finance.yahoo.com/v8/finance/chart/%5EDJI' -TimeoutSec 8
        $price = $r.chart.result[0].meta.regularMarketPrice
        if (-not $price -or $price -le 0) { return $null }
        # F0, no N0: N0 agrega separador de miles segun la configuracion regional,
        # y en varias configuraciones (confirmado con es-CO) ese separador no es
        # un espacio ASCII normal sino un espacio Unicode que la fuente del
        # display no tiene, se ve como un glifo de reemplazo (rombo con "?").
        return "{0:F0}" -f [double]$price
    } catch { return $null }
}

function Get-UsdRate {
    param([string]$Currency)
    if ($Currency -eq 'USD') { return '1' }
    try {
        # Frankfurter/ECB no cubre COP (ni la mayoria de monedas latinoamericanas,
        # solo BRL y MXN) - confirmado con un "not found" real al probarlo. Este
        # proyecto (JSON estatico via jsdelivr, sin key) si trae COP, confirmado:
        # .usd.cop devolvio 3257.16 en una prueba real.
        $code = $Currency.ToLower()
        $r = Invoke-RestMethod -Uri 'https://cdn.jsdelivr.net/npm/@fawazahmed0/currency-api@latest/v1/currencies/usd.json' -TimeoutSec 8
        $rate = $r.usd.$code
        if (-not $rate) { return $null }
        return "{0:F0}" -f [double]$rate
    } catch { return $null }
}

function Get-TopSong {
    try {
        $resp = Invoke-WebRequest -Uri 'https://kworb.net/spotify/country/global_daily.html' `
            -TimeoutSec 8 -UseBasicParsing -UserAgent 'Mozilla/5.0 (Windows NT 10.0; Win64; x64)'
        # Fila #1 real (confirmada contra el HTML de kworb, mi primer regex asumia
        # el titulo como texto plano, en realidad tambien es un <a>, dentro de un
        # <div>):
        #   <td class="text mp"><div><a href="...artist...">Artista</a> - <a href="...track...">Titulo</a>(...)</div></td>
        # (?s) para que "." cruce saltos de linea (la fila viene partida en varias
        # lineas de HTML). Solo se usa el primer match del documento (fila #1 es
        # la primera aparicion), se ignora cualquier "(w/ Otro Artista)" que venga
        # despues del titulo.
        if ($resp.Content -match '(?s)<td class="text mp"><div><a[^>]*>([^<]+)</a>\s*-\s*<a[^>]*>([^<]+)</a>') {
            $artist = $Matches[1].Trim()
            $title  = $Matches[2].Trim()
            return "$title - $artist"
        }
        return $null
    } catch { return $null }
}

# Region de Windows -> codigo de moneda ISO (ej. es-CO -> COP, en-US -> USD).
# Si falla (region rara, sin match en RegionInfo), cae a USD (tasa = 1, no falla).
$currency = 'USD'
try {
    $currency = ([System.Globalization.RegionInfo]((Get-Culture).Name)).ISOCurrencySymbol
} catch { }

# Ultimo valor conocido de cada dato, para no dejar la pantalla en blanco si una
# consulta puntual falla (red caida, endpoint lento, etc.) - mismo criterio que ZMK
# Companion ya usa para {battery.percent} ("--" en vez de crash o vacio).
$dow  = '--'
$rate = '--'
$song = '-- - --'

while ($true) {
    $d = Get-DowJones
    if ($d) { $dow = $d }

    $r = Get-UsdRate -Currency $currency
    if ($r) { $rate = $r }

    $s = Get-TopSong
    if ($s) { $song = $s }

    # Corta el titulo+artista en dos lineas de hasta 10 caracteres cada una (20 en
    # total). No es "prose-aware": puede cortar a mitad de palabra, es un ticker, no
    # una lectura completa.
    $songLine1 = $song.Substring(0, [Math]::Min(10, $song.Length))
    $rest      = if ($song.Length -gt 10) { $song.Substring(10) } else { '' }
    $songLine2 = $rest.Substring(0, [Math]::Min(10, $rest.Length))

    $lines = @(
        'from APIs:'
        'DOW JONES'
        $dow
        ''
        "USD/$currency"
        $rate
        ''
        'Spotify'
        'TOP SONG'
        $songLine1
        $songLine2
    )

    # "\n" literal (2 caracteres), no un salto de linea real: zkc trata cada salto
    # real como un envio independiente. Con \n literal viaja como un solo envio y
    # zkc lo interpreta como salto de renglon en el display (mismo patron ya usado
    # en reloj_unicode.py e hypenator.py). En PowerShell, "\n" dentro de comillas
    # dobles YA es literal (no hace falta escapar como en Python); el salto real
    # en PowerShell es `n con backtick, que aqui deliberadamente no se usa.
    Write-Output ($lines -join "\n")

    # 5 minutos: Dow Jones, tipo de cambio y el chart de Spotify no cambian
    # segundo a segundo, y kworb.net es un sitio de terceros sin API, mejor no
    # golpearlo mas seguido de lo necesario.
    Start-Sleep -Seconds 300
}

} finally {
    # Solo se alcanza si el bucle termina (Ctrl+C, cierre de ventana, etc.), no en
    # el "exit" de arriba (ese ya se fue antes de tomar el mutex). Liberar aqui es
    # higiene, no algo de lo que dependa la logica: si el proceso muere sin pasar
    # por aqui, Windows libera el mutex solo al terminar el proceso.
    $mutex.ReleaseMutex() | Out-Null
}
