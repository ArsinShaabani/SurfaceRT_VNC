#!/usr/bin/env python3
"""Structural checks for the Windows Store app sources.

The Windows RT 8.1 app can only be *compiled* on Windows with the Windows 8.1 SDK, so this
script performs the checks that are possible anywhere: it validates the XML files, verifies that
every event handler referenced from XAML exists in the code-behind with the expected signature,
verifies that the project file lists every source file on disk, and verifies that the shared core
sources are safe for the WinRT build.

Usage:  python3 tools/check-store-app.py
"""
import os
import re
import sys
import xml.etree.ElementTree as ElementTree

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
STORE_DIR = os.path.join(ROOT, "src", "SurfaceVnc.Store")
CORE_DIR = os.path.join(ROOT, "src", "SurfaceVnc.Core")

problems = []
notes = []


def fail(message):
    problems.append(message)


def note(message):
    notes.append(message)


def read(path):
    with open(path, "r", encoding="utf-8") as handle:
        return handle.read()


def check_xml(path):
    try:
        ElementTree.parse(path)
    except Exception as error:
        fail("{0}: XML is not well formed: {1}".format(os.path.relpath(path, ROOT), error))


# ---------------------------------------------------------------------------------------
# 1. XML well formedness
# ---------------------------------------------------------------------------------------
xml_files = [
    os.path.join(STORE_DIR, "App.xaml"),
    os.path.join(STORE_DIR, "MainPage.xaml"),
    os.path.join(STORE_DIR, "Package.appxmanifest"),
    os.path.join(STORE_DIR, "SurfaceVnc.Store.csproj"),
]
for path in xml_files:
    if not os.path.exists(path):
        fail("{0}: file is missing".format(os.path.relpath(path, ROOT)))
        continue
    check_xml(path)

solution = os.path.join(ROOT, "SurfaceVnc.sln")
if "SurfaceVnc.Store.csproj" not in read(solution):
    fail("SurfaceVnc.sln does not reference the store project")

# ---------------------------------------------------------------------------------------
# 2. Event handlers referenced from XAML must exist in the code-behind
# ---------------------------------------------------------------------------------------
EXPECTED_ARGS = {
    "Click": "RoutedEventArgs",
    "SizeChanged": "SizeChangedEventArgs",
    "TextChanged": "TextChangedEventArgs",
    "PointerPressed": "PointerRoutedEventArgs",
    "PointerMoved": "PointerRoutedEventArgs",
    "PointerReleased": "PointerRoutedEventArgs",
    "PointerCanceled": "PointerRoutedEventArgs",
    "PointerCaptureLost": "PointerRoutedEventArgs",
}

pairs = [
    (os.path.join(STORE_DIR, "MainPage.xaml"), os.path.join(STORE_DIR, "MainPage.xaml.cs")),
    (os.path.join(STORE_DIR, "App.xaml"), os.path.join(STORE_DIR, "App.xaml.cs")),
]

for xaml_path, code_path in pairs:
    xaml = read(xaml_path)
    code = read(code_path)
    tree = ElementTree.parse(xaml_path)

    handlers = set()
    for element in tree.iter():
        for attribute, value in element.attrib.items():
            name = attribute.split("}")[-1]
            if name in EXPECTED_ARGS:
                handlers.add((name, value))

    for attribute, handler in sorted(handlers):
        expected = EXPECTED_ARGS[attribute]
        pattern = re.compile(
            r"void\s+" + re.escape(handler) + r"\s*\(\s*object\s+\w+\s*,\s*([A-Za-z0-9_\.]+)\s+\w+\s*\)")
        match = pattern.search(code)
        if match is None:
            fail("{0}: handler '{1}' for {2} is not defined in {3}".format(
                os.path.relpath(xaml_path, ROOT), handler, attribute, os.path.basename(code_path)))
            continue
        actual = match.group(1).split(".")[-1]
        if actual != expected:
            fail("{0}: handler '{1}' takes {2} but {3} requires {4}".format(
                os.path.relpath(xaml_path, ROOT), handler, actual, attribute, expected))

# ---------------------------------------------------------------------------------------
# 3. Every x:Name in XAML should be used by the code-behind (informational)
# ---------------------------------------------------------------------------------------
for xaml_path, code_path in pairs:
    code = read(code_path)
    tree = ElementTree.parse(xaml_path)

    for element in tree.iter():
        declared = None
        has_handler = False
        for attribute, value in element.attrib.items():
            name = attribute.split("}")[-1]
            if name == "Name":
                declared = value
            if name in EXPECTED_ARGS:
                has_handler = True

        if declared is None or has_handler:
            continue
        if not re.search(r"\b" + re.escape(declared) + r"\b", code):
            note("{0}: '{1}' is declared but never used in {2}".format(
                os.path.relpath(xaml_path, ROOT), declared, os.path.basename(code_path)))

# ---------------------------------------------------------------------------------------
# 4. The project file must list every source file that exists on disk
# ---------------------------------------------------------------------------------------
project = read(os.path.join(STORE_DIR, "SurfaceVnc.Store.csproj"))
listed = set(m.replace("\\", "/") for m in re.findall(r'Include="([^"]+\.(?:cs|xaml|png|appxmanifest))"', project))

