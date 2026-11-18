#!/bin/sh
set -eu

install_dir="$HOME/Applications/Dilim.app"
if [ -L "$install_dir" ] || [ ! -d "$install_dir" ]; then
    printf '%s\n' "No ordinary Dilim app bundle was found at $install_dir."
    exit 1
fi
resolved_parent=$(CDPATH= cd -- "$HOME/Applications" && pwd -P)
resolved_app=$(CDPATH= cd -- "$install_dir" && pwd -P)
if [ "$resolved_app" != "$resolved_parent/Dilim.app" ]; then
    printf '%s\n' 'Refusing to remove an app outside the expected Applications directory.'
    exit 1
fi
bundle_id=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$install_dir/Contents/Info.plist")
if [ "$bundle_id" != 'com.dilim.app' ]; then
    printf '%s\n' 'The bundle is not Dilim.'
    exit 1
fi
"$install_dir/Contents/MacOS/Dilim" --uninstall-cleanup
if [ -L "$HOME/.local/bin/dilim" ] && [ "$(readlink "$HOME/.local/bin/dilim")" = "$install_dir/Contents/MacOS/dilim-cli" ]; then
    rm -- "$HOME/.local/bin/dilim"
fi
rm -rf -- "$install_dir"
printf '%s\n' 'Dilim was removed. Your settings and conversion results were kept.'
