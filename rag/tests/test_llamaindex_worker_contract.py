import json
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
WORKER = ROOT / "llamaindex_worker.py"


def run_worker(payload):
    completed = subprocess.run(
        [sys.executable, str(WORKER)],
        input=json.dumps(payload),
        text=True,
        capture_output=True,
        check=False,
    )
    assert completed.stdout.strip(), completed.stderr
    return completed.returncode, json.loads(completed.stdout)


def test_preflight_returns_structured_status():
    code, response = run_worker({"command": "preflight"})

    assert code == 0
    assert response["ok"] in (True, False)
    assert response["provider"] == "llamaindex"
    assert isinstance(response["dependencies"], dict)


def test_unknown_command_returns_structured_error():
    code, response = run_worker({"command": "does_not_exist"})

    assert code == 2
    assert response["ok"] is False
    assert response["error"]["code"] == "unknown_command"


if __name__ == "__main__":
    test_preflight_returns_structured_status()
    test_unknown_command_returns_structured_error()
    print("llamaindex worker contract tests passed")
