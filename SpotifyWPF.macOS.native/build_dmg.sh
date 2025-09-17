#!/bin/bash

# Spofyfy macOS DMG Builder
# This script builds the native macOS app and creates a .dmg installer

set -e

echo "Building Spofyfy for macOS..."

# Clean previous build
rm -rf build/
rm -f Spofyfy.dmg

# Build the Xcode project
xcodebuild -project SpotifyWPF.xcodeproj -scheme SpotifyWPF -configuration Release build

# Locate the built .app. Prefer DerivedData Products, then fallback to build/Release.
APP_PATH=""

# Try to discover DerivedData path from xcodebuild output (default location)
DERIVED_DATA_DIR="$HOME/Library/Developer/Xcode/DerivedData"
if [ -d "$DERIVED_DATA_DIR" ]; then
	# look for any matching Products/Release/*.app
	APP_PATH=$(find "$DERIVED_DATA_DIR" -maxdepth 3 -type d -path "*/Build/Products/Release/*.app" -print -quit 2>/dev/null || true)
fi

# Fallback: check repository-local build/Release for common names
if [ -z "$APP_PATH" ]; then
	for name in "SpofifyWPF.app" "SpotifyWPF.app"; do
		if [ -d "build/Release/$name" ]; then
			APP_PATH="build/Release/$name"
			break
		fi
	done
fi

if [ -z "$APP_PATH" ]; then
	echo "Error: built .app not found. Searched DerivedData and build/Release." >&2
	exit 1
fi

# Create DMG structure and copy the app
mkdir -p build/dmg
cp -r "$APP_PATH" build/dmg/

# Create Applications symlink
ln -s /Applications build/dmg/Applications

# Create DMG
hdiutil create -volname "Spofyfy" -srcfolder build/dmg -ov -format UDZO Spofyfy.dmg

echo "DMG created successfully: Spofyfy.dmg"
echo "You can now distribute this DMG file for installation on macOS systems."