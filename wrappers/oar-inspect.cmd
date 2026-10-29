@echo off
rem Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
rem oar-inspect is the old name of "oar-tools inspect". It still works in this release and passes its arguments through.
setlocal
set "OAR_TOOLS_LEGACY_NAME=oar-inspect"
call "%~dp0oar-tools" inspect %*
exit /b %ERRORLEVEL%
