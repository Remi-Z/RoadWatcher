"""Export and package a locked YOLO11n-compatible ONNX asset for RoadWatcher."""

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
import tempfile
import time
from typing import Callable

from deterministic_artifact_zip import BuildError as PackageError
from deterministic_artifact_zip import build_archive


class RecipeError(ValueError):
    """Raised when an ONNX recipe, exporter, or output violates the locked contract."""


_ID = re.compile(r"^[a-z0-9-]+$")
_VERSION = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$")
_TOOL_VERSION = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._+-]{0,99}$")
_SHA256 = re.compile(r"^[a-f0-9]{64}$")
_TOP_KEYS = {
    "schemaVersion", "id", "version", "platform", "sourceDateEpoch",
    "artifactLicense", "sources", "exporter", "imageSize", "opset"
}
_SOURCE_KEYS = {
    "id", "role", "localPath", "url", "version", "license",
    "downloadedSha256", "publisherSha256", "retrievedAt", "etag", "lastModified"
}
_LICENSE_KEYS = {"id", "name", "url"}
_EXPORTER_KEYS = {"path", "pythonVersion", "ultralyticsVersion", "torchVersion", "onnxVersion"}
_SOURCE_ROLES = {"weights", "labels"}
_COPY_CHUNK = 1024 * 1024
_REPARSE_POINT = 0x400
_MAX_PROCESS_OUTPUT_BYTES = 16 * 1024 * 1024
_MAX_LABEL_BYTES = 2 * 1024 * 1024
_PROBE_MARKER = "ROADWATCHER_EXPORTER_IDENTITY="
_EXPORT_MARKER = "ROADWATCHER_ONNX_CONTRACT="

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
        raise RecipeError(f"cannot read ONNX build recipe: {error}") from error
    validate_recipe(value)
    return value


def validate_recipe(value: object) -> None:
    if not isinstance(value, dict) or set(value) != _TOP_KEYS:
        raise RecipeError("ONNX recipe has unsupported or missing top-level fields")
    if value["schemaVersion"] != 1 or value["id"] != "cv-yolo11n":
        raise RecipeError("ONNX recipe identity is invalid")
    if value["platform"] != "windows-x86_64":
        raise RecipeError("ONNX recipe platform must be windows-x86_64")
    if not isinstance(value["version"], str) or not _VERSION.fullmatch(value["version"]):
        raise RecipeError("ONNX artifact version is invalid")
    _source_date_epoch(value["sourceDateEpoch"])
    _license(value["artifactLicense"], "artifact license")
    if isinstance(value["imageSize"], bool) or not isinstance(value["imageSize"], int) or not 320 <= value["imageSize"] <= 1280 or value["imageSize"] % 32:
        raise RecipeError("ONNX imageSize must be a multiple of 32 between 320 and 1280")
    if isinstance(value["opset"], bool) or not isinstance(value["opset"], int) or not 12 <= value["opset"] <= 21:
        raise RecipeError("ONNX opset must be between 12 and 21")

    sources = value["sources"]
    if not isinstance(sources, list) or len(sources) != 2:
        raise RecipeError("ONNX recipe must contain weights and labels sources")
    roles: set[str] = set()
    ids: set[str] = set()
    for source in sources:
        _source(source)
        if source["role"] not in _SOURCE_ROLES or source["role"] in roles:
            raise RecipeError("ONNX source roles are invalid or duplicated")
        if source["id"] in ids:
            raise RecipeError("ONNX source ids are duplicated")
        roles.add(source["role"])
        ids.add(source["id"])
    if roles != _SOURCE_ROLES:
        raise RecipeError("ONNX recipe source roles are incomplete")
    weights = next(source for source in sources if source["role"] == "weights")
    if Path(weights["localPath"]).suffix.lower() != ".pt":
        raise RecipeError("approved YOLO source weights must use the .pt extension")

    exporter = value["exporter"]
    if not isinstance(exporter, dict) or set(exporter) != _EXPORTER_KEYS:
        raise RecipeError("ONNX exporter identity is invalid")
    _safe_local_file(exporter["path"], "exporter Python path")
    for key in ("pythonVersion", "ultralyticsVersion", "torchVersion", "onnxVersion"):
        version = _nonblank(exporter[key], f"exporter {key}", 100)
        if not _TOOL_VERSION.fullmatch(version):
            raise RecipeError(f"exporter {key} is invalid")


