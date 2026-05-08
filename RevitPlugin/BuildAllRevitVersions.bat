@echo off
echo === Compilando Revit Plugin para TODAS las versiones ===

:: Ruta al proyecto
set PROJECT=RevitPlugin.csproj

:: Revit 2020-2021 (net47)
echo -- Compilando para Revit 2020 (net47)...
dotnet build %PROJECT% -c Release -f net47 /p:DefineConstants="REVIT2020" /p:OutputPath=bin\Revit2020

echo -- Compilando para Revit 2021 (net47)...
dotnet build %PROJECT% -c Release -f net47 /p:DefineConstants="REVIT2021" /p:OutputPath=bin\Revit2021

:: Revit 2022-2025 (net48)
echo -- Compilando para Revit 2022 (net48)...
dotnet build %PROJECT% -c Release -f net48 /p:DefineConstants="REVIT2022" /p:OutputPath=bin\Revit2022

echo -- Compilando para Revit 2023 (net48)...
dotnet build %PROJECT% -c Release -f net48 /p:DefineConstants="REVIT2023" /p:OutputPath=bin\Revit2023

echo -- Compilando para Revit 2024 (net48)...
dotnet build %PROJECT% -c Release -f net48 /p:DefineConstants="REVIT2024" /p:OutputPath=bin\Revit2024

echo -- Compilando para Revit 2025 (net48)...
dotnet build %PROJECT% -c Release -f net48 /p:DefineConstants="REVIT2025" /p:OutputPath=bin\Revit2025

echo === Compilación finalizada ===
pause
