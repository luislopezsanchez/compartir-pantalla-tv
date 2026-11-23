@echo off
rem Compila CompartirPantalla.exe con el compilador de C# incluido en Windows (.NET Framework 4).
rem Uso: build.cmd [ruta_salida.exe]   (por defecto, junto a este archivo)
set "OUT=%~1"
if "%OUT%"=="" set "OUT=%~dp0CompartirPantalla.exe"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /unsafe /platform:x64 ^
  /win32icon:"%~dp0icon.ico" /out:"%OUT%" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0Program.cs" "%~dp0Live.cs" "%~dp0Dxgi.cs"