def build_onnx_stage(
    recipe: dict[str, object],
    work_root: Path,
    *,
    runner: Runner | None = None,
    timeout_seconds: int = 7_200,
) -> tuple[Path, dict[str, object], dict[str, object]]:
    validate_recipe(recipe)
    if not 60 <= timeout_seconds <= 7_200:
        raise RecipeError("ONNX build timeout must be between 60 seconds and 2 hours")
    runner = runner or _run_bounded
    work_root.mkdir(parents=True, exist_ok=False)
    artifact_root = work_root / "artifact"
    artifact_root.mkdir()
    sources = {source["role"]: source for source in recipe["sources"]}
    for source in sources.values():
        local_path = _safe_local_file(source["localPath"], f"{source['role']} source")
        if _sha256_file(local_path) != source["downloadedSha256"]:
            raise RecipeError(f"{source['role']} SHA-256 does not match its approved recipe")

    labels = _load_labels(Path(sources["labels"]["localPath"]).resolve(strict=True))
    labels_output = artifact_root / "labels.txt"
    labels_output.write_text("\n".join(labels) + "\n", encoding="utf-8", newline="\n")

    exporter = recipe["exporter"]
    python = str(Path(exporter["path"]).resolve(strict=True))
    script = str(Path(__file__).resolve(strict=True))
    probe_output = runner(
        [python, script, "__probe_worker"], work_root, 120, 1024 * 1024
    )
    identity = _marked_json(probe_output, _PROBE_MARKER)
    expected_identity = {
        "python": exporter["pythonVersion"],
        "ultralytics": exporter["ultralyticsVersion"],
        "torch": exporter["torchVersion"],
        "onnx": exporter["onnxVersion"],
    }
    if identity != expected_identity:
        raise RecipeError(f"exporter identity mismatch: expected {expected_identity}, got {identity}")

    weights_copy = work_root / "approved-yolo11n.pt"
    shutil.copyfile(Path(sources["weights"]["localPath"]).resolve(strict=True), weights_copy)
    model_output = artifact_root / "yolo11n.onnx"
    export_output = runner(
        [
            python,
            script,
            "__export_worker",
            str(weights_copy),
            str(model_output),
            str(recipe["imageSize"]),
            str(recipe["opset"]),
            str(len(labels)),
        ],
        work_root,
        timeout_seconds,
        _MAX_PROCESS_OUTPUT_BYTES,
    )
    contract = _marked_json(export_output, _EXPORT_MARKER)
    _validate_onnx_contract(contract, len(labels), recipe["imageSize"], recipe["opset"])
    if not model_output.is_file() or model_output.stat().st_size <= 0:
        raise RecipeError("exporter did not produce a non-empty yolo11n.onnx")

    package_files = _locked_files(artifact_root)
    package_lock = {
        "schemaVersion": 1,
        "id": recipe["id"],
        "sourceDateEpoch": recipe["sourceDateEpoch"],
        "files": package_files,
    }
    definition = {
        "id": recipe["id"],
        "kind": "model",
        "version": recipe["version"],
        "platform": recipe["platform"],
        "artifactLicense": recipe["artifactLicense"],
        "sources": [
            _manifest_source(source)
            for source in sorted(recipe["sources"], key=lambda item: item["role"])
        ],
        "tools": [
            {"name": "onnx", "version": exporter["onnxVersion"]},
            {"name": "python", "version": exporter["pythonVersion"]},
            {"name": "torch", "version": exporter["torchVersion"]},
            {"name": "ultralytics", "version": exporter["ultralyticsVersion"]},
        ],
        "build": {
            "recipe": "scripts/build_yolo_onnx_asset.py",
            "recipeVersion": "1",
            "parameters": {
                "batch": 1,
                "device": "cpu",
                "dynamic": False,
                "imageSize": recipe["imageSize"],
                "labelCount": len(labels),
                "opset": recipe["opset"],
                "simplify": False,
                "weightsSha256": sources["weights"]["downloadedSha256"],
            },
        },
    }
    return artifact_root, package_lock, definition


