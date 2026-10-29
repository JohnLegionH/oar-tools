@echo off
rem Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
rem oar-export is the old name of "oar-tools export". It still works in this release and passes its arguments through.
setlocal
set "OAR_TOOLS_LEGACY_NAME=oar-export"
call "%~dp0oar-tools" export %*
exit /b %ERRORLEVEL%
