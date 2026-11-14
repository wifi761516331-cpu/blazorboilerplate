#!/usr/bin/env bash
# 构建并打包业务模块到受控模块目录（不可变版本目录）。
# 用法: ./scripts/build-module.sh [module-id] [version]
#   module-id: catalog | identity （默认 catalog，保持向后兼容）
#   version:   默认 1.0.0
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MODULE_ID="${1:-catalog}"
VERSION="${2:-1.0.0}"

case "$MODULE_ID" in
  catalog)
    MODULE_PROJECT="$ROOT/src/Modules/Catalog/BlazorBoilerplate.Modules.Catalog/BlazorBoilerplate.Modules.Catalog.csproj"
    MODULE_NAME="BlazorBoilerplate.Modules.Catalog"
    ;;
  identity)
    MODULE_PROJECT="$ROOT/src/Modules/Identity/BlazorBoilerplate.Modules.Identity/BlazorBoilerplate.Modules.Identity.csproj"
    MODULE_NAME="BlazorBoilerplate.Modules.Identity"
    ;;
  *)
    echo "未知模块：$MODULE_ID（支持：catalog / identity）" >&2
    exit 1
    ;;
esac

MODULE_SRC="$(dirname "$MODULE_PROJECT")"
PACKAGE_DIR="$ROOT/modules/installed/$MODULE_ID/$VERSION"

echo "==> Building module $MODULE_ID (Release)..."
dotnet build "$MODULE_PROJECT" -c Release --nologo -v:q

BIN="$MODULE_SRC/bin/Release/net10.0"
echo "==> Packing into $PACKAGE_DIR"
rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR/backend" "$PACKAGE_DIR/localization"

cp "$BIN/$MODULE_NAME.dll" "$PACKAGE_DIR/backend/"
cp "$BIN/$MODULE_NAME.deps.json" "$PACKAGE_DIR/backend/" 2>/dev/null || true
cp "$MODULE_SRC/manifest.json" "$PACKAGE_DIR/manifest.json"

# 校验和文件（签名与包仓库为批次 2 待办，见 docs/ROADMAP.md）
( cd "$PACKAGE_DIR/backend" && sha256sum ./* > ../checksums.sha256 )

echo "==> Package ready:"
ls -la "$PACKAGE_DIR" "$PACKAGE_DIR/backend"
