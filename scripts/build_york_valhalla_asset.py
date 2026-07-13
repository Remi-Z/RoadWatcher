"""Build a locked York Region + 10 km Valhalla release asset from local approved inputs."""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tempfile
import time
from typing import Callable

from deterministic_artifact_zip import BuildError as PackageError
from deterministic_artifact_zip import build_archive


class RecipeError(ValueError):
    """Raised when a release recipe or build output is unsafe or inconsistent."""


_ID = re.compile(r"^[a-z0-9-]+$")
_VERSION = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")
_SHA256 = re.compile(r"^[a-f0-9]{64}$")
_TOP_KEYS = {
    "schemaVersion", "id", "version", "platform", "sourceDateEpoch",
    "artifactLicense", "sources", "tools", "bounds", "bufferKm"
}
_SOURCE_KEYS = {
    "id", "role", "localPath", "url", "version", "license",
    "downloadedSha256", "publisherSha256", "retrievedAt", "etag", "lastModified"
}
_LICENSE_KEYS = {"id", "name", "url"}
_TOOL_KEYS = {"name", "path", "version", "versionArgs"}
_BOUND_KEYS = {"west", "south", "east", "north"}
_SOURCE_ROLES = {"osmExtract", "yorkBoundary", "valhallaConfigTemplate"}
_TOOL_NAMES = {"osmium", "valhalla-build-tiles"}
_MAX_BOUNDARY_BYTES = 64 * 1024 * 1024
_MAX_CONFIG_BYTES = 4 * 1024 * 1024
_MAX_PROCESS_OUTPUT_BYTES = 16 * 1024 * 1024
_COPY_CHUNK = 1024 * 1024
_REPARSE_POINT = 0x400
_TILE_TOKEN = "${ROADWATCHER_TILE_DIR}"

Runner = Callable[[list[str], Path, int, int], str]


def _reject_duplicate_keys(pairs: list[tuple[str, object]]) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise RecipeError(f"duplicate JSON key: {key}")
        result[key] = value
    return result


def load_recipe(path: Path) -> dict[str, object]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_reject_duplicate_keys)
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RecipeError(f"cannot read York build recipe: {error}") from error
    validate_recipe(value)
    return value


def validate_recipe(value: object) -> None:
    if not isinstance(value, dict) or set(value) != _TOP_KEYS:
        raise RecipeError("York recipe has unsupported or missing top-level fields")
    if value["schemaVersion"] != 1 or value["id"] != "york-valhalla-tiles":
        raise RecipeError("York recipe identity is invalid")
    if value["platform"] != "windows-x86_64":
        raise RecipeError("York recipe platform must be windows-x86_64")
    if not isinstance(value["version"], str) or not _VERSION.fullmatch(value["version"]):
        raise RecipeError("York artifact version is invalid")
    _source_date_epoch(value["sourceDateEpoch"])
    _license(value["artifactLicense"], "artifact license")
    if value["bufferKm"] != 10:
        raise RecipeError("York coverage buffer must be exactly 10 km")
    bounds = value["bounds"]
    if not isinstance(bounds, dict) or set(bounds) != _BOUND_KEYS:
        raise RecipeError("York extraction bounds are invalid")
    west, south, east, north = (_finite(bounds[name], f"bounds.{name}") for name in ("west", "south", "east", "north"))
    if not (-180 <= west < east <= 180 and -90 <= south < north <= 90):
        raise RecipeError("York extraction bounds are outside WGS84")

    sources = value["sources"]
    if not isinstance(sources, list) or len(sources) != len(_SOURCE_ROLES):
        raise RecipeError("York recipe must have exactly three source roles")
    roles: set[str] = set()
    ids: set[str] = set()
    for source in sources:
        _source(source)
        if source["role"] not in _SOURCE_ROLES or source["role"] in roles:
            raise RecipeError("York source roles are invalid or duplicated")
        if source["id"] in ids:
            raise RecipeError("York source ids are duplicated")
        roles.add(source["role"])
        ids.add(source["id"])
    if roles != _SOURCE_ROLES:
        raise RecipeError("York recipe source roles are incomplete")

    tools = value["tools"]
    if not isinstance(tools, list) or len(tools) != len(_TOOL_NAMES):
        raise RecipeError("York recipe must identify osmium and valhalla-build-tiles")
    names: set[str] = set()
    for tool in tools:
        if not isinstance(tool, dict) or set(tool) != _TOOL_KEYS or tool.get("name") not in _TOOL_NAMES:
            raise RecipeError("York build tool definition is invalid")
        if tool["name"] in names:
            raise RecipeError("York build tools are duplicated")
        names.add(tool["name"])
        _nonblank(tool["version"], "tool version", 100)
        _safe_local_file(tool["path"], f"{tool['name']} path")
        args = tool["versionArgs"]
        if not isinstance(args, list) or not 1 <= len(args) <= 4:
            raise RecipeError("tool version arguments are invalid")
        for argument in args:
            text = _nonblank(argument, "tool version argument", 40)
            if any(character in text for character in "\r\n\0"):
                raise RecipeError("tool version argument is unsafe")
    if names != _TOOL_NAMES:
        raise RecipeError("York build tools are incomplete")


