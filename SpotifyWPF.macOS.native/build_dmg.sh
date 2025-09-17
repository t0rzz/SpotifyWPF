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
	echo "Searching DerivedData for built .app under: $DERIVED_DATA_DIR"
	# look for any matching Products/Release/*.app anywhere under DerivedData
	# Don't restrict depth here: DerivedData layout can vary. Use -path to focus on Release products.
	APP_PATH=$(find "$DERIVED_DATA_DIR" -type d -path "*/Build/Products/Release/*.app" -print -quit 2>/dev/null || true)
	if [ -n "$APP_PATH" ]; then
		echo "Found .app in DerivedData: $APP_PATH"
	else
		echo "No .app found in DerivedData (will try repository-local build/Release)."
		# For debugging, list up to 10 matching candidates so CI logs show what's present
		echo "DerivedData candidates (first 10):"
		find "$DERIVED_DATA_DIR" -type d -path "*/Build/Products/Release/*.app" -print 2>/dev/null | head -n 10 || true
	fi
fi

# As an extra fallback, explicitly look for common product names under DerivedData's Products/Release
if [ -z "$APP_PATH" ] && [ -d "$DERIVED_DATA_DIR" ]; then
	for name in "SpofifyWPF.app" "SpotifyWPF.app"; do
		CANDIDATE=$(find "$DERIVED_DATA_DIR" -type d -path "*/Build/Products/Release/$name" -print -quit 2>/dev/null || true)
		if [ -n "$CANDIDATE" ]; then
			APP_PATH="$CANDIDATE"
			echo "Found explicit product name in DerivedData: $APP_PATH"
			break
		fi
	done
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
echo "Copying .app from: $APP_PATH"
echo "Contents of .app directory:"
ls -la "$APP_PATH" | head -20
echo "Size of .app:"
du -sh "$APP_PATH"
cp -r "$APP_PATH" build/dmg/

# Check what was copied
echo "Contents of build/dmg after copy:"
ls -la build/dmg/
echo "Size of copied .app:"
du -sh build/dmg/*.app 2>/dev/null || echo "No .app found in build/dmg"

# Create Applications symlink
ln -s /Applications build/dmg/Applications

# Create DMG
echo "Creating DMG..."
hdiutil create -volname "Spofyfy" -srcfolder build/dmg -ov -format UDZO Spofyfy.dmg

echo "DMG created successfully: Spofyfy.dmg"
echo "DMG size:"
ls -lh Spofyfy.dmg