def build_release_asset(
    recipe: dict[str, object],
    output_directory: Path,
    *,
    timeout_seconds: int = 7_200,
    runner: Runner | None = None,
) -> dict[str, object]:
    validate_recipe(recipe)
    output_directory = output_directory.resolve(strict=False)
    output_directory.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix=".roadwatcher-onnx-build-", dir=output_directory))
    published: list[Path] = []
    try:
        artifact_root, package_lock, definition = build_onnx_stage(
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
            [node, str(manifest_tool), "build", str(definition_path), str(archive), str(manifest)],
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
            raise RecipeError("one or more ONNX release outputs already exist")
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


def _probe_worker() -> int:
    import onnx  # type: ignore
    import torch  # type: ignore
    import ultralytics  # type: ignore

    identity = {
        "python": ".".join(str(value) for value in sys.version_info[:3]),
        "ultralytics": ultralytics.__version__,
        "torch": torch.__version__,
        "onnx": onnx.__version__,
    }
    print(_PROBE_MARKER + json.dumps(identity, sort_keys=True, separators=(",", ":")))
    return 0


def _export_worker(arguments: list[str]) -> int:
    if len(arguments) != 5:
        raise RecipeError("internal ONNX export worker arguments are invalid")
    weights, output = Path(arguments[0]), Path(arguments[1])
    image_size, opset, label_count = (int(value) for value in arguments[2:])
    from ultralytics import YOLO  # type: ignore

    result = YOLO(str(weights)).export(
        format="onnx",
        imgsz=image_size,
        opset=opset,
        simplify=False,
        dynamic=False,
        batch=1,
        device="cpu",
    )
    exported = Path(str(result)).resolve(strict=True)
    if exported.suffix.lower() != ".onnx" or not exported.is_file():
        raise RecipeError("Ultralytics did not return a regular ONNX file")
    contract = _inspect_onnx(exported)
    _validate_onnx_contract(contract, label_count, image_size, opset)
    if output.exists():
        raise RecipeError("internal ONNX output already exists")
    shutil.copyfile(exported, output)
    print(_EXPORT_MARKER + json.dumps(contract, sort_keys=True, separators=(",", ":")))
    return 0


def _inspect_onnx(path: Path) -> dict[str, object]:
    import onnx  # type: ignore

    model = onnx.load(str(path), load_external_data=False)
    if any(initializer.external_data or initializer.data_location for initializer in model.graph.initializer):
        raise RecipeError("ONNX model cannot depend on external tensor data")
    onnx.checker.check_model(model, full_check=True)
    initializer_names = {initializer.name for initializer in model.graph.initializer}
    inputs = [value for value in model.graph.input if value.name not in initializer_names]
    outputs = list(model.graph.output)
    if len(inputs) != 1 or not outputs:
        raise RecipeError("ONNX detector must expose exactly one input and at least one output")
    return {
        "inputCount": len(inputs),
        "outputCount": len(outputs),
        "inputType": inputs[0].type.tensor_type.elem_type,
        "outputType": outputs[0].type.tensor_type.elem_type,
        "inputShape": _onnx_shape(inputs[0]),
        "outputShape": _onnx_shape(outputs[0]),
        "irVersion": model.ir_version,
        "opsets": sorted(
            [
                {"domain": item.domain or "ai.onnx", "version": item.version}
                for item in model.opset_import
            ],
            key=lambda item: item["domain"],
        ),
    }


def _onnx_shape(value_info) -> list[int | None]:
    result: list[int | None] = []
    for dimension in value_info.type.tensor_type.shape.dim:
        result.append(dimension.dim_value if dimension.HasField("dim_value") else None)
    return result


def _validate_onnx_contract(contract: object, label_count: int, image_size: int, opset: int) -> None:
    if not isinstance(contract, dict):
        raise RecipeError("ONNX exporter contract is invalid")
    if contract.get("inputCount") != 1 or not isinstance(contract.get("outputCount"), int) or contract["outputCount"] < 1:
        raise RecipeError("ONNX detector input/output count is incompatible")
    if contract.get("inputType") != 1 or contract.get("outputType") != 1:
        raise RecipeError("ONNX detector tensors must be float32")
    if contract.get("inputShape") != [1, 3, image_size, image_size]:
        raise RecipeError("ONNX detector input must be static [1,3,imageSize,imageSize]")
    output_shape = contract.get("outputShape")
    if (
        not isinstance(output_shape, list)
        or len(output_shape) != 3
        or output_shape[0] != 1
        or not all(isinstance(value, int) and value > 0 for value in output_shape)
        or label_count + 4 not in output_shape[1:]
    ):
        raise RecipeError("ONNX detector first output is incompatible with RoadWatcher labels")
    opsets = contract.get("opsets")
    if not isinstance(opsets, list) or not any(
        isinstance(item, dict) and item.get("domain") == "ai.onnx" and item.get("version") == opset
        for item in opsets
    ):
        raise RecipeError("ONNX detector opset does not match the approved export")


def _marked_json(output: str, marker: str) -> dict[str, object]:
    matches = [line[len(marker):] for line in output.splitlines() if line.startswith(marker)]
    if len(matches) != 1:
        raise RecipeError(f"worker output did not contain exactly one {marker.rstrip('=')} record")
    try:
        value = json.loads(matches[0], object_pairs_hook=_reject_duplicate_keys)
    except json.JSONDecodeError as error:
        raise RecipeError("worker identity output is invalid JSON") from error
    if not isinstance(value, dict):
        raise RecipeError("worker identity output must be an object")
    return value


def _load_labels(path: Path) -> list[str]:
    if path.stat().st_size > _MAX_LABEL_BYTES:
        raise RecipeError("labels source exceeds 2 MiB")
    try:
        labels = [line.strip() for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    except (OSError, UnicodeError) as error:
        raise RecipeError(f"cannot read labels source: {error}") from error
    if not labels or len(labels) > 10_000:
        raise RecipeError("labels must contain between 1 and 10000 classes")
    if len(set(labels)) != len(labels):
        raise RecipeError("labels contain duplicates")
    if any(len(label) > 160 or any(ord(character) < 32 for character in label) for label in labels):
        raise RecipeError("labels contain invalid class names")
    return labels


def _source(source: object) -> None:
    if not isinstance(source, dict) or not set(source).issubset(_SOURCE_KEYS):
        raise RecipeError("ONNX source definition has unsupported fields")
    required = _SOURCE_KEYS - {"etag", "lastModified"}
    if not required.issubset(source):
        raise RecipeError("ONNX source definition is incomplete")
    if not isinstance(source["id"], str) or not _ID.fullmatch(source["id"]):
        raise RecipeError("ONNX source id is invalid")
    _nonblank(source["role"], "source role", 50)
    _safe_local_file(source["localPath"], "source localPath")
    _https(source["url"], "source URL")
    _nonblank(source["version"], "source version", 200)
    _license(source["license"], "source license")
    _hash(source["downloadedSha256"], "downloaded source SHA-256")
    if source["publisherSha256"] is not None:
        _hash(source["publisherSha256"], "publisher source SHA-256")
    _iso_utc(source["retrievedAt"], "source retrievedAt")
    if source["publisherSha256"] is None and not source.get("etag") and not source.get("lastModified"):
        raise RecipeError("source without publisher hash needs ETag or Last-Modified evidence")
    if source.get("etag") is not None:
        _nonblank(source["etag"], "source ETag", 512)
    if source.get("lastModified") is not None:
        _nonblank(source["lastModified"], "source Last-Modified", 512)


def _license(value: object, label: str) -> None:
    if not isinstance(value, dict) or set(value) != _LICENSE_KEYS:
        raise RecipeError(f"{label} is invalid")
    if not isinstance(value["id"], str) or not _ID.fullmatch(value["id"]):
        raise RecipeError(f"{label} id is invalid")
    _nonblank(value["name"], f"{label} name", 200)
    _https(value["url"], f"{label} URL")


def _manifest_source(source: dict[str, object]) -> dict[str, object]:
    return {key: value for key, value in source.items() if key not in {"role", "localPath"}}


def _regular_tree_files(root: Path) -> list[Path]:
    result: list[Path] = []
    for path in sorted(root.rglob("*")):
        metadata = path.lstat()
        if path.is_symlink() or getattr(metadata, "st_file_attributes", 0) & _REPARSE_POINT:
            raise RecipeError("ONNX output contains a link or reparse point")
        if stat.S_ISREG(metadata.st_mode):
            if metadata.st_size <= 0:
                raise RecipeError("ONNX output contains an empty file")
            result.append(path)
        elif not stat.S_ISDIR(metadata.st_mode):
            raise RecipeError("ONNX output contains a special file")
    return result


def _locked_files(root: Path) -> list[dict[str, str]]:
    files = _regular_tree_files(root)
    return [
        {"path": PurePosixPath(path.relative_to(root).as_posix()).as_posix(), "sha256": _sha256_file(path)}
        for path in files
    ]


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
    if not text.startswith("https://"):
        raise RecipeError(f"{label} must use HTTPS")
    return text


def _nonblank(value: object, label: str, limit: int) -> str:
    if not isinstance(value, str) or not value.strip() or len(value) > limit or any(character in value for character in "\r\n\0"):
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


def _sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        while chunk := source.read(_COPY_CHUNK):
            digest.update(chunk)
    return digest.hexdigest()


def main(argv: list[str] | None = None) -> int:
    arguments = list(sys.argv[1:] if argv is None else argv)
    if arguments[:1] == ["__probe_worker"]:
        return _probe_worker()
    if arguments[:1] == ["__export_worker"]:
        return _export_worker(arguments[1:])
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("recipe", type=Path, help="strict owner-approved YOLO ONNX recipe JSON")
    parser.add_argument("output_directory", type=Path, help="directory for new release outputs")
    parser.add_argument("--timeout-seconds", type=int, default=7_200, help="export timeout (60-7200)")
    args = parser.parse_args(arguments)
    try:
        result = build_release_asset(load_recipe(args.recipe), args.output_directory, timeout_seconds=args.timeout_seconds)
    except (RecipeError, OSError) as error:
        parser.exit(1, f"YOLO ONNX asset build failed: {error}\n")
    print(json.dumps(result, sort_keys=True, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (RecipeError, OSError, ImportError) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2) from error