def build_york_stage(
    recipe: dict[str, object],
    work_root: Path,
    *,
    runner: Runner | None = None,
    timeout_seconds: int = 14_400,
) -> tuple[Path, dict[str, object], dict[str, object]]:
    validate_recipe(recipe)
    if not 60 <= timeout_seconds <= 14_400:
        raise RecipeError("York build timeout must be between 60 seconds and 4 hours")
    runner = runner or _run_bounded
    work_root.mkdir(parents=True, exist_ok=False)
    artifact_root = work_root / "artifact"
    tiles = artifact_root / "tiles"
    tiles.mkdir(parents=True)

    sources = {source["role"]: source for source in recipe["sources"]}
    source_sizes: dict[str, int] = {}
    for source in sources.values():
        local_path = _safe_local_file(source["localPath"], f"{source['role']} source")
        actual = _sha256_file(local_path)
        if actual != source["downloadedSha256"]:
            raise RecipeError(f"{source['role']} SHA-256 does not match its approved recipe")
        source_sizes[source["role"]] = local_path.stat().st_size

    boundary_path = Path(sources["yorkBoundary"]["localPath"]).resolve(strict=True)
    boundary_bounds = _geojson_bounds(boundary_path)
    _verify_ten_kilometre_coverage(boundary_bounds, recipe["bounds"], recipe["bufferKm"])

    config_source = Path(sources["valhallaConfigTemplate"]["localPath"]).resolve(strict=True)
    config = _load_portable_config(config_source)
    shutil.copyfile(config_source, artifact_root / "valhalla.json")
    build_config = json.loads(json.dumps(config))
    build_config["mjolnir"]["tile_dir"] = str(tiles.resolve(strict=True))
    build_config["mjolnir"].pop("tile_extract", None)
    build_config_path = work_root / "valhalla.build.json"
    _write_new_json(build_config_path, build_config)

    tools = {tool["name"]: tool for tool in recipe["tools"]}
    for tool in tools.values():
        output = runner(
            [str(Path(tool["path"]).resolve(strict=True)), *tool["versionArgs"]],
            work_root,
            30,
            256 * 1024,
        )
        if not _version_output_matches(output, tool["version"]):
            raise RecipeError(f"{tool['name']} version output does not contain {tool['version']}")

    osmium_output = work_root / "york-buffered.osm.pbf"
    bounds = recipe["bounds"]
    bbox = ",".join(_stable_number(bounds[name]) for name in ("west", "south", "east", "north"))
    osmium = str(Path(tools["osmium"]["path"]).resolve(strict=True))
    osm_input = str(Path(sources["osmExtract"]["localPath"]).resolve(strict=True))
    runner(
        [osmium, "extract", "-b", bbox, "-s", "complete_ways", "-o", str(osmium_output), osm_input],
        work_root,
        timeout_seconds,
        _MAX_PROCESS_OUTPUT_BYTES,
    )
    if not osmium_output.is_file() or osmium_output.stat().st_size <= 0:
        raise RecipeError("osmium did not produce a non-empty buffered OSM input")

    valhalla = str(Path(tools["valhalla-build-tiles"]["path"]).resolve(strict=True))
    runner(
        [valhalla, "-c", str(build_config_path), str(osmium_output)],
        work_root,
        timeout_seconds,
        _MAX_PROCESS_OUTPUT_BYTES,
    )
    tile_files = _regular_tree_files(tiles)
    if not tile_files:
        raise RecipeError("Valhalla did not produce any tile files")

    package_files = _locked_files(artifact_root)
    package_lock = {
        "schemaVersion": 1,
        "id": recipe["id"],
        "sourceDateEpoch": recipe["sourceDateEpoch"],
        "files": package_files,
    }
    definition = {
        "id": recipe["id"],
        "kind": "tiles",
        "version": recipe["version"],
        "platform": recipe["platform"],
        "artifactLicense": recipe["artifactLicense"],
        "sources": [
            _manifest_source(source, source_sizes[source["role"]])
            for source in sorted(recipe["sources"], key=lambda item: item["role"])
        ],
        "tools": [
            {"name": tool["name"], "version": tool["version"]}
            for tool in sorted(recipe["tools"], key=lambda item: item["name"])
        ],
        "build": {
            "recipe": "scripts/build_york_valhalla_asset.py",
            "recipeVersion": "1",
            "parameters": {
                "bufferKm": 10,
                "configTemplateSha256": sources["valhallaConfigTemplate"]["downloadedSha256"],
                "east": bounds["east"],
                "extractStrategy": "complete_ways",
                "north": bounds["north"],
                "south": bounds["south"],
                "tileFileCount": len(tile_files),
                "west": bounds["west"],
            },
        },
    }
    return artifact_root, package_lock, definition


