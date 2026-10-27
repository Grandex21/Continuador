@echo off
echo codex %*>>"%CONTINUADOR_FAKE_LOG%"
if /I "%CONTINUADOR_FAKE_MODE%"=="rate-limit" (
  echo 429 rate limit: credits unavailable 1>&2
  exit /b 29
)
if /I "%CONTINUADOR_FAKE_MODE%"=="queue-fails" (
  if /I "%1"=="queue" (
    echo queue unavailable 1>&2
    exit /b 7
  )
)
echo ok
exit /b 0
