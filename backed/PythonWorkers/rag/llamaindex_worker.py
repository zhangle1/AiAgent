import runpy
import sys
from pathlib import Path


def main():
    current = Path(__file__).resolve()
    backend_root = current.parents[2]
    legacy_worker = backend_root / "Rag" / "llamaindex_worker.py"
    if not legacy_worker.exists():
        sys.stdout.write(
            '{"ok":false,"provider":"llamaindex","error":{"code":"worker_missing","message":"Legacy LlamaIndex worker was not found."}}'
        )
        sys.exit(1)

    runpy.run_path(str(legacy_worker), run_name="__main__")


if __name__ == "__main__":
    main()
