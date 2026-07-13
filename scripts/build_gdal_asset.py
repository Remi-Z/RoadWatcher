"""Build a source-locked, minimal Windows GDAL/OGR release asset.

This is deliberately not a general purpose build runner.  It accepts a narrow
recipe containing only locally available, hash-locked source trees, dependency
prefixes, license material, and tool binaries.  The executable commands and
CMake vector are fixed in this file; recipes never carry commands, URLs to
download, tool arguments, or install destinations.
"""

from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import time
from typing import Callable

from deterministic_artifact_zip import BuildError as PackageError
from deterministic_artifact_zip import build_archive


class RecipeError(ValueError):
    """Raised when a GDAL release recipe, input, or output is unsafe."""


_ID = re.compile(r"^[a-z0-9-]+$")
_VERSION = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")
_SHA256 = re.compile(r"^[a-f0-9]{64}$")
_RUNTIME_DLL = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.dll$", re.IGNORECASE)
_LICENSE_FILE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._ /-]{0,199}$")

_GDAL_COMMIT = "f2ff911fee59d4b647dd7b2c030c389c9c062d8c"
_GDAL_VERSION = "3.12.4"
_GDAL_RECIPE_SCHEMA_VERSION = 2
_PROFILE = "roadwatcher-minimal-open-vector-v1"
_TOP_KEYS = {
    "schemaVersion",
    "id",
    "version",
    "platform",
    "sourceDateEpoch",
    "artifactLicense",
    "sources",
    "tools",
    "runtimeFiles",
    "expectedOgrFormats",
    "expectedGdalFormats",
}
_SOURCE_COMMON_KEYS = {
    "id",
    "role",
    "localPath",
    "url",
    "version",
    "license",
    "noticeFiles",
    "treeSha256",
    "publisherSha256",
    "retrievedAt",
}
_GDAL_SOURCE_KEYS = _SOURCE_COMMON_KEYS | {
    "commit",
    "archivePath",
    "archiveSha256",
    "archiveSizeBytes",
    "archiveFormat",
    "archiveRoot",
}
_LICENSE_KEYS = {"id", "name", "url"}
_TOOL_KEYS = {"name", "path", "sha256", "version"}
_RUNTIME_FILE_KEYS = {"name", "sha256", "licenses"}
_SOURCE_ROLES = {"gdalSource", "dependencyPrefix", "licenseBundle"}
_TOOL_NAMES = {"cmake", "ninja", "msvc-cl", "msvc-link", "dumpbin", "node"}
_COPY_CHUNK = 1024 * 1024
_MAX_INPUT_FILES = 200_000
_MAX_INPUT_BYTES = 8 * 1024 * 1024 * 1024
_MAX_SOURCE_ARCHIVE_BYTES = 1024 * 1024 * 1024
_MAX_ARTIFACT_FILES = 25_000
_MAX_ARTIFACT_BYTES = 4 * 1024 * 1024 * 1024
_MAX_PROCESS_OUTPUT_BYTES = 16 * 1024 * 1024
_REPARSE_POINT = 0x400

# CMake's documented minimal-build controls are used before the first
# configuration.  Explicit OGR enables are intentional: GeoJSON is a
# non-disableable GDAL built-in and remains required for RoadWatcher's
# normalization output, while the five entries here cover the approved vector
# input surface.
_CACHE_EXPECTATIONS = {
    "BUILD_APPS": "ON",
    "BUILD_SHARED_LIBS": "ON",
    "BUILD_TESTING": "OFF",
    "BUILD_PYTHON_BINDINGS": "OFF",
    "BUILD_JAVA_BINDINGS": "OFF",
    "BUILD_CSHARP_BINDINGS": "OFF",
    "GDAL_BUILD_OPTIONAL_DRIVERS": "OFF",
    "OGR_BUILD_OPTIONAL_DRIVERS": "OFF",
    "GDAL_ENABLE_PLUGINS": "OFF",
    "GDAL_ENABLE_PLUGINS_NO_DEPS": "OFF",
    "GDAL_AUTOLOAD_PLUGINS": "OFF",
    "GDAL_USE_EXTERNAL_LIBS": "OFF",
    "GDAL_USE_INTERNAL_LIBS": "ON",
    "GDAL_USE_CURL": "OFF",
    "GDAL_VRT_ENABLE_RAWRASTERBAND": "OFF",
    "GDAL_USE_PROJ": "ON",
    "GDAL_USE_SQLITE3": "ON",
    "GDAL_USE_PUBLICDECOMPWT": "OFF",
    "OGR_ENABLE_DRIVER_SHAPE": "ON",
    "OGR_ENABLE_DRIVER_GPKG": "ON",
    "OGR_ENABLE_DRIVER_SQLITE": "ON",
    "OGR_ENABLE_DRIVER_FLATGEOBUF": "ON",
    "OGR_ENABLE_DRIVER_OPENFILEGDB": "ON",
    "OGR_ENABLE_DRIVER_PG": "OFF",
    "OGR_ENABLE_DRIVER_MYSQL": "OFF",
    "OGR_ENABLE_DRIVER_MSSQLSPATIAL": "OFF",
    "OGR_ENABLE_DRIVER_ODBC": "OFF",
    "OGR_ENABLE_DRIVER_OCI": "OFF",
    "OGR_ENABLE_DRIVER_FILEGDB": "OFF",
    "OGR_ENABLE_DRIVER_SDE": "OFF",
    "OGR_ENABLE_DRIVER_WFS": "OFF",
    "OGR_ENABLE_DRIVER_WMS": "OFF",
    "OGR_ENABLE_DRIVER_WMTS": "OFF",
    "OGR_ENABLE_DRIVER_OAPIF": "OFF",
    "OGR_ENABLE_DRIVER_CSW": "OFF",
    "OGR_ENABLE_DRIVER_ELASTIC": "OFF",
    "CMAKE_DISABLE_FIND_PACKAGE_CURL": "ON",
    "CMAKE_DISABLE_FIND_PACKAGE_Git": "ON",
    "CMAKE_FIND_USE_CMAKE_PATH": "FALSE",
    "CMAKE_FIND_USE_CMAKE_ENVIRONMENT_PATH": "FALSE",
    "CMAKE_FIND_USE_SYSTEM_ENVIRONMENT_PATH": "FALSE",
    "CMAKE_FIND_USE_CMAKE_SYSTEM_PATH": "FALSE",
    "CMAKE_FIND_USE_INSTALL_PREFIX": "FALSE",
    "CMAKE_FIND_USE_PACKAGE_ROOT_PATH": "FALSE",
    "CMAKE_FIND_USE_PACKAGE_REGISTRY": "FALSE",
    "CMAKE_FIND_USE_SYSTEM_PACKAGE_REGISTRY": "FALSE",
    "CMAKE_FIND_ROOT_PATH_MODE_PACKAGE": "ONLY",
    "CMAKE_FIND_ROOT_PATH_MODE_INCLUDE": "ONLY",
    "CMAKE_FIND_ROOT_PATH_MODE_LIBRARY": "ONLY",
    "CMAKE_FIND_ROOT_PATH_MODE_PROGRAM": "NEVER",
    "FETCHCONTENT_FULLY_DISCONNECTED": "ON",
    "FETCHCONTENT_UPDATES_DISCONNECTED": "ON",
    "CMAKE_MSVC_RUNTIME_LIBRARY": "MultiThreaded",
}
_CMAKE_STRING_CACHE_KEYS = {
    "GDAL_USE_INTERNAL_LIBS",
    "CMAKE_MSVC_RUNTIME_LIBRARY",
    "CMAKE_FIND_ROOT_PATH_MODE_PACKAGE",
    "CMAKE_FIND_ROOT_PATH_MODE_INCLUDE",
    "CMAKE_FIND_ROOT_PATH_MODE_LIBRARY",
    "CMAKE_FIND_ROOT_PATH_MODE_PROGRAM",
}
_CMAKE_FLAG_EXPECTATIONS = {
    "CMAKE_C_FLAGS_RELEASE": "/O2 /Brepro",
    "CMAKE_CXX_FLAGS_RELEASE": "/O2 /Brepro",
    "CMAKE_SHARED_LINKER_FLAGS_RELEASE": "/Brepro",
    "CMAKE_EXE_LINKER_FLAGS_RELEASE": "/Brepro",
}
_REQUIRED_FORMATS = {
    "ESRI Shapefile",
    "GPKG",
    "SQLite",
    "FlatGeobuf",
    "OpenFileGDB",
    "GeoJSON",
}
_FORBIDDEN_FORMATS = {
    "PostgreSQL",
    "PGDump",
    "MySQL",
    "MSSQLSpatial",
    "OCI",
    "ODBC",
    "PGeo",
    "FileGDB",
    "SDE",
    "WFS",
    "WMS",
    "WMTS",
    "OAPIF",
    "CSW",
    "ElasticSearch",
    "MongoDBv3",
    "Parquet",
    "Arrow",
    "ECW",
    "JP2ECW",
    "MrSID",
    "PDF",
}
_FORBIDDEN_RUNTIME_TOKENS = (
    "curl",
    "pq",
    "postgres",
    "mysql",
    "mariadb",
    "odbc",
    "oci",
    "oracle",
    "spatialite",
    "geos",
    "sde",
    "ecw",
    "mrsid",
    "filegdb",
    "mongo",
    "arrow",
    "parquet",
)
_WINDOWS_SYSTEM_DLLS = {
    "advapi32.dll",
    "bcrypt.dll",
    "combase.dll",
    "crypt32.dll",
    "gdi32.dll",
    "kernel32.dll",
    "kernelbase.dll",
    "msvcp_win.dll",
    "ntdll.dll",
    "ole32.dll",
    "oleaut32.dll",
    "rpcrt4.dll",
    "secur32.dll",
    "shell32.dll",
    "shlwapi.dll",
    "user32.dll",
    "version.dll",
    "winhttp.dll",
    "winmm.dll",
    "winspool.drv",
    "ws2_32.dll",
}
_INHERITED_RUNTIME_VARIABLES = {
    "GDAL_CONFIG_FILE",
    "GDAL_DATA",
    "GDAL_DRIVER_PATH",
    "GDAL_PYTHON_DRIVER_PATH",
    "GDAL_SKIP",
    "GDAL_PAM_ENABLED",
    "OGR_DRIVER_PATH",
    "OGR_SKIP",
    "PROJ_AUX_DB",
    "PROJ_CURL_CA_BUNDLE",
    "PROJ_DATA",
    "PROJ_LIB",
    "PROJ_NETWORK",
    "PROJ_NETWORK_ENDPOINT",
    "PROJ_USER_WRITABLE_DIRECTORY",
    "PYTHONSO",
    "CPL_CONFIG",
}
_NETWORK_VARIABLES = {
    "ALL_PROXY",
    "FTP_PROXY",
    "HTTPS_PROXY",
    "HTTP_PROXY",
    "NO_PROXY",
    "all_proxy",
    "ftp_proxy",
    "https_proxy",
    "http_proxy",
    "no_proxy",
}
_INHERITED_BUILD_VARIABLES = {
    "CC",
    "CXX",
    "CL",
    "LINK",
    "CFLAGS",
    "CXXFLAGS",
    "CPPFLAGS",
    "LDFLAGS",
    "CPATH",
    "C_INCLUDE_PATH",
    "CPLUS_INCLUDE_PATH",
    "LIBRARY_PATH",
    "PKG_CONFIG_PATH",
    "PKG_CONFIG_LIBDIR",
    "PKG_CONFIG_SYSROOT_DIR",
    "CMAKE_ARGS",
    "CMAKE_BUILD_PARALLEL_LEVEL",
    "CMAKE_BUILD_TYPE",
    "CMAKE_CONFIGURATION_TYPES",
    "CMAKE_GENERATOR",
    "CMAKE_GENERATOR_INSTANCE",
    "CMAKE_GENERATOR_PLATFORM",
    "CMAKE_GENERATOR_TOOLSET",
    "CMAKE_PREFIX_PATH",
    "CMAKE_TOOLCHAIN_FILE",
    "PROJ_ROOT",
    "SQLite3_ROOT",
    "VCPKG_ROOT",
    "VCPKG_DEFAULT_TRIPLET",
    "VCPKG_FEATURE_FLAGS",
    "VCPKG_BINARY_SOURCES",
    "VCPKG_KEEP_ENV_VARS",
}
_EPSG_26917_SAMPLE = (630000.0, 4860000.0)
_EPSG_26917_EXPECTED_WGS84 = (-79.381751, 43.881643)
_EPSG_26917_TOLERANCE_DEGREES = 0.0002

