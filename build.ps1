$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root 'build'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) {
    throw "No se encontró el compilador de .NET Framework: $csc"
}
New-Item -ItemType Directory -Force -Path $build | Out-Null
& (Join-Path $root 'generate-icon.ps1')

$commonReferences = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Web.Extensions.dll'
)

$appSources = Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' |
    Sort-Object Name |
    ForEach-Object { $_.FullName }
& $csc /nologo /target:exe /platform:anycpu /optimize+ "/win32icon:$build\Continuador.ico" "/out:$build\Continuador.exe" $commonReferences $appSources
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de Continuador.exe.' }

$setupArgs = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    "/win32icon:$build\Continuador.ico",
    "/out:$build\Continuador-Setup.exe",
    '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll', '/r:System.Web.Extensions.dll', '/r:Microsoft.CSharp.dll',
    "/resource:$build\Continuador.exe,Continuador.Payload.exe",
    "/resource:$root\skills\codex\SKILL.md,Continuador.CodexSkill.md",
    "/resource:$root\skills\claude\SKILL.md,Continuador.ClaudeSkill.md",
    "/resource:$root\installer\LEEME.txt,Continuador.Readme.txt",
    "$root\installer\Setup.cs"
)
& $csc $setupArgs
if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de Continuador-Setup.exe.' }

Write-Host "Compilación terminada: $build"
