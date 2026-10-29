@echo off
rem Copyright (c) 2026 Legion Builds. BSD-3-Clause, see LICENSE.
rem oar-restore is the old name of "oar-tools restore". It still works in this release and passes its arguments through.
setlocal
set "OAR_TOOLS_LEGACY_NAME=oar-restore"
call "%~dp0oar-tools" restore %*
exit /b %ERRORLEVEL%
