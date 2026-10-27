---
name: continuar-tarea
description: Programa, cambia o cancela una continuación automática de la tarea actual en Codex. Úsala cuando el usuario pida continuar, reanudar o enviar un mensaje a este mismo chat en una fecha u hora futura.
---

# Continuar tarea

Usa la aplicación local Continuador. No crees una automatización interna de Codex para estas solicitudes.

## Programar

1. Convierte la fecha y hora solicitadas a ISO 8601 con desplazamiento UTC, usando la zona horaria del cliente salvo indicación contraria. Si el usuario da solo una hora, usa hoy si no ha pasado; de lo contrario, mañana.
2. Ejecuta en PowerShell:

   `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" schedule --client codex --at "<ISO-8601>" --message "Continúa la tarea pendiente desde donde te quedaste." --cwd "$($PWD.Path)"`

3. Lee el JSON devuelto. Confirma el horario local, la zona horaria y el identificador. Si devuelve varias sesiones candidatas, muéstralas y pide al usuario que elija; no programes una sesión distinta por conjetura.

La petición explícita de programar autoriza este comando. No pidas otra confirmación cuando la fecha y hora sean inequívocas.

## Administrar

- Lista: `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" list --json`
- Cambia la hora: `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" update --id "<id>" --at "<ISO-8601>"`
- Cancela: `& "$env:USERPROFILE\AppData\Local\Programs\Continuador\Continuador.exe" cancel --id "<id>"`

Si hay varias continuaciones pendientes, pide que el usuario elija una. Nunca afirmes que se programó si el ejecutable devolvió un error.
