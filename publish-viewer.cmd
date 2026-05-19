@echo off
setlocal

echo Publishing DeckMark.Viewer...
dotnet publish .\DeckMark.Viewer\DeckMark.Viewer.csproj /p:PublishProfile=Win-x64
if errorlevel 1 exit /b %errorlevel%

echo.
echo Output:
echo   ..\DeckMark-publish\DeckMark.Viewer\win-x64\
echo.
echo Run:
echo   ..\DeckMark-publish\DeckMark.Viewer\win-x64\DeckMark.Viewer.exe
