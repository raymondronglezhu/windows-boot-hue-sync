from __future__ import annotations

import atexit
from datetime import datetime
from pathlib import Path
import signal
import subprocess
import sys
import threading
import time


ROOT = Path(__file__).resolve().parents[1]
LOG_DIR = ROOT / ".hue-agent"
LOG_PATH = LOG_DIR / "power-watcher.log"
HUE_SCRIPT = ROOT / "hue_agent" / "__main__.py"


class HuePowerWatcher:
    def __init__(self) -> None:
        self._shutdown_started = False
        self._lock = threading.Lock()

    def log(self, message: str) -> None:
        LOG_DIR.mkdir(parents=True, exist_ok=True)
        timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
        with LOG_PATH.open("a", encoding="utf-8") as handle:
            handle.write(f"[{timestamp}] {message}\n")

    def run_hue(self, *args: str) -> subprocess.CompletedProcess[str]:
        command = [sys.executable, "-m", "hue_agent", *args]
        self.log(f"Running: {' '.join(command)}")
        completed = subprocess.run(
            command,
            cwd=str(ROOT),
            text=True,
            capture_output=True,
        )
        if completed.stdout.strip():
            self.log(f"stdout: {completed.stdout.strip()}")
        if completed.stderr.strip():
            self.log(f"stderr: {completed.stderr.strip()}")
        self.log(f"exit_code: {completed.returncode}")
        return completed

    def turn_lights_on(self) -> None:
        self.log("Startup detected, turning Hue lights on")
        self.run_hue("all-on")

    def turn_lights_off(self, reason: str) -> None:
        with self._lock:
            if self._shutdown_started:
                return
            self._shutdown_started = True

        self.log(f"Shutdown detected ({reason}), turning Hue lights off")
        self.run_hue("all-off")

    def install_signal_handlers(self) -> None:
        def handle_signal(signum: int, _frame) -> None:
            self.turn_lights_off(f"signal {signum}")
            raise SystemExit(0)

        for sig_name in ("SIGINT", "SIGTERM", "SIGBREAK"):
            sig = getattr(signal, sig_name, None)
            if sig is not None:
                signal.signal(sig, handle_signal)

        atexit.register(lambda: self.turn_lights_off("process exit"))

    def main(self) -> int:
        self.install_signal_handlers()

        # Give Windows networking and the Hue bridge a moment to settle after login/startup.
        time.sleep(10)
        self.turn_lights_on()
        self.log("Watcher is now idle and waiting for shutdown/logoff")

        while True:
            time.sleep(60)


def main() -> int:
    watcher = HuePowerWatcher()
    return watcher.main()


if __name__ == "__main__":
    raise SystemExit(main())

