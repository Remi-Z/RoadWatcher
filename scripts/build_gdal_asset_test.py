from __future__ import annotations

import copy
import hashlib
import io
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import patch

from build_gdal_asset import (
    RecipeError,
    _CACHE_EXPECTATIONS,
    _tree_digest,
    _tree_inventory,
    _runtime_environment,
    build_gdal_stage,
    build_release_asset,
    validate_recipe,
)


class GdalAssetBuilderTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="roadwatcher-gdal-builder-test-"))
        self.source = self.root / "gdal-source"
        self.prefix = self.root / "dependency-prefix"
        self.licenses = self.root / "licenses"
        self._write(self.source / "CMakeLists.txt", b"cmake_minimum_required(VERSION 3.20)\n")
        self._write(self.source / "LICENSE", b"GDAL MIT fixture\n")
        self.source_archive = self._archive_source_tree(self.source)
        self._write(self.prefix / "include" / "proj.h", b"proj fixture\n")
        self._write(self.prefix / "include" / "sqlite3.h", b"sqlite fixture\n")
        self._write(self.prefix / "lib" / "proj.lib", b"proj static fixture\n")
        self._write(self.prefix / "lib" / "sqlite3.lib", b"sqlite static fixture\n")
        self._write(self.prefix / "share" / "proj" / "proj.db", b"proj database fixture\n")
        self._write(self.licenses / "GDAL-MIT.txt", b"GDAL MIT\n")
        self._write(self.licenses / "PROJ-MIT.txt", b"PROJ MIT\n")
        self._write(self.licenses / "SQLITE-PUBLIC-DOMAIN.txt", b"SQLite public domain\n")
        self._write(self.licenses / "THIRD-PARTY-NOTICES.txt", b"RoadWatcher GDAL notice bundle\n")
        self.tools = {
            name: self._write(self.root / "tools" / file_name, f"{name} fixture\n".encode())
            for name, file_name in {
                "cmake": "cmake.exe",
                "ninja": "ninja.exe",
                "msvc-cl": "cl.exe",
                "msvc-link": "link.exe",
                "dumpbin": "dumpbin.exe",
                "node": "node.exe",
            }.items()
        }
        self.recipe = {
            "schemaVersion": 2,
            "id": "gdal-ogr",
            "version": "3.12.4-test.1",
            "platform": "windows-x86_64",
            "sourceDateEpoch": "2026-07-13T12:00:00Z",
            "artifactLicense": self._license("gdal-proj-sqlite", "GDAL/PROJ MIT and SQLite notices"),
            "expectedOgrFormats": ["ESRI Shapefile", "FlatGeobuf", "GPKG", "GeoJSON", "OpenFileGDB", "SQLite"],
            "expectedGdalFormats": ["ESRI Shapefile", "FlatGeobuf", "GPKG", "GeoJSON", "OpenFileGDB", "SQLite"],
            "sources": [
                self._source("gdal-3-12-4", "gdalSource", self.source, "3.12.4"),
                self._source("pinned-proj-sqlite", "dependencyPrefix", self.prefix, "vcpkg-locked"),
                self._source("gdal-proj-sqlite-notices", "licenseBundle", self.licenses, "notices-v1"),
            ],
            "tools": [
                self._tool("cmake", "4.3.3"),
                self._tool("ninja", "1.12.1"),
                self._tool("msvc-cl", "19.44.35207"),
                self._tool("msvc-link", "14.44.35207"),
                self._tool("dumpbin", "14.44.35207"),
                self._tool("node", "v24.0.0"),
            ],
            "runtimeFiles": [],
        }

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def test_builds_minimal_locked_stage_and_runs_runtime_qualification(self) -> None:
        calls: list[list[str]] = []
        artifact, package_lock, definition = build_gdal_stage(
            self.recipe, self.root / "work", runner=self._runner(calls), timeout_seconds=60
        )

        self.assertEqual(
            [entry["path"] for entry in package_lock["files"]],
            [
                "bin/gdal.dll",
                "bin/ogr2ogr.exe",
                "bin/ogrinfo.exe",
                "licenses/GDAL-MIT.txt",
                "licenses/PROJ-MIT.txt",
                "licenses/SQLITE-PUBLIC-DOMAIN.txt",
                "licenses/THIRD-PARTY-NOTICES.txt",
                "share/gdal/gdal_datum.csv",
                "share/proj/proj.db",
            ],
        )
        self.assertEqual(definition["build"]["parameters"]["profile"], "roadwatcher-minimal-open-vector-v1")
        self.assertEqual(definition["build"]["parameters"]["curl"], False)
        self.assertTrue((artifact / "share" / "proj" / "proj.db").is_file())
        configure = next(args for args in calls if "-S" in args)
        self.assertIn("-DGDAL_USE_CURL:BOOL=OFF", configure)
        self.assertIn("-DGDAL_ENABLE_PLUGINS:BOOL=OFF", configure)
        self.assertIn("-DGDAL_VRT_ENABLE_RAWRASTERBAND:BOOL=OFF", configure)
        self.assertIn("-DCMAKE_FIND_USE_SYSTEM_ENVIRONMENT_PATH:BOOL=FALSE", configure)
        self.assertIn("-DCMAKE_FIND_ROOT_PATH_MODE_LIBRARY:STRING=ONLY", configure)
        self.assertTrue(any("/dependents" in args for args in calls))
        self.assertTrue(any("/imports" in args for args in calls))
        self.assertTrue(any("-s_srs" in args and "EPSG:26917" in args and "-t_srs" in args and "EPSG:4326" in args for args in calls))

    def test_publishes_complete_manifested_release_set_without_overwrite(self) -> None:
        calls: list[list[str]] = []
        output = self.root / "release"
        result = build_release_asset(
            self.recipe, output, runner=self._runner(calls), timeout_seconds=60
        )
        base = "gdal-ogr-3.12.4-test.1-windows-x86_64"
        self.assertEqual(
            {path.name for path in output.iterdir()},
            {
                f"{base}.zip",
                f"{base}.definition.json",
                f"{base}.manifest.json",
                f"{base}.package-lock.json",
            },
        )
        manifest = json.loads((output / f"{base}.manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["artifact"]["sha256"], result["sha256"])
        self.assertEqual(manifest["generatedAt"], self.recipe["sourceDateEpoch"])
        self.assertTrue(all(source["sizeBytes"] > 0 for source in manifest["sources"]))
        gdal_source = next(source for source in manifest["sources"] if source["id"] == "gdal-3-12-4")
        self.assertEqual(gdal_source["downloadedSha256"], self._sha256(self.source_archive))
        self.assertEqual(gdal_source["sizeBytes"], self.source_archive.stat().st_size)
        self.assertTrue(any("managed-artifact-manifest.mjs" in " ".join(args) and "build" in args for args in calls))
        self.assertTrue(any("managed-artifact-manifest.mjs" in " ".join(args) and "verify" in args for args in calls))
        self.assertTrue(any(args[0] == str(self.tools["node"]) and "managed-artifact-manifest.mjs" in " ".join(args) for args in calls))
        with self.assertRaisesRegex(RecipeError, "already exist"):
            build_release_asset(self.recipe, output, runner=self._runner([]), timeout_seconds=60)

    def test_rejects_source_cache_and_pe_dependency_drift(self) -> None:
        source_drift = copy.deepcopy(self.recipe)
        source_drift["sources"][0]["treeSha256"] = "0" * 64
        with self.assertRaisesRegex(RecipeError, "source tree"):
            build_gdal_stage(source_drift, self.root / "source-drift", runner=self._runner([]), timeout_seconds=60)

        cache_drift = self._runner([], cache_overrides={"GDAL_USE_CURL": "ON"})
        with self.assertRaisesRegex(RecipeError, "fixed GDAL profile"):
            build_gdal_stage(self.recipe, self.root / "cache-drift", runner=cache_drift, timeout_seconds=60)

        repro_drift = self._runner([], cache_flag_overrides={"CMAKE_EXE_LINKER_FLAGS_RELEASE": "/INCREMENTAL"})
        with self.assertRaisesRegex(RecipeError, "fixed reproducibility flags"):
            build_gdal_stage(self.recipe, self.root / "repro-drift", runner=repro_drift, timeout_seconds=60)

        foreign_dependency = self._runner([], foreign_dependency="libpq.dll")
        with self.assertRaisesRegex(RecipeError, "disabled GDAL profile"):
            build_gdal_stage(self.recipe, self.root / "pe-drift", runner=foreign_dependency, timeout_seconds=60)

        with self.assertRaisesRegex(RecipeError, "binary outside the allowed bin directory"):
            build_gdal_stage(
                self.recipe,
                self.root / "layout-drift",
                runner=self._runner([], malicious_install_relative="share/gdal/evil.dll"),
                timeout_seconds=60,
            )

        unused_runtime = copy.deepcopy(self.recipe)
        runtime = self._write(self.prefix / "bin" / "proj_9_4.dll", b"unused proj runtime fixture\n")
        unused_runtime["sources"][1]["treeSha256"] = self._tree_hash(self.prefix)
        unused_runtime["runtimeFiles"] = [{
            "name": "proj_9_4.dll",
            "sha256": self._sha256(runtime),
            "licenses": ["PROJ-MIT.txt"],
        }]
        with self.assertRaisesRegex(RecipeError, "unused declared runtime dependency"):
            build_gdal_stage(unused_runtime, self.root / "unused-runtime", runner=self._runner([]), timeout_seconds=60)

    def test_rejects_source_archive_provenance_and_complete_driver_or_cache_drift(self) -> None:
        archive_hash_drift = copy.deepcopy(self.recipe)
        archive_hash_drift["sources"][0]["archiveSha256"] = "0" * 64
        with self.assertRaisesRegex(RecipeError, "archive SHA-256"):
            build_gdal_stage(archive_hash_drift, self.root / "archive-hash-drift", runner=self._runner([]), timeout_seconds=60)

        self._archive_source_tree(self.source, extra_members=[("gdal-3.12.4/../escape.txt", b"escape")])
        traversal = copy.deepcopy(self.recipe)
        traversal["sources"][0]["archiveSha256"] = self._sha256(self.source_archive)
        traversal["sources"][0]["archiveSizeBytes"] = self.source_archive.stat().st_size
        with self.assertRaisesRegex(RecipeError, "unsafe GDAL source archive member path"):
            build_gdal_stage(traversal, self.root / "archive-traversal", runner=self._runner([]), timeout_seconds=60)

        self.source_archive = self._archive_source_tree(self.source)
        exact_driver = copy.deepcopy(self.recipe)
        exact_driver["sources"][0]["archiveSha256"] = self._sha256(self.source_archive)
        exact_driver["sources"][0]["archiveSizeBytes"] = self.source_archive.stat().st_size
        with self.assertRaisesRegex(RecipeError, "GDAL complete driver inventory does not exactly match"):
            build_gdal_stage(exact_driver, self.root / "driver-drift", runner=self._runner([], extra_gdal_format="VRT"), timeout_seconds=60)

        with self.assertRaisesRegex(RecipeError, "outside the approved staged roots"):
            build_gdal_stage(
                exact_driver,
                self.root / "cache-path-drift",
                runner=self._runner([], cache_path_overrides={"ZLIB_LIBRARY": "C:\\untrusted\\zlib.lib"}),
                timeout_seconds=60,
            )

        with self.assertRaisesRegex(RecipeError, "did not consume"):
            build_gdal_stage(
                exact_driver,
                self.root / "unused-setting",
                runner=self._runner([], configure_unused_setting=True),
                timeout_seconds=60,
            )

    def test_rejects_unknown_recipe_fields_and_wrong_gdal_identity(self) -> None:
        unsupported = copy.deepcopy(self.recipe)
        unsupported["download"] = "https://example.com/unapproved"
        with self.assertRaisesRegex(RecipeError, "top-level"):
            validate_recipe(unsupported)

        wrong_gdal = copy.deepcopy(self.recipe)
        wrong_gdal["sources"][0]["version"] = "3.12.3"
        with self.assertRaisesRegex(RecipeError, "pinned 3.12.4"):
            validate_recipe(wrong_gdal)

        missing_notice = copy.deepcopy(self.recipe)
        missing_notice["sources"][0]["noticeFiles"] = ["missing.txt"]
        with self.assertRaisesRegex(RecipeError, "gdalSource notice"):
            build_gdal_stage(missing_notice, self.root / "missing-notice", runner=self._runner([]), timeout_seconds=60)

        unsorted_formats = copy.deepcopy(self.recipe)
        unsorted_formats["expectedOgrFormats"] = list(reversed(unsorted_formats["expectedOgrFormats"]))
        with self.assertRaisesRegex(RecipeError, "expectedOgrFormats must be sorted and unique"):
            validate_recipe(unsorted_formats)

    def test_runtime_qualification_clears_inherited_gdal_and_proj_state(self) -> None:
        inherited = {
            "GDAL_CONFIG_FILE": "C:\\untrusted\\gdal.ini",
            "GDAL_DRIVER_PATH": "C:\\untrusted\\plugins",
            "OGR_DRIVER_PATH": "C:\\untrusted\\ogr-plugins",
            "OGR_SKIP": "GPKG",
            "PROJ_AUX_DB": "C:\\untrusted\\proj.db",
            "PROJ_CURL_CA_BUNDLE": "C:\\untrusted\\ca.pem",
            "PROJ_NETWORK_ENDPOINT": "https://untrusted.example.test",
            "PROJ_USER_WRITABLE_DIRECTORY": "C:\\untrusted\\proj",
            "PYTHONSO": "C:\\untrusted\\python.dll",
            "CMAKE_PREFIX_PATH": "C:\\untrusted\\cmake-prefix",
            "VCPKG_ROOT": "C:\\untrusted\\vcpkg",
            "PKG_CONFIG_PATH": "C:\\untrusted\\pkg-config",
        }
        with patch.dict(os.environ, inherited, clear=False):
            environment = _runtime_environment(self.root / "artifact", "2026-07-13T12:00:00Z")
        for name in set(inherited) - {"GDAL_DRIVER_PATH", "OGR_DRIVER_PATH"}:
            self.assertNotIn(name, environment)
        self.assertEqual(environment["GDAL_DRIVER_PATH"], "disable")
        self.assertEqual(environment["OGR_DRIVER_PATH"], "disable")
        self.assertEqual(environment["PROJ_NETWORK"], "OFF")

    def _runner(
        self,
        calls: list[list[str]],
        *,
        cache_overrides: dict[str, str] | None = None,
        cache_flag_overrides: dict[str, str] | None = None,
        cache_path_overrides: dict[str, str] | None = None,
        foreign_dependency: str | None = None,
        malicious_install_relative: str | None = None,
        extra_gdal_format: str | None = None,
        configure_unused_setting: bool = False,
    ):
        cache_overrides = cache_overrides or {}
        cache_flag_overrides = cache_flag_overrides or {}
        cache_path_overrides = cache_path_overrides or {}

        def runner(args: list[str], cwd: Path, _timeout: int, _limit: int, environment: dict[str, str]) -> str:
            calls.append(args)
            executable = Path(args[0]).name.lower()
            if executable == "cmake.exe" and args[1:] == ["--version"]:
                return "cmake version 4.3.3\n"
            if executable == "ninja.exe" and args[1:] == ["--version"]:
                return "1.12.1\n"
            if executable == "node.exe" and args[1:] == ["--version"]:
                return "v24.0.0\n"
            if "managed-artifact-manifest.mjs" in " ".join(args):
                node = shutil.which("node")
                if not node:
                    raise AssertionError("Node.js is required for the manifest fixture")
                completed = subprocess.run([node, *args[1:]], cwd=cwd, env=environment, capture_output=True, text=True, check=False)
                if completed.returncode:
                    raise RecipeError(completed.stderr or completed.stdout)
                return completed.stdout + completed.stderr
            if executable == "cmake.exe" and "-S" in args:
                self._write_cache(args, cache_overrides, cache_flag_overrides, cache_path_overrides)
                return "Manually-specified variables were not used by the project\n" if configure_unused_setting else "configured\n"
            if executable == "cmake.exe" and "--build" in args:
                build_root = Path(args[args.index("--build") + 1])
                install_root = self._cache_value(build_root / "CMakeCache.txt", "CMAKE_INSTALL_PREFIX")
                self._write(install_root / "bin" / "gdal.dll", b"gdal fixture\n")
                self._write(install_root / "bin" / "gdalinfo.exe", b"gdalinfo fixture\n")
                self._write(install_root / "bin" / "ogrinfo.exe", b"ogrinfo fixture\n")
                self._write(install_root / "bin" / "ogr2ogr.exe", b"ogr2ogr fixture\n")
                self._write(install_root / "share" / "gdal" / "gdal_datum.csv", b"gdal data fixture\n")
                if malicious_install_relative:
                    self._write(install_root / Path(malicious_install_relative), b"malicious binary fixture\n")
                return "built\n"
            if executable == "ogrinfo.exe" and args[1:] == ["--version"]:
                return "GDAL 3.12.4\n"
            if executable == "ogr2ogr.exe" and args[1:] == ["--version"]:
                return "GDAL 3.12.4\n"
            if executable == "gdalinfo.exe" and args[1:] == ["--version"]:
                return "GDAL 3.12.4\n"
            if executable == "ogrinfo.exe" and args[1:] == ["--formats"]:
                return "\n".join(
                    f"  {name} -vector- (rw+v): fixture"
                    for name in ["ESRI Shapefile", "GPKG", "SQLite", "FlatGeobuf", "OpenFileGDB", "GeoJSON"]
                )
            if executable == "gdalinfo.exe" and args[1:] == ["--formats"]:
                formats = ["ESRI Shapefile", "GPKG", "SQLite", "FlatGeobuf", "OpenFileGDB", "GeoJSON"]
                if extra_gdal_format:
                    formats.append(extra_gdal_format)
                return "\n".join(
                    f"  {name} -vector- (rw+v): fixture"
                    for name in formats
                )
            if executable == "ogr2ogr.exe" and "-t_srs" in args:
                output = Path(args[-2])
                output.write_text(
                    json.dumps({"type": "FeatureCollection", "features": [{"type": "Feature", "geometry": {"type": "Point", "coordinates": [-79.381751, 43.881643]}}]}),
                    encoding="utf-8",
                )
                return "converted\n"
            if executable == "dumpbin.exe":
                binary = Path(args[-1]).name.lower()
                dependencies = ["KERNEL32.dll"]
                if binary != "gdal.dll":
                    dependencies.insert(0, foreign_dependency or "gdal.dll")
                return "Image has the following dependencies:\n" + "\n".join(f"    {name}" for name in dependencies)
            raise AssertionError(f"unexpected fixed GDAL build command: {args}")

        return runner

    def _write_cache(
        self,
        args: list[str],
        overrides: dict[str, str],
        flag_overrides: dict[str, str],
        path_overrides: dict[str, str],
    ) -> None:
        source = Path(args[args.index("-S") + 1])
        build = Path(args[args.index("-B") + 1])
        values = self._cmake_values(args)
        build.mkdir(parents=True, exist_ok=True)
        cache = dict(_CACHE_EXPECTATIONS)
        cache.update(overrides)
        flags = {
            "CMAKE_C_FLAGS_RELEASE": values["CMAKE_C_FLAGS_RELEASE"],
            "CMAKE_CXX_FLAGS_RELEASE": values["CMAKE_CXX_FLAGS_RELEASE"],
            "CMAKE_SHARED_LINKER_FLAGS_RELEASE": values["CMAKE_SHARED_LINKER_FLAGS_RELEASE"],
            "CMAKE_EXE_LINKER_FLAGS_RELEASE": values["CMAKE_EXE_LINKER_FLAGS_RELEASE"],
        }
        flags.update(flag_overrides)
        lines = [f"{name}:BOOL={value}" for name, value in cache.items()]
        lines.extend(
            [
                "CMAKE_GENERATOR:INTERNAL=Ninja",
                "CMAKE_BUILD_TYPE:STRING=Release",
                f"CMAKE_MAKE_PROGRAM:FILEPATH={values['CMAKE_MAKE_PROGRAM']}",
                f"CMAKE_C_COMPILER:FILEPATH={values['CMAKE_C_COMPILER']}",
                f"CMAKE_CXX_COMPILER:FILEPATH={values['CMAKE_CXX_COMPILER']}",
                f"CMAKE_LINKER:FILEPATH={values['CMAKE_LINKER']}",
                f"CMAKE_INSTALL_PREFIX:PATH={values['CMAKE_INSTALL_PREFIX']}",
                f"CMAKE_PREFIX_PATH:PATH={values['CMAKE_PREFIX_PATH']}",
                f"CMAKE_FIND_ROOT_PATH:PATH={values['CMAKE_FIND_ROOT_PATH']}",
                f"PROJ_INCLUDE_DIR:PATH={values['PROJ_INCLUDE_DIR']}",
                f"PROJ_LIBRARY_RELEASE:FILEPATH={values['PROJ_LIBRARY_RELEASE']}",
                f"SQLite3_INCLUDE_DIR:PATH={values['SQLite3_INCLUDE_DIR']}",
                f"SQLite3_LIBRARY:FILEPATH={values['SQLite3_LIBRARY']}",
                f"CMAKE_C_FLAGS_RELEASE:STRING={flags['CMAKE_C_FLAGS_RELEASE']}",
                f"CMAKE_CXX_FLAGS_RELEASE:STRING={flags['CMAKE_CXX_FLAGS_RELEASE']}",
                f"CMAKE_SHARED_LINKER_FLAGS_RELEASE:STRING={flags['CMAKE_SHARED_LINKER_FLAGS_RELEASE']}",
                f"CMAKE_EXE_LINKER_FLAGS_RELEASE:STRING={flags['CMAKE_EXE_LINKER_FLAGS_RELEASE']}",
                f"CMAKE_HOME_DIRECTORY:INTERNAL={source}",
            ]
        )
        lines.extend(f"{name}:FILEPATH={value}" for name, value in path_overrides.items())
        (build / "CMakeCache.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")

    @staticmethod
    def _cmake_values(args: list[str]) -> dict[str, str]:
        values: dict[str, str] = {}
        for argument in args:
            if not argument.startswith("-D") or "=" not in argument:
                continue
            name_and_type, value = argument[2:].split("=", 1)
            values[name_and_type.split(":", 1)[0]] = value
        return values

    @staticmethod
    def _cache_value(cache: Path, key: str) -> Path:
        for line in cache.read_text(encoding="utf-8").splitlines():
            if line.startswith(f"{key}:"):
                return Path(line.split("=", 1)[1])
        raise AssertionError(f"missing {key} in fixture cache")

    def _source(self, source_id: str, role: str, path: Path, version: str) -> dict[str, object]:
        digest = self._tree_hash(path)
        result: dict[str, object] = {
            "id": source_id,
            "role": role,
            "localPath": str(path.resolve()),
            "url": f"https://example.com/{source_id}",
            "version": version,
            "license": self._license("mit", "MIT"),
            "noticeFiles": {
                "gdalSource": ["GDAL-MIT.txt"],
                "dependencyPrefix": ["PROJ-MIT.txt", "SQLITE-PUBLIC-DOMAIN.txt"],
                "licenseBundle": ["THIRD-PARTY-NOTICES.txt"],
            }[role],
            "treeSha256": digest,
            "publisherSha256": digest,
            "retrievedAt": "2026-07-13T12:00:00Z",
        }
        if role == "gdalSource":
            result.update({
                "commit": "f2ff911fee59d4b647dd7b2c030c389c9c062d8c",
                "archivePath": str(self.source_archive),
                "archiveSha256": self._sha256(self.source_archive),
                "archiveSizeBytes": self.source_archive.stat().st_size,
                "archiveFormat": "tar.gz",
                "archiveRoot": "gdal-3.12.4",
            })
        return result

    def _tool(self, name: str, version: str) -> dict[str, str]:
        path = self.tools[name]
        return {
            "name": name,
            "path": str(path),
            "sha256": self._sha256(path),
            "version": version,
        }

    @staticmethod
    def _license(identifier: str, name: str) -> dict[str, str]:
        return {"id": identifier, "name": name, "url": "https://example.com/license"}

    def _write(self, path: Path, content: bytes) -> Path:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
        return path.resolve()

    def _archive_source_tree(self, source: Path, *, extra_members: list[tuple[str, bytes]] | None = None) -> Path:
        archive = self.root / "gdal-3.12.4.tar.gz"
        with tarfile.open(archive, "w:gz") as output:
            directory = tarfile.TarInfo("gdal-3.12.4/")
            directory.type = tarfile.DIRTYPE
            directory.mtime = 0
            output.addfile(directory)
            for relative, _digest, size in _tree_inventory(source, "source fixture", 200_000, 8 * 1024 * 1024 * 1024):
                content = (source / relative).read_bytes()
                entry = tarfile.TarInfo(f"gdal-3.12.4/{relative}")
                entry.size = size
                entry.mtime = 0
                output.addfile(entry, io.BytesIO(content))
            for relative, content in extra_members or []:
                entry = tarfile.TarInfo(relative)
                entry.size = len(content)
                entry.mtime = 0
                output.addfile(entry, io.BytesIO(content))
        return archive.resolve()

    @staticmethod
    def _sha256(path: Path) -> str:
        return hashlib.sha256(path.read_bytes()).hexdigest()

    @staticmethod
    def _tree_hash(path: Path) -> str:
        return _tree_digest(_tree_inventory(path, "test input", 200_000, 8 * 1024 * 1024 * 1024))


if __name__ == "__main__":
    unittest.main()
