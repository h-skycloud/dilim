#!/bin/sh
set -eu

data_dir=${XDG_DATA_HOME:-"$HOME/.local/share"}
install_dir="$data_dir/dilim/application"
desktop_file="$data_dir/applications/com.dilim.app.desktop"
python3 - "$data_dir" "$install_dir" <<'PY'
import pathlib
import sys

root, target = map(pathlib.Path, sys.argv[1:])
expected = root.resolve() / 'dilim' / 'application'
if target.is_symlink() or target.resolve() != expected:
    raise SystemExit('Refusing to remove an installation outside its expected directory.')
if (target / '.dilim-package').read_text(encoding='utf-8').strip() != 'com.dilim.app':
    raise SystemExit('The directory is not an installation created by Dilim.')
PY
"$install_dir/Dilim" --uninstall-cleanup
if [ -L "$HOME/.local/bin/dilim" ] && [ "$(readlink "$HOME/.local/bin/dilim")" = "$install_dir/dilim-cli" ]; then
    rm -- "$HOME/.local/bin/dilim"
fi
if [ -f "$desktop_file" ] && grep -q '^X-Dilim-Managed=true$' "$desktop_file"; then
    rm -- "$desktop_file"
fi
rm -rf -- "$install_dir"
printf '%s\n' 'Dilim was removed. Your settings and conversion results were kept.'