def build_release_asset(
    recipe: dict[str, object],
    output_directory: Path,
    *,
    timeout_seconds: int = 14_400,
    runner: Runner | None = None,
) -> dict[str, object]:
    validate_recipe(recipe)
    output_directory = output_directory.resolve(strict=False)
    output_directory.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".roadwatcher-york-build-", dir=output_directory))
    published: list[Path] = []
    try:
        artifact_root, package_lock, definition = build_york_stage(
            recipe, staging / "work", runner=runner, timeout_seconds=timeout_seconds
        )
        base = f"{recipe['id']}-{recipe['version']}-windows-x86_64"
        archive = staging / f"{base}.zip"
        manifest = staging / f"{base}.manifest.json"
        package_lock_path = staging / f"{base}.package-lock.json"
        definition_path = staging / f"{base}.definition.json"
        _write_new_json(package_lock_path, package_lock)
        _write_new_json(definition_path, definition)
        build_archive(package_lock, artifact_root, archive)

        node = shutil.which("node")
        if not node:
            raise RecipeError("Node.js is required to emit the managed artifact manifest")
        manifest_tool = Path(__file__).with_name("managed-artifact-manifest.mjs").resolve(strict=True)
        _run_bounded(
            [node, str(manifest_tool), "build", str(definition_path), str(archive), str(manifest), f"--generated-at={recipe['sourceDateEpoch']}"],
            staging,
            120,
            1024 * 1024,
        )
        _run_bounded(
            [node, str(manifest_tool), "verify", str(manifest), str(archive)],
            staging,
            120,
            1024 * 1024,
        )

        staged_outputs = [archive, manifest, package_lock_path, definition_path]
        final_outputs = [output_directory / path.name for path in staged_outputs]
        if any(path.exists() for path in final_outputs):
            raise RecipeError("one or more York release outputs already exist")
        for source, destination in zip(staged_outputs, final_outputs, strict=True):
            os.link(source, destination)
            published.append(destination)
        return {
            "id": recipe["id"],
            "version": recipe["version"],
            "archive": str(final_outputs[0]),
            "manifest": str(final_outputs[1]),
            "sha256": _sha256_file(final_outputs[0]),
            "sizeBytes": final_outputs[0].stat().st_size,
        }
    except (OSError, PackageError) as error:
        raise RecipeError(str(error)) from error
    finally:
        if sys.exc_info()[0] is not None:
            for path in published:
                path.unlink(missing_ok=True)
        shutil.rmtree(staging, ignore_errors=True)


