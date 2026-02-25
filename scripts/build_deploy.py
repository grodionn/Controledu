import argparse
import getpass
import json
import os
import subprocess
import sys
from datetime import datetime
from pathlib import Path
from urllib.parse import urlparse


def log(message: str, file_handle=None) -> None:
    line = message.rstrip()
    print(line)
    if file_handle is not None:
        file_handle.write(line + "\n")
        file_handle.flush()


def require_paramiko():
    try:
        import paramiko  # type: ignore
    except Exception as ex:
        raise RuntimeError(
            "Python package 'paramiko' is required. Install it with: pip install paramiko"
        ) from ex
    return paramiko


def run_build(repo_root: Path, log_file) -> None:
    build_cmd = str(repo_root / "scripts" / "build.cmd")
    log(f"> {build_cmd}", log_file)
    completed = subprocess.run([build_cmd], cwd=repo_root, shell=True)
    if completed.returncode != 0:
        raise RuntimeError(f"Build failed with exit code {completed.returncode}")


def load_manifest(manifest_path: Path) -> dict:
    # PowerShell may write UTF-8 files with BOM on Windows.
    with manifest_path.open("r", encoding="utf-8-sig") as fh:
        return json.load(fh)


def installer_name_from_manifest(manifest: dict, manifest_path: Path) -> str:
    installer_url = (manifest.get("installerUrl") or "").strip()
    if not installer_url:
        raise RuntimeError(f"installerUrl is missing in manifest: {manifest_path}")
    parsed = urlparse(installer_url)
    name = Path(parsed.path).name
    if not name:
        raise RuntimeError(f"Failed to extract installer file name from installerUrl in {manifest_path}")
    return name


def sftp_mkdir_p(sftp, remote_dir: str) -> None:
    remote_dir = remote_dir.replace("\\", "/").rstrip("/")
    if not remote_dir:
        return
    parts = remote_dir.split("/")
    current = ""
    if remote_dir.startswith("/"):
        current = "/"
    for part in parts:
        if not part:
            continue
        if current in ("", "/"):
            current = f"{current}{part}" if current == "/" else part
        else:
            current = f"{current}/{part}"
        try:
            sftp.stat(current)
        except Exception:
            sftp.mkdir(current)


def sftp_put_file(sftp, local_path: Path, remote_path: str, log_file) -> None:
    remote_path = remote_path.replace("\\", "/")
    log(f"> put {local_path} -> {remote_path}", log_file)
    sftp.put(str(local_path), remote_path)


