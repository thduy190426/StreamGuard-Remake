"""Build-time only: package the verified, pinned local runtime, retaining license metadata."""
import json
import os
from pathlib import Path
import zipfile
import importlib.metadata as metadata

root = Path(os.environ["LOCALAPPDATA"]) / "StreamGuardBuildTools" / "captions-package"
target = Path(__file__).resolve().parents[2] / "Captions" / "Runtime.zip"
packages = sorted(({"name": item.metadata["Name"], "version": item.version} for item in metadata.distributions(path=[str(root / "Lib" / "site-packages")])), key=lambda item: item["name"].lower())
(target.parent / "runtime-packages.json").write_text(json.dumps(packages, indent=2), encoding="utf-8")
with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for file in sorted(root.rglob("*")):
        if not file.is_file() or any(part in ("__pycache__", ".cache") for part in file.parts) or file.suffix == ".pyc":
            continue
        archive.write(file, file.relative_to(root).as_posix())
print(f"Packaged caption runtime: {target.stat().st_size:,} bytes")