Runner = Callable[[list[str], Path, int, int, dict[str, str]], str]


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
        raise RecipeError(f"cannot read GDAL build recipe: {error}") from error
    validate_recipe(value)
    return value


def validate_recipe(value: object) -> None:
    """Validate the deliberately narrow, source-only recipe contract."""
    if not isinstance(value, dict) or set(value) != _TOP_KEYS:
        raise RecipeError("GDAL recipe has unsupported or missing top-level fields")
    if value["schemaVersion"] != _GDAL_RECIPE_SCHEMA_VERSION or value["id"] != "gdal-ogr":
        raise RecipeError("GDAL recipe identity is invalid")
    if value["platform"] != "windows-x86_64":
        raise RecipeError("GDAL recipe platform must be windows-x86_64")
    if not isinstance(value["version"], str) or not _VERSION.fullmatch(value["version"]):
        raise RecipeError("GDAL artifact version is invalid")
    _source_date_epoch(value["sourceDateEpoch"])
    _license(value["artifactLicense"], "artifact license")

    sources = value["sources"]
    if not isinstance(sources, list) or len(sources) != len(_SOURCE_ROLES):
        raise RecipeError("GDAL recipe must contain source, dependency-prefix, and license-bundle inputs")
    roles: set[str] = set()
    ids: set[str] = set()
    for source in sources:
        _source(source)
        assert isinstance(source, dict)
        role = source["role"]
        if role not in _SOURCE_ROLES or role in roles:
            raise RecipeError("GDAL source roles are invalid or duplicated")
        if source["id"] in ids:
            raise RecipeError("GDAL source ids are duplicated")
        roles.add(role)
        ids.add(source["id"])
    if roles != _SOURCE_ROLES:
        raise RecipeError("GDAL source roles are incomplete")

    expected_ogr_formats = _format_inventory(value["expectedOgrFormats"], "expectedOgrFormats")
    expected_gdal_formats = _format_inventory(value["expectedGdalFormats"], "expectedGdalFormats")
    if not expected_ogr_formats <= expected_gdal_formats:
        raise RecipeError("expectedGdalFormats must include every expectedOgrFormats entry")
    missing_required = sorted(_REQUIRED_FORMATS - expected_ogr_formats)
    forbidden_declared = sorted(_FORBIDDEN_FORMATS & expected_gdal_formats)
    if missing_required:
        raise RecipeError("expectedOgrFormats lacks required RoadWatcher formats: " + ", ".join(missing_required))
    if forbidden_declared:
        raise RecipeError("expected GDAL inventory includes forbidden formats: " + ", ".join(forbidden_declared))

    tools = value["tools"]
    if not isinstance(tools, list) or len(tools) != len(_TOOL_NAMES):
        raise RecipeError("GDAL recipe must lock CMake, Ninja, MSVC cl/link, dumpbin, and Node")
    names: set[str] = set()
    for tool in tools:
        _tool(tool)
        assert isinstance(tool, dict)
        if tool["name"] in names:
            raise RecipeError("GDAL build tools are duplicated")
        names.add(tool["name"])
    if names != _TOOL_NAMES:
        raise RecipeError("GDAL build tools are incomplete")

    runtime_files = value["runtimeFiles"]
    if not isinstance(runtime_files, list) or len(runtime_files) > 64:
        raise RecipeError("GDAL runtimeFiles must be a bounded array")
    seen_runtime: set[str] = set()
    for entry in runtime_files:
        _runtime_file(entry)
        assert isinstance(entry, dict)
        lower = entry["name"].lower()
        if lower in seen_runtime:
            raise RecipeError("GDAL runtime files are duplicated")
        seen_runtime.add(lower)


