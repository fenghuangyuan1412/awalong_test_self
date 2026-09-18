# 构建单文件分发版：把 core.js 内联进 index.html，生成 avalon-standalone.html
# 用法：python build.py
import pathlib

root = pathlib.Path(__file__).parent
html = (root / "index.html").read_text(encoding="utf-8")
core = (root / "core.js").read_text(encoding="utf-8")

tag = '<script src="core.js"></script>'
assert tag in html, "index.html 中找不到 core.js 引用"
out = html.replace(tag, "<script>\n" + core + "\n</script>")

dst = root / "avalon-standalone.html"
dst.write_text(out, encoding="utf-8")
print("已生成:", dst, f"({dst.stat().st_size / 1024:.1f} KB)")
