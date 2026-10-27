$ErrorActionPreference = 'Stop'

$app = Join-Path $PSScriptRoot 'Continuador.exe'
if (-not (Test-Path -LiteralPath $app)) {
    throw "No se encontró Continuador.exe junto a este script."
}

function Install-Skill([string]$source, [string]$destination) {
    if (Test-Path -LiteralPath $destination) {
        $backup = "$destination.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        Move-Item -LiteralPath $destination -Destination $backup
        Write-Host "Copia de seguridad: $backup"
    }
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $text = Get-Content -LiteralPath $source -Raw -Encoding UTF8
    $installedPath = '$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe'
    $text = $text.Replace($installedPath, $app)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllText((Join-Path $destination 'SKILL.md'), $text, $utf8NoBom)
    [IO.File]::WriteAllText((Join-Path $destination '.continuador-portable-installed'), $app, $utf8NoBom)
}

$userRoot = if ($env:CONTINUADOR_PORTABLE_TEST_ROOT) { $env:CONTINUADOR_PORTABLE_TEST_ROOT } else { $env:USERPROFILE }
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $userRoot '.codex' }
Install-Skill (Join-Path $PSScriptRoot 'skills\codex\SKILL.md') (Join-Path $codexHome 'skills\continuar-tarea')
Install-Skill (Join-Path $PSScriptRoot 'skills\claude\SKILL.md') (Join-Path $userRoot '.claude\skills\continuar-tarea')

Write-Host ''
Write-Host 'Integraciones portátiles configuradas. Reinicia Codex y Claude Code.'
