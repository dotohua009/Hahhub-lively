"""Run with the installed panel open and a video playing. Leaves the panel open."""
import ctypes
import os
import subprocess
import time
from ctypes import wintypes
from pathlib import Path

user32 = ctypes.windll.user32
user32.FindWindowW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR]
user32.FindWindowW.restype = wintypes.HWND
panel = user32.FindWindowW(None, "hahhub")
player = user32.FindWindowW(None, "hahhub Video Cast")
exe = Path(os.environ["LOCALAPPDATA"]) / "HAHHAN/Interaction Studio/app/HAHHAN Studio.exe"


def wait_for(check):
    for _ in range(60):
        if check():
            return
        time.sleep(0.05)
    raise AssertionError("Window state did not change")


for hwnd in (panel, player):
    assert hwnd and user32.IsWindowVisible(hwnd)
    style = user32.GetWindowLongW(hwnd, -20)
    assert style & 0x80 and not style & 0x40000, "Unexpected Alt+Tab window style"

for message, command in ((0x0112, 0xf020), (0x0010, 0)):  # Minimize, then close.
    user32.PostMessageW(panel, message, command, 0)
    try:
        wait_for(lambda: not user32.IsWindowVisible(panel))
        assert user32.FindWindowW(None, "hahhub Video Cast") == player and user32.IsWindowVisible(player)
    finally:
        subprocess.run([str(exe)], check=True, timeout=4)
    wait_for(lambda: user32.IsWindowVisible(panel) and not user32.IsIconic(panel))
    assert user32.FindWindowW(None, "hahhub") == panel
print("Minimize and close keep the video running. Relaunch restores the original panel.")