def build_gdal_stage(
    recipe: dict[str, object],
    work_root: Path,
    *,
    runner: Runner | None = None,
    timeout_seconds: int = 21_600,
) -> tuple[Path, dict[str, object], dict[str, object]]:
    """Build a staged, hash-locked GDAL package without publishing anything."""
    validate_recipe(recipe)
    if not 60 <= timeout_seconds <= 21_600:
        raise RecipeError("GDAL build timeout must be between 60 seconds and 6 hours")
    runner = runner or _run_bounded
    work_root.mkdir(parents=True, exist_ok=False)

    sources = {source["role"]: source for source in recipe["sources"]}
    tools = {tool["name"]: tool for tool in recipe["tools"]}
    assert all(isinstance(source, dict) for source in sources.values())
    assert all(isinstance(tool, dict) for tool in tools.values())

    _verify_gdal_source_archive(sources["gdalSource"])

    source_root = _copy_verified_tree(
        _safe_local_directory(sources["gdalSource"]["localPath"], "GDAL source"),
        work_root / "source",
        sources["gdalSource"]["treeSha256"],
        "GDAL source",
    )
    prefix_root = _copy_verified_tree(
        _safe_local_directory(sources["dependencyPrefix"]["localPath"], "dependency prefix"),
        work_root / "prefix",
        sources["dependencyPrefix"]["treeSha256"],
        "dependency prefix",
    )
    license_root = _copy_verified_tree(
        _safe_local_directory(sources["licenseBundle"]["localPath"], "license bundle"),
        work_root / "license-bundle",
        sources["licenseBundle"]["treeSha256"],
        "license bundle",
    )
    staged_source_sizes = {
        "gdalSource": _tree_size_bytes(source_root, "staged GDAL source"),
        "dependencyPrefix": _tree_size_bytes(prefix_root, "staged dependency prefix"),
        "licenseBundle": _tree_size_bytes(license_root, "staged license bundle"),
    }
    if not (source_root / "CMakeLists.txt").is_file():
        raise RecipeError("pinned GDAL source does not contain CMakeLists.txt")

    _validate_dependency_prefix(prefix_root)
    _validate_runtime_inputs(prefix_root, license_root, sources, recipe["runtimeFiles"])
    _verify_tools(tools)

    build_root = work_root / "build"
    install_root = work_root / "installed"
    artifact_root = work_root / "artifact"
    # CMake records the prefix during configuration but normally creates it
    # only at install time.  Materialize our owned, empty prefix first so the
    # cache identity check can resolve it strictly rather than accepting a
    # merely lexical path.
    install_root.mkdir()
    configure_environment = _build_environment(recipe["sourceDateEpoch"])
    cmake = _tool_path(tools["cmake"], "cmake")
    ninja = _tool_path(tools["ninja"], "ninja")
    compiler = _tool_path(tools["msvc-cl"], "msvc-cl")
    linker = _tool_path(tools["msvc-link"], "msvc-link")
    dumpbin = _tool_path(tools["dumpbin"], "dumpbin")
    node = _tool_path(tools["node"], "node")

    _verify_tool_hash(cmake, tools["cmake"]["sha256"], "cmake")
    _verify_tool_hash(ninja, tools["ninja"]["sha256"], "ninja")
    _assert_version(
        runner([str(cmake), "--version"], work_root, 60, 256 * 1024, configure_environment),
        tools["cmake"]["version"],
        "CMake",
    )
    _assert_version(
        runner([str(ninja), "--version"], work_root, 60, 256 * 1024, configure_environment),
        tools["ninja"]["version"],
        "Ninja",
    )
    _assert_version(
        runner([str(node), "--version"], work_root, 60, 256 * 1024, configure_environment),
        tools["node"]["version"],
        "Node.js",
    )

    configure_args = _configure_args(
        cmake,
        ninja,
        compiler,
        linker,
        source_root,
        build_root,
        install_root,
        prefix_root,
    )
    configure_output = runner(configure_args, work_root, timeout_seconds, _MAX_PROCESS_OUTPUT_BYTES, configure_environment)
    _validate_configure_output(configure_output)
    _validate_cmake_cache(build_root / "CMakeCache.txt", source_root, prefix_root, install_root, ninja, compiler, linker)

    # The exact target and parallelism are intentionally fixed.  There is no
    # recipe-controlled build command, target, environment, or output path.
    _verify_tool_hash(cmake, tools["cmake"]["sha256"], "cmake")
    _verify_tool_hash(ninja, tools["ninja"]["sha256"], "ninja")
    _verify_tool_hash(compiler, tools["msvc-cl"]["sha256"], "msvc-cl")
    _verify_tool_hash(linker, tools["msvc-link"]["sha256"], "msvc-link")
    _verify_tool_hash(dumpbin, tools["dumpbin"]["sha256"], "dumpbin")
    _verify_tool_hash(node, tools["node"]["sha256"], "node")
    runner(
        [str(cmake), "--build", _cmake_path(build_root), "--target", "install", "--parallel", "1"],
        work_root,
        timeout_seconds,
        _MAX_PROCESS_OUTPUT_BYTES,
        configure_environment,
    )

    gdalinfo = install_root / "bin" / "gdalinfo.exe"
    if not gdalinfo.is_file() or gdalinfo.is_symlink():
        raise RecipeError("CMake install is missing build-only gdalinfo.exe for driver inventory qualification")
    _assemble_artifact(install_root, prefix_root, license_root, recipe["runtimeFiles"], artifact_root)
    runtime_environment = _runtime_environment(artifact_root, recipe["sourceDateEpoch"])
    _validate_runtime(
        artifact_root,
        sources["gdalSource"]["version"],
        gdalinfo,
        recipe["expectedOgrFormats"],
        recipe["expectedGdalFormats"],
        dumpbin,
        recipe["runtimeFiles"],
        runner,
        runtime_environment,
    )

    package_lock = {
        "schemaVersion": 1,
        "id": recipe["id"],
        "sourceDateEpoch": recipe["sourceDateEpoch"],
        "files": _locked_files(artifact_root),
    }
    definition = {
        "id": recipe["id"],
        "kind": "executable",
        "version": recipe["version"],
        "platform": recipe["platform"],
        "artifactLicense": recipe["artifactLicense"],
        "sources": [
            _manifest_source(source, staged_source_sizes[source["role"]])
            for source in sorted(recipe["sources"], key=lambda item: item["role"])
        ],
        "tools": [
            {"name": tool["name"], "version": tool["version"]}
            for tool in sorted(recipe["tools"], key=lambda item: item["name"])
        ],
        "build": {
            "recipe": "scripts/build_gdal_asset.py",
            "recipeVersion": "1",
            "parameters": {
                "curl": False,
                "gdalCommit": _GDAL_COMMIT,
                "gdalSourceArchiveSha256": sources["gdalSource"]["archiveSha256"],
                "gdalSourceTreeSha256": sources["gdalSource"]["treeSha256"],
                "expectedGdalFormatInventorySha256": _format_inventory_digest(recipe["expectedGdalFormats"]),
                "expectedOgrFormatInventorySha256": _format_inventory_digest(recipe["expectedOgrFormats"]),
                "licenseBundleTreeSha256": sources["licenseBundle"]["treeSha256"],
                "optionalDrivers": False,
                "plugins": False,
                "profile": _PROFILE,
                "projNetwork": False,
                "runtimeFileCount": len(recipe["runtimeFiles"]),
                "sourceDateEpoch": recipe["sourceDateEpoch"],
                "toolchainTreeSha256": sources["dependencyPrefix"]["treeSha256"],
            },
        },
    }
    return artifact_root, package_lock, definition


def build_release_asset(
    recipe: dict[str, object],
    output_directory: Path,
    *,
    timeout_seconds: int = 21_600,
    runner: Runner | None = None,
) -> dict[str, object]:
    """Build and atomically publish the ZIP, lock, definition, and manifest."""
    validate_recipe(recipe)
    runner = runner or _run_bounded
    tools = {tool["name"]: tool for tool in recipe["tools"]}
    assert all(isinstance(tool, dict) for tool in tools.values())
    node = _tool_path(tools["node"], "node")
    _verify_tool_hash(node, tools["node"]["sha256"], "node")
    output_directory = output_directory.resolve(strict=False)
    output_directory.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".roadwatcher-gdal-build-", dir=output_directory))
    published: list[Path] = []
    try:
        artifact_root, package_lock, definition = build_gdal_stage(
            recipe, staging / "work", runner=runner, timeout_seconds=timeout_seconds
        )
        base = f"{recipe['id']}-{recipe['version']}-windows-x86_64"
        archive = staging / f"{base}.zip"
        package_lock_path = staging / f"{base}.package-lock.json"
        definition_path = staging / f"{base}.definition.json"
        manifest_path = staging / f"{base}.manifest.json"
        _write_new_json(package_lock_path, package_lock)
        _write_new_json(definition_path, definition)
        build_archive(package_lock, artifact_root, archive)
        _verify_tool_hash(node, tools["node"]["sha256"], "node")
        _emit_and_verify_manifest(
            definition,
            archive,
            manifest_path,
            recipe["sourceDateEpoch"],
            node,
            runner,
            staging,
        )

        staged_outputs = [archive, manifest_path, package_lock_path, definition_path]
        final_outputs = [output_directory / path.name for path in staged_outputs]
        if any(path.exists() for path in final_outputs):
            raise RecipeError("one or more GDAL release outputs already exist")
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


def _source(value: object) -> None:
    if not isinstance(value, dict):
        raise RecipeError("GDAL source definition is invalid")
    role = value.get("role")
    expected = _GDAL_SOURCE_KEYS if role == "gdalSource" else _SOURCE_COMMON_KEYS
    if set(value) != expected:
        raise RecipeError("GDAL source definition has unsupported or missing fields")
    if not isinstance(value.get("id"), str) or not _ID.fullmatch(value["id"]):
        raise RecipeError("GDAL source id is invalid")
    _nonblank(role, "GDAL source role", 50)
    _safe_local_directory(value["localPath"], "GDAL source localPath")
    _https(value["url"], "GDAL source URL")
    _nonblank(value["version"], "GDAL source version", 200)
    _license(value["license"], "GDAL source license")
    notice_files = value["noticeFiles"]
    if not isinstance(notice_files, list) or not notice_files or len(notice_files) > 128:
        raise RecipeError("GDAL source noticeFiles are invalid")
    seen_notice_files: set[str] = set()
    for notice_file in notice_files:
        normalized_notice_file = _license_path(notice_file)
        if normalized_notice_file.lower() in seen_notice_files:
            raise RecipeError("GDAL source noticeFiles are duplicated")
        seen_notice_files.add(normalized_notice_file.lower())
    _hash(value["treeSha256"], "GDAL source tree SHA-256")
    _hash(value["publisherSha256"], "GDAL publisher source SHA-256")
    _iso_utc(value["retrievedAt"], "GDAL source retrievedAt")
    if role == "gdalSource" and (
        value["commit"] != _GDAL_COMMIT or value["version"] != _GDAL_VERSION
    ):
        raise RecipeError("GDAL source must use pinned 3.12.4 commit f2ff911fee59d4b647dd7b2c030c389c9c062d8c")
    if role == "gdalSource":
        archive = _safe_local_file(value["archivePath"], "GDAL source archivePath")
        size_bytes = value["archiveSizeBytes"]
        if not isinstance(size_bytes, int) or isinstance(size_bytes, bool) or size_bytes <= 0:
            raise RecipeError("GDAL source archiveSizeBytes is invalid")
        if archive.stat().st_size != size_bytes:
            raise RecipeError("GDAL source archiveSizeBytes does not match the retained archive")
        if archive.stat().st_size <= 0 or archive.stat().st_size > _MAX_SOURCE_ARCHIVE_BYTES:
            raise RecipeError("GDAL source archive exceeds its size bound")
        _hash(value["archiveSha256"], "GDAL source archive SHA-256")
        if value["archiveFormat"] != "tar.gz":
            raise RecipeError("GDAL source archiveFormat must be tar.gz")
        if value["archiveRoot"] != f"gdal-{_GDAL_VERSION}":
            raise RecipeError("GDAL source archiveRoot must match the pinned GDAL release root")


