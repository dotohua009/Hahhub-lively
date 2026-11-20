"""Opt-in smoke test for the Bilibili projection stylesheet against the real embed."""
import re
from pathlib import Path
from playwright.sync_api import sync_playwright

source = (Path(__file__).resolve().parents[1] / 'src/VideoWindow.cs').read_text(encoding='utf-8-sig')
literal = source.split('const string ProjectionCss=', 1)[1].split('";', 1)[0] + '"'
css = ''.join(re.findall(r'"([^"]*)"', literal))
with sync_playwright() as p:
    browser = p.chromium.launch(channel='msedge', args=['--autoplay-policy=no-user-gesture-required'])
    page = browser.new_page(viewport={'width': 1920, 'height': 720})
    url = 'https://player.bilibili.com/player.html?bvid=BV1RqHY6kEHA&autoplay=1&muted=1&danmaku=0'
    page.goto(url, wait_until='domcontentloaded')
    page.wait_for_function("document.querySelector('video')?.currentTime > 0", timeout=35000)
    page.add_style_tag(content=css)
    bars = page.locator('.bpx-player-control-wrap, .bpx-player-top-wrap, .bpx-player-state-wrap')
    assert bars.count() >= 2
    for paused in (False, True):
        if paused:
            page.locator('video').evaluate('v => v.pause()')
        page.mouse.move(880, 340)
        page.mouse.move(1000, 705)
        page.wait_for_timeout(300)
        assert all(not element.is_visible() for element in bars.all())
        assert page.locator('video').evaluate("v => !v.controls && getComputedStyle(v).objectFit === 'cover'")
    browser.close()
print('Playing, paused and pointer hover all keep projection controls hidden.')