def main() -> int:
    parser = argparse.ArgumentParser(description="Build and deploy Controledu updates via SSH/SFTP using password auth.")
    parser.add_argument("--host", default="51.195.91.55")
    parser.add_argument("--port", type=int, default=22)
    parser.add_argument("--user", default="ubuntu")
    parser.add_argument("--password", default="", help="If omitted, script will ask securely.")
    parser.add_argument("--build", action="store_true", help="Run scripts/build.cmd before deploy.")
    parser.add_argument("--clean", action="store_true", help="Remove remote files under target directories before upload.")
    parser.add_argument("--with-installers", action="store_true", help="Also upload artifacts/installers/* to /installers.")
    parser.add_argument("--updates-path", default="/var/www/controledu/updates")
    parser.add_argument("--installers-path", default="/var/www/controledu/installers")
    parser.add_argument("--artifacts-root", default="artifacts")
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parents[1]
    artifacts_root = (repo_root / args.artifacts_root).resolve()
    logs_dir = artifacts_root / "logs"
    logs_dir.mkdir(parents=True, exist_ok=True)
    log_path = logs_dir / "deploy-updates-python.log"

    with log_path.open("w", encoding="utf-8") as log_file:
        log(f"[deploy] started at {datetime.now().isoformat(sep=' ', timespec='seconds')}", log_file)
        log(f"[deploy] log file: {log_path}", log_file)

        try:
            if args.build:
                run_build(repo_root, log_file)

            updates_local = artifacts_root / "updates"
            teacher_manifest_path = updates_local / "teacher" / "manifest.json"
            student_manifest_path = updates_local / "student" / "manifest.json"

            if not teacher_manifest_path.exists():
                raise RuntimeError(f"Missing {teacher_manifest_path}. Run scripts/build.cmd first.")
            if not student_manifest_path.exists():
                raise RuntimeError(f"Missing {student_manifest_path}. Run scripts/build.cmd first.")

            teacher_manifest = load_manifest(teacher_manifest_path)
            student_manifest = load_manifest(student_manifest_path)
            teacher_installer_name = installer_name_from_manifest(teacher_manifest, teacher_manifest_path)
            student_installer_name = installer_name_from_manifest(student_manifest, student_manifest_path)

            teacher_installer_local = updates_local / "teacher" / teacher_installer_name
            student_installer_local = updates_local / "student" / student_installer_name
            if not teacher_installer_local.exists():
                raise RuntimeError(f"Missing installer referenced by teacher manifest: {teacher_installer_local}")
            if not student_installer_local.exists():
                raise RuntimeError(f"Missing installer referenced by student manifest: {student_installer_local}")

            installers_local = artifacts_root / "installers"
            if args.with_installers and not installers_local.exists():
                raise RuntimeError(f"Installers folder not found: {installers_local}")

            password = args.password or getpass.getpass(f"Password for {args.user}@{args.host}: ")
            paramiko = require_paramiko()

            log(f"> ssh connect {args.user}@{args.host}:{args.port}", log_file)
            ssh = paramiko.SSHClient()
            ssh.set_missing_host_key_policy(paramiko.AutoAddPolicy())
            ssh.connect(
                hostname=args.host,
                port=args.port,
                username=args.user,
                password=password,
                look_for_keys=False,
                allow_agent=False,
                timeout=30,
            )
            try:
                if args.clean:
                    updates_q = "'" + args.updates_path.replace("'", "'\"'\"'") + "'"
                    cmd = f"rm -rf {updates_q}/*"
                    if args.with_installers:
                        installers_q = "'" + args.installers_path.replace("'", "'\"'\"'") + "'"
                        cmd += f" && rm -rf {installers_q}/*"
                    log(f"> {cmd}", log_file)
                    stdin, stdout, stderr = ssh.exec_command(cmd)
                    exit_code = stdout.channel.recv_exit_status()
                    err_text = stderr.read().decode("utf-8", errors="replace").strip()
                    if exit_code != 0:
                        raise RuntimeError(f"Remote clean failed ({exit_code}): {err_text or 'unknown error'}")

                sftp = ssh.open_sftp()
                try:
                    updates_base = args.updates_path.rstrip("/")
                    teacher_remote_dir = f"{updates_base}/teacher"
                    student_remote_dir = f"{updates_base}/student"
                    sftp_mkdir_p(sftp, teacher_remote_dir)
                    sftp_mkdir_p(sftp, student_remote_dir)
                    if args.with_installers:
                        sftp_mkdir_p(sftp, args.installers_path.rstrip("/"))

                    sftp_put_file(sftp, teacher_manifest_path, f"{teacher_remote_dir}/manifest.json", log_file)
                    sftp_put_file(sftp, teacher_installer_local, f"{teacher_remote_dir}/{teacher_installer_name}", log_file)
                    sftp_put_file(sftp, student_manifest_path, f"{student_remote_dir}/manifest.json", log_file)
                    sftp_put_file(sftp, student_installer_local, f"{student_remote_dir}/{student_installer_name}", log_file)

                    if args.with_installers:
                        files = [p for p in installers_local.iterdir() if p.is_file()]
                        if not files:
                            raise RuntimeError(f"No files found in installers folder: {installers_local}")
                        installers_remote = args.installers_path.rstrip("/")
                        for p in files:
                            sftp_put_file(sftp, p, f"{installers_remote}/{p.name}", log_file)
                finally:
                    sftp.close()
            finally:
                ssh.close()

            log("[deploy] completed successfully", log_file)
            return 0
        except Exception as ex:
            log(f"[deploy] FAILED: {ex}", log_file)
            return 1


if __name__ == "__main__":
    sys.exit(main())
