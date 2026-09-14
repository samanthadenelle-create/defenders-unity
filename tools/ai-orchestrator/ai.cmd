@echo off
REM `ai run "<request>"` (WO-1706, spec section 18).
REM Uses the tool's own venv so the caller needs nothing on PATH but this file.
setlocal
set "AI_HOME=%~dp0"
if exist "%AI_HOME%.venv\Scripts\python.exe" (
  set "AI_PY=%AI_HOME%.venv\Scripts\python.exe"
) else (
  set "AI_PY=python"
)
pushd "%AI_HOME%"
"%AI_PY%" -m ai_orchestrator %*
set "AI_RC=%ERRORLEVEL%"
popd
endlocal & exit /b %AI_RC%
