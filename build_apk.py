# 一键构建安卓 APK：同步 assets → Gradle 构建 → 产物复制到 release/
# 用法：python build_apk.py            （首次构建会下载 Gradle 依赖，需联网）
import os
import pathlib
import shutil
import subprocess
import sys

root = pathlib.Path(__file__).parent
preview = root / "avalon-preview"
proj = root / "avalon-android"
assets = proj / "app" / "src" / "main" / "assets"
gradle_bin = root / "_tools" / "gradle-8.9" / "bin"
sdk = root / "android-sdk"

def step(msg):
    print(f"\n==> {msg}")

# 1. 同步游戏文件到 assets（单一事实来源：avalon-preview/）
step("同步游戏文件到 assets")
assets.mkdir(parents=True, exist_ok=True)
for f in ("index.html", "core.js"):
    shutil.copy2(preview / f, assets / f)
    print("  copied", f)

# 2. local.properties 指定 SDK 路径
(lp := proj / "local.properties").write_text(
    f"sdk.dir={sdk.as_posix()}\n", encoding="utf-8")

# 3. Gradle 构建
env = os.environ.copy()
env["JAVA_HOME"] = env.get("JAVA_HOME", r"D:\programme\java")
env["ANDROID_HOME"] = str(sdk)
env["PATH"] = str(gradle_bin) + os.pathsep + env["PATH"]

step("Gradle assembleDebug")
gradle_bat = gradle_bin / "gradle.bat"
r = subprocess.run(
    [str(gradle_bat), "-p", str(proj), ":app:assembleDebug", "--no-daemon"],
    env=env, shell=False)
if r.returncode != 0:
    sys.exit(r.returncode)

# 4. 复制产物
step("复制 APK 到 release/")
out_dir = root / "release"
out_dir.mkdir(exist_ok=True)
apk_src = proj / "app" / "build" / "outputs" / "apk" / "debug" / "app-debug.apk"
apk_dst = out_dir / "avalon-v0.1.0-debug.apk"
shutil.copy2(apk_src, apk_dst)
print("APK:", apk_dst, f"({apk_dst.stat().st_size / 1024:.0f} KB)")
