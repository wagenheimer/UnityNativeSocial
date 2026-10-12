#!/usr/bin/env bash
# Compiles Runtime/ against the real Unity assemblies once per platform define set (Android, Android+Editor,
# iOS, macOS, Steam, Windows), WITHOUT opening Unity. The CI only bumps the version and tags, so a C# error in a
# platform-only #if block (Steam, Game Center...) used to reach a release unnoticed. Run it before pushing.
#
# Usage (Git Bash on Windows, or macOS):
#   Tools~/compile-check.sh [unity-project-that-uses-this-package]
#
# Needs: the .NET SDK (any version that builds netstandard2.1), a Unity Editor install and a Unity project that
# has already compiled this package once (its Library/ScriptAssemblies provides Google Play Games and Steamworks).
# Override the auto-detection with UNITY_MANAGED_DIR (…/Editor/Data/Managed/UnityEngine) and SCRIPT_ASSEMBLIES.
# The "Tools~" folder is ignored by Unity, so nothing here is imported into a game.
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
package_root="$(cd "$here/.." && pwd)"
project="${1:-${UNITY_PROJECT:-}}"

if [ -z "${SCRIPT_ASSEMBLIES:-}" ]; then
    [ -n "$project" ] || { echo "Pass the path of a Unity project that uses this package (or set SCRIPT_ASSEMBLIES)." >&2; exit 2; }
    SCRIPT_ASSEMBLIES="$project/Library/ScriptAssemblies"
fi
[ -d "$SCRIPT_ASSEMBLIES" ] || { echo "ScriptAssemblies not found: $SCRIPT_ASSEMBLIES (open the project in Unity once)." >&2; exit 2; }

if [ -z "${UNITY_MANAGED_DIR:-}" ]; then
    version=""
    [ -n "$project" ] && [ -f "$project/ProjectSettings/ProjectVersion.txt" ] && \
        version="$(sed -n 's/^m_EditorVersion: //p' "$project/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"
    for hub in "/c/Program Files/Unity/Hub/Editor" "C:/Program Files/Unity/Hub/Editor" "/Applications/Unity/Hub/Editor"; do
        [ -d "$hub" ] || continue
        if [ -n "$version" ] && [ -d "$hub/$version" ]; then editor="$hub/$version"; else editor="$(ls -d "$hub"/*/ 2>/dev/null | sort -V | tail -n1)"; fi
        for managed in "$editor/Editor/Data/Managed/UnityEngine" "$editor/Unity.app/Contents/Managed/UnityEngine"; do
            [ -d "$managed" ] && UNITY_MANAGED_DIR="$managed" && break 2
        done
    done
fi
[ -d "${UNITY_MANAGED_DIR:-}" ] || { echo "Unity managed assemblies not found; set UNITY_MANAGED_DIR." >&2; exit 2; }

# MSBuild on Windows does not understand Git Bash paths (/k/Games/...): without this it silently compiled
# nothing and every platform "passed".
native() { if command -v cygpath >/dev/null 2>&1; then cygpath -m "$1"; else printf '%s' "$1"; fi; }
package_root="$(native "$package_root")"
UNITY_MANAGED_DIR="$(native "$UNITY_MANAGED_DIR")"
SCRIPT_ASSEMBLIES="$(native "$SCRIPT_ASSEMBLIES")"
ls "$package_root"/Runtime/*.cs >/dev/null 2>&1 || { echo "No Runtime/*.cs under $package_root" >&2; exit 2; }

work="$(mktemp -d "${TMPDIR:-/tmp}/nativesocial-check.XXXXXX")"
trap 'rm -rf "$work"' EXIT
cat > "$work/check.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <DefineConstants>\$(ExtraDefines)</DefineConstants>
    <!-- Unity-style warnings that are noise outside the Editor (obsolete APIs, unused fields, nullable). -->
    <NoWarn>CS0618;CS0067;CS0414;CS0649;CS8632;CS0162</NoWarn>
    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$package_root/Runtime/**/*.cs" />
    <Reference Include="$UNITY_MANAGED_DIR/*.dll" Private="false" />
    <Reference Include="$SCRIPT_ASSEMBLIES/Google.Play.Games.dll" Private="false" />
    <Reference Include="$SCRIPT_ASSEMBLIES/com.rlabrecque.steamworks.net.dll" Private="false" />
  </ItemGroup>
</Project>
EOF

failed=0
for defines in \
    "UNITY_ANDROID" \
    "UNITY_ANDROID;UNITY_EDITOR" \
    "UNITY_IOS" \
    "UNITY_STANDALONE_OSX" \
    "UNITY_STANDALONE_WIN;WAGENHEIMER_NATIVESOCIAL_STEAM" \
    "UNITY_STANDALONE_WIN"; do
    # MSBuild needs ";" escaped as %3B inside a /p: value, or only the first define is applied.
    if out="$(dotnet build "$(native "$work/check.csproj")" --no-incremental -v q -nologo "-p:ExtraDefines=${defines//;/%3B}" 2>&1)"; then
        echo "OK    $defines"
    else
        echo "FAIL  $defines"
        echo "$out" | grep -E "error CS" | sed 's#.*/Runtime/#   Runtime/#' | sort -u
        failed=1
    fi
done
exit $failed
