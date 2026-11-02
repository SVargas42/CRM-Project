#!/usr/bin/env python3
"""Validate a trusted local NuGet feed and restore without using network sources."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET


def local_path(str_path: str, b_must_be_file: bool = False) -> Path:
    obj_path = Path(str_path).expanduser().resolve(strict=True)
    if b_must_be_file and not obj_path.is_file():
        raise ValueError(f"Expected a local file: {obj_path}")
    if not b_must_be_file and not obj_path.is_dir():
        raise ValueError(f"Expected a local directory: {obj_path}")

    str_mounts = subprocess.check_output(["/sbin/mount"], text=True)
    arr_matches = []
    for str_line in str_mounts.splitlines():
        if " on " not in str_line or " (" not in str_line:
            continue
        str_source, str_rest = str_line.rsplit(" on ", 1)
        str_mountpoint, str_details = str_rest.split(" (", 1)
        str_mountpoint = str_mountpoint.replace("\\040", " ")
        if str_path_is_under(obj_path, Path(str_mountpoint)):
            arr_matches.append((len(str_mountpoint), str_source, str_details.lower()))
    if not arr_matches:
        raise ValueError(f"Could not verify the volume containing {obj_path}")
    _, str_source, str_details = max(arr_matches)
    if str_source.startswith("//") or any(str_type in str_details for str_type in ("smbfs", "nfs", "afpfs", "webdav", "sshfs")):
        raise ValueError(f"Network-mounted package paths are not allowed: {obj_path}")
    return obj_path


def str_path_is_under(obj_path: Path, obj_mountpoint: Path) -> bool:
    try:
        obj_path.relative_to(obj_mountpoint)
        return True
    except ValueError:
        return False


def verify_feed(obj_feed: Path, obj_manifest_path: Path) -> None:
    with obj_manifest_path.open("r", encoding="utf-8") as obj_manifest_file:
        obj_manifest = json.load(obj_manifest_file)
    if not isinstance(obj_manifest, dict):
        raise ValueError("The approved package manifest must be a JSON object.")

    arr_packages = sorted(obj_feed.glob("*.nupkg"))
    if not arr_packages:
        raise ValueError("The verified local feed is empty.")
    for obj_package in arr_packages:
        str_expected = obj_manifest.get(obj_package.name)
        if not isinstance(str_expected, str) or not re.fullmatch(r"[0-9a-fA-F]{64}", str_expected):
            raise ValueError(f"Missing approved SHA-256 for {obj_package.name}.")
        obj_hash = hashlib.sha256()
        with obj_package.open("rb") as obj_package_file:
            for arr_chunk in iter(lambda: obj_package_file.read(1024 * 1024), b""):
                obj_hash.update(arr_chunk)
        if obj_hash.hexdigest().lower() != str_expected.lower():
            raise ValueError(f"Package checksum mismatch: {obj_package.name}.")


def main() -> int:
    obj_parser = argparse.ArgumentParser(description=__doc__)
    obj_parser.add_argument("feed", help="attached local folder containing approved .nupkg files")
    obj_parser.add_argument("manifest", help="independently approved JSON SHA-256 manifest")
    obj_args = obj_parser.parse_args()

    try:
        obj_feed = local_path(obj_args.feed)
        obj_manifest = local_path(obj_args.manifest, b_must_be_file=True)
        verify_feed(obj_feed, obj_manifest)

        obj_root = Path(__file__).resolve().parent.parent
        obj_cache = obj_root / "artifacts" / "verified-packages"
        if obj_cache.exists() and any(obj_cache.iterdir()):
            raise ValueError("Use a fresh package cache; review the existing offline cache before reuse.")

        obj_env = os.environ.copy()
        obj_env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        obj_env["DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE"] = "true"
        obj_env["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false"
        obj_env["DOTNET_CLI_HOME"] = str(obj_root / "artifacts" / "dotnet-home")
        obj_solution = ET.parse(obj_root / "HippoCrm.slnx").getroot()
        arr_projects = [obj_root / obj_project.attrib["Path"] for obj_project in obj_solution.iter("Project")]
        if not arr_projects:
            raise ValueError("The solution file contains no projects.")

        # Restore individually: this avoids a .NET 10 solution-level restore
        # stall observed on this Mac while retaining the same locked graph.
        for obj_project in arr_projects:
            arr_command = [
                "dotnet", "restore", str(obj_project),
                "--configfile", str(obj_root / "NuGet.Config"),
                "--source", str(obj_feed),
                "--packages", str(obj_cache),
                "--no-http-cache", "--force-evaluate",
                "-p:RestoreLockedMode=true", "-p:NuGetAudit=false",
            ]
            i_result = subprocess.run(arr_command, cwd=obj_root, env=obj_env, check=False).returncode
            if i_result != 0:
                raise RuntimeError(f"Offline restore failed for {obj_project.relative_to(obj_root)}. Review the approved local package set; do not add an internet source.")
        print(f"Offline restore completed for {len(arr_projects)} projects. Review regenerated package lock files before building.")
        return 0
    except (OSError, ValueError, json.JSONDecodeError, RuntimeError) as obj_error:
        print(str(obj_error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