def _format_inventory(value: object, label: str) -> set[str]:
    if not isinstance(value, list) or not value or len(value) > 512:
        raise RecipeError(f"{label} must be a non-empty bounded list")
    entries: list[str] = []
    for entry in value:
        text = _nonblank(entry, f"{label} entry", 100)
        if "\r" in text or "\n" in text or "\0" in text:
            raise RecipeError(f"{label} entry is invalid")
        entries.append(text)
    if entries != sorted(entries) or len(set(entries)) != len(entries):
        raise RecipeError(f"{label} must be sorted and unique")
    return set(entries)


def _tool(value: object) -> None:
    if not isinstance(value, dict) or set(value) != _TOOL_KEYS or value.get("name") not in _TOOL_NAMES:
        raise RecipeError("GDAL build tool definition is invalid")
    _safe_local_file(value["path"], f"{value['name']} path")
    _hash(value["sha256"], f"{value['name']} SHA-256")
    if not isinstance(value["version"], str) or not _VERSION.fullmatch(value["version"]):
        raise RecipeError(f"{value['name']} version is invalid")


def _runtime_file(value: object) -> None:
    if not isinstance(value, dict) or set(value) != _RUNTIME_FILE_KEYS:
        raise RecipeError("GDAL runtime file definition is invalid")
    name = value["name"]
    if not isinstance(name, str) or not _RUNTIME_DLL.fullmatch(name) or name.lower() in {"gdal.dll"}:
        raise RecipeError("GDAL runtime file name is invalid")
    lowered = name.lower()
    if any(token in lowered for token in _FORBIDDEN_RUNTIME_TOKENS):
        raise RecipeError("GDAL runtime file belongs to a disabled database, network, or proprietary surface")
    _hash(value["sha256"], "GDAL runtime file SHA-256")
    licenses = value["licenses"]
    if not isinstance(licenses, list) or not licenses or len(licenses) > 16:
        raise RecipeError("GDAL runtime file licenses are invalid")
    seen: set[str] = set()
    for path in licenses:
        path = _license_path(path)
        if path.lower() in seen:
            raise RecipeError("GDAL runtime file licenses are duplicated")
        seen.add(path.lower())


def _verify_tools(tools: dict[str, dict[str, object]]) -> None:
    for name, tool in tools.items():
        path = _tool_path(tool, name)
        _verify_tool_hash(path, tool["sha256"], name)


def _tool_path(tool: dict[str, object], label: str) -> Path:
    return _safe_local_file(tool["path"], f"{label} path")


def _verify_tool_hash(path: Path, expected: object, label: str) -> None:
    if _sha256_file(path) != expected:
        raise RecipeError(f"{label} SHA-256 does not match its approved recipe")


def _validate_dependency_prefix(prefix: Path) -> None:
    required = (
        prefix / "include" / "proj.h",
        prefix / "include" / "sqlite3.h",
        prefix / "lib" / "proj.lib",
        prefix / "lib" / "sqlite3.lib",
        prefix / "share" / "proj" / "proj.db",
    )
    for path in required:
        if not path.is_file():
            raise RecipeError(f"pinned dependency prefix is missing {path.relative_to(prefix).as_posix()}")


def _validate_runtime_inputs(
    prefix: Path,
    license_root: Path,
    sources: dict[str, dict[str, object]],
    runtime_files: object,
) -> None:
    assert isinstance(runtime_files, list)
    license_inventory = _tree_file_names(license_root, "license bundle")
    if not license_inventory:
        raise RecipeError("license bundle is empty")
    for role, source in sources.items():
        for notice_path in source["noticeFiles"]:
            if _license_path(notice_path) not in license_inventory:
                raise RecipeError(f"{role} notice is missing from the approved bundle: {notice_path}")
    for entry in runtime_files:
        assert isinstance(entry, dict)
        source = prefix / "bin" / entry["name"]
        if not source.is_file() or source.is_symlink():
            raise RecipeError(f"pinned dependency prefix is missing runtime file bin/{entry['name']}")
        if _sha256_file(source) != entry["sha256"]:
            raise RecipeError(f"runtime file SHA-256 does not match its approved recipe: {entry['name']}")
        for license_path in entry["licenses"]:
            if _license_path(license_path) not in license_inventory:
                raise RecipeError(f"runtime file license is missing from the approved bundle: {license_path}")


def _configure_args(
    cmake: Path,
    ninja: Path,
    compiler: Path,
    linker: Path,
    source_root: Path,
    build_root: Path,
    install_root: Path,
    prefix_root: Path,
) -> list[str]:
    args = [
        str(cmake),
        "-S",
        _cmake_path(source_root),
        "-B",
        _cmake_path(build_root),
        "-G",
        "Ninja",
        f"-DCMAKE_MAKE_PROGRAM:FILEPATH={_cmake_path(ninja)}",
        f"-DCMAKE_C_COMPILER:FILEPATH={_cmake_path(compiler)}",
        f"-DCMAKE_CXX_COMPILER:FILEPATH={_cmake_path(compiler)}",
        f"-DCMAKE_LINKER:FILEPATH={_cmake_path(linker)}",
        f"-DCMAKE_PREFIX_PATH:PATH={_cmake_path(prefix_root)}",
        f"-DCMAKE_FIND_ROOT_PATH:PATH={_cmake_path(prefix_root)}",
        f"-DPROJ_ROOT:PATH={_cmake_path(prefix_root)}",
        f"-DSQLite3_ROOT:PATH={_cmake_path(prefix_root)}",
        f"-DPROJ_INCLUDE_DIR:PATH={_cmake_path(prefix_root / 'include')}",
        f"-DPROJ_LIBRARY_RELEASE:FILEPATH={_cmake_path(prefix_root / 'lib' / 'proj.lib')}",
        f"-DSQLite3_INCLUDE_DIR:PATH={_cmake_path(prefix_root / 'include')}",
        f"-DSQLite3_LIBRARY:FILEPATH={_cmake_path(prefix_root / 'lib' / 'sqlite3.lib')}",
        f"-DCMAKE_INSTALL_PREFIX:PATH={_cmake_path(install_root)}",
        "-DCMAKE_BUILD_TYPE:STRING=Release",
        "-DCMAKE_C_FLAGS_RELEASE:STRING=/O2 /Brepro",
        "-DCMAKE_CXX_FLAGS_RELEASE:STRING=/O2 /Brepro",
        "-DCMAKE_SHARED_LINKER_FLAGS_RELEASE:STRING=/Brepro",
        "-DCMAKE_EXE_LINKER_FLAGS_RELEASE:STRING=/Brepro",
    ]
    for name, expected in _CACHE_EXPECTATIONS.items():
        value_type = "STRING" if name in _CMAKE_STRING_CACHE_KEYS else "BOOL"
        args.append(f"-D{name}:{value_type}={expected}")
    return args


