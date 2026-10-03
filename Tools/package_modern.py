"""Pacotes Modern; somente biblioteca padrao, sips/iconutil opcionais no macOS."""
import argparse
from pathlib import Path
import plistlib
import shutil
import stat
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile

RIDS = ("win-x64", "linux-x64", "osx-arm64", "osx-x64")
BUNDLE = "PKHeX Modern.app"


def make_icon(source: Path, destination: Path, work: Path) -> bool:
    if not source.is_file() or not shutil.which("sips") or not shutil.which("iconutil"):
        print("Icone .icns indisponivel: sem sips/iconutil ou PNG; bundle sem icone.")
        return False
    iconset = work / "Modern.iconset"
    iconset.mkdir()
    try:
        for size in (16, 32, 128, 256, 512):
            for scale in (1, 2):
                suffix = "@2x" if scale == 2 else ""
                output = iconset / f"icon_{size}x{size}{suffix}.png"
                subprocess.run(["sips", "-z", str(size * scale), str(size * scale), str(source), "--out", str(output)],
                               check=True, capture_output=True)
        subprocess.run(["iconutil", "-c", "icns", str(iconset), "-o", str(destination)], check=True, capture_output=True)
        return True
    except subprocess.CalledProcessError:
        destination.unlink(missing_ok=True)
        print("Falha ao gerar .icns; bundle sem icone.")
        return False


def package(rid: str, published: Path, output: Path, version: str, icon: Path | None = None) -> Path:
    if rid not in RIDS:
        raise ValueError(f"RID nao suportado: {rid}")
    executable = "PKHeX.Modern.exe" if rid == "win-x64" else "PKHeX.Modern"
    if not (published / executable).is_file():
        raise FileNotFoundError(published / executable)
    output.mkdir(parents=True, exist_ok=True)
    destination = output / f"PKHeX.Modern-{rid}.zip"
    with tempfile.TemporaryDirectory(prefix="pkhex-package-", dir=output) as staging:
        work = Path(staging)
        if rid.startswith("osx-"):
            bundle = work / BUNDLE
            contents = bundle / "Contents"
            shutil.copytree(published, contents / "MacOS")
            resources = contents / "Resources"
            resources.mkdir()
            metadata = {
                "CFBundleExecutable": executable,
                "CFBundleIdentifier": "io.github.carlosmozart.pkhexmodern",
                "CFBundleName": "PKHeX Modern",
                "CFBundleDisplayName": "PKHeX Modern",
                "CFBundlePackageType": "APPL",
                "CFBundleShortVersionString": version,
                "CFBundleVersion": version,
                "LSMinimumSystemVersion": "14.0",
                "NSHighResolutionCapable": True,
            }
            if icon is not None and make_icon(icon, resources / "Modern.icns", work):
                metadata["CFBundleIconFile"] = "Modern.icns"
            with (contents / "Info.plist").open("wb") as handle:
                plistlib.dump(metadata, handle)
            root = work
            files = sorted(bundle.rglob("*"))
        else:
            root = published
            files = sorted(published.rglob("*"))
        with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
            for source in files:
                if not source.is_file():
                    continue
                relative = source.relative_to(root).as_posix()
                entry = zipfile.ZipInfo.from_file(source, relative)
                entry.create_system = 3
                mode = 0o755 if source.name == executable and rid != "win-x64" else 0o644
                entry.external_attr = (stat.S_IFREG | mode) << 16
                entry.compress_type = zipfile.ZIP_DEFLATED
                with source.open("rb") as handle, archive.open(entry, "w") as target:
                    shutil.copyfileobj(handle, target)
    print(f"Pacote: {destination} ({destination.stat().st_size} bytes)")
    return destination


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rid", required=True, choices=RIDS)
    parser.add_argument("--published", type=Path)
    parser.add_argument("--output", type=Path, default=Path.cwd())
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    project = root / "PKHeX.Modern.Version.props"
    version = ET.parse(project).findtext(".//Version")
    if not version:
        raise ValueError("Version ausente no csproj")
    published = args.published or root / "PKHeX.Modern.Desktop" / "bin" / "publish" / args.rid
    package(args.rid, published, args.output, version, root / "icon.png")


if __name__ == "__main__":
    main()
