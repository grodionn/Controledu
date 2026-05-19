# Controledu Windows deployment checklist

This checklist targets school lab deployments where setup is run by an administrator
but students sign in with standard, guest, shared, or mandatory-profile accounts.

## Recommended student deployment

1. Install `Controledu Student` with administrator rights.
2. Keep `Start Controledu Student when any user signs in` enabled.
   This writes a machine-wide Windows `Run` entry so the host starts for the
   actual interactive student account, not for the administrator who installed it.
3. Keep `Create diagnostic logs and allow diagnostics export` enabled while piloting.
4. Leave `Advanced: install Student.Agent as Windows Service` disabled unless you
   are testing a service-only scenario. Screen capture, overlay, and input features
   require the agent to run in the interactive user session.
5. Pair the endpoint from the student/guest Windows session that will be used in class.

## Teacher firewall

The teacher installer creates inbound Windows Firewall rules for:

- UDP `40555` discovery
- TCP `40556` teacher server

Rules are limited to `localsubnet` but use `profile=any`, because school machines
may be on Domain, Private, or Public profiles depending on policy.

## Data and logs

Controledu prefers `%ProgramData%\Controledu` for shared data. The installer grants
standard users modify access to this application directory. If that path is not
writable at runtime, the app falls back to `%LocalAppData%\Controledu` for the
current user.

Diagnostic logs are controlled by:

- `%ProgramData%\Controledu\diagnostics.enabled`
- `CONTROLEDU_DIAGNOSTIC_LOGS=true|false`

Runtime logs are written under `logs`, and diagnostics archives are written under
`exports`. Teacher-requested diagnostics archives include logs and runtime metadata;
dataset samples are included only if data collection was separately enabled.

## Quick verification

On the student machine, after a student signs in:

```bat
reg query "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v ControleduStudentHost
dir "%ProgramData%\Controledu\logs"
netstat -ano | findstr 40557
```

On the teacher machine:

```bat
netsh advfirewall firewall show rule name="Controledu Teacher Server TCP 40556"
netsh advfirewall firewall show rule name="Controledu Teacher Discovery UDP 40555"
netstat -ano | findstr 40556
```

From a student machine, the teacher server should respond:

```bat
powershell -NoProfile -Command "Invoke-WebRequest http://<teacher-ip>:40556/api/server/health -UseBasicParsing"
```

