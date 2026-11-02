# SPDX-License-Identifier: MIT
# Copyright (c) 2026 sadwind227
"""Verify the release file list, dependencies, notices, manifests and ZIP bytes."""
import argparse
import ast
import datetime
import hashlib
import json
import posixpath
from pathlib import Path, PurePosixPath
import re
import sys
import zipfile

MANIFEST = "MANIFEST.sha256"
RUNTIME_FILES = set()
LOCAL_MODULES = {"hid_win", "battery_bridge", "read_logitech", "adapter_tool", "adapter_runtime"}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def expected(config, kind):
    return set(config["source_files"] if kind == "source" else config["program_files"] + config["document_files"])


def category(name):
    if name == "Examples/CustomPet/batterypet-pet.png":
        return "Maintainer-created image; separate bundle authorization in Examples/CustomPet/SOURCE.md"
    if name.startswith("licenses/dotnet/"):
        return "Upstream .NET license/notices or their provenance record"
    if name == "BatteryPet.exe":
        return ".NET apphost; MIT and upstream notices in licenses/dotnet"
    if name in RUNTIME_FILES:
        return "Microsoft runtime asset; licenses/dotnet; Windows native exceptions retain vendor terms"
    return "BatteryPet MIT; separate asset and third-party notices apply"


def validate_bytes(files, config, kind):
    wanted = expected(config, kind)
    actual = set(files) - {MANIFEST}
    if actual != wanted:
        raise ValueError(f"{kind}: missing={sorted(wanted - actual)}, unexpected={sorted(actual - wanted)}")
    for name, data in files.items():
        path = PurePosixPath(name)
        if path.is_absolute() or ".." in path.parts or "\\" in name:
            raise ValueError("Unsafe release path: " + name)
        if digest(data) in config["blocked_sha256"]:
            raise ValueError("Excluded upstream file detected: " + name)
        if name in config["asset_sha256"] and digest(data) != config["asset_sha256"][name]:
            raise ValueError("Image differs from the maintainer's provenance record: " + name)
    if b"MIT License" not in files["LICENSE"][:200]:
        raise ValueError("Root MIT license is missing")
    for name in ("licenses/dotnet/LICENSE.TXT", "licenses/dotnet/THIRD-PARTY-NOTICES.TXT"):
        if len(files[name]) < 100:
            raise ValueError("Missing .NET notice: " + name)
    imports = set()
    for name, data in files.items():
        if name.endswith(".py"):
            tree = ast.parse(data.decode("utf-8-sig"), filename=name)
            for node in ast.walk(tree):
                modules = [alias.name for alias in node.names] if isinstance(node, ast.Import) else [node.module] if isinstance(node, ast.ImportFrom) and node.level == 0 else []
                for module in modules:
                    if module:
                        root = module.split(".")[0]
                        imports.add(root)
                        if root not in sys.stdlib_module_names and root not in LOCAL_MODULES:
                            raise ValueError(f"Unreviewed Python dependency {module} in {name}")
        if name.endswith(".md"):
            text = data.decode("utf-8-sig")
            links = re.findall(r'!?\[[^\]]*\]\(([^)]+)\)|<img[^>]+src="([^"]+)"', text)
            for pair in links:
                link = next(x for x in pair if x).strip("<>")
                if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", link) or link.startswith("#"):
                    continue
                target = posixpath.normpath((PurePosixPath(name).parent / link.split("#")[0]).as_posix())
                if target not in files:
                    raise ValueError(f"Broken relative documentation link: {name} -> {link}")
    if kind == "source":
        project = files["BatteryPet.csproj"].decode("utf-8-sig")
        if "<PackageReference" in project:
            raise ValueError("NuGet dependencies require an updated rights review")
        if f"<Version>{config['version']}</Version>" not in project:
            raise ValueError("Project version mismatch")
    else:
        deps = json.loads(files["BatteryPet.deps.json"])
        if set(deps.get("libraries", {})) != {"BatteryPet/" + config["version"], "runtimepack.Microsoft.NETCore.App.Runtime.win-x64/" + config["runtime_version"], "runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/" + config["runtime_version"]}:
            raise ValueError("Unexpected published assembly dependencies")
    if kind == "windows":
        if any(name.endswith(".py") for name in files):
            raise ValueError("Python must not enter the native executable payload")
        for name, provenance in config["runtime_assets"].items():
            if digest(files[name]) != provenance["sha256"]:
                raise ValueError("Bundled runtime differs from official package bytes: " + name)
        runtime = json.loads(files["BatteryPet.runtimeconfig.json"])["runtimeOptions"]
        if "framework" in runtime or "frameworks" in runtime or len(runtime.get("includedFrameworks", [])) != 2:
            raise ValueError("Application is not self-contained")
    return sorted(imports)