def _validate_cmake_cache(
    cache_path: Path,
    source_root: Path,
    prefix_root: Path,
    install_root: Path,
    ninja: Path,
    compiler: Path,
    linker: Path,
) -> None:
    cache = _parse_cmake_cache(cache_path)
    for name, expected in _CACHE_EXPECTATIONS.items():
        if _normalize_cache_value(cache.get(name)) != _normalize_cache_value(expected):
            actual = cache.get(name, "<missing>")
            raise RecipeError(f"CMake cache violates fixed GDAL profile: {name}={actual!r}, expected {expected}")
    for name, expected in _CMAKE_FLAG_EXPECTATIONS.items():
        if _normalize_cmake_flags(cache.get(name)) != _normalize_cmake_flags(expected):
            actual = cache.get(name, "<missing>")
            raise RecipeError(f"CMake cache violates fixed reproducibility flags: {name}={actual!r}, expected {expected}")
    if _normalize_cache_value(cache.get("CMAKE_GENERATOR")) != _normalize_cache_value("Ninja"):
        raise RecipeError("CMake cache did not use the fixed Ninja generator")
    if _normalize_cache_value(cache.get("CMAKE_BUILD_TYPE")) != _normalize_cache_value("Release"):
        raise RecipeError("CMake cache did not use Release mode")
    expected_paths = {
        "CMAKE_MAKE_PROGRAM": ninja,
        "CMAKE_C_COMPILER": compiler,
        "CMAKE_CXX_COMPILER": compiler,
        "CMAKE_LINKER": linker,
        "CMAKE_INSTALL_PREFIX": install_root,
        "CMAKE_PREFIX_PATH": prefix_root,
        "CMAKE_FIND_ROOT_PATH": prefix_root,
        "PROJ_INCLUDE_DIR": prefix_root / "include",
        "PROJ_LIBRARY_RELEASE": prefix_root / "lib" / "proj.lib",
        "SQLite3_INCLUDE_DIR": prefix_root / "include",
        "SQLite3_LIBRARY": prefix_root / "lib" / "sqlite3.lib",
    }
    for name, expected_path in expected_paths.items():
        actual = cache.get(name)
        if actual is None or not _same_path(actual, expected_path):
            raise RecipeError(f"CMake cache did not use the approved {name} path")
    source_cache_path = cache.get("CMAKE_HOME_DIRECTORY")
    if source_cache_path is None or not _same_path(source_cache_path, source_root):
        raise RecipeError("CMake cache source directory differs from the staged pinned GDAL source")
    _validate_dependency_cache_paths(cache, (source_root, prefix_root, install_root, cache_path.parent))


def _parse_cmake_cache(path: Path) -> dict[str, str]:
    if not path.is_file() or path.is_symlink():
        raise RecipeError("CMake did not produce a regular CMakeCache.txt")
    if path.stat().st_size > 16 * 1024 * 1024:
        raise RecipeError("CMakeCache.txt exceeds its bound")
    try:
        lines = path.read_text(encoding="utf-8", errors="strict").splitlines()
    except (OSError, UnicodeError) as error:
        raise RecipeError(f"cannot read CMakeCache.txt: {error}") from error
    result: dict[str, str] = {}
    for line in lines:
        if not line or line.startswith("//") or line.startswith("#") or "=" not in line or ":" not in line:
            continue
        before, value = line.split("=", 1)
        key, _kind = before.split(":", 1)
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", key):
            continue
        if key in result:
            raise RecipeError(f"CMakeCache.txt duplicates {key}")
        result[key] = value.strip()
    if not result:
        raise RecipeError("CMakeCache.txt has no usable cache entries")
    return result


def _normalize_cache_value(value: str | None) -> str:
    if value is None:
        return ""
    return value.strip().upper()


def _normalize_cmake_flags(value: str | None) -> str:
    if value is None:
        return ""
    return " ".join(value.split()).upper()


def _same_path(value: str, expected: Path) -> bool:
    normalized_value = value.replace("/", "\\")
    normalized_expected = str(expected.resolve(strict=True)).replace("/", "\\")
    return os.path.normcase(os.path.normpath(normalized_value)) == os.path.normcase(os.path.normpath(normalized_expected))


def _validate_dependency_cache_paths(cache: dict[str, str], allowed_roots: tuple[Path, ...]) -> None:
    """Reject CMake-discovered dependency paths outside the staged build roots."""
    dependency_key = re.compile(r"(?:^|_)(?:LIBRARY|LIBRARIES|INCLUDE|INCLUDES|ROOT|DIR)(?:_|$)")
    for name, value in cache.items():
        if name.startswith("CMAKE_") or not dependency_key.search(name):
            continue
        for candidate in value.split(";"):
            candidate = candidate.strip().strip('"')
            if not _looks_like_absolute_path(candidate):
                continue
            if not any(_path_is_within(candidate, root) for root in allowed_roots):
                raise RecipeError(f"CMake cache resolved {name} outside the approved staged roots")


def _looks_like_absolute_path(value: str) -> bool:
    return bool(re.fullmatch(r"(?:[A-Za-z]:[\\/].*|/.*)", value))


def _path_is_within(value: str, root: Path) -> bool:
    normalized_value = os.path.normcase(os.path.normpath(value.replace("/", "\\")))
    normalized_root = os.path.normcase(os.path.normpath(str(root.resolve(strict=True)).replace("/", "\\")))
    return normalized_value == normalized_root or normalized_value.startswith(normalized_root + "\\")


def _assemble_artifact(
    install_root: Path,
    prefix_root: Path,
    license_root: Path,
    runtime_files: object,
    artifact_root: Path,
) -> None:
    if artifact_root.exists():
        raise RecipeError("artifact staging directory already exists")
    bin_source = install_root / "bin"
    gdal_data = install_root / "share" / "gdal"
    required_install_files = (bin_source / "gdal.dll", bin_source / "ogrinfo.exe", bin_source / "ogr2ogr.exe")
    for path in required_install_files:
        if not path.is_file() or path.is_symlink():
            raise RecipeError(f"CMake install is missing required package file {path.relative_to(install_root).as_posix()}")
    if not gdal_data.is_dir() or gdal_data.is_symlink():
        raise RecipeError("CMake install is missing share/gdal")

    artifact_root.mkdir(parents=True)
    artifact_bin = artifact_root / "bin"
    artifact_bin.mkdir()
    for name in ("gdal.dll", "ogrinfo.exe", "ogr2ogr.exe"):
        _copy_regular_file(bin_source / name, artifact_bin / name, "CMake installed binary")
    _copy_tree_contents(gdal_data, artifact_root / "share" / "gdal", "CMake installed GDAL data")
    _copy_tree_contents(prefix_root / "share" / "proj", artifact_root / "share" / "proj", "pinned PROJ data")
    _copy_tree_contents(license_root, artifact_root / "licenses", "approved license bundle")
    assert isinstance(runtime_files, list)
    for entry in runtime_files:
        assert isinstance(entry, dict)
        _copy_regular_file(prefix_root / "bin" / entry["name"], artifact_bin / entry["name"], "pinned runtime DLL")
    _validate_artifact_layout(artifact_root, runtime_files)


def _validate_artifact_layout(artifact_root: Path, runtime_files: object) -> None:
    inventory = _tree_inventory(artifact_root, "GDAL artifact", _MAX_ARTIFACT_FILES, _MAX_ARTIFACT_BYTES)
    names = {relative for relative, _digest, _size in inventory}
    top_levels = {PurePosixPath(name).parts[0] for name in names}
    if top_levels != {"bin", "share", "licenses"}:
        raise RecipeError("GDAL artifact layout may contain only bin, share, and licenses")
    required = {
        "bin/gdal.dll",
        "bin/ogrinfo.exe",
        "bin/ogr2ogr.exe",
        "share/proj/proj.db",
    }
    if not required.issubset(names):
        raise RecipeError("GDAL artifact layout is missing a required binary or PROJ database")
    if not any(name.startswith("share/gdal/") for name in names):
        raise RecipeError("GDAL artifact layout is missing GDAL runtime data")
    if not any(name.startswith("licenses/") for name in names):
        raise RecipeError("GDAL artifact layout is missing license evidence")
    allowed_bin_paths = {"bin/gdal.dll", "bin/ogrinfo.exe", "bin/ogr2ogr.exe"}
    assert isinstance(runtime_files, list)
    allowed_bin_paths.update(f"bin/{entry['name']}" for entry in runtime_files if isinstance(entry, dict))
    actual_bin_paths = {name for name in names if PurePosixPath(name).parts[0] == "bin"}
    if actual_bin_paths != allowed_bin_paths:
        raise RecipeError("GDAL artifact contains an undeclared executable or runtime DLL")
    for name in names:
        parts = PurePosixPath(name).parts
        if parts[0] == "share" and (len(parts) < 2 or parts[1] not in {"gdal", "proj"}):
            raise RecipeError("GDAL artifact contains an unsupported share directory")
        if Path(name).suffix.lower() in {".dll", ".exe", ".lib", ".pdb"} and parts[0] != "bin":
            raise RecipeError("GDAL artifact contains a binary outside the allowed bin directory")
        if parts[0] == "licenses":
            leaf = PurePosixPath(name).name.lower()
            if leaf.endswith((".dll", ".exe", ".lib", ".pdb")):
                raise RecipeError("license bundle contains a binary artifact")


def _validate_runtime(
    artifact_root: Path,
    gdal_version: object,
    gdalinfo: Path,
    expected_ogr_formats: object,
    expected_gdal_formats: object,
    dumpbin: Path,
    runtime_files: object,
    runner: Runner,
    environment: dict[str, str],
) -> None:
    bin_root = artifact_root / "bin"
    ogrinfo = bin_root / "ogrinfo.exe"
    ogr2ogr = bin_root / "ogr2ogr.exe"
    expected_ogr = _format_inventory(expected_ogr_formats, "expectedOgrFormats")
    expected_gdal = _format_inventory(expected_gdal_formats, "expectedGdalFormats")
    _assert_version(
        runner([str(ogrinfo), "--version"], artifact_root, 120, 1024 * 1024, environment),
        gdal_version,
        "ogrinfo",
    )
    _assert_version(
        runner([str(ogr2ogr), "--version"], artifact_root, 120, 1024 * 1024, environment),
        gdal_version,
        "ogr2ogr",
    )
    _assert_version(
        runner([str(gdalinfo), "--version"], artifact_root, 120, 1024 * 1024, environment),
        gdal_version,
        "gdalinfo",
    )
    ogr_formats = _parse_formats(
        runner([str(ogrinfo), "--formats"], artifact_root, 120, 4 * 1024 * 1024, environment)
    )
    gdal_formats = _parse_formats(
        runner([str(gdalinfo), "--formats"], artifact_root, 120, 4 * 1024 * 1024, environment)
    )
    _validate_format_inventory(ogr_formats, expected_ogr, "OGR vector")
    _validate_format_inventory(gdal_formats, expected_gdal, "GDAL complete")
    _validate_epsg_26917_conversion(artifact_root, ogr2ogr, runner, environment)
    _audit_pe_dependencies(artifact_root, dumpbin, runtime_files, runner, environment)


