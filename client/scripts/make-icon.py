#!/usr/bin/env python3
"""Generate KunBuddy platform icons from the canonical AiAgent mark."""
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name("make-kunbuddy-icons.py")), run_name="__main__")
