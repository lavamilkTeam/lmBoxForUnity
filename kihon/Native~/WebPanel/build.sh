#!/bin/sh
set -eu
native_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
plugin_dir="$native_dir/../../Editor/Plugins/macOS/LmboxWebPanel.bundle/Contents"
mkdir -p "$plugin_dir/MacOS"
xcrun clang -bundle -fobjc-arc -Wall -Wextra -Wno-unused-parameter \
  -arch arm64 -arch x86_64 -mmacosx-version-min=11.0 \
  -framework Cocoa -framework WebKit \
  "$native_dir/WebPanel.m" -o "$plugin_dir/MacOS/LmboxWebPanel"
codesign --force --sign - "$plugin_dir/.."
