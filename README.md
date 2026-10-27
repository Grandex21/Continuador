# Continuador

Aplicación local para Windows 10/11 que programa mensajes de continuación en
Codex Desktop, Codex CLI y Claude Code.

## Descargar

### [Descargar el instalador para Windows](https://github.com/Grandex21/Continuador/releases/latest/download/Continuador-Setup.exe)

También puedes usar la [versión portátil](https://github.com/Grandex21/Continuador/releases/latest/download/Continuador-portable.zip), que no necesita instalación.

Consulta [todas las versiones y archivos publicados](https://github.com/Grandex21/Continuador/releases/latest). Windows puede mostrar una advertencia de SmartScreen porque la aplicación todavía no tiene una firma comercial.

La versión 1.4.0 carga las conversaciones en segundo plano para mantener la interfaz fluida,
mejora la detección y el respaldo por ventana de Claude Code y comprueba automáticamente
si hay actualizaciones al iniciar y cada hora. También permite buscarlas manualmente desde
la aplicación. El procesamiento periódico usa un worker `WinExe` independiente para que
el Programador de tareas no abra una ventana de consola cada diez minutos.

## Componentes

- `src/`: aplicación WinForms, bandeja, CLI, worker silencioso, persistencia y adaptadores.
- `installer/`: instalador/desinstalador por usuario con recursos incrustados.
- `skills/`: integraciones personales para Codex y Claude Code.
- `portable/`: configuración de la edición portátil.
- `tests.ps1`: pruebas aisladas con dobles de `codex.exe` y `claude.exe`, incluida una
  ejecución real del worker mediante el Programador de tareas.

## Construcción

En Windows con .NET Framework 4.8:

```powershell
.\build.ps1
.\tests.ps1
.\package.ps1
```

No se descargan dependencias. Los binarios se compilan con el `csc.exe`
incluido en Windows y el instalador no requiere privilegios de administrador.

## Datos locales

La aplicación guarda `schedules.json` y `continuador.log` en
`%LOCALAPPDATA%\Continuador`. No guarda credenciales.
