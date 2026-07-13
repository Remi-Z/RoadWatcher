"""Build a deterministic, content-locked ZIP for managed RoadWatcher artifacts."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import sys
import tempfile
import zipfile


_ID = re.compile(r"^[a-z0-9-]+$")
_SHA256 = re.compile(r"^[a-f0-9]{64}$")
_TOP_LEVEL_KEYS = {"schemaVersion", "id", "sourceDateEpoch", "files"}
_FILE_KEYS = {"path", "sha256"}
_REPARSE_POINT = 0x400
_COPY_CHUNK = 1024 * 1024


class BuildError(ValueError):
    """Raised when a package lock or source tree is unsafe or inconsistent."""


def _reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise BuildError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_lock(path: Path) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_reject_duplicate_keys)
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise BuildError(f"cannot read package lock: {error}") from error
    validate_lock(value)
    return value


def validate_lock(value: object) -> None:
    if not isinstance(value, dict) or set(value) != _TOP_LEVEL_KEYS:
        raise BuildError("package lock must contain only schemaVersion, id, sourceDateEpoch, and files")
    if value["schemaVersion"] != 1:
        raise BuildError("unsupported package lock schema")
    if not isinstance(value["id"], str) or not _ID.fullmatch(value["id"]):
        raise BuildError("package lock id is invalid")
    _zip_timestamp(value["sourceDateEpoch"])
    files = value["files"]
    if not isinstance(files, list) or not files or len(files) > 100_000:
        raise BuildError("package lock files must be a non-empty bounded array")
    seen: set[str] = set()
    for entry in files:
        if not isinstance(entry, dict) or set(entry) != _FILE_KEYS:
            raise BuildError("each package file must contain only path and sha256")
        path = _archive_path(entry["path"])
        digest = entry["sha256"]
        if not isinstance(digest, str) or not _SHA256.fullmatch(digest):
            raise BuildError(f"invalid SHA-256 for {path}")
        if path in seen:
            raise BuildError(f"duplicate package path: {path}")
        seen.add(path)


def build_archive(lock: dict[str, object], source_root: Path, output_path: Path) -> dict[str, object]:
    validate_lock(lock)
    try:
        root_metadata = source_root.lstat()
    except OSError as error:
        raise BuildError(f"cannot inspect package source root: {error}") from error
    if source_root.is_symlink() or getattr(root_metadata, "st_file_attributes", 0) & _REPARSE_POINT:
        raise BuildError("package source root cannot be a link or reparse point")
    source_root = source_root.resolve(strict=True)
    output_path = output_path.resolve(strict=False)
    if not source_root.is_dir():
        raise BuildError("package source root is not a directory")
    if output_path == source_root or output_path.is_relative_to(source_root):
        raise BuildError("package output must be outside the source tree")
    if output_path.exists():
        raise BuildError("package output already exists")
    output_path.parent.mkdir(parents=True, exist_ok=True)

    expected = {entry["path"]: entry["sha256"] for entry in lock["files"]}
    actual = _scan_tree(source_root)
    missing = sorted(set(expected) - actual)
    extra = sorted(actual - set(expected))
    if missing or extra:
        details = []
        if missing:
            details.append(f"missing: {', '.join(missing[:10])}")
        if extra:
            details.append(f"undeclared: {', '.join(extra[:10])}")
        raise BuildError("package source tree differs from lock (" + "; ".join(details) + ")")

    timestamp = _zip_timestamp(lock["sourceDateEpoch"])
    temporary: Path | None = None
    try:
        descriptor, temporary_name = tempfile.mkstemp(prefix=f".{output_path.name}.", suffix=".staging", dir=output_path.parent)
        os.close(descriptor)
        temporary = Path(temporary_name)
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_STORED, allowZip64=True) as archive:
            for archive_name in sorted(expected):
                source_path = source_root.joinpath(*PurePosixPath(archive_name).parts)
                info = zipfile.ZipInfo(archive_name, date_time=timestamp)
                info.create_system = 3
                info.compress_type = zipfile.ZIP_STORED
                info.external_attr = (stat.S_IFREG | 0o644) << 16
                info.flag_bits = 0
                digest = hashlib.sha256()
                with _open_regular_file(source_path) as source, archive.open(info, "w", force_zip64=True) as destination:
                    while chunk := source.read(_COPY_CHUNK):
                        digest.update(chunk)
                        destination.write(chunk)
                if digest.hexdigest() != expected[archive_name]:
                    raise BuildError(f"source SHA-256 changed or does not match lock: {archive_name}")
        _publish_without_overwrite(temporary, output_path)
        temporary = None
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)

    output_digest = _sha256_file(output_path)
    return {
        "id": lock["id"],
        "fileName": output_path.name,
        "fileCount": len(expected),
        "sizeBytes": output_path.stat().st_size,
        "sha256": output_digest,
        "sourceDateEpoch": lock["sourceDateEpoch"],
    }


def _scan_tree(root: Path) -> set[str]:
    files: set[str] = set()

    def visit(directory: Path, prefix: PurePosixPath) -> None:
        try:
            entries = sorted(os.scandir(directory), key=lambda entry: entry.name)
        except OSError as error:
            raise BuildError(f"cannot scan package source tree: {error}") from error
        for entry in entries:
            relative = prefix / entry.name
            archive_name = _archive_path(relative.as_posix())
            metadata = entry.stat(follow_symlinks=False)
            if entry.is_symlink() or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT:
                raise BuildError(f"links and reparse points are not allowed: {archive_name}")
            if stat.S_ISDIR(metadata.st_mode):
                visit(Path(entry.path), relative)
            elif stat.S_ISREG(metadata.st_mode):
                files.add(archive_name)
            else:
                raise BuildError(f"special files are not allowed: {archive_name}")

    visit(root, PurePosixPath())
    return files


def _open_regular_file(path: Path):
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(path, flags)
    except OSError as error:
        raise BuildError(f"cannot open package file safely: {path.name}: {error}") from error
    metadata = os.fstat(descriptor)
    if not stat.S_ISREG(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT:
        os.close(descriptor)
        raise BuildError(f"package file is not a regular owned file: {path.name}")
    return os.fdopen(descriptor, "rb")


def _publish_without_overwrite(temporary: Path, output: Path) -> None:
    try:
        os.link(temporary, output)
    except FileExistsError as error:
        raise BuildError("package output was created concurrently; refusing to overwrite") from error
    except OSError as error:
        raise BuildError(f"cannot atomically publish package output: {error}") from error
    temporary.unlink()


def _archive_path(value: object) -> str:
    if not isinstance(value, str) or not value or len(value) > 240 or "\\" in value:
        raise BuildError("package path is invalid")
    try:
        value.encode("ascii")
    except UnicodeEncodeError as error:
        raise BuildError("package paths must be ASCII") from error
    path = PurePosixPath(value)
    if path.is_absolute() or path.as_posix() != value or any(part in {"", ".", ".."} for part in path.parts):
        raise BuildError(f"unsafe package path: {value}")
    if any(ord(character) < 32 or character == ":" for character in value):
        raise BuildError(f"unsafe package path: {value}")
    return value


def _zip_timestamp(value: object) -> tuple[int, int, int, int, int, int]:
    if not isinstance(value, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", value):
        raise BuildError("sourceDateEpoch must be an exact UTC timestamp")
    try:
        parsed = dt.datetime.strptime(value, "%Y-%m-%dT%H:%M:%SZ")
    except ValueError as error:
        raise BuildError("sourceDateEpoch is invalid") from error
    if parsed.year < 1980 or parsed.year > 2107:
        raise BuildError("sourceDateEpoch is outside the ZIP timestamp range")
    return parsed.year, parsed.month, parsed.day, parsed.hour, parsed.minute, parsed.second - parsed.second % 2


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(_COPY_CHUNK):
            digest.update(chunk)
    return digest.hexdigest()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("lock", type=Path, help="strict package lock JSON")
    parser.add_argument("source_root", type=Path, help="directory containing only locked package files")
    parser.add_argument("output", type=Path, help="new ZIP path; existing files are never overwritten")
    args = parser.parse_args(argv)
    try:
        result = build_archive(load_lock(args.lock), args.source_root, args.output)
    except (BuildError, OSError) as error:
        parser.exit(1, f"artifact package failed: {error}\n")
    print(json.dumps(result, sort_keys=True, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
