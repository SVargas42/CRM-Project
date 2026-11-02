#!/usr/bin/env python3
"""Create a local NuGet feed after verifying packages against committed lock hashes."""

import argparse
import base64
import hashlib
import json
from pathlib import Path
import shutil
import sys


def locked_packages(obj_root: Path) -> dict[tuple[str, str], str]:
    dic_packages: dict[tuple[str, str], str] = {}
    arr_locks = sorted(obj_root.rglob("packages.lock.json"))
    arr_locks = [obj_path for obj_path in arr_locks if not {".git", "artifacts", "bin", "obj"}.intersection(obj_path.parts)]
    if not arr_locks:
        raise ValueError("No committed packages.lock.json files were found.")
    for obj_lock in arr_locks:
        with obj_lock.open("r", encoding="utf-8") as obj_file:
            obj_data = json.load(obj_file)
        for obj_target in obj_data.get("dependencies", {}).values():
            for str_name, obj_package in obj_target.items():
                if obj_package.get("type") == "Project":
                    continue
                str_version = obj_package.get("resolved")
                str_hash = obj_package.get("contentHash")
                if not str_version or not str_hash:
                    raise ValueError(f"Lock entry lacks a resolved version or content hash: {obj_lock}: {str_name}")
                str_key = (str_name.lower(), str_version.lower())
                str_prior_hash = dic_packages.setdefault(str_key, str_hash)
                if str_prior_hash != str_hash:
                    raise ValueError(f"Conflicting lock hashes for {str_name} {str_version}.")
    return dic_packages


def sha256_hex(obj_path: Path) -> str:
    obj_hash = hashlib.sha256()
    with obj_path.open("rb") as obj_file:
        for arr_chunk in iter(lambda: obj_file.read(1024 * 1024), b""):
            obj_hash.update(arr_chunk)
    return obj_hash.hexdigest()


def sha512_base64(obj_path: Path) -> str:
    obj_hash = hashlib.sha512()
    with obj_path.open("rb") as obj_file:
        for arr_chunk in iter(lambda: obj_file.read(1024 * 1024), b""):
            obj_hash.update(arr_chunk)
    return base64.b64encode(obj_hash.digest()).decode("ascii")


def main() -> int:
    obj_parser = argparse.ArgumentParser(description=__doc__)
    obj_parser.add_argument("cache", help="NuGet global packages folder populated by locked restore")
    obj_parser.add_argument("feed", help="new empty local directory for verified .nupkg files")
    obj_args = obj_parser.parse_args()

    try:
        obj_root = Path(__file__).resolve().parent.parent
        obj_cache = Path(obj_args.cache).expanduser().resolve(strict=True)
        if not obj_cache.is_dir():
            raise ValueError("The NuGet cache must be a directory.")
        obj_feed = Path(obj_args.feed).expanduser().resolve()
        if obj_feed.exists() and (not obj_feed.is_dir() or any(obj_feed.iterdir())):
            raise ValueError("Choose a new or empty local feed directory.")

        dic_locked = locked_packages(obj_root)
        arr_verified = []
        for (str_name, str_version), str_expected_hash in sorted(dic_locked.items()):
            obj_package = obj_cache / str_name / str_version / f"{str_name}.{str_version}.nupkg"
            if not obj_package.is_file():
                raise ValueError(f"Package is missing from the restore cache: {str_name} {str_version}.")
            # NuGet's lock-file contentHash is computed over package contents,
            # excluding the .signature.p7s entry. Use NuGet's cache metadata,
            # produced by the locked restore, to compare that content hash.
            obj_metadata_path = obj_package.parent / ".nupkg.metadata"
            if not obj_metadata_path.is_file():
                raise ValueError(f"NuGet restore metadata is missing: {str_name} {str_version}.")
            with obj_metadata_path.open("r", encoding="utf-8") as obj_metadata_file:
                obj_metadata = json.load(obj_metadata_file)
            if obj_metadata.get("version") != 2 or obj_metadata.get("source") != "https://api.nuget.org/v3/index.json":
                raise ValueError(f"Package was not cached by the approved NuGet.org source: {str_name} {str_version}.")
            if obj_metadata.get("contentHash") != str_expected_hash:
                raise ValueError(f"Committed NuGet contentHash mismatch: {str_name} {str_version}.")

            obj_raw_hash_path = obj_package.with_name(obj_package.name + ".sha512")
            if not obj_raw_hash_path.is_file():
                raise ValueError(f"NuGet package byte-hash metadata is missing: {str_name} {str_version}.")
            str_raw_expected_hash = obj_raw_hash_path.read_text(encoding="ascii").strip()
            if sha512_base64(obj_package) != str_raw_expected_hash:
                raise ValueError(f"NuGet package byte-hash mismatch: {str_name} {str_version}.")

            # The lock hash excludes signatures; the raw SHA-256 manifest below
            # pins the exact signed .nupkg bytes that will be used offline.
            arr_verified.append((obj_package, sha256_hex(obj_package)))

        obj_feed.mkdir(parents=True, exist_ok=True)
        dic_manifest = {}
        for obj_package, str_sha256 in arr_verified:
            shutil.copy2(obj_package, obj_feed / obj_package.name)
            dic_manifest[obj_package.name] = str_sha256
        with (obj_feed / "packages.sha256.json").open("w", encoding="utf-8") as obj_manifest_file:
            json.dump(dic_manifest, obj_manifest_file, indent=2, sort_keys=True)
            obj_manifest_file.write("\n")
        print(f"Prepared {len(arr_verified)} packages verified against NuGet restore metadata and committed lock-file hashes.")
        print(f"Feed: {obj_feed}")
        print(f"Manifest: {obj_feed / 'packages.sha256.json'}")
        return 0
    except (OSError, ValueError, json.JSONDecodeError) as obj_error:
        print(str(obj_error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
