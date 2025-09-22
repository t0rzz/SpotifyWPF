#!/bin/bash

# SpotifyWPF macOS DMG Builder
# This script builds the native macOS app and creates a .dmg installer

set -e

echo "Building SpotifyWPF for macOS..."

# Clean previous build
rm -rf build/
rm -f SpotifyWPF.dmg

# Build the Xcode project with explicit output directory
echo "Building Xcode project..."
BUILD_DIR="$(pwd)/build"
xcodebuild -project SpotifyWPF.xcodeproj -scheme SpotifyWPF -configuration Release -derivedDataPath "$BUILD_DIR/DerivedData" build

# First, check if the build succeeded by looking for any .app files
echo "Searching for any .app files in the workspace..."
find . -name "*.app" -type d 2>/dev/null | head -10

# Locate the built .app. Check multiple possible locations
APP_PATH=""

# Check explicit build directory first
if [ -d "$BUILD_DIR/DerivedData" ]; then
	echo "Checking explicit build directory: $BUILD_DIR/DerivedData"
	APP_PATH=$(find "$BUILD_DIR/DerivedData" -name "*.app" -type d -print -quit 2>/dev/null || true)
	if [ -n "$APP_PATH" ]; then
		echo "Found .app in explicit build directory: $APP_PATH"
	fi
fi

# Try to discover DerivedData path from xcodebuild output (default location)
DERIVED_DATA_DIR="$HOME/Library/Developer/Xcode/DerivedData"
if [ -z "$APP_PATH" ] && [ -d "$DERIVED_DATA_DIR" ]; then
	echo "Searching DerivedData for built .app under: $DERIVED_DATA_DIR"
	# look for any matching Products/Release/*.app anywhere under DerivedData
	APP_PATH=$(find "$DERIVED_DATA_DIR" -type d -path "*/Build/Products/Release/*.app" -print -quit 2>/dev/null || true)
	if [ -n "$APP_PATH" ]; then
		echo "Found .app in DerivedData: $APP_PATH"
	else
		echo "No .app found in DerivedData"
		# For debugging, list all .app files in DerivedData
		echo "All .app files in DerivedData:"
		find "$DERIVED_DATA_DIR" -name "*.app" -type d 2>/dev/null | head -10 || true
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

# Check if build actually produced any output
if [ -z "$APP_PATH" ]; then
	echo "No .app found in standard locations. Checking build directory..."
	if [ -d "build" ]; then
		echo "Contents of build directory:"
		find build -type f -name "*.app" 2>/dev/null || echo "No .app files in build directory"
		ls -la build/
	fi
	
	# Check if there are any .app files anywhere in the current directory tree
	echo "Searching entire workspace for .app files..."
	find . -name "*.app" -type d 2>/dev/null | head -10 || echo "No .app files found anywhere"
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
	echo "Error: built .app not found. Searched all possible locations."
	echo "Build may have failed or output location is different."
	echo "Checking if build directory exists and has content..."
	if [ -d "build" ]; then
		echo "Build directory contents:"
		ls -la build/
		echo "Checking for any build artifacts:"
		find build -type f 2>/dev/null | head -20 || echo "No files found in build directory"
	else
		echo "Build directory does not exist - build likely failed"
	fi
	exit 1
fi

# Create DMG structure and copy the app
mkdir -p build/dmg
echo "Copying .app from: $APP_PATH"
echo "Contents of .app directory:"
ls -la "$APP_PATH" | head -20
echo "Size of .app:"
du -sh "$APP_PATH"

# Check if .app has reasonable content
APP_SIZE=$(du -sk "$APP_PATH" | cut -f1)
if [ "$APP_SIZE" -lt 1000 ]; then
	echo "WARNING: .app bundle is very small (${APP_SIZE}KB). This may indicate missing resources."
	echo "Detailed contents of .app:"
	find "$APP_PATH" -type f | head -20
fi

cp -r "$APP_PATH" build/dmg/

# Check what was copied
echo "Contents of build/dmg after copy:"
ls -la build/dmg/
echo "Size of copied .app:"
du -sh build/dmg/*.app 2>/dev/null || echo "No .app found in build/dmg"

# Verify the copied .app has content
if [ -d "build/dmg/SpotifyWPF.app" ]; then
	echo "Verifying copied .app bundle contents:"
	ls -la build/dmg/SpotifyWPF.app/Contents/ 2>/dev/null || echo "Contents directory missing"
	
	# Check if executable exists and has size
	if [ -f "build/dmg/SpotifyWPF.app/Contents/MacOS/SpotifyWPF" ]; then
		echo "Executable size:"
		ls -lh build/dmg/SpotifyWPF.app/Contents/MacOS/SpotifyWPF
	else
		echo "ERROR: Executable missing!"
	fi
	
	# Check if WebApp resources exist
	if [ -d "build/dmg/SpotifyWPF.app/Contents/Resources/WebApp" ]; then
		echo "WebApp resources found:"
		find build/dmg/SpotifyWPF.app/Contents/Resources/WebApp -name "*.html" -o -name "*.css" -o -name "*.js" | head -10
	else
		echo "ERROR: WebApp resources missing!"
	fi
else
	echo "ERROR: .app bundle not found in build/dmg!"
	exit 1
fi

# Create Applications symlink
echo "Creating Applications symlink..."
if ln -s /Applications build/dmg/Applications; then
	echo "Applications symlink created successfully"
	ls -la build/dmg/
else
	echo "WARNING: Failed to create Applications symlink"
fi

# Create DMG
echo "Creating DMG..."
DMG_DIR_SIZE=$(du -sk build/dmg | cut -f1)
echo "DMG source directory size: ${DMG_DIR_SIZE}KB"

hdiutil create -volname "SpotifyWPF" -srcfolder build/dmg -ov -format UDZO SpotifyWPF.dmg

echo "DMG created successfully: SpotifyWPF.dmg"
echo "DMG size:"
ls -lh SpotifyWPF.dmg

# Verify DMG contents
echo "Verifying DMG contents..."
if [ -f "SpotifyWPF.dmg" ]; then
	DMG_SIZE=$(ls -l SpotifyWPF.dmg | awk '{print $5}')
	DMG_SIZE_KB=$((DMG_SIZE / 1024))
	echo "DMG file size: ${DMG_SIZE_KB}KB"
	
	# Expected size should be reasonable compression of source
	if [ "$DMG_SIZE_KB" -lt 50 ]; then
		echo "WARNING: DMG is suspiciously small (${DMG_SIZE_KB}KB). Expected >50KB for a complete app."
		echo "This may indicate the .app bundle is empty or missing."
	fi
else
	echo "ERROR: DMG file was not created!"
	exit 1
fi