def _validate_epsg_26917_conversion(
    artifact_root: Path,
    ogr2ogr: Path,
    runner: Runner,
    environment: dict[str, str],
) -> None:
    validation_root = artifact_root.parent / "validation"
    validation_root.mkdir(exist_ok=False)
    source = validation_root / "epsg-26917.geojson"
    converted = validation_root / "epsg-4326.geojson"
    source.write_text(
        json.dumps(
            {
                "type": "FeatureCollection",
                "crs": {
                    "type": "name",
                    "properties": {"name": "urn:ogc:def:crs:EPSG::26917"},
                },
                "features": [
                    {
                        "type": "Feature",
                        "properties": {"kind": "traffic_signal"},
                        "geometry": {"type": "Point", "coordinates": list(_EPSG_26917_SAMPLE)},
                    }
                ],
            },
            sort_keys=True,
            separators=(",", ":"),
        ),
        encoding="utf-8",
        newline="\n",
    )
    runner(
        [
            str(ogr2ogr),
            "-f", "GeoJSON",
            "-s_srs", "EPSG:26917",
            "-t_srs", "EPSG:4326",
            str(converted),
            str(source),
        ],
        validation_root,
        120,
        4 * 1024 * 1024,
        environment,
    )
    try:
        value = json.loads(converted.read_text(encoding="utf-8"))
        coordinates = value["features"][0]["geometry"]["coordinates"]
        longitude, latitude = float(coordinates[0]), float(coordinates[1])
    except (OSError, UnicodeError, ValueError, KeyError, IndexError, TypeError, json.JSONDecodeError) as error:
        raise RecipeError(f"GDAL EPSG:26917 conversion output is invalid: {error}") from error
    expected_longitude, expected_latitude = _EPSG_26917_EXPECTED_WGS84
    if (
        abs(longitude - expected_longitude) > _EPSG_26917_TOLERANCE_DEGREES
        or abs(latitude - expected_latitude) > _EPSG_26917_TOLERANCE_DEGREES
    ):
        raise RecipeError("GDAL EPSG:26917 conversion did not produce the expected WGS84 coordinates")


def _audit_pe_dependencies(
    artifact_root: Path,
    dumpbin: Path,
    runtime_files: object,
    runner: Runner,
    environment: dict[str, str],
) -> None:
    assert isinstance(runtime_files, list)
    allowed_local = {"gdal.dll", "ogrinfo.exe", "ogr2ogr.exe"}
    allowed_local.update(
        entry["name"].lower() for entry in runtime_files if isinstance(entry, dict)
    )
    binaries = [
        artifact_root / "bin" / "gdal.dll",
        artifact_root / "bin" / "ogrinfo.exe",
        artifact_root / "bin" / "ogr2ogr.exe",
    ]
    binaries.extend(artifact_root / "bin" / entry["name"] for entry in runtime_files if isinstance(entry, dict))
    dependency_graph: dict[str, set[str]] = {}
    for binary in binaries:
        dependencies: set[str] = set()
        for mode in ("/dependents", "/imports"):
            output = runner(
                [str(dumpbin), "/nologo", mode, str(binary)],
                artifact_root,
                120,
                4 * 1024 * 1024,
                environment,
            )
            dependencies.update(_parse_dumpbin_dependencies(output))
        if not dependencies:
            raise RecipeError(f"dumpbin did not report PE dependencies for {binary.name}")
        local_dependencies: set[str] = set()
        for dependency in dependencies:
            lower = dependency.lower()
            if any(token in lower for token in _FORBIDDEN_RUNTIME_TOKENS):
                raise RecipeError(f"PE dependency violates the disabled GDAL profile: {dependency}")
            if lower in allowed_local:
                local_dependencies.add(lower)
                continue
            if _is_windows_system_dll(lower):
                continue
            raise RecipeError(f"PE dependency is not declared in the managed GDAL package: {dependency}")
        dependency_graph[binary.name.lower()] = local_dependencies

    reachable = {"ogrinfo.exe", "ogr2ogr.exe"}
    pending = list(reachable)
    while pending:
        current = pending.pop()
        for dependency in dependency_graph.get(current, set()):
            if dependency not in reachable:
                reachable.add(dependency)
                pending.append(dependency)
    unused = sorted(allowed_local - reachable)
    if unused:
        raise RecipeError("GDAL artifact contains an unused declared runtime dependency: " + ", ".join(unused))


def _parse_dumpbin_dependencies(output: str) -> set[str]:
    if len(output.encode("utf-8", errors="replace")) > 4 * 1024 * 1024:
        raise RecipeError("dumpbin output exceeds its bound")
    dependencies = {
        match.group(1)
        for line in output.splitlines()
        if (match := re.match(r"^\s*([A-Za-z0-9_.-]+\.(?:dll|drv))\s*$", line, re.IGNORECASE))
    }
    return dependencies


def _is_windows_system_dll(name: str) -> bool:
    return (
        name in _WINDOWS_SYSTEM_DLLS
        or name.startswith("api-ms-win-") and name.endswith(".dll")
        or name.startswith("ext-ms-win-") and name.endswith(".dll")
    )


def _parse_formats(output: str) -> set[str]:
    if len(output.encode("utf-8", errors="replace")) > 4 * 1024 * 1024:
        raise RecipeError("ogrinfo --formats output exceeds its bound")
    formats: set[str] = set()
    for line in output.splitlines():
        # GDAL 3.x emits e.g. '  GPKG -raster,vector- (rw+v): GeoPackage'
        # and older output variants may prefix a numeric ordinal.
        match = re.match(r"^\s*(?:\d+\s*[-:]\s*)?(.+?)\s*-\s*(?:raster|vector)", line, re.IGNORECASE)
        if match:
            formats.add(match.group(1).strip())
    if not formats:
        raise RecipeError("ogrinfo --formats output did not contain a parseable driver list")
    return formats


def _validate_format_inventory(actual: set[str], expected: set[str], label: str) -> None:
    forbidden = sorted(_FORBIDDEN_FORMATS & actual)
    if forbidden:
        raise RecipeError(f"{label} driver inventory includes disabled database, network, or proprietary formats: " + ", ".join(forbidden))
    missing = sorted(expected - actual)
    unexpected = sorted(actual - expected)
    if missing or unexpected:
        details: list[str] = []
        if missing:
            details.append("missing " + ", ".join(missing))
        if unexpected:
            details.append("unexpected " + ", ".join(unexpected))
        raise RecipeError(f"{label} driver inventory does not exactly match its approved recipe: " + "; ".join(details))


def _assert_version(output: str, expected: object, label: str) -> None:
    version = _nonblank(expected, f"{label} version", 100)
    if re.search(rf"(?<![0-9A-Za-z]){re.escape(version)}(?![0-9A-Za-z])", output) is None:
        raise RecipeError(f"{label} version output does not contain {version}")


def _build_environment(source_date_epoch: object) -> dict[str, str]:
    environment = dict(os.environ)
    for key in _INHERITED_RUNTIME_VARIABLES | _NETWORK_VARIABLES | _INHERITED_BUILD_VARIABLES:
        environment.pop(key, None)
    parsed = dt.datetime.strptime(_source_date_epoch(source_date_epoch), "%Y-%m-%dT%H:%M:%SZ").replace(tzinfo=dt.timezone.utc)
    environment.update({
        "SOURCE_DATE_EPOCH": str(int(parsed.timestamp())),
        "VCPKG_DISABLE_METRICS": "1",
        "FETCHCONTENT_FULLY_DISCONNECTED": "ON",
        "FETCHCONTENT_UPDATES_DISCONNECTED": "ON",
        "PROJ_NETWORK": "OFF",
    })
    return environment


def _runtime_environment(artifact_root: Path, source_date_epoch: object) -> dict[str, str]:
    environment = _build_environment(source_date_epoch)
    bin_root = artifact_root / "bin"
    existing_path = environment.get("PATH", "")
    environment.update({
        "PATH": str(bin_root) + (os.pathsep + existing_path if existing_path else ""),
        "GDAL_DATA": str(artifact_root / "share" / "gdal"),
        "GDAL_DRIVER_PATH": "disable",
        "OGR_DRIVER_PATH": "disable",
        "GDAL_PAM_ENABLED": "NO",
        "GDAL_VRT_ENABLE_PYTHON": "NO",
        "GDAL_VRT_ENABLE_RAWRASTERBAND": "NO",
        "PROJ_DATA": str(artifact_root / "share" / "proj"),
        "PROJ_NETWORK": "OFF",
    })
    return environment


