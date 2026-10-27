$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $root 'build\Continuador.exe'
$setup = Join-Path $root 'build\Continuador-Setup.exe'
$testRoot = Join-Path $root 'test-output'
$data = Join-Path $testRoot 'data'
$fakeLog = Join-Path $testRoot 'fake-clients.log'

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw "PRUEBA FALLIDA: $message" }
}

function Invoke-Continuador([string[]]$arguments) {
    $raw = & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "Continuador devolvió $LASTEXITCODE`: $raw" }
    return ($raw | Out-String | ConvertFrom-Json)
}

if (Test-Path -LiteralPath $testRoot) {
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $data | Out-Null

$env:CONTINUADOR_DATA_DIR = $data
$env:CONTINUADOR_NO_DAEMON = '1'
$env:CONTINUADOR_CODEX_EXE = Join-Path $root 'tests\fake-codex.cmd'
$env:CONTINUADOR_CLAUDE_EXE = Join-Path $root 'tests\fake-claude.cmd'
$env:CONTINUADOR_FAKE_LOG = $fakeLog
$env:CONTINUADOR_FAKE_MODE = ''
$env:CONTINUADOR_CODEX_SESSION_ACTIVE = '1'

$future = [DateTimeOffset]::Now.AddHours(2).ToString('o')
$scheduled = Invoke-Continuador @('schedule','--client','codex','--at',$future,'--message','prueba futura','--session','codex-test-1','--cwd',$root)
Assert-True ($scheduled.status -eq 'Pendiente') 'La tarea futura debe quedar pendiente.'

$listed = Invoke-Continuador @('list','--json')
Assert-True (@($listed).Count -eq 1) 'La programación debe persistir entre procesos.'

$changedAt = [DateTimeOffset]::Now.AddHours(3).ToString('o')
$changed = Invoke-Continuador @('update','--id',$scheduled.id.Substring(0,12),'--at',$changedAt)
Assert-True ($changed.status -eq 'Pendiente') 'Cambiar la hora debe conservar el estado pendiente.'
$cancelled = Invoke-Continuador @('cancel','--id',$scheduled.id)
Assert-True ($cancelled.status -eq 'Cancelada') 'Cancelar debe cambiar el estado.'

$due = [DateTimeOffset]::Now.ToString('o')
$codex = Invoke-Continuador @('schedule','--client','codex','--at',$due,'--message','mensaje codex','--session','codex-test-2','--cwd',$root)
$processed = Invoke-Continuador @('process-due')
Assert-True ($processed.processed -eq 1) 'Debe procesarse una tarea vencida de Codex.'
$afterCodex = @(Invoke-Continuador @('list','--json')) | Where-Object { $_.id -eq $codex.id }
Assert-True ($afterCodex.status -eq 'Entregada') 'Codex debe quedar entregada.'

$claude = Invoke-Continuador @('schedule','--client','claude','--at',([DateTimeOffset]::Now.ToString('o')),'--message','mensaje claude','--session','claude-test-1','--cwd',$root)
$processed = Invoke-Continuador @('process-due')
Assert-True ($processed.processed -eq 1) 'Debe procesarse una tarea vencida de Claude.'
$afterClaude = @(Invoke-Continuador @('list','--json')) | Where-Object { $_.id -eq $claude.id }
Assert-True ($afterClaude.status -eq 'Entregada') 'Claude debe quedar entregada.'

$clientCalls = Get-Content -LiteralPath $fakeLog -Raw
Assert-True ($clientCalls -match 'codex queue --thread codex-test-2 --message "mensaje codex"') 'El adaptador Codex debe usar queue y la sesión correcta.'
Assert-True ($clientCalls -match 'claude -p --resume claude-test-1 "mensaje claude"') 'El adaptador Claude debe usar --resume y la sesión correcta.'

$env:CONTINUADOR_FAKE_MODE = 'queue-fails'
$fallback = Invoke-Continuador @('schedule','--client','codex','--at',([DateTimeOffset]::Now.ToString('o')),'--message','prueba fallback','--session','codex-fallback','--cwd',$root)
$processed = Invoke-Continuador @('process-due')
$afterFallback = @(Invoke-Continuador @('list','--json')) | Where-Object { $_.id -eq $fallback.id }
Assert-True ($afterFallback.status -eq 'Entregada') 'Codex debe usar exec resume si queue falla.'
$clientCalls = Get-Content -LiteralPath $fakeLog -Raw
Assert-True ($clientCalls -match 'codex exec resume --skip-git-repo-check codex-fallback "prueba fallback"') 'Debe invocarse el respaldo exec resume.'

$env:CONTINUADOR_FAKE_MODE = 'rate-limit'
$retry = Invoke-Continuador @('schedule','--client','claude','--at',([DateTimeOffset]::Now.ToString('o')),'--message','prueba limite','--session','claude-retry','--cwd',$root)
$processed = Invoke-Continuador @('process-due')
$afterRetry = @(Invoke-Continuador @('list','--json')) | Where-Object { $_.id -eq $retry.id }
Assert-True ($afterRetry.status -eq 'Pendiente') 'Un límite de créditos debe dejar la tarea pendiente.'
Assert-True ($afterRetry.attempts -eq 1) 'El intento fallido debe registrarse una sola vez.'
$processedAgain = Invoke-Continuador @('process-due')
Assert-True ($processedAgain.processed -eq 0) 'No debe duplicarse antes de los diez minutos.'

$env:CONTINUADOR_FAKE_MODE = ''
$env:CONTINUADOR_CODEX_SESSION_ACTIVE = '0'
$inactive = Invoke-Continuador @('schedule','--client','codex','--at',([DateTimeOffset]::Now.ToString('o')),'--message','sesion inactiva','--session','codex-inactive','--cwd',$root)
$processed = Invoke-Continuador @('process-due')
$afterInactive = @(Invoke-Continuador @('list','--json')) | Where-Object { $_.id -eq $inactive.id }
Assert-True ($afterInactive.status -eq 'Entregada') 'Una sesión inactiva de Codex debe reanudarse.'
$clientCalls = Get-Content -LiteralPath $fakeLog -Raw
Assert-True ($clientCalls -match 'codex exec resume --skip-git-repo-check codex-inactive "sesion inactiva"') 'Una sesión inactiva debe omitir queue y usar exec resume.'

$statePath = Join-Path $data 'schedules.json'
$state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
$expired = $state.Schedules | Where-Object { $_.Id -eq $retry.id }
$expired.NextAttemptUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
$expired.RetryUntilUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString('o')
$state | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath $statePath -Encoding UTF8
$processedExpired = Invoke-Continuador @('process-due')
$afterExpired = @(Invoke-Continuador @('list','--json')) | Where-Object { $_.id -eq $retry.id }
Assert-True ($afterExpired.status -eq 'Fallida') 'Al terminar seis horas debe pasar a Fallida.'

$env:CONTINUADOR_FAKE_MODE = ''
$env:CONTINUADOR_CODEX_SESSION_ACTIVE = '1'
$dst = Invoke-Continuador @('schedule','--client','codex','--at','2027-11-07T01:30:00-04:00','--message','prueba dst','--session','codex-dst','--cwd',$root)
Assert-True ($dst.status -eq 'Pendiente') 'Una fecha ISO con desplazamiento horario debe aceptarse.'

$catalogRoot = Join-Path $testRoot 'catalog-user'
$env:CODEX_HOME = Join-Path $catalogRoot '.codex'
$env:CLAUDE_CONFIG_DIR = Join-Path $catalogRoot '.claude'
$codexSessions = Join-Path $env:CODEX_HOME 'sessions\2026\10\26'
$claudeProject = Join-Path $env:CLAUDE_CONFIG_DIR 'projects\test-project'
New-Item -ItemType Directory -Force -Path $codexSessions, $claudeProject | Out-Null
$codexFixture = Join-Path $codexSessions 'rollout-codex-list-1.jsonl'
$codexMeta = [ordered]@{ type='session_meta'; payload=[ordered]@{ id='codex-list-1'; session_id='codex-list-1'; cwd='C:\catalog-codex'; source='cli' } } | ConvertTo-Json -Depth 10 -Compress
$codexMessage = [ordered]@{ type='response_item'; payload=[ordered]@{ type='message'; role='user'; content=@([ordered]@{ type='input_text'; text='Conversación visible de Codex' }) } } | ConvertTo-Json -Depth 10 -Compress
[IO.File]::WriteAllLines($codexFixture, @($codexMeta,$codexMessage), (New-Object Text.UTF8Encoding($false)))
$codexIndex = [ordered]@{ id='codex-list-1'; thread_name='Título real de la barra lateral'; updated_at='2026-10-26T20:00:00Z' } | ConvertTo-Json -Compress
[IO.File]::WriteAllText((Join-Path $env:CODEX_HOME 'session_index.jsonl'), $codexIndex, (New-Object Text.UTF8Encoding($false)))
$claudeFixture = Join-Path $claudeProject 'claude-list-1.jsonl'
$claudeMessage = [ordered]@{ type='user'; sessionId='claude-list-1'; cwd='C:\catalog-claude'; message=[ordered]@{ role='user'; content='Conversación visible de Claude' } } | ConvertTo-Json -Depth 10 -Compress
[IO.File]::WriteAllText($claudeFixture, $claudeMessage, (New-Object Text.UTF8Encoding($false)))
$claudeIndex = [ordered]@{ version=1; entries=@(
    [ordered]@{ sessionId='claude-list-1'; customTitle='Nombre real de Claude Code'; summary='Resumen automático distinto'; firstPrompt='Conversación visible de Claude' },
    [ordered]@{ sessionId='claude-list-2'; summary='Resumen anterior del índice'; firstPrompt='Segundo chat de Claude' }
) } | ConvertTo-Json -Depth 10 -Compress
[IO.File]::WriteAllText((Join-Path $claudeProject 'sessions-index.json'), $claudeIndex, (New-Object Text.UTF8Encoding($false)))
$claudeLongFixture = Join-Path $claudeProject 'claude-list-2.jsonl'
$claudeLongLines = New-Object Collections.Generic.List[string]
$claudeLongLines.Add(([ordered]@{ type='user'; sessionId='claude-list-2'; cwd='C:\catalog-claude'; message=[ordered]@{ role='user'; content='Segundo chat de Claude' } } | ConvertTo-Json -Depth 10 -Compress))
1..405 | ForEach-Object { $claudeLongLines.Add('{"type":"progress"}') }
$claudeLongLines.Add(([ordered]@{ type='custom-title'; customTitle='Renombrada dentro de Claude'; sessionId='claude-list-2' } | ConvertTo-Json -Compress))
[IO.File]::WriteAllLines($claudeLongFixture, $claudeLongLines, (New-Object Text.UTF8Encoding($false)))
$catalog = & $exe sessions --json | ConvertFrom-Json
Assert-True (@($catalog | Where-Object { $_.client -eq 'codex' -and $_.session -eq 'codex-list-1' -and $_.title -eq 'Título real de la barra lateral' }).Count -eq 1) 'Debe usar el título real del índice de Codex.'
Assert-True (@($catalog | Where-Object { $_.client -eq 'claude' -and $_.session -eq 'claude-list-1' -and $_.title -eq 'Nombre real de Claude Code' }).Count -eq 1) 'Debe usar el nombre real del índice de Claude Code.'
Assert-True (@($catalog | Where-Object { $_.client -eq 'claude' -and $_.session -eq 'claude-list-2' -and $_.title -eq 'Renombrada dentro de Claude' }).Count -eq 1) 'Debe priorizar nombres asignados con /rename aunque estén al final del historial.'
$preferenceState = Get-Content -LiteralPath (Join-Path $data 'schedules.json') -Raw | ConvertFrom-Json
$preferenceState.Settings.Conversations = @([pscustomobject]@{ Key='codex|codex-list-1'; Favorite=$true; CustomName='Mi conversación favorita' })
$preferenceState | ConvertTo-Json -Depth 20 -Compress | Set-Content -LiteralPath (Join-Path $data 'schedules.json') -Encoding UTF8
$catalogWithPreference = & $exe sessions --json | ConvertFrom-Json
$favoriteConversation = $catalogWithPreference | Where-Object { $_.session -eq 'codex-list-1' }
Assert-True ($favoriteConversation.favorite -eq $true) 'Debe conservar conversaciones favoritas.'
Assert-True ($favoriteConversation.title -eq 'Mi conversación favorita') 'Debe conservar nombres personalizados.'

$portableFixture = Join-Path $testRoot 'portable'
New-Item -ItemType Directory -Force -Path $portableFixture | Out-Null
Copy-Item -LiteralPath $exe -Destination $portableFixture
Copy-Item -LiteralPath (Join-Path $root 'portable\Configurar-portable.ps1') -Destination $portableFixture
Copy-Item -LiteralPath (Join-Path $root 'skills') -Destination $portableFixture -Recurse
$portableUser = Join-Path $testRoot 'portable-user'
$env:CONTINUADOR_PORTABLE_TEST_ROOT = $portableUser
$env:CODEX_HOME = Join-Path $portableUser '.codex'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $portableFixture 'Configurar-portable.ps1') | Out-Null
Assert-True ($LASTEXITCODE -eq 0) 'La configuración portátil debe terminar correctamente.'
$portableCodexSkill = Join-Path $env:CODEX_HOME 'skills\continuar-tarea\SKILL.md'
$portableSkillText = [IO.File]::ReadAllText($portableCodexSkill)
Assert-True ($portableSkillText.StartsWith('---')) 'La skill portátil debe conservar frontmatter válido sin BOM.'
Assert-True ($portableSkillText.Contains((Join-Path $portableFixture 'Continuador.exe'))) 'La skill portátil debe apuntar a su ejecutable real.'

