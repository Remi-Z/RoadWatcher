from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import unittest

from build_york_valhalla_asset import RecipeError, build_release_asset, build_york_stage, validate_recipe


class YorkValhallaAssetBuilderTests(unittest.TestCase):
    def setUp(self) -> None:
        self.root = Path(tempfile.mkdtemp(prefix="roadwatcher-york-builder-test-"))
        self.osm = self._write("greater-toronto.osm.pbf", b"approved-osm-extract")
        self.boundary = self._write_json(
            "york-boundary.geojson",
            {
                "type": "FeatureCollection",
                "features": [{
                    "type": "Feature",
                    "properties": {"unrelatedNumbers": [1, 2, 3]},
                    "geometry": {
                        "type": "Polygon",
                        "coordinates": [[
                            [-79.5, 43.8], [-79.4, 43.8], [-79.4, 43.9],
                            [-79.5, 43.9], [-79.5, 43.8]
                        ]]
                    }
                }]
            },
        )
        self.config = self._write_json(
            "valhalla.json",
            {"mjolnir": {"tile_dir": "${ROADWATCHER_TILE_DIR}"}, "service_limits": {}},
        )
        self.osmium = self._write("osmium.exe", b"fixture-tool")
        self.valhalla = self._write("valhalla_build_tiles.exe", b"fixture-tool")
        self.recipe = {
            "schemaVersion": 1,
            "id": "york-valhalla-tiles",
            "version": "2026.07.13-test.1",
            "platform": "windows-x86_64",
            "sourceDateEpoch": "2026-07-13T12:00:00Z",
            "artifactLicense": self._license("odbl-1-0", "ODbL 1.0"),
            "sources": [
                self._source("osm-extract", "osmExtract", self.root / "greater-toronto.osm.pbf"),
                self._source("york-boundary", "yorkBoundary", self.boundary),
                self._source("valhalla-config", "valhallaConfigTemplate", self.config),
            ],
            "tools": [
                {"name": "osmium", "path": str(self.osmium), "version": "1.16.0", "versionArgs": ["--version"]},
                {"name": "valhalla-build-tiles", "path": str(self.valhalla), "version": "3.7.0", "versionArgs": ["--version"]},
            ],
            "bounds": {"west": -79.7, "south": 43.6, "east": -79.2, "north": 44.1},
            "bufferKm": 10,
        }

    def tearDown(self) -> None:
        shutil.rmtree(self.root, ignore_errors=True)

    def test_builds_locked_portable_stage_with_provenance(self) -> None:
        calls: list[list[str]] = []
        runner = self._runner(calls)

        artifact_root, package_lock, definition = build_york_stage(
            self.recipe, self.root / "work", runner=runner, timeout_seconds=60
        )

        self.assertEqual(
            [entry["path"] for entry in package_lock["files"]],
            ["tiles/2/000/001.gph", "valhalla.json"],
        )
        self.assertEqual(
            json.loads((artifact_root / "valhalla.json").read_text(encoding="utf-8"))["mjolnir"]["tile_dir"],
            "${ROADWATCHER_TILE_DIR}",
        )
        self.assertEqual(definition["build"]["parameters"]["bufferKm"], 10)
        self.assertEqual(definition["build"]["parameters"]["tileFileCount"], 1)
        self.assertEqual({source["id"] for source in definition["sources"]}, {"osm-extract", "york-boundary", "valhalla-config"})
        self.assertEqual(len(calls), 4)
        self.assertIn("complete_ways", calls[2])

    def test_publishes_archive_lock_definition_and_verified_manifest_together(self) -> None:
        output = self.root / "release"
        result = build_release_asset(
            self.recipe, output, timeout_seconds=60, runner=self._runner([])
        )
        base = "york-valhalla-tiles-2026.07.13-test.1-windows-x86_64"
        expected = {
            f"{base}.zip",
            f"{base}.manifest.json",
            f"{base}.package-lock.json",
            f"{base}.definition.json",
        }
        self.assertEqual({path.name for path in output.iterdir()}, expected)
        manifest = json.loads((output / f"{base}.manifest.json").read_text(encoding="utf-8"))
        self.assertEqual(manifest["artifact"]["sha256"], result["sha256"])
        self.assertEqual(manifest["build"]["parameters"]["bufferKm"], 10)
        self.assertEqual(manifest["generatedAt"], self.recipe["sourceDateEpoch"])
        self.assertTrue(all(source["sizeBytes"] > 0 for source in manifest["sources"]))

    def test_rejects_insufficient_coverage_and_source_drift(self) -> None:
        insufficient = copy.deepcopy(self.recipe)
        insufficient["bounds"]["west"] = -79.51
        with self.assertRaisesRegex(RecipeError, "plus 10 km"):
            build_york_stage(insufficient, self.root / "insufficient", runner=lambda *_: "", timeout_seconds=60)

        drifted = copy.deepcopy(self.recipe)
        drifted["sources"][0]["downloadedSha256"] = "0" * 64
        with self.assertRaisesRegex(RecipeError, "SHA-256"):
            build_york_stage(drifted, self.root / "drifted", runner=lambda *_: "", timeout_seconds=60)

    def test_rejects_baked_tile_paths_and_unknown_recipe_fields(self) -> None:
        self.config.write_text('{"mjolnir":{"tile_dir":"C:/build-machine/tiles"}}', encoding="utf-8")
        baked = copy.deepcopy(self.recipe)
        config_source = next(source for source in baked["sources"] if source["role"] == "valhallaConfigTemplate")
        config_source["downloadedSha256"] = self._sha256(self.config)
        config_source["publisherSha256"] = config_source["downloadedSha256"]
        with self.assertRaisesRegex(RecipeError, "tile token"):
            build_york_stage(baked, self.root / "baked", runner=lambda *_: "", timeout_seconds=60)

        unsupported = copy.deepcopy(self.recipe)
        unsupported["downloadUrl"] = "https://example.com/unapproved"
        with self.assertRaisesRegex(RecipeError, "top-level"):
            validate_recipe(unsupported)

    def test_rejects_every_external_mjolnir_path(self) -> None:
        for path_key in (
            "tile_extract", "admin", "admins", "incident_dir", "timezones", "timezone",
            "traffic_extract", "transit_dir",
        ):
            with self.subTest(path_key=path_key):
                self.config.write_text(
                    json.dumps({
                        "mjolnir": {
                            "tile_dir": "${ROADWATCHER_TILE_DIR}",
                            path_key: "C:/untrusted",
                        }
                    }),
                    encoding="utf-8",
                )
                recipe = copy.deepcopy(self.recipe)
                config_source = next(
                    source for source in recipe["sources"]
                    if source["role"] == "valhallaConfigTemplate"
                )
                config_source["downloadedSha256"] = self._sha256(self.config)
                config_source["publisherSha256"] = config_source["downloadedSha256"]
                with self.assertRaisesRegex(RecipeError, path_key):
                    build_york_stage(
                        recipe,
                        self.root / f"external-{path_key}",
                        runner=lambda *_: "",
                        timeout_seconds=60,
                    )

    def _source(self, source_id: str, role: str, path: Path) -> dict[str, object]:
        digest = self._sha256(path)
        return {
            "id": source_id,
            "role": role,
            "localPath": str(path.resolve()),
            "url": f"https://example.com/{source_id}",
            "version": "approved-v1",
            "license": self._license("odbl-1-0", "ODbL 1.0"),
            "downloadedSha256": digest,
            "publisherSha256": digest,
            "retrievedAt": "2026-07-13T12:00:00Z",
        }

    def _runner(self, calls: list[list[str]]):
        def runner(args: list[str], _cwd: Path, _timeout: int, _limit: int) -> str:
            calls.append(args)
            if args[1:] == ["--version"]:
                return "osmium version 1.16.0" if Path(args[0]) == self.osmium else "Valhalla 3.7.0"
            if args[1] == "extract":
                Path(args[args.index("-o") + 1]).write_bytes(b"buffered-osm")
                return "extracted"
            config = json.loads(Path(args[args.index("-c") + 1]).read_text(encoding="utf-8"))
            tile_root = Path(config["mjolnir"]["tile_dir"])
            tile_root.joinpath("2", "000").mkdir(parents=True)
            tile_root.joinpath("2", "000", "001.gph").write_bytes(b"valhalla-tile")
            return "tiles built"

        return runner

    @staticmethod
    def _license(identifier: str, name: str) -> dict[str, str]:
        return {"id": identifier, "name": name, "url": "https://example.com/license"}

    def _write(self, name: str, content: bytes) -> Path:
        path = self.root / name
        path.write_bytes(content)
        return path.resolve()

    def _write_json(self, name: str, value: object) -> Path:
        path = self.root / name
        path.write_text(json.dumps(value), encoding="utf-8")
        return path.resolve()

    @staticmethod
    def _sha256(path: Path) -> str:
        return hashlib.sha256(path.read_bytes()).hexdigest()


if __name__ == "__main__":
    unittest.main()