def folder_files(root):
    if root.is_symlink():
        raise ValueError("Release root must not be a symlink")
    files = {}
    for path in root.rglob("*"):
        if path.is_symlink():
            raise ValueError("Release contains a symlink: " + str(path))
        if path.is_file():
            files[path.relative_to(root).as_posix()] = path.read_bytes()
    return files


def manifest(files):
    return "".join(f"{digest(files[name])}  {name}\n" for name in sorted(files) if name != MANIFEST).encode("utf-8")


def verify_manifest(files):
    if files.get(MANIFEST) != manifest(files):
        raise ValueError("File checksum manifest mismatch")


def write_zip(path, files):
    if path.exists():
        raise ValueError("Refusing to overwrite release archive: " + str(path))
    with zipfile.ZipFile(path, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name in sorted(files):
            info = zipfile.ZipInfo("BatteryPet/" + name, date_time=(2026, 11, 2, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            info.external_attr = 0o100644 << 16
            archive.writestr(info, files[name], compress_type=zipfile.ZIP_DEFLATED, compresslevel=9)


def read_zip(path):
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        if len(names) != len({n.casefold() for n in names}):
            raise ValueError("Duplicate archive paths")
        if any(not n.startswith("BatteryPet/") for n in names):
            raise ValueError("Unexpected archive root")
        if archive.testzip() is not None:
            raise ValueError("ZIP CRC check failed")
        return {n[len("BatteryPet/"):]: archive.read(n) for n in names}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    sub = parser.add_subparsers(dest="command", required=True)
    folder = sub.add_parser("folder")
    folder.add_argument("--kind", choices=("source", "windows"), required=True)
    folder.add_argument("--root", type=Path, required=True)
    pack = sub.add_parser("pack")
    pack.add_argument("--source", type=Path, required=True)
    pack.add_argument("--app", type=Path, required=True)
    pack.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    config = json.loads(args.config.read_text(encoding="utf-8-sig"))
    RUNTIME_FILES.update(config.get("runtime_assets", {}))
    if args.command == "folder":
        files = folder_files(args.root)
        imports = validate_bytes(files, config, args.kind)
        (args.root / MANIFEST).write_bytes(manifest(files))
        print(json.dumps({"kind": args.kind, "files": len(files) + (MANIFEST not in files), "imports": imports}))
        return
    report = {"version": config["version"], "created": datetime.datetime.now().astimezone().isoformat(),
              "status": "package-file-and-dependency-checks-passed",
              "scope": "Only the two archives listed here; not older workspace copies or repository history",
              "rights_limit": "File review is not a guarantee against all claims. Image provenance/public-domain status relies on the maintainer declaration.",
              "copyright_review": "docs/COPYRIGHT_AUDIT.zh-CN.md",
              "dotnet_apphost_version": config["dotnet_host_version"], "archives": []}
    for kind, root, suffix in (("source", args.source, "source"), ("windows", args.app, "windows-x64-preview")):
        files = folder_files(root)
        imports = validate_bytes(files, config, kind)
        verify_manifest(files)
        path = args.output / f"BatteryPet-v{config['version']}-{suffix}.zip"
        write_zip(path, files)
        unpacked = read_zip(path)
        validate_bytes(unpacked, config, kind)
        verify_manifest(unpacked)
        if files != unpacked:
            raise ValueError("Archive content differs from staging folder")
        report["archives"].append({"file": path.name, "sha256": digest(path.read_bytes()), "bytes": path.stat().st_size,
                                  "python_imports": imports, "file_count": len(files),
                                  "files": [{"path": name, "sha256": digest(data), "bytes": len(data), "license_basis": category(name)} for name, data in sorted(files.items())]})
    (args.output / "RELEASE_AUDIT.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    (args.output / "SHA256SUMS.txt").write_text("".join(f"{item['sha256']}  {item['file']}\n" for item in report["archives"]), encoding="ascii")
    print(json.dumps({"status": report["status"], "archives": [{k: item[k] for k in ("file", "bytes", "file_count", "sha256")} for item in report["archives"]]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
