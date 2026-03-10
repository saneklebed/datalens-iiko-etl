@echo off
cd /d %~dp0

echo ==== TG SENDER START ====
echo Current folder: %cd%
echo.

where python
echo.

python --version
echo.

python send.py

echo.
echo ==== TG SENDER END ====
pause
