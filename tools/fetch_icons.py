"""
从各软件官网抓取官方图标，生成 ui/icons/<id>.png（128×128）和 ui/icons/index.json。

由 GitHub Actions（.github/workflows/icons.yml）运行：目录变化时自动更新图标并提交。
本地运行：pip install pillow cairosvg && python tools/fetch_icons.py

图标来源优先级：catalog.json 中的 iconUrl > 网页里声明的 SVG 图标 > 最大的位图图标
（apple-touch-icon / icon 链接 / favicon.ico）。图标仅用于在启动器中标识对应软件。
"""
import io
import json
import re
import sys
import urllib.parse
import urllib.request
from html.parser import HTMLParser
from pathlib import Path

from PIL import Image

try:
    import cairosvg
except ImportError:  # 没有 cairosvg 时跳过 SVG
    cairosvg = None

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "ui" / "icons"
SIZE = 128
UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
      "(KHTML, like Gecko) Chrome/128.0 Safari/537.36")


def fetch(url, limit=5_000_000):
    req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "*/*"})
    with urllib.request.urlopen(req, timeout=20) as res:
        return res.geturl(), res.headers.get("Content-Type", ""), res.read(limit)


class LinkParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.icons = []

    def handle_starttag(self, tag, attrs):
        if tag != "link":
            return
        a = {k.lower(): (v or "") for k, v in attrs}
        rel = a.get("rel", "").lower()
        if "icon" in rel and a.get("href"):
            self.icons.append((rel, a["href"], a.get("sizes", ""), a.get("type", "")))


KNOWN_ICONS = {
    "qianwen": "https://img.alicdn.com/imgextra/i2/O1CN01taBbMS1CfyJoOt0lB_!!6000000000109-2-tps-80-80.png",
    "trae": "https://lf-cdn.trae.com.cn/obj/trae-com-cn/trae_website_prod_cn/favicon.png",
    "perplexity": "https://www.perplexity.ai/apple-touch-icon.png",
    "chatgpt": "https://cdn.oaistatic.com/_next/static/media/apple-touch-icon.82af6fe1.png",
}


def candidates(app):
    seen, out = set(), []

    def add(url, hint):
        if url and url not in seen:
            seen.add(url)
            out.append((url, hint))

    if app.get("id") in KNOWN_ICONS:
        add(KNOWN_ICONS[app["id"]], "known")
    for u in ([app["iconUrl"]] if app.get("iconUrl") else []):
        add(u, "catalog")
    for page in filter(None, [app.get("iconPage"), app.get("homepage"), app.get("webUrl")]):
        try:
            final, ctype, body = fetch(page)
        except Exception as e:  # noqa: BLE001
            print(f"  ! {page}: {e}")
            continue
        if "html" in ctype:
            p = LinkParser()
            try:
                p.feed(body.decode("utf-8", "ignore"))
            except Exception:  # noqa: BLE001
                pass
            for rel, href, sizes, typ in p.icons:
                add(urllib.parse.urljoin(final, href), f"{rel} {sizes} {typ}".strip())
        origin = "{0.scheme}://{0.netloc}".format(urllib.parse.urlparse(final))
        add(origin + "/apple-touch-icon.png", "apple-touch-icon")
        add(origin + "/favicon.svg", "favicon.svg")
        add(origin + "/favicon.ico", "favicon.ico")
    return out


def load(url):
    """返回 (RGBA 图像, 是否矢量)。"""
    _, ctype, data = fetch(url)
    if url.lower().split("?")[0].endswith(".svg") or "svg" in ctype or data.lstrip()[:5] in (b"<svg ", b"<?xml"):
        if cairosvg is None:
            raise ValueError("缺少 cairosvg")
        data = cairosvg.svg2png(bytestring=data, output_width=SIZE * 2, output_height=SIZE * 2)
        return Image.open(io.BytesIO(data)).convert("RGBA"), True
    img = Image.open(io.BytesIO(data))
    if img.format == "ICO":
        sizes = sorted(img.info.get("sizes", [img.size]), key=lambda s: s[0] * s[1])
        img.size = sizes[-1]
    return img.convert("RGBA"), False


def is_full_bleed(img):
    """四角不透明 = 图标自带底色的方形图（如 apple-touch-icon），界面上直接铺满显示。"""
    w, h = img.size
    pts = [(1, 1), (w - 2, 1), (1, h - 2), (w - 2, h - 2)]
    return all(img.getpixel(p)[3] > 240 for p in pts)


def trim(img):
    """去掉透明留白，让不同来源的图标视觉大小一致。"""
    bbox = img.getchannel("A").getbbox()
    return img.crop(bbox) if bbox else img


def normalize(img, full):
    if not full:
        img = trim(img)
    w, h = img.size
    side = max(w, h)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(img, ((side - w) // 2, (side - h) // 2))
    return canvas.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    catalog = json.loads((ROOT / "ui" / "catalog.json").read_text(encoding="utf-8"))
    OUT.mkdir(parents=True, exist_ok=True)
    index, missing = {}, []
    for app in catalog["apps"]:
        print(f"[{app['id']}] {app['name']}")
        best = None
        for url, hint in candidates(app):
            try:
                img, vector = load(url)
            except Exception as e:  # noqa: BLE001
                continue
            w, h = img.size
            if min(w, h) < 32 or abs(w - h) > max(w, h) * 0.2:
                continue
            score = (2 if hint == "catalog" else 0, 1 if vector else 0, min(w, h))
            print(f"  - {w}x{h} {'svg' if vector else ''} {url}")
            if best is None or score > best[0]:
                best = (score, img, url)
        if best is None:
            print("  × 没有找到可用图标")
            missing.append(app["id"])
            continue
        img = best[1]
        full = is_full_bleed(img)
        normalize(img, full).save(OUT / f"{app['id']}.png", optimize=True)
        index[app["id"]] = {"full": full, "source": best[2]}
        print(f"  ✓ {best[2]}（{'满版' if full else '透明底'}）")
    (OUT / "index.json").write_text(json.dumps(index, ensure_ascii=False, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    print(f"\n完成：{len(index)}/{len(catalog['apps'])}，未找到：{', '.join(missing) or '无'}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
