@echo off
rem Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
rem oar-convert is the old name of "oar-tools convert". It still works in this release and passes its arguments through.
setlocal
set "OAR_TOOLS_LEGACY_NAME=oar-convert"
call "%~dp0oar-tools" convert %*
exit /b %ERRORLEVEL%
