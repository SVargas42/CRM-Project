#!/usr/bin/env python3
"""Wrap a self-contained osx-arm64 publish directory as a local .app bundle."""

import argparse
from pathlib import Path
import plistlib
import shutil
import sys


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("publish", nargs="?", default="artifacts/osx-arm64",
                        help="self-contained osx-arm64 publish directory")
    parser.add_argument("bundle", nargs="?", default="artifacts/HippoCrm.app",
                        help="destination .app bundle")
    args = parser.parse_args()

    try:
        root = Path(__file__).resolve().parent.parent
        publish = (root / args.publish).resolve(strict=True)
        bundle = (root / args.bundle).resolve()
        executable = publish / "HippoCrm"
        if not publish.is_dir() or not executable.is_file():
            raise ValueError(f"Not a published HippoCrm apphost directory: {publish}")
        if not executable.stat().st_mode & 0o111:
            raise ValueError(f"The macOS apphost is not executable: {executable}")
        if bundle.suffix != ".app" or not bundle.is_relative_to(root / "artifacts"):
            raise ValueError("The app bundle destination must be a .app inside artifacts/.")

        contents = bundle / "Contents"
        macos = contents / "MacOS"
        macos.mkdir(parents=True, exist_ok=True)
        shutil.copytree(publish, macos, dirs_exist_ok=True, copy_function=shutil.copy2)

        info = {
            "CFBundleDevelopmentRegion": "en",
            "CFBundleExecutable": "HippoCrm",
            "CFBundleIdentifier": "com.bigeaglegroup.hippocrm",
            "CFBundleInfoDictionaryVersion": "6.0",
            "CFBundleName": "HippoCrm",
            "CFBundlePackageType": "APPL",
            "CFBundleShortVersionString": "1.0",
            "CFBundleVersion": "1",
            "LSApplicationCategoryType": "public.app-category.business",
            "NSHighResolutionCapable": True,
        }
        with (contents / "Info.plist").open("wb") as info_file:
            plistlib.dump(info, info_file, sort_keys=True)

        print(f"Packaged {bundle}")
        return 0
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
