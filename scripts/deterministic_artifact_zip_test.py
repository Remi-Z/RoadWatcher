from __future__ import annotations

import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

from deterministic_artifact_zip import BuildError, build_archive, load_lock


def sha256(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


class DeterministicArtifactZipTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="roadwatcher-zip-test-")
        self.root = Path(self.temporary.name)
        self.source = self.root / "source"
        (self.source / "tiles" / "2").mkdir(parents=True)
        (self.source / "tiles" / "2" / "000.tar").write_bytes(b"tile-data")
        (self.source / "valhalla.json").write_bytes(b'{"mjolnir":{}}\n')
        self.lock_path = self.root / "package-lock.json"
        self.lock = {
            "schemaVersion": 1,
            "id": "york-valhalla-tiles",
            "sourceDateEpoch": "2026-07-13T12:34:57Z",
            "files": [
                {"path": "valhalla.json", "sha256": sha256(b'{"mjolnir":{}}\n')},
                {"path": "tiles/2/000.tar", "sha256": sha256(b"tile-data")},
            ],
        }
        self.lock_path.write_text(json.dumps(self.lock), encoding="utf-8")

    def tearDown(self) -> None:
        self.temporary.cleanup()

    def test_builds_byte_identical_content_locked_archives(self) -> None:
        lock = load_lock(self.lock_path)
        first = self.root / "first.zip"
        second = self.root / "second.zip"
        first_result = build_archive(lock, self.source, first)
        second_result = build_archive(lock, self.source, second)

        self.assertEqual(first.read_bytes(), second.read_bytes())
        self.assertEqual(first_result["sha256"], second_result["sha256"])
        with zipfile.ZipFile(first) as archive:
            self.assertEqual(archive.namelist(), ["tiles/2/000.tar", "valhalla.json"])
            self.assertTrue(all(entry.compress_type == zipfile.ZIP_STORED for entry in archive.infolist()))
            self.assertTrue(all(entry.date_time == (2026, 7, 13, 12, 34, 56) for entry in archive.infolist()))
            self.assertTrue(all((entry.external_attr >> 16) & 0o777 == 0o644 for entry in archive.infolist()))

    def test_rejects_hash_drift_missing_and_undeclared_files_without_output(self) -> None:
        lock = load_lock(self.lock_path)
        (self.source / "valhalla.json").write_text("changed", encoding="utf-8")
        output = self.root / "hash-drift.zip"
        with self.assertRaisesRegex(BuildError, "SHA-256"):
            build_archive(lock, self.source, output)
        self.assertFalse(output.exists())

        (self.source / "valhalla.json").write_bytes(b'{"mjolnir":{}}\n')
        (self.source / "extra.txt").write_text("extra", encoding="utf-8")
        with self.assertRaisesRegex(BuildError, "undeclared"):
            build_archive(lock, self.source, self.root / "extra.zip")

    def test_rejects_unsafe_locks_and_never_overwrites(self) -> None:
        unsafe_path = self.root / "unsafe-lock.json"
        unsafe = dict(self.lock)
        unsafe["files"] = [{"path": "../escape", "sha256": "a" * 64}]
        unsafe_path.write_text(json.dumps(unsafe), encoding="utf-8")
        with self.assertRaisesRegex(BuildError, "unsafe package path"):
            load_lock(unsafe_path)

        output = self.root / "existing.zip"
        output.write_bytes(b"owner-data")
        with self.assertRaisesRegex(BuildError, "already exists"):
            build_archive(load_lock(self.lock_path), self.source, output)
        self.assertEqual(output.read_bytes(), b"owner-data")

        with self.assertRaisesRegex(BuildError, "outside the source tree"):
            build_archive(load_lock(self.lock_path), self.source, self.source / "artifact.zip")


if __name__ == "__main__":
    unittest.main()
