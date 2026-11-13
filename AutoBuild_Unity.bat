@echo off
setlocal
cd /d "%~dp0"
if not defined VRESQ_UNITY_EDITOR set "VRESQ_UNITY_EDITOR=E:\unity\6000.3.22f1\Editor\Unity.exe"
if not exist "%VRESQ_UNITY_EDITOR%" (
  echo Unity Editor not found. Set VRESQ_UNITY_EDITOR to your Unity.exe path.
  exit /b 1
)
if not exist "%~dp0Logs" mkdir "%~dp0Logs"
echo Building V-ResQ Windows Operator. Close this project in Unity before running this BAT.
start "" /wait "%VRESQ_UNITY_EDITOR%" -batchmode -force-d3d11 -quit -projectPath "%~dp0." -executeMethod VResqAutoSetup.BuildWindows -logFile "%~dp0Logs\autobuild.log"
if errorlevel 1 (
  echo Build failed. See Logs\autobuild.log.
  exit /b 1
)
echo Build ready: %~dp0Builds\Windows\VResQ-Operator.exe
exit /b 0
