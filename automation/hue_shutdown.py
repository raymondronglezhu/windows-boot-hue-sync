from __future__ import annotations

from datetime import datetime
from pathlib import Path
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parents[1]
LOG_DIR = ROOT / ".hue-agent"
LOG_PATH = LOG_DIR / "power-hooks.log"
ROOM_NAME = "Living room"
MAX_ATTEMPTS = 4
RETRY_DELAY_SECONDS = 2
COMMAND_TIMEOUT_SECONDS = 15


def log(message: str) -> None:
    LOG_DIR.mkdir(parents=True, exist_ok=True)
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    with LOG_PATH.open("a", encoding="utf-8") as handle:
        handle.write(f"[{timestamp}] [shutdown] {message}\n")


def run_hue(*args: str) -> subprocess.CompletedProcess[str]:
    command = [sys.executable, "-m", "hue_agent", *args]
    log(f"Running: {' '.join(command)}")
    completed = subprocess.run(
        command,
        cwd=str(ROOT),
        text=True,
        capture_output=True,
        timeout=COMMAND_TIMEOUT_SECONDS,
    )
    if completed.stdout.strip():
        log(f"stdout: {completed.stdout.strip()}")
    if completed.stderr.strip():
        log(f"stderr: {completed.stderr.strip()}")
    log(f"exit_code: {completed.returncode}")
    return completed


def main() -> int:
    for attempt in range(1, MAX_ATTEMPTS + 1):
        log(f"Shutdown attempt {attempt}/{MAX_ATTEMPTS}")
        result = run_hue("room-off", ROOM_NAME)
        if result.returncode == 0:
            log("Shutdown room-off succeeded")
            return 0

        if attempt < MAX_ATTEMPTS:
            time.sleep(RETRY_DELAY_SECONDS)

    log("Shutdown room-off failed after all attempts")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
