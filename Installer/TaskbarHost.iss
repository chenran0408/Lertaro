// Called only after Restart Manager has closed the main application. The taskbar supervisor
// is allowed to restore Windows appearance before any installed file is replaced.
procedure RunTaskbarHostWait(ScriptPath: String);
var
  ExitCode: Integer;
begin
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' + AddQuotes(ScriptPath) +
    ' -WaitTaskbarHost -Destination ' + AddQuotes(ExpandConstant('{app}')),
    ExpandConstant('{tmp}'), SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    RaiseException(CustomMessage('TaskbarCloseFailed'));
  if ExitCode <> 0 then RaiseException(CustomMessage('TaskbarCloseFailed'));
end;

procedure WaitForTaskbarHost;
begin
  if TaskbarHostWaited then exit;
  ExtractTemporaryFile('portable-updater.ps1');
  RunTaskbarHostWait(ExpandConstant('{tmp}\portable-updater.ps1'));
  TaskbarHostWaited := True;
end;

procedure WaitForInstalledTaskbarHost;
begin
  if FileExists(ExpandConstant('{app}\portable-updater.ps1')) then
    RunTaskbarHostWait(ExpandConstant('{app}\portable-updater.ps1'));
end;