disk = set()
for folder, _, files in os.walk(STORE_DIR):
    for name in files:
        if name.endswith((".cs", ".xaml", ".png", ".appxmanifest")):
            full = os.path.join(folder, name)
            disk.add(os.path.relpath(full, STORE_DIR).replace("\\", "/"))

for name in sorted(disk - listed):
    fail("SurfaceVnc.Store.csproj does not include {0} (it would not be compiled/deployed)".format(name))
for name in sorted(listed):
    if "*" in name:
        continue    # wildcard include of the shared core sources
    if not os.path.exists(os.path.join(STORE_DIR, name)):
        fail("SurfaceVnc.Store.csproj includes {0} but the file does not exist".format(name))

# ---------------------------------------------------------------------------------------
# 5. The manifest must be able to reach the local network and launch the right class
# ---------------------------------------------------------------------------------------
manifest = read(os.path.join(STORE_DIR, "Package.appxmanifest"))
if 'Name="privateNetworkClientServer"' not in manifest:
    fail("Package.appxmanifest is missing the privateNetworkClientServer capability (LAN access)")
if 'EntryPoint="SurfaceVnc.Store.App"' not in manifest:
    fail("Package.appxmanifest EntryPoint must be SurfaceVnc.Store.App")
if 'Executable="SurfaceVnc.Store.exe"' not in manifest:
    fail("Package.appxmanifest Executable must be SurfaceVnc.Store.exe")
for asset in re.findall(r'(?:Logo|Image)="([^"]+\.png)"', manifest):
    asset_path = os.path.join(STORE_DIR, asset.replace("\\", "/"))
    if not os.path.exists(asset_path):
        fail("Package.appxmanifest references {0} but the file does not exist".format(asset))

# ---------------------------------------------------------------------------------------
# 6. Shared core sources must be WinRT safe
# ---------------------------------------------------------------------------------------
socket_transport = os.path.join(CORE_DIR, "SocketRfbTransport.cs")
if os.path.exists(socket_transport):
    if "#if !NETFX_CORE" not in read(socket_transport):
        fail("SocketRfbTransport.cs uses System.Net.Sockets and must be wrapped in #if !NETFX_CORE "
             "so it is excluded from the WinRT build")

winrt_forbidden = [
    ("Encoding.GetEncoding", "code page lookups are not available in Windows Store apps"),
    ("System.Net.Sockets", "not available in Windows Store apps"),
    ("Span<", "not available on the Windows 8.1 tool chain"),
]
for folder, _, files in os.walk(CORE_DIR):
    if "obj" in folder or "bin" in folder:
        continue
    for name in files:
        if not name.endswith(".cs") or name == "SocketRfbTransport.cs":
            continue
        path = os.path.join(folder, name)
        text = read(path)
        # Comments mention these APIs on purpose; only real code counts.
        text = re.sub(r"//[^\n]*", "", text)
        text = re.sub(r"/\*.*?\*/", "", text, flags=re.S)
        text = re.sub(r"///[^\n]*", "", text)
        for needle, why in winrt_forbidden:
            if needle in text:
                fail("{0} uses '{1}': {2}".format(os.path.relpath(path, ROOT), needle, why))

# ---------------------------------------------------------------------------------------
# 7. C# files must have balanced braces / parentheses and no C# 6+ syntax we cannot use
# ---------------------------------------------------------------------------------------
csharp_files = []
for base in (STORE_DIR, CORE_DIR):
    for folder, _, files in os.walk(base):
        if "obj" in folder or "bin" in folder:
            continue
        for name in files:
            if name.endswith(".cs"):
                csharp_files.append(os.path.join(folder, name))

for path in csharp_files:
    text = read(path)
    stripped = re.sub(r'"(?:[^"\\]|\\.)*"', '""', text)
    stripped = re.sub(r"'(?:[^'\\]|\\.)*'", "''", stripped)
    stripped = re.sub(r"//[^\n]*", "", stripped)
    stripped = re.sub(r"/\*.*?\*/", "", stripped, flags=re.S)

    for open_char, close_char in (("{", "}"), ("(", ")")):
        if stripped.count(open_char) != stripped.count(close_char):
            fail("{0}: unbalanced '{1}{2}' ({3} vs {4})".format(
                os.path.relpath(path, ROOT), open_char, close_char,
                stripped.count(open_char), stripped.count(close_char)))

    if "?." in stripped:
        fail("{0}: null conditional operator is not available on the Windows 8.1 tool chain".format(
            os.path.relpath(path, ROOT)))
    if re.search(r'\$"', stripped):
        fail("{0}: string interpolation is not available on the Windows 8.1 tool chain".format(
            os.path.relpath(path, ROOT)))
    if re.search(r"out\s+var\s", stripped):
        fail("{0}: 'out var' is not available on the Windows 8.1 tool chain".format(
            os.path.relpath(path, ROOT)))

# ---------------------------------------------------------------------------------------
print("checked {0} C# files and {1} XML files".format(len(csharp_files), len(xml_files)))
for message in notes:
    print("  note: " + message)
if problems:
    print()
    for message in problems:
        print("  FAIL: " + message)
    print("\n{0} problem(s) found".format(len(problems)))
    sys.exit(1)

print("\nall structural checks passed")