def _source(source: object) -> None:
    if not isinstance(source, dict) or not set(source).issubset(_SOURCE_KEYS):
        raise RecipeError("York source definition has unsupported fields")
    required = _SOURCE_KEYS - {"etag", "lastModified"}
    if not required.issubset(source):
        raise RecipeError("York source definition is incomplete")
    if not isinstance(source["id"], str) or not _ID.fullmatch(source["id"]):
        raise RecipeError("York source id is invalid")
    _nonblank(source["role"], "source role", 50)
    _safe_local_file(source["localPath"], "source localPath")
    _https(source["url"], "source URL")
    _nonblank(source["version"], "source version", 200)
    _license(source["license"], "source license")
    _hash(source["downloadedSha256"], "downloaded source SHA-256")
    if source["publisherSha256"] is not None:
        _hash(source["publisherSha256"], "publisher source SHA-256")
    _iso_utc(source["retrievedAt"], "source retrievedAt")
    etag = source.get("etag")
    modified = source.get("lastModified")
    if source["publisherSha256"] is None and not etag and not modified:
        raise RecipeError("source without publisher hash needs ETag or Last-Modified evidence")
    if etag is not None:
        _nonblank(etag, "source ETag", 512)
    if modified is not None:
        _nonblank(modified, "source Last-Modified", 512)


def _license(value: object, label: str) -> None:
    if not isinstance(value, dict) or set(value) != _LICENSE_KEYS:
        raise RecipeError(f"{label} is invalid")
    if not isinstance(value["id"], str) or not _ID.fullmatch(value["id"]):
        raise RecipeError(f"{label} id is invalid")
    _nonblank(value["name"], f"{label} name", 200)
    _https(value["url"], f"{label} URL")


def _manifest_source(source: dict[str, object], size_bytes: int) -> dict[str, object]:
    result = {key: value for key, value in source.items() if key not in {"role", "localPath"}}
    result["sizeBytes"] = size_bytes
    return result


def _load_portable_config(path: Path) -> dict[str, object]:
    if path.stat().st_size > _MAX_CONFIG_BYTES:
        raise RecipeError("Valhalla config template exceeds 4 MiB")
    try:
        value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_reject_duplicate_keys)
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RecipeError(f"Valhalla config template is invalid: {error}") from error
    if not isinstance(value, dict) or not isinstance(value.get("mjolnir"), dict):
        raise RecipeError("Valhalla config template is missing mjolnir")
    mjolnir = value["mjolnir"]
    if mjolnir.get("tile_dir") != _TILE_TOKEN:
        raise RecipeError("Valhalla config template must use the RoadWatcher tile token")
    if mjolnir.get("tile_extract") not in (None, ""):
        raise RecipeError("Valhalla config template cannot select tile_extract")
    for path_key in (
        "admin", "admins", "incident_dir", "timezones", "timezone",
        "traffic_extract", "transit_dir"
    ):
        if mjolnir.get(path_key) not in (None, ""):
            raise RecipeError(f"Valhalla config template cannot bake mjolnir.{path_key}")
    return value


