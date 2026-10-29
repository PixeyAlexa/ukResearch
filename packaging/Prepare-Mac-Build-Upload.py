"""Create a source-only GitHub upload kit, never an installer or user-data backup."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib

root = Path(__file__).resolve().parent.parent
output = root / "Releases" / "macOS" / "PrasaResearch-Mac-Build-Source.zip"
output.parent.mkdir(parents=True, exist_ok=True)
directories = ["PrasaResearch.Core", "PrasaResearch.Desktop", "PrasaResearch.Core.Tests",
               "PrasaResarch", "PrasaResarch.Tests", "packaging", ".github", "docs"]
skip = {"bin", "obj", ".vs", ".git", "TestResults", "__pycache__"}
extensions = {".cs", ".csproj", ".slnx", ".axaml", ".xaml", ".md", ".txt", ".json",
              ".ps1", ".py", ".sh", ".yml", ".yaml", ".ico", ".png", ".bmp", ".plist", ".svg", ".iss"}
files = [root / p for p in ["PrasaResarch.slnx", "README.md", ".gitignore", ".gitattributes"]]
for directory in directories:
    for path in (root / directory).rglob("*"):
        if path.is_file() and not skip.intersection(path.relative_to(root).parts) and path.suffix.lower() in extensions:
            files.append(path)
with ZipFile(output, "w", ZIP_DEFLATED) as archive:
    for path in sorted(set(files)):
        content = path.read_bytes()
        if path.suffix.lower() in {".sh", ".yml", ".yaml"}:
            content = content.removeprefix(b"\xef\xbb\xbf").replace(b"\r\n", b"\n")
        archive.writestr(path.relative_to(root).as_posix(), content)
with ZipFile(output) as archive:
    assert archive.testzip() is None
    assert ".github/workflows/release-desktop.yml" in archive.namelist()
    assert b"\r" not in archive.read("packaging/Build-macOS.sh")
    assert not any("/bin/" in name or "/obj/" in name or name.endswith("credential.bin") for name in archive.namelist())
print(f"Source upload kit: {output}")
print(f"SHA256: {hashlib.sha256(output.read_bytes()).hexdigest()}")
