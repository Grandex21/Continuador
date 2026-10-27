$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspace = Split-Path -Parent (Split-Path -Parent $root)
$outputs = Join-Path $workspace 'outputs'
$build = Join-Path $root 'build'
$portableStage = Join-Path $build 'portable-package'
$sourceStage = Join-Path $build 'source-package'

& (Join-Path $root 'build.ps1')

New-Item -ItemType Directory -Force -Path $outputs | Out-Null
foreach ($stage in @($portableStage, $sourceStage)) {
    if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
}

Copy-Item -LiteralPath (Join-Path $build 'Continuador.exe') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $root 'installer\LEEME.txt') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $root 'portable\LEEME-PORTABLE.txt') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $root 'portable\Configurar-portable.ps1') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $root 'portable\Configurar-portable.cmd') -Destination $portableStage
Copy-Item -LiteralPath (Join-Path $root 'skills') -Destination $portableStage -Recurse

foreach ($directory in @('src','installer','skills','portable','tests')) {
    Copy-Item -LiteralPath (Join-Path $root $directory) -Destination $sourceStage -Recurse
}
Copy-Item -LiteralPath (Join-Path $root 'build.ps1') -Destination $sourceStage
Copy-Item -LiteralPath (Join-Path $root 'generate-icon.ps1') -Destination $sourceStage
Copy-Item -LiteralPath (Join-Path $root 'tests.ps1') -Destination $sourceStage
Copy-Item -LiteralPath (Join-Path $root 'package.ps1') -Destination $sourceStage
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $sourceStage

$setupOutput = Join-Path $outputs 'Continuador-Setup.exe'
$portableOutput = Join-Path $outputs 'Continuador-portable.zip'
$sourceOutput = Join-Path $outputs 'Continuador-source.zip'
Copy-Item -LiteralPath (Join-Path $build 'Continuador-Setup.exe') -Destination $setupOutput -Force
foreach ($archive in @($portableOutput, $sourceOutput)) {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
}
Compress-Archive -Path (Join-Path $portableStage '*') -DestinationPath $portableOutput -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $sourceStage '*') -DestinationPath $sourceOutput -CompressionLevel Optimal

$hashLines = foreach ($file in @($setupOutput, $portableOutput, $sourceOutput)) {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $file
    "$($hash.Hash)  $([IO.Path]::GetFileName($file))"
}
$hashLines | Set-Content -LiteralPath (Join-Path $outputs 'Continuador-SHA256.txt') -Encoding ASCII

Get-Item -LiteralPath $setupOutput, $portableOutput, $sourceOutput, (Join-Path $outputs 'Continuador-SHA256.txt') |
    Select-Object FullName, Length