def _geojson_bounds(path: Path) -> tuple[float, float, float, float]:
    if path.stat().st_size > _MAX_BOUNDARY_BYTES:
        raise RecipeError("York boundary GeoJSON exceeds 64 MiB")
    try:
        document = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=_reject_duplicate_keys)
    except (OSError, UnicodeError, json.JSONDecodeError) as error:
        raise RecipeError(f"York boundary GeoJSON is invalid: {error}") from error
    if not isinstance(document, dict) or document.get("crs") is not None:
        raise RecipeError("York boundary must be unambiguous WGS84 GeoJSON")
    document_type = document.get("type")
    if document_type == "FeatureCollection" and isinstance(document.get("features"), list):
        stack: list[object] = [feature.get("geometry") for feature in document["features"] if isinstance(feature, dict)]
    elif document_type == "Feature":
        stack = [document.get("geometry")]
    elif isinstance(document_type, str):
        stack = [document]
    else:
        stack = []
    west, south, east, north = 180.0, 90.0, -180.0, -90.0
    count = 0
    while stack:
        value = stack.pop()
        if isinstance(value, dict):
            if value.get("type") == "GeometryCollection" and isinstance(value.get("geometries"), list):
                stack.extend(value["geometries"])
            elif "coordinates" in value:
                stack.append(value["coordinates"])
        elif isinstance(value, list):
            if len(value) >= 2 and all(isinstance(item, (int, float)) and not isinstance(item, bool) for item in value[:2]):
                longitude, latitude = float(value[0]), float(value[1])
                if not (math.isfinite(longitude) and math.isfinite(latitude) and -180 <= longitude <= 180 and -90 <= latitude <= 90):
                    raise RecipeError("York boundary contains an invalid WGS84 coordinate")
                west, south = min(west, longitude), min(south, latitude)
                east, north = max(east, longitude), max(north, latitude)
                count += 1
                if count > 5_000_000:
                    raise RecipeError("York boundary has too many coordinates")
            else:
                stack.extend(value)
    if count < 3 or west >= east or south >= north:
        raise RecipeError("York boundary does not contain a usable polygon extent")
    return west, south, east, north


def _verify_ten_kilometre_coverage(
    boundary: tuple[float, float, float, float], bounds: dict[str, object], buffer_km: object
) -> None:
    west, south, east, north = boundary
    latitude_buffer = float(buffer_km) / 111.32
    latitude = max(abs(south), abs(north)) + latitude_buffer
    longitude_scale = 111.32 * math.cos(math.radians(min(latitude, 89.0)))
    longitude_buffer = float(buffer_km) / longitude_scale
    tolerance = 1e-7
    if (
        float(bounds["west"]) > west - longitude_buffer + tolerance
        or float(bounds["east"]) < east + longitude_buffer - tolerance
        or float(bounds["south"]) > south - latitude_buffer + tolerance
        or float(bounds["north"]) < north + latitude_buffer - tolerance
    ):
        raise RecipeError("York extraction bounds do not cover the approved boundary plus 10 km")


def _regular_tree_files(root: Path) -> list[Path]:
    result: list[Path] = []
    for path in sorted(root.rglob("*")):
        metadata = path.lstat()
        if path.is_symlink() or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT:
            raise RecipeError("Valhalla output contains a link or reparse point")
        if stat.S_ISREG(metadata.st_mode):
            if metadata.st_size <= 0:
                raise RecipeError("Valhalla output contains an empty file")
            result.append(path)
        elif not stat.S_ISDIR(metadata.st_mode):
            raise RecipeError("Valhalla output contains a special file")
    return result


def _locked_files(root: Path) -> list[dict[str, str]]:
    files = _regular_tree_files(root)
    result = []
    for path in files:
        relative = PurePosixPath(path.relative_to(root).as_posix()).as_posix()
        result.append({"path": relative, "sha256": _sha256_file(path)})
    return result


