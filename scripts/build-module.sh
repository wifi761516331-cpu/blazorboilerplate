#!/usr/bin/env bash
# 构建并打包业务模块到受控模块目录（不可变版本目录）。
# 用法: ./scripts/build-module.sh [module-id] [version]
#   module-id: catalog | identity | cms | attachments | localization | domains | demo（默认 catalog，保持向后兼容）
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
  cms)
    MODULE_PROJECT="$ROOT/src/Modules/Cms/BlazorBoilerplate.Modules.Cms/BlazorBoilerplate.Modules.Cms.csproj"
    MODULE_NAME="BlazorBoilerplate.Modules.Cms"
    ;;
  attachments)
    MODULE_PROJECT="$ROOT/src/Modules/Attachments/BlazorBoilerplate.Modules.Attachments/BlazorBoilerplate.Modules.Attachments.csproj"
    MODULE_NAME="BlazorBoilerplate.Modules.Attachments"
    ;;
  localization)
    MODULE_PROJECT="$ROOT/src/Modules/Localization/BlazorBoilerplate.Modules.Localization/BlazorBoilerplate.Modules.Localization.csproj"
    MODULE_NAME="BlazorBoilerplate.Modules.Localization"
    ;;
  domains)
    MODULE_PROJECT="$ROOT/src/Modules/Domains/BlazorBoilerplate.Modules.Domains/BlazorBoilerplate.Modules.Domains.csproj"
    MODULE_NAME="BlazorBoilerplate.Modules.Domains"
    ;;
  demo)
    MODULE_PROJECT="$ROOT/src/Modules/Demo/BlazorBoilerplate.Modules.Demo/BlazorBoilerplate.Modules.Demo.csproj"
    MODULE_NAME="BlazorBoilerplate.Modules.Demo"
    ;;
  *)
    echo "未知模块：$MODULE_ID（支持：catalog / identity / cms / attachments / localization / domains / demo）" >&2
    exit 1
    ;;
esac

MODULE_SRC="$(dirname "$MODULE_PROJECT")"
PACKAGE_DIR="$ROOT/modules/installed/$MODULE_ID/$VERSION"

# 每次构建到唯一 staging 目录，再从 staging 覆盖复制到包目录。
# 不能从 src/**/bin/Release/net10.0 复制：该目录可能被其它构建/清理操作抹掉，
# 导致 deps.json 已声明 NuGet 依赖、backend/ 却缺 DLL（运行期 ALC FileNotFound）。
# 也不做 rm -rf：批量删除会被宿主安全策略拦截（超过阈值直接失败）。
STAGE_PARENT="$ROOT/modules/.build/$MODULE_ID"
mkdir -p "$STAGE_PARENT"
STAGE_ROOT="$(mktemp -d "$STAGE_PARENT/${VERSION}.XXXXXX")"
BACKEND_STAGE="$STAGE_ROOT/backend"
FRONTEND_STAGE="$STAGE_ROOT/frontend"
mkdir -p "$BACKEND_STAGE"

echo "==> Building module $MODULE_ID (Release) -> $BACKEND_STAGE"
dotnet build "$MODULE_PROJECT" -c Release --nologo -v:q -o "$BACKEND_STAGE"

FRONTEND_PROJECT="$MODULE_SRC/../$MODULE_NAME.Frontend/$MODULE_NAME.Frontend.csproj"
if [ -f "$FRONTEND_PROJECT" ]; then
  echo "==> Building module frontend $MODULE_ID (Release) -> $FRONTEND_STAGE"
  mkdir -p "$FRONTEND_STAGE"
  dotnet build "$FRONTEND_PROJECT" -c Release --nologo -v:q -o "$FRONTEND_STAGE"
fi

echo "==> Packing into $PACKAGE_DIR"
# 覆盖复制而非删除重建：旧文件保留不会破坏 ALC（deps.json 决定实际加载集）。
mkdir -p "$PACKAGE_DIR/backend" "$PACKAGE_DIR/localization"

# 复制模块程序集 + 全部私有依赖（Microsoft.Extensions.AI / OpenAI 等）。
# 通用规则：模块私有依赖随包分发，宿主 csproj 不需要感知；共享契约由 ALC 白名单回落宿主。
cp "$BACKEND_STAGE"/*.dll "$PACKAGE_DIR/backend/"
cp "$BACKEND_STAGE/$MODULE_NAME.deps.json" "$PACKAGE_DIR/backend/" 2>/dev/null || true
cp "$MODULE_SRC/manifest.json" "$PACKAGE_DIR/manifest.json"

if [ -d "$MODULE_SRC/localization" ]; then
  cp "$MODULE_SRC"/localization/*.json "$PACKAGE_DIR/localization/"
fi

if [ -d "$FRONTEND_STAGE" ] && compgen -G "$FRONTEND_STAGE/*.dll" > /dev/null; then
  mkdir -p "$PACKAGE_DIR/frontend"
  cp "$FRONTEND_STAGE"/*.dll "$PACKAGE_DIR/frontend/"
  cp "$FRONTEND_STAGE/$MODULE_NAME.Frontend.deps.json" "$PACKAGE_DIR/frontend/" 2>/dev/null || true
fi

# 校验和文件（签名与包仓库为批次 2 待办，见 docs/ROADMAP.md）
( cd "$PACKAGE_DIR/backend" && sha256sum ./* > ../checksums.sha256 )

echo "==> Package ready:"
ls -la "$PACKAGE_DIR" "$PACKAGE_DIR/backend"
