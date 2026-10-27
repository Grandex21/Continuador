---
name: continuar-tarea
description: Programa, cambia o cancela una continuación automática de la sesión actual de Claude Code. Úsala cuando el usuario pida continuar, reanudar o enviar un mensaje a esta misma sesión en una fecha u hora futura.
shell: powershell
---

# Continuar tarea

Usa la aplicación local Continuador y el identificador de esta sesión: `${CLAUDE_SESSION_ID}`.

## Programar

1. Convierte la fecha y hora solicitadas a ISO 8601 con desplazamiento UTC, usando la zona horaria local salvo indicación contraria. Si el usuario da solo una hora, usa hoy si no ha pasado; de lo contrario, mañana.
2. Ejecuta en PowerShell:

   `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" schedule --client claude --session "${CLAUDE_SESSION_ID}" --at "<ISO-8601>" --message "Continúa la tarea pendiente desde donde te quedaste." --cwd "$($PWD.Path)"`

3. Lee el JSON devuelto y confirma la hora local, la zona horaria y el identificador.

La petición explícita de programar autoriza este comando. No pidas otra confirmación cuando la fecha y hora sean inequívocas.

## Administrar

- Lista: `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" list --json`
- Cambia la hora: `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" update --id "<id>" --at "<ISO-8601>"`
- Cancela: `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" cancel --id "<id>"`

Al administrar, elige la continuación cuyo campo `session` coincida con `${CLAUDE_SESSION_ID}`. Si hay varias, pide que el usuario elija. Nunca afirmes que se programó si el ejecutable devolvió un error.
