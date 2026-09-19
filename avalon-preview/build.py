# 构建单文件分发版：把 core.js / art.js 内联进 index.html，生成 avalon-standalone.html
# 用法：python build.py
import pathlib

root = pathlib.Path(__file__).parent
html = (root / "index.html").read_text(encoding="utf-8")

for name in ("core.js", "art.js"):
    js = (root / name).read_text(encoding="utf-8")
    tag = f'<script src="{name}"></script>'
    assert tag in html, f"index.html 中找不到 {name} 引用"
    html = html.replace(tag, "<script>\n" + js + "\n</script>")

dst = root / "avalon-standalone.html"
dst.write_text(html, encoding="utf-8")
print("已生成:", dst, f"({dst.stat().st_size / 1024:.1f} KB)")