def _copy_verified_tree(source: Path, destination: Path, expected_hash: object, label: str) -> Path:
    expected = _hash(expected_hash, f"{label} tree SHA-256")
    inventory = _tree_inventory(source, label, _MAX_INPUT_FILES, _MAX_INPUT_BYTES)
    if _tree_digest(inventory) != expected:
        raise RecipeError(f"{label} tree SHA-256 does not match its approved recipe")
    _copy_tree_contents(source, destination, label)
    copied = _tree_inventory(destination, f"copied {label}", _MAX_INPUT_FILES, _MAX_INPUT_BYTES)
    if _tree_digest(copied) != expected:
        raise RecipeError(f"{label} changed while it was being staged")
    return destination.resolve(strict=True)


def _verify_gdal_source_archive(source: dict[str, object]) -> None:
    """Bind the staged source tree to a retained, immutable official tarball."""
    archive = _safe_local_file(source["archivePath"], "GDAL source archive")
    expected_size = source["archiveSizeBytes"]
    if not isinstance(expected_size, int) or isinstance(expected_size, bool) or expected_size <= 0:
        raise RecipeError("GDAL source archiveSizeBytes is invalid")
    if archive.stat().st_size != expected_size:
        raise RecipeError("GDAL source archive size does not match its approved recipe")
    expected_hash = _hash(source["archiveSha256"], "GDAL source archive SHA-256")
    if _sha256_file_safe(archive, "GDAL source archive") != expected_hash:
        raise RecipeError("GDAL source archive SHA-256 does not match its approved recipe")
    inventory = _tar_gz_tree_inventory(
        archive,
        source["archiveRoot"],
        "GDAL source archive",
        _MAX_INPUT_FILES,
        _MAX_INPUT_BYTES,
    )
    if _tree_digest(inventory) != source["treeSha256"]:
        raise RecipeError("GDAL source archive contents do not match the approved source tree")
    if archive.stat().st_size != expected_size or _sha256_file_safe(archive, "GDAL source archive") != expected_hash:
        raise RecipeError("GDAL source archive changed while it was being verified")


def _tar_gz_tree_inventory(
    archive: Path,
    archive_root: object,
    label: str,
    max_files: int,
    max_bytes: int,
) -> list[tuple[str, str, int]]:
    """Read a tar.gz as a bounded regular-file tree without extracting it."""
    root = _nonblank(archive_root, f"{label} root", 200)
    if "/" in root or "\\" in root or root in {".", ".."} or any(ord(character) < 32 or character == ":" for character in root):
        raise RecipeError(f"{label} root is invalid")
    result: list[tuple[str, str, int]] = []
    seen_members: set[str] = set()
    total_bytes = 0
    member_count = 0
    saw_root = False
    try:
        with tarfile.open(archive, mode="r:gz") as input_archive:
            for member in input_archive:
                member_count += 1
                if member_count > max_files:
                    raise RecipeError(f"{label} exceeds its member-count bound")
                raw_name = member.name
                if not isinstance(raw_name, str) or not raw_name:
                    raise RecipeError(f"{label} contains an unnamed member")
                if raw_name == root or raw_name == f"{root}/":
                    if not member.isdir() or saw_root:
                        raise RecipeError(f"{label} has an invalid root member")
                    saw_root = True
                    continue
                prefix = f"{root}/"
                if not raw_name.startswith(prefix):
                    raise RecipeError(f"{label} member escapes the declared root: {raw_name!r}")
                relative = _safe_relative_path(raw_name[len(prefix):], f"{label} member path")
                casefolded = relative.casefold()
                if casefolded in seen_members:
                    raise RecipeError(f"{label} contains duplicate or case-colliding members: {relative}")
                seen_members.add(casefolded)
                if member.isdir():
                    continue
                if not member.isfile():
                    raise RecipeError(f"{label} contains a link or special member: {relative}")
                if member.size < 0:
                    raise RecipeError(f"{label} contains a file with an invalid size")
                total_bytes += member.size
                if len(result) >= max_files or total_bytes > max_bytes:
                    raise RecipeError(f"{label} exceeds its file-count or size bound")
                source = input_archive.extractfile(member)
                if source is None:
                    raise RecipeError(f"{label} cannot read member: {relative}")
                digest = hashlib.sha256()
                bytes_read = 0
                with source:
                    while chunk := source.read(_COPY_CHUNK):
                        bytes_read += len(chunk)
                        if bytes_read > member.size:
                            raise RecipeError(f"{label} member size changed while being read: {relative}")
                        digest.update(chunk)
                if bytes_read != member.size:
                    raise RecipeError(f"{label} member size is truncated: {relative}")
                result.append((relative, digest.hexdigest(), member.size))
    except (OSError, EOFError, tarfile.TarError) as error:
        raise RecipeError(f"cannot safely read {label}: {error}") from error
    if not saw_root or not result:
        raise RecipeError(f"{label} must contain its declared root and at least one regular file")
    return sorted(result)


def _format_inventory_digest(value: object) -> str:
    entries = sorted(_format_inventory(value, "format inventory"))
    encoded = json.dumps(entries, ensure_ascii=True, separators=(",", ":")).encode("ascii")
    return hashlib.sha256(encoded).hexdigest()


def _validate_configure_output(output: str) -> None:
    if len(output.encode("utf-8", errors="replace")) > _MAX_PROCESS_OUTPUT_BYTES:
        raise RecipeError("CMake configuration output exceeds its bound")
    if re.search(r"manually[- ]specified variables were not used", output, re.IGNORECASE):
        raise RecipeError("CMake did not consume one or more fixed GDAL profile settings")


def _copy_tree_contents(source: Path, destination: Path, label: str) -> None:
    if destination.exists():
        raise RecipeError(f"{label} destination already exists")
    destination.mkdir(parents=True)
    for relative, _digest, _size in _tree_inventory(source, label, _MAX_INPUT_FILES, _MAX_INPUT_BYTES):
        _copy_regular_file(source.joinpath(*PurePosixPath(relative).parts), destination.joinpath(*PurePosixPath(relative).parts), label)


def _copy_regular_file(source: Path, destination: Path, label: str) -> None:
    try:
        metadata = source.lstat()
    except OSError as error:
        raise RecipeError(f"cannot inspect {label}: {error}") from error
    if source.is_symlink() or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT or not stat.S_ISREG(metadata.st_mode):
        raise RecipeError(f"{label} must be a regular non-link file")
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        raise RecipeError(f"{label} destination already exists")
    with _open_regular_file(source, label) as input_file, destination.open("xb") as output_file:
        while chunk := input_file.read(_COPY_CHUNK):
            output_file.write(chunk)


def _tree_inventory(root: Path, label: str, max_files: int, max_bytes: int) -> list[tuple[str, str, int]]:
    try:
        metadata = root.lstat()
    except OSError as error:
        raise RecipeError(f"cannot inspect {label}: {error}") from error
    if root.is_symlink() or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT or not stat.S_ISDIR(metadata.st_mode):
        raise RecipeError(f"{label} must be a regular non-link directory")
    result: list[tuple[str, str, int]] = []
    casefolded_paths: set[str] = set()
    total_bytes = 0

    def visit(directory: Path, prefix: PurePosixPath) -> None:
        nonlocal total_bytes
        try:
            entries = sorted(os.scandir(directory), key=lambda item: item.name)
        except OSError as error:
            raise RecipeError(f"cannot scan {label}: {error}") from error
        for entry in entries:
            relative = prefix / entry.name
            relative_text = _safe_relative_path(relative.as_posix(), f"{label} path")
            casefolded = relative_text.casefold()
            if casefolded in casefolded_paths:
                raise RecipeError(f"{label} contains case-colliding paths: {relative_text}")
            casefolded_paths.add(casefolded)
            entry_metadata = entry.stat(follow_symlinks=False)
            if entry.is_symlink() or getattr(entry_metadata, "st_file_attributes", 0) & _REPARSE_POINT:
                raise RecipeError(f"{label} contains a link or reparse point: {relative_text}")
            if stat.S_ISDIR(entry_metadata.st_mode):
                visit(Path(entry.path), relative)
                continue
            if not stat.S_ISREG(entry_metadata.st_mode):
                raise RecipeError(f"{label} contains a special file: {relative_text}")
            if entry_metadata.st_size < 0:
                raise RecipeError(f"{label} contains a file with an invalid size")
            total_bytes += entry_metadata.st_size
            if len(result) >= max_files or total_bytes > max_bytes:
                raise RecipeError(f"{label} exceeds its file-count or size bound")
            file_path = Path(entry.path)
            result.append((relative_text, _sha256_file_safe(file_path, label), entry_metadata.st_size))

    visit(root, PurePosixPath())
    return result


