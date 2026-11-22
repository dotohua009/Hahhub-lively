"""Run on Windows with the hahhub panel open at normal size."""
import ctypes
from ctypes import wintypes
from time import sleep

user32 = ctypes.windll.user32
user32.SetThreadDpiAwarenessContext(ctypes.c_void_p(-4))
window = user32.FindWindowW(None, "hahhub")
assert window and user32.IsWindowVisible(window) and not user32.IsZoomed(window)


class MonitorInfo(ctypes.Structure):
    _fields_ = [("size", wintypes.DWORD), ("screen", wintypes.RECT),
                ("work", wintypes.RECT), ("flags", wintypes.DWORD)]


def bounds():
    rect = wintypes.RECT()
    assert user32.GetWindowRect(window, ctypes.byref(rect))
    return rect.left, rect.top, rect.right, rect.bottom


def point(x, y):
    return ((y & 65535) << 16) | (x & 65535)


original = bounds()
left, top, right, bottom = original
dpi = user32.GetDpiForWindow(window)
# The maximize button is the middle of three 44-DIP buttons, inset 6 DIP.
button = point(right - round(72 * dpi / 96), top + round(20 * dpi / 96))
title = point((left + right) // 2, top + round(16 * dpi / 96))
assert user32.SendMessageW(window, 0x84, 0, button) == 9
assert user32.SendMessageW(window, 0x84, 0, title) == 2
monitor = MonitorInfo(ctypes.sizeof(MonitorInfo))
assert user32.GetMonitorInfoW(user32.MonitorFromWindow(window, 2), ctypes.byref(monitor))
work = monitor.work
try:
    user32.SendMessageW(window, 0xa1, 9, button)
    user32.SendMessageW(window, 0xa2, 9, button)
    sleep(0.3)
    assert user32.IsZoomed(window)
    assert bounds() == (work.left, work.top, work.right, work.bottom)
finally:
    if user32.IsZoomed(window):
        user32.SendMessageW(window, 0xa3, 2, point((work.left + work.right) // 2, work.top + 20))
        sleep(0.3)
assert bounds() == original
print("Verified caption hit tests, maximize to working area and double-click restore.")
