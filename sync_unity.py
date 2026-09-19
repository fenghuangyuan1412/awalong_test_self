# 把 _unity_staging/Assets 同步进 avalon-unity/Assets（单一事实来源：staging 目录，可入库；
# 引擎生成的 Library/Temp/logs 不入库）。用法：python sync_unity.py
import pathlib, shutil, sys

root = pathlib.Path(__file__).parent
src = root / "_unity_staging" / "Assets"
dst = root / "avalon-unity" / "Assets"

if not src.exists():
    sys.exit("staging 目录不存在: " + str(src))
if not (root / "avalon-unity").exists():
    sys.exit("工程尚未创建（先运行 make_unity_project.py 或在编辑器激活后创建）")

n = 0
for f in src.rglob("*"):
    if f.is_file() and not f.name.endswith(".meta"):
        rel = f.relative_to(src)
        target = dst / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(f, target)
        n += 1
print(f"已同步 {n} 个文件 -> {dst}")

# Packages/manifest.json 也在 staging 维护（如 com.unity.ugui 依赖）
psrc = root / "_unity_staging" / "Packages"
pdst = root / "avalon-unity" / "Packages"
if psrc.exists():
    pdst.mkdir(parents=True, exist_ok=True)
    for f in psrc.rglob("*"):
        if f.is_file():
            t = pdst / f.relative_to(psrc)
            t.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(f, t)
            n += 1
    print(f"含 Packages，共 {n} 个文件")