def _run_bounded(args: list[str], cwd: Path, timeout_seconds: int, max_output_bytes: int) -> str:
    stdout_path = cwd / f".process-{time.time_ns()}.stdout"
    stderr_path = cwd / f".process-{time.time_ns()}.stderr"
    with stdout_path.open("wb") as stdout, stderr_path.open("wb") as stderr:
        process = subprocess.Popen(args, cwd=cwd, stdin=subprocess.DEVNULL, stdout=stdout, stderr=stderr, shell=False)
        deadline = time.monotonic() + timeout_seconds
        while process.poll() is None:
            if time.monotonic() > deadline:
                process.kill()
                process.wait()
                raise RecipeError(f"process timed out after {timeout_seconds} seconds: {Path(args[0]).name}")
            if stdout_path.stat().st_size + stderr_path.stat().st_size > max_output_bytes:
                process.kill()
                process.wait()
                raise RecipeError(f"process output exceeded its bound: {Path(args[0]).name}")
            time.sleep(0.1)
    if stdout_path.stat().st_size + stderr_path.stat().st_size > max_output_bytes:
        raise RecipeError(f"process output exceeded its bound: {Path(args[0]).name}")
    output = stdout_path.read_bytes() + b"\n" + stderr_path.read_bytes()
    stdout_path.unlink(missing_ok=True)
    stderr_path.unlink(missing_ok=True)
    text = output.decode("utf-8", errors="replace")
    if process.returncode != 0:
        raise RecipeError(f"process exited with {process.returncode}: {Path(args[0]).name}: {text[-4000:]}")
    return text


def _write_new_json(path: Path, value: object) -> None:
    with path.open("x", encoding="utf-8", newline="\n") as output:
        json.dump(value, output, indent=2, sort_keys=True, ensure_ascii=True)
        output.write("\n")


def _safe_local_file(value: object, label: str) -> Path:
    text = _nonblank(value, label, 32_000)
    path = Path(text)
    if not path.is_absolute():
        raise RecipeError(f"{label} must be absolute")
    try:
        metadata = path.lstat()
        resolved = path.resolve(strict=True)
    except OSError as error:
        raise RecipeError(f"{label} is unavailable: {error}") from error
    if path.is_symlink() or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT or not stat.S_ISREG(metadata.st_mode):
        raise RecipeError(f"{label} must be a regular non-link file")
    return resolved


def _hash(value: object, label: str) -> str:
    if not isinstance(value, str) or not _SHA256.fullmatch(value):
        raise RecipeError(f"{label} is invalid")
    return value


def _https(value: object, label: str) -> str:
    text = _nonblank(value, label, 2048)
    if not text.startswith("https://") or "\r" in text or "\n" in text:
        raise RecipeError(f"{label} must use HTTPS")
    return text


def _nonblank(value: object, label: str, limit: int) -> str:
    if not isinstance(value, str) or not value.strip() or len(value) > limit or "\0" in value:
        raise RecipeError(f"{label} is invalid")
    return value.strip()


def _finite(value: object, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(float(value)):
        raise RecipeError(f"{label} must be finite")
    return float(value)


def _iso_utc(value: object, label: str) -> str:
    text = _nonblank(value, label, 50)
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z", text):
        raise RecipeError(f"{label} is invalid")
    try:
        parsed = dt.datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError as error:
        raise RecipeError(f"{label} is invalid") from error
    if not text.endswith("Z") or parsed.tzinfo != dt.timezone.utc:
        raise RecipeError(f"{label} must be UTC")
    return text


def _source_date_epoch(value: object) -> str:
    text = _iso_utc(value, "sourceDateEpoch")
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", text):
        raise RecipeError("sourceDateEpoch must have whole-second precision")
    return text


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(_COPY_CHUNK):
            digest.update(chunk)
    return digest.hexdigest()


def _stable_number(value: object) -> str:
    return format(float(value), ".10g")


def _version_output_matches(output: str, version: object) -> bool:
    text = _nonblank(version, "tool version", 100)
    return re.search(rf"(?<![0-9A-Za-z]){re.escape(text)}(?![0-9A-Za-z])", output) is not None


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recipe", type=Path, help="strict owner-approved York recipe JSON")
    parser.add_argument("output_directory", type=Path, help="directory for new release outputs")
    parser.add_argument("--timeout-seconds", type=int, default=14_400, help="per-build-command timeout (60-14400)")
    args = parser.parse_args(argv)
    try:
        result = build_release_asset(load_recipe(args.recipe), args.output_directory, timeout_seconds=args.timeout_seconds)
    except (RecipeError, OSError) as error:
        parser.exit(1, f"York Valhalla asset build failed: {error}\n")
    print(json.dumps(result, sort_keys=True, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
