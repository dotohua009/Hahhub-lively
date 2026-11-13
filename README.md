# KeyBloom · 键下生花

为透明屏幕键盘制作的开源互动壁纸。按下实体键，图案从对应键位下方出现；点按立即触发，长按持续触发，支持多键同时互动。

基于 **[Lively Wallpaper](https://github.com/lively-community/lively)** 运行。Lively 提供桌面承载、多屏管理与自定义设置；本项目提供键位映射、互动动画和后台按键转发组件。本项目为独立社区壁纸项目，与 Lively 官方无隶属关系。

- [Lively 官方下载](https://github.com/lively-community/lively/releases)
- [Lively 源码与 GPL-3.0 许可](https://github.com/lively-community/lively)
- [下载可直接导入的壁纸 ZIP](downloads/KeyBloom-IPI84-v1.1.0.zip)

![暗背景下的按键照明](docs/dark-key-light.png)

## 能做什么

- 八种互动：花纹绽放、水波扩散、粒子喷溅、流光连线、打地鼠、节奏接球、像素生长、可修复玻璃裂纹。
- 六种背景：纯白、柔彩、极光、星空、网格、自选纯色。背景亮度调为 0 即为纯黑。
- 暗背景按键中心有白蓝色亮光，“暗背景键帽照明”可调至 0–100。背景与照明分开控制。
- 点按一次触发一次；长按约 350 毫秒后每 240 毫秒继续触发。玻璃裂纹按住时加深，松开 1.6 秒后缓慢修复。
- IPI 84 的 16u × 6 行实体布局，保留中心缩放与四边出界校准。
- 默认不显示键名、注释、分数或提示。

## 安装

适用于 Windows x64。已在 Lively 2.2.1.0 的 WebView2 网页播放器上验证。

1. 从上面的官方链接安装 Lively。将键盘屏设置为 Windows 扩展显示器。
2. 下载本项目的 `KeyBloom-IPI84-v1.1.0.zip`，直接拖入 Lively 图库。不要先解压。
3. 在 Lively 中选择实际键盘屏，再应用“键下生花 · IPI 84”。显示器编号因电脑而异。
4. 点壁纸的“…” → “打开文件所在位置”，在这个**已导入的壁纸库目录**中运行 `KeyBloomInput.exe`。
5. 将 Lively 输入转发设为“鼠标和键盘”。在其他窗口按 W、A、空格、方向键，检查对应位置。
6. 要随登录自动启用，在上述目录运行 `Enable-KeyboardInteraction.ps1`；关闭则运行 `Disable-KeyboardInteraction.ps1`。Lively 自身也需开启随系统启动。

导入 ZIP 不会自动启动后台组件。组件依赖 WebView2，不适用于 CefSharp 播放器。请只运行当前导入目录中的一份；更换副本前先关闭旧副本。

## 自定义与校准

壁纸“…” → “自定义”中选择效果、背景、帧率、键帽照明和玻璃修复时间。

默认白底；喜欢黑底时将“背景亮度”设为 0。“暗背景键帽照明”默认 85；它只增强按键附近，不抬亮整个背景。像素生长可连续点按或长按至八级。节奏接球使用 A/S/D/F/J/K/L；打地鼠按角色所在键。切换效果或重新加载会清空临时游戏进度。

默认校准适用于 IPI 84。换屏时临时打开校准线，使用中心偏移和水平/垂直键距缩放，将十字对准轴心；上下左右允许出界。校准后关闭辅助线。不同键盘布局需修改 `wallpaper/layout.js`，本项目不会自动识别其他键盘。

## 资源占用

- 静态背景、没有按键动画时停止申请动画帧；保留静态画面。
- 按键或设置变化重新唤醒。动态背景、进行中的游戏和修复中的玻璃继续刷新。
- 默认上限 30 FPS，可选 15–60 FPS。逻辑按实际经过的时间推进。
- 背景亮度为 0 时跳过不可见的星空、渐变等绘制。
- 一倍画布像素密度，最多保留 96 个动画事件；单键照明每帧合并绘制一次。
- 后台组件缓存播放器窗口；连接有效时不反复执行 WMI 查询，诊断心跳每 15 秒更新一次。

这是减少计算和绘制的优化，不代表整机瓦数下降同样比例。液晶屏的背光功耗通常不能靠黑色画面消除；实际透光亮度受屏幕、键帽和环境光影响。[测试方法与结果](docs/PERFORMANCE.md)。

## 从源码构建

网页部分不需要 npm、远程字体或网络服务。直接打开 `wallpaper/index.html` 可预览；网页预览只有在页面获得焦点时接收按键。

Windows 上使用自带的 .NET Framework C# 编译器构建输入组件并打包：

```powershell
pwsh -File scripts/build.ps1
```

输出 `dist/KeyBloom-IPI84-v1.1.0.zip`。也可用 Windows PowerShell 5.1 运行该脚本。构建只使用本地源文件和系统编译器，不下载依赖。

测试游戏逻辑（Node.js）：

```sh
node tests/games.cjs
```

浏览器回归使用 Playwright CLI。在仓库根目录用 Python 启动仅本机可访问的测试服务器，并保持此终端打开：

```sh
python -m http.server 8765 --bind 127.0.0.1
```

在另一个终端运行：

```sh
npx --yes --package @playwright/cli playwright-cli -s=keybloom open http://127.0.0.1:8765/wallpaper/index.html
npx --yes --package @playwright/cli playwright-cli -s=keybloom run-code --filename tests/interaction.cjs
npx --yes --package @playwright/cli playwright-cli -s=keybloom run-code --filename tests/power-light.cjs
```

浏览器测试从当前打开的壁纸页面执行。完成后停止测试服务器即可，壁纸本身不依赖此服务器。

## 输入与隐私

后台组件通过 Windows Raw Input 接收即时按下/松开事件，并转发给**当前目录对应的 Lively 网页播放器**。不转换文字、不保存按键历史、不访问网络。状态文件只有连接状态、进程号和累计转发次数。切换到其他壁纸后解除输入注册。

原应用仍会收到正常按键，所以游戏模式下的打字也会进入当前应用。Fn 是否上报由键盘固件决定。组件与 Lively 版本的窗口结构有关，升级播放器后可能需要适配。

## 许可与贡献

本项目以 [GPL-3.0](LICENSE) 开源。Lively 的名称、源码和许可证属于其原作者，链接与说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。仓库发布包仅包含本项目；Lively 从官方渠道单独下载。

欢迎提交其他实体布局、效果和性能改进。请保留点按/长按回归、真实屏幕校准、后台输入隐私边界，并说明是否经过实体设备验证。
