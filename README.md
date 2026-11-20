# hahhub

hahhub turns a transparent keyboard display into a live surface for your keystrokes. Press a key on an IPI 84 and the wallpaper beneath it fractures or spirals inward. You can also cast video to the keyboard display and use the same effects on the moving picture.

[Download for Windows x64](https://raw.githubusercontent.com/dotohua009/keybloom-lively/main/downloads/hahhub/v1.0.0/hahhub-windows-x64.zip) · [Source and build instructions](hahhub/) · [简体中文](#简体中文)

![hahhub showing the Fracture preview and its controls](docs/hahhub-studio.png)

## Effects and video

- **Fracture** starts a crack at the calibrated switch position. Hold to extend it, press a nearby key to influence its path, and release to let it heal in reverse after an adjustable delay.
- **Black Hole** pulls the live image into a rotating, irregular opening. Adjacent keys merge their holes. Hold for about 4.7 seconds to swallow the display, then release to let the picture return.
- **Video Cast** plays a Bilibili video page or a direct MP4, WebM or M4V link on one selected extended display. Playback controls stay in hahhub. The active effect samples the video while third-party wallpaper sampling is paused.

## Get started

1. Extract the Windows ZIP and run `powershell -ExecutionPolicy Bypass -File .\install.ps1` from its folder. Video playback uses Microsoft Edge WebView2 Runtime.
2. Open **hahhub**, select the keyboard display and choose **Enable effects**. The IPI 84 layout follows the calibrated physical keys. You can choose any extended display by name.
3. Minimize or close the panel to keep it in the system tray. Double-click the **H** icon to reopen it, or use **Quit hahhub** to stop the app and its renderer.

Choose **Settings → Language** for English or 简体中文. **Settings → Updates** checks the public update channel and installs a verified package in place. Display selection, effect settings and language are kept across updates.

## 简体中文

hahhub 是为透明屏键盘制作的 Windows 桌面程序。选中键盘屏后，实体按键会在对应位置生成沿画面生长的裂纹，或将周围纹理卷入黑洞。视频上屏支持同一组交互，播放进度条和按钮放在控制面板里。最小化或点 × 后，视频和交互继续在托盘运行。

[中文使用说明](hahhub/README.md) · [KeyBloom「键下生花」旧版壁纸、下载与说明](KEYBLOOM.md)

Developed by **HAHHAN**. Licensed under GPL-3.0. [Third-party notices](hahhub/NOTICE.md)
