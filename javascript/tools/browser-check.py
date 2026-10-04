"""Run the native ESM package in a real browser against pinned .NET result digests."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
import json
import os
import shutil
import subprocess
import threading
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parent.parent

class QuietHandler(SimpleHTTPRequestHandler):
    def log_message(self, *args):
        pass

server = ThreadingHTTPServer(("127.0.0.1", 0), partial(QuietHandler, directory=str(ROOT)))
thread = threading.Thread(target=server.serve_forever, daemon=True)
thread.start()
report = {"completed": False}
try:
    with sync_playwright() as playwright:
        executable = os.environ.get("CHROMIUM") or shutil.which("chromium")
        browser = playwright.chromium.launch(headless=True, **({"executable_path": executable} if executable else {}))
        try:
            errors = []
            page = browser.new_page()
            page.on("pageerror", lambda error: errors.append(str(error)))
            page.goto(f"http://127.0.0.1:{server.server_port}/tests/browser/index.html")
            page.wait_for_function("window.testResults !== undefined", timeout=300000)
            report = page.evaluate("window.testResults")
            report["browser"] = browser.version
            report["pageErrors"] = errors
            proof = json.loads(subprocess.check_output([
                os.environ.get("NODE", "node"), "--input-type=module", "-e",
                "import {runtimeFingerprint,verificationFingerprint} from './tools/evidence.mjs'; "
                "console.log(JSON.stringify({runtimeFingerprint:runtimeFingerprint(),verificationFingerprint:verificationFingerprint()}));"
            ], cwd=ROOT, text=True))
            if errors or any(report.get(key) != value for key, value in proof.items()):
                report["completed"] = False
                report["fatal"] = "Browser error or source/verifier drift during execution."
        finally:
            browser.close()
except Exception as error:
    report["completed"] = False
    report["fatal"] = repr(error)
finally:
    server.shutdown()
    server.server_close()
    output = ROOT / "artifacts/browser/results.json"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
if not report.get("completed") or report.get("failures") or report.get("fatal"):
    raise SystemExit("Browser differential verification failed.")
