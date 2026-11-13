param(
    [string]$DownloadUrl = 'https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-lgpl.zip'
)

$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent $PSScriptRoot
$toolDirectory = Join-Path $projectDirectory '.tools/ffmpeg'
$archivePath = Join-Path $projectDirectory '.tools/ffmpeg-download.zip'
$ffmpegPath = Join-Path $toolDirectory 'ffmpeg.exe'
$ffprobePath = Join-Path $toolDirectory 'ffprobe.exe'

if ((Test-Path -LiteralPath $ffmpegPath) -and (Test-Path -LiteralPath $ffprobePath)) {
    Write-Output "FFmpeg y FFprobe ya están disponibles en $toolDirectory"
    exit 0
}

New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath $archivePath)) {
    $partialDownload = $archivePath + '.download'
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $partialDownload
    Move-Item -LiteralPath $partialDownload -Destination $archivePath -Force
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    foreach ($name in @('ffmpeg.exe', 'ffprobe.exe', 'LICENSE.txt')) {
        $entry = $archive.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
        if ($null -eq $entry) { throw "La descarga no contiene $name." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $toolDirectory $name), $true)
    }
}
finally { $archive.Dispose() }

@(
    "Download: $DownloadUrl"
    "Archive SHA256: $((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash)"
    "FFmpeg SHA256: $((Get-FileHash -LiteralPath $ffmpegPath -Algorithm SHA256).Hash)"
    & $ffmpegPath -version
) | Set-Content -LiteralPath (Join-Path $toolDirectory 'BUILD.txt') -Encoding utf8

Write-Output "Herramientas de vídeo preparadas en $toolDirectory"