def _tree_digest(inventory: list[tuple[str, str, int]]) -> str:
    digest = hashlib.sha256()
    digest.update(b"RoadWatcher-GDAL-tree-v1\0")
    for relative, file_hash, size in inventory:
        digest.update(relative.encode("utf-8"))
        digest.update(b"\0")
        digest.update(bytes.fromhex(file_hash))
        digest.update(b"\0")
        digest.update(str(size).encode("ascii"))
        digest.update(b"\n")
    return digest.hexdigest()


def _tree_file_names(root: Path, label: str) -> set[str]:
    return {relative for relative, _digest, _size in _tree_inventory(root, label, _MAX_INPUT_FILES, _MAX_INPUT_BYTES)}


def _tree_size_bytes(root: Path, label: str) -> int:
    return sum(size for _relative, _digest, size in _tree_inventory(root, label, _MAX_INPUT_FILES, _MAX_INPUT_BYTES))


def _locked_files(root: Path) -> list[dict[str, str]]:
    return [
        {"path": relative, "sha256": digest}
        for relative, digest, _size in _tree_inventory(root, "GDAL artifact", _MAX_ARTIFACT_FILES, _MAX_ARTIFACT_BYTES)
    ]


def _run_bounded(args: list[str], cwd: Path, timeout_seconds: int, max_output_bytes: int, environment: dict[str, str]) -> str:
    if not args or any(not isinstance(value, str) or not value or "\0" in value for value in args):
        raise RecipeError("internal GDAL build command is invalid")
    stdout_path = cwd / f".process-{time.time_ns()}.stdout"
    stderr_path = cwd / f".process-{time.time_ns()}.stderr"
    try:
        with stdout_path.open("xb") as stdout, stderr_path.open("xb") as stderr:
            process = subprocess.Popen(
                args,
                cwd=cwd,
                env=environment,
                stdin=subprocess.DEVNULL,
                stdout=stdout,
                stderr=stderr,
                shell=False,
            )
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
        text = output.decode("utf-8", errors="replace")
        if process.returncode != 0:
            raise RecipeError(f"process exited with {process.returncode}: {Path(args[0]).name}: {text[-4000:]}")
        return text
    finally:
        stdout_path.unlink(missing_ok=True)
        stderr_path.unlink(missing_ok=True)


def _emit_and_verify_manifest(
    definition: dict[str, object],
    archive: Path,
    manifest_path: Path,
    source_date_epoch: object,
    node: Path,
    runner: Runner,
    cwd: Path,
) -> None:
    if not archive.is_file() or archive.stat().st_size <= 0:
        raise RecipeError("managed GDAL archive must be a non-empty file")
    definition_path = manifest_path.with_suffix(".definition-input.json")
    _write_new_json(definition_path, definition)
    manifest_tool = Path(__file__).with_name("managed-artifact-manifest.mjs").resolve(strict=True)
    environment = _build_environment(source_date_epoch)
    try:
        runner(
            [str(node), str(manifest_tool), "build", str(definition_path), str(archive), str(manifest_path), f"--generated-at={source_date_epoch}"],
            cwd,
            120,
            _MAX_PROCESS_OUTPUT_BYTES,
            environment,
        )
        runner(
            [str(node), str(manifest_tool), "verify", str(manifest_path), str(archive)],
            cwd,
            120,
            _MAX_PROCESS_OUTPUT_BYTES,
            environment,
        )
    finally:
        definition_path.unlink(missing_ok=True)
    if not manifest_path.is_file() or manifest_path.stat().st_size == 0:
        raise RecipeError("managed artifact manifest was not published by its verifier")


def _manifest_source(source: dict[str, object], size_bytes: int) -> dict[str, object]:
    downloaded_sha256 = source["treeSha256"]
    manifest_size = size_bytes
    if source["role"] == "gdalSource":
        downloaded_sha256 = source["archiveSha256"]
        manifest_size = source["archiveSizeBytes"]
    result = {
        "id": source["id"],
        "url": source["url"],
        "version": source["version"],
        "license": source["license"],
        # The retained GDAL release tarball is verified before its canonical
        # content tree is compared to the staged source. Prefixes and notices
        # are local source-build inputs, so their canonical tree digests remain
        # the most precise available identity until their owner supplies archive
        # evidence for a production recipe.
        "downloadedSha256": downloaded_sha256,
        "publisherSha256": source["publisherSha256"],
        "retrievedAt": source["retrievedAt"],
        "sizeBytes": manifest_size,
    }
    if source["role"] == "gdalSource":
        result["commit"] = source["commit"]
    return result


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


def _safe_local_directory(value: object, label: str) -> Path:
    text = _nonblank(value, label, 32_000)
    path = Path(text)
    if not path.is_absolute():
        raise RecipeError(f"{label} must be absolute")
    try:
        metadata = path.lstat()
        resolved = path.resolve(strict=True)
    except OSError as error:
        raise RecipeError(f"{label} is unavailable: {error}") from error
    if path.is_symlink() or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT or not stat.S_ISDIR(metadata.st_mode):
        raise RecipeError(f"{label} must be a regular non-link directory")
    return resolved


def _open_regular_file(path: Path, label: str):
    flags = os.O_RDONLY | getattr(os, "O_BINARY", 0) | getattr(os, "O_NOFOLLOW", 0)
    try:
        descriptor = os.open(path, flags)
    except OSError as error:
        raise RecipeError(f"cannot open {label} safely: {error}") from error
    metadata = os.fstat(descriptor)
    if not stat.S_ISREG(metadata.st_mode) or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT:
        os.close(descriptor)
        raise RecipeError(f"{label} is not a regular owned file")
    return os.fdopen(descriptor, "rb")


def _sha256_file_safe(path: Path, label: str) -> str:
    digest = hashlib.sha256()
    with _open_regular_file(path, label) as source:
        while chunk := source.read(_COPY_CHUNK):
            digest.update(chunk)
    return digest.hexdigest()


def _sha256_file(path: Path) -> str:
    return _sha256_file_safe(path, path.name)


def _license(value: object, label: str) -> None:
    if not isinstance(value, dict) or set(value) != _LICENSE_KEYS:
        raise RecipeError(f"{label} is invalid")
    if not isinstance(value["id"], str) or not _ID.fullmatch(value["id"]):
        raise RecipeError(f"{label} id is invalid")
    _nonblank(value["name"], f"{label} name", 200)
    _https(value["url"], f"{label} URL")


def _license_path(value: object) -> str:
    path = _safe_relative_path(value, "license path")
    if not _LICENSE_FILE.fullmatch(path) or path.lower().endswith((".dll", ".exe", ".lib", ".pdb")):
        raise RecipeError("license path is invalid")
    return path


def _safe_relative_path(value: object, label: str) -> str:
    if not isinstance(value, str) or not value or len(value) > 240 or "\\" in value:
        raise RecipeError(f"{label} is invalid")
    try:
        value.encode("ascii")
    except UnicodeEncodeError as error:
        raise RecipeError(f"{label} must be ASCII") from error
    path = PurePosixPath(value)
    if path.is_absolute() or path.as_posix() != value or any(part in {"", ".", ".."} for part in path.parts):
        raise RecipeError(f"unsafe {label}: {value}")
    if any(ord(character) < 32 or character == ":" for character in value):
        raise RecipeError(f"unsafe {label}: {value}")
    return value


def _hash(value: object, label: str) -> str:
    if not isinstance(value, str) or not _SHA256.fullmatch(value):
        raise RecipeError(f"{label} is invalid")
    return value


def _https(value: object, label: str) -> str:
    text = _nonblank(value, label, 2048)
    if not text.startswith("https://") or any(character in text for character in "\r\n"):
        raise RecipeError(f"{label} must use HTTPS")
    return text


def _nonblank(value: object, label: str, limit: int) -> str:
    if not isinstance(value, str) or not value.strip() or len(value) > limit or "\0" in value:
        raise RecipeError(f"{label} is invalid")
    return value.strip()


def _iso_utc(value: object, label: str) -> str:
    text = _nonblank(value, label, 50)
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{3})?Z", text):
        raise RecipeError(f"{label} is invalid")
    try:
        parsed = dt.datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError as error:
        raise RecipeError(f"{label} is invalid") from error
    if parsed.tzinfo != dt.timezone.utc:
        raise RecipeError(f"{label} must be UTC")
    return text


def _source_date_epoch(value: object) -> str:
    text = _iso_utc(value, "sourceDateEpoch")
    if not re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z", text):
        raise RecipeError("sourceDateEpoch must have whole-second precision")
    return text


def _cmake_path(path: Path) -> str:
    return path.resolve(strict=False).as_posix()


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recipe", type=Path, help="strict local source-only GDAL recipe JSON")
    parser.add_argument("output_directory", type=Path, help="directory for new GDAL release outputs")
    parser.add_argument("--timeout-seconds", type=int, default=21_600, help="fixed CMake build timeout (60-21600)")
    args = parser.parse_args(argv)
    try:
        result = build_release_asset(load_recipe(args.recipe), args.output_directory, timeout_seconds=args.timeout_seconds)
    except (RecipeError, OSError) as error:
        parser.exit(1, f"GDAL source asset build failed: {error}\n")
    print(json.dumps(result, sort_keys=True, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RecipeError, OSError, PackageError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2) from error
