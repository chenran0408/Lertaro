# 任务栏美化

在 **设置 → 外观 → 任务栏 → 调整任务栏** 中启用。Lertaro 随包提供任务栏组件，无需另行安装 TranslucentTB。默认关闭，当前内置组件面向 Windows 11。

可选择透明、纯色、亚克力等效果，使用颜色选择器和透明度滑块调节。点击“预览任务栏”后，后续调节会自动更新；撤销预览或关闭未保存的设置会恢复保存前的外观。点击应用或确定保存。

首次启用会检查所需的微软运行组件，缺少时为当前用户准备；已有较新版本会保留。准备期间可以继续使用搜索。停用后恢复系统任务栏；内置组件由独立后台程序管理，主程序退出后会请求恢复外观并退出。

不同场景可单独设置：窗口最大化、开始菜单、系统搜索、任务视图等。部分效果受 Windows 版本影响。已有其他 TranslucentTB 正在运行时，会提示先退出它；Lertaro 不会强制结束外部程序。

高级用法：填写“外部程序”可使用自己的便携版，留空使用内置组件。即时预览仅支持内置组件；外部程序通过保存设置应用。更换外部程序前请先关闭其现有实例。

<details>
<summary>组件、配置及开源信息</summary>

内置的 TranslucentTB 2026.2 引擎保持独立、未修改。GPLv3 许可证、对应源码归档及说明随安装版和便携版一并提供，位于 Components/TranslucentTB。下载版本及校验值固定在 ThirdParty/TranslucentTB/assets.json，由 Tools/PrepareTaskbarBundle.ps1 准备。

每位用户的可写引擎副本和配置放在 Lertaro 用户数据目录的 Taskbar/2026.2-<架构> 中，不向 Program Files 写用户配置。首次修改已有配置前会备份；配置无法读取时不会覆盖它。升级和卸载等待自带后台程序退出，超时会停止替换文件。

微软运行组件遵循其各自的适用条款。Windows 10 可以选择兼容的外部程序；ARM64 包含对应的上游组件，实机验收需在相应设备完成。

参考：[TranslucentTB](https://github.com/TranslucentTB/TranslucentTB)、[2026.2 发行](https://github.com/TranslucentTB/TranslucentTB/releases/tag/2026.2)、[配置结构](https://github.com/TranslucentTB/TranslucentTB/blob/release/settings.schema.json)。

</details>
