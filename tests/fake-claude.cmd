@echo off
echo claude %*>>"%CONTINUADOR_FAKE_LOG%"
if /I "%CONTINUADOR_FAKE_MODE%"=="rate-limit" (
  echo usage limit reached 1>&2
  exit /b 29
)
echo ok
exit /b 0
