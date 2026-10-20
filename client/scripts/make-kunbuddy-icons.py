#!/usr/bin/env python3
"""Build platform icon formats from the unchanged KunBuddy source mark."""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
BUILD = ROOT / "apps" / "desktop" / "build"
BRAND = ROOT / "apps" / "desktop" / "src" / "assets" / "brand"
SOURCE = BRAND / "kunbuddy.png"

def main() -> None:
    BUILD.mkdir(parents=True, exist_ok=True)
    with Image.open(SOURCE) as source:
        master = source.convert("RGBA").resize((1024, 1024), Image.Resampling.LANCZOS)
    master.save(BUILD / "icon_1024.png")
    master.resize((512, 512), Image.Resampling.LANCZOS).save(BUILD / "icon.png")
    windows_icon = BUILD / "icon.ico"
    master.save(windows_icon, format="ICO", sizes=[(16,16),(32,32),(48,48),(64,64),(128,128),(256,256)])
    master.save(BUILD / "icon.icns", format="ICNS")
    tray = Image.new("RGBA", master.size)
    tray.putalpha(master.getchannel("A"))
    tray.save(BUILD / "tray-icon-mac.png")
    for theme in ("light", "dark"):
        master.resize((128,128), Image.Resampling.LANCZOS).save(BRAND / f"logo-{theme}.png")
    print("Generated KunBuddy Windows/macOS/Linux icons from the preserved source mark")

if __name__ == "__main__":
    main()
