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

# Create DMG structure
mkdir -p build/dmg
cp -r build/Release/SpotifyWPF.app build/dmg/

# Create Applications symlink
ln -s /Applications build/dmg/Applications

# Create DMG
hdiutil create -volname "Spofyfy" -srcfolder build/dmg -ov -format UDZO Spofyfy.dmg

echo "DMG created successfully: Spofyfy.dmg"
echo "You can now distribute this DMG file for installation on macOS systems."