$installRoot = Join-Path $testRoot 'fake-user'
$codexHome = Join-Path $installRoot '.codex'
$oldCodexSkill = Join-Path $codexHome 'skills\continuar-tarea'
New-Item -ItemType Directory -Force -Path $oldCodexSkill | Out-Null
Set-Content -LiteralPath (Join-Path $oldCodexSkill 'SKILL.md') -Value 'skill anterior' -Encoding UTF8
$env:CONTINUADOR_INSTALL_TEST_MODE = '1'
$env:CONTINUADOR_INSTALL_TEST_ROOT = $installRoot
$env:CODEX_HOME = $codexHome
$installProcess = Start-Process -FilePath $setup -ArgumentList '/install-silent' -Wait -PassThru
Assert-True ($installProcess.ExitCode -eq 0) 'El instalador silencioso debe terminar correctamente.'
$installedDir = Join-Path $installRoot 'AppData\Local\Programs\Continuador'
Assert-True (Test-Path -LiteralPath (Join-Path $installedDir 'Continuador.exe')) 'El instalador debe desplegar la aplicación.'
Assert-True (Test-Path -LiteralPath (Join-Path $codexHome 'skills\continuar-tarea\SKILL.md')) 'Debe instalar la skill de Codex.'
Assert-True (Test-Path -LiteralPath (Join-Path $installRoot '.claude\skills\continuar-tarea\SKILL.md')) 'Debe instalar la skill de Claude.'
$backup = Get-ChildItem -LiteralPath (Join-Path $codexHome 'skills') -Directory -Filter 'continuar-tarea.backup-*'
Assert-True (@($backup).Count -eq 1) 'Debe respaldar la skill preexistente.'
$upgradeProcess = Start-Process -FilePath $setup -ArgumentList '/install-silent' -Wait -PassThru
Assert-True ($upgradeProcess.ExitCode -eq 0) 'Una actualización sobre la versión instalada debe terminar correctamente.'
$backupAfterUpgrade = Get-ChildItem -LiteralPath (Join-Path $codexHome 'skills') -Directory -Filter 'continuar-tarea.backup-*'
Assert-True (@($backupAfterUpgrade).Count -eq 1) 'Actualizar no debe convertir la skill anterior de Continuador en otra copia de seguridad.'

$uninstaller = Join-Path $installedDir 'Desinstalar.exe'
$uninstallProcess = Start-Process -FilePath $uninstaller -ArgumentList '/uninstall-silent' -Wait -PassThru
Assert-True ($uninstallProcess.ExitCode -eq 0) 'El desinstalador silencioso debe terminar correctamente.'
Start-Sleep -Seconds 4
Assert-True (-not (Test-Path -LiteralPath $installedDir)) 'El desinstalador debe quitar la carpeta del programa.'
$restored = Get-Content -LiteralPath (Join-Path $oldCodexSkill 'SKILL.md') -Raw
Assert-True ($restored -match 'skill anterior') 'El desinstalador debe restaurar la skill anterior.'

Write-Host 'OK: todas las pruebas de Continuador pasaron.'
