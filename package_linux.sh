#!/bin/bash

# Configuration
# Allow VERSION to be passed as environment variable, default to 3.1.0
VERSION="${VERSION:-3.1.0}"
APP_NAME="fugo-launcher"
DISPLAY_NAME="Fugo Launcher"
MAINTAINER="Fugo Launcher Team"
DESCRIPTION="A fast, modern Minecraft launcher built with Avalonia."

# Directories
PUBLISH_DIR="dist/publish"
DEB_DIR="dist/deb"
DIST_DIR="dist"

mkdir -p "$PUBLISH_DIR"
mkdir -p "$DEB_DIR"

build_and_package() {
    local arch=$1
    local dotnet_arch=$2
    local deb_arch=$3

    echo "Building for $arch..."
    dotnet publish OfflineMinecraftLauncher.csproj -c Release -r "$dotnet_arch" --self-contained true -p:PublishSingleFile=true -p:PublishReadyToRun=true -o "$PUBLISH_DIR/$arch"
    
    if [ $? -ne 0 ]; then
        echo "Build failed for $arch"
        return 1
    fi

    echo "Packaging for $deb_arch..."
    local pkg_dir="$DEB_DIR/${APP_NAME}_${VERSION}_${deb_arch}"
    mkdir -p "$pkg_dir/opt/fugo-launcher"
    mkdir -p "$pkg_dir/usr/local/bin"
    mkdir -p "$pkg_dir/usr/share/applications"
    mkdir -p "$pkg_dir/usr/share/pixmaps"
    mkdir -p "$pkg_dir/DEBIAN"

    # Copy all files and folders from the publish directory
    cp -r "$PUBLISH_DIR/$arch/"* "$pkg_dir/opt/fugo-launcher/"
    chmod +x "$pkg_dir/opt/fugo-launcher/FugoLauncher"

    # Create symlink at /usr/local/bin/fugo-launcher pointing to /opt/fugo-launcher/FugoLauncher
    ln -sf "/opt/fugo-launcher/FugoLauncher" "$pkg_dir/usr/local/bin/${APP_NAME}"

    # Copy icon
    if [ -f "assets/fugo-logo.png" ]; then
        cp "assets/fugo-logo.png" "$pkg_dir/usr/share/pixmaps/${APP_NAME}.png"
    elif [ -f "assets/aether-logo.png" ]; then
        cp "assets/aether-logo.png" "$pkg_dir/usr/share/pixmaps/${APP_NAME}.png"
    fi

    # Create desktop entry
    cat <<EOT > "$pkg_dir/usr/share/applications/${APP_NAME}.desktop"
[Desktop Entry]
Version=1.0
Type=Application
Name=${DISPLAY_NAME}
Comment=${DESCRIPTION}
Exec=${APP_NAME}
Icon=${APP_NAME}
Terminal=false
Categories=Game;
StartupWMClass=FugoLauncher
EOT
    chmod 644 "$pkg_dir/usr/share/applications/${APP_NAME}.desktop"

    # Create control file
    cat <<EOT > "$pkg_dir/DEBIAN/control"
Package: ${APP_NAME}
Version: ${VERSION}
Section: games
Priority: optional
Architecture: ${deb_arch}
Maintainer: ${MAINTAINER}
Depends: libc6, libgcc1, libgssapi-krb5-2, libicu74 | libicu72 | libicu70 | libicu67 | libicu66 | libicu60 | libicu57 | libicu55 | libicu52 | libicu48 | libicu-dev, libssl3 | libssl1.1 | libssl1.0.0, libstdc++6, zlib1g, libx11-6
Description: ${DISPLAY_NAME} Minecraft client
 ${DESCRIPTION}
EOT

    # Build deb
    dpkg-deb --build "$pkg_dir" "$DIST_DIR/${APP_NAME}_${VERSION}_${deb_arch}.deb"
}

# Build for x64
build_and_package "linux-x64" "linux-x64" "amd64"

# Build for arm64
build_and_package "linux-arm64" "linux-arm64" "arm64"

echo "Done! Packages are in $DIST_DIR/"
