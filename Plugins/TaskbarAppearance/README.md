# 任务栏美化（TranslucentTB 联动）

在 **设置 → 插件 → 任务栏美化** 中启用。商店版自动识别；便携版选择 `TranslucentTB.exe`。默认不接管任务栏。

选择透明、纯色、亚克力等效果，填写颜色和不透明度，再保存设置。TranslucentTB 会更新实际任务栏；不同系统支持的效果可能不同。可为窗口最大化、开始菜单、系统搜索、任务视图等场景分别设置样式。Windows 10 的桌面预览按钮及 Windows 11 的任务栏边线也可设置。

“让 TranslucentTB 使用简体中文”复用官方翻译。关闭本插件内的美化开关并保存，可恢复系统外观；外部 TranslucentTB 进程仍可运行。仅从插件列表禁用 DLL 不会终止外部软件，请先关闭美化开关。当前使用通用插件设置页，颜色选择器和即时预览界面随整体外观方案后续完善。

首次写入前，原配置备份为旁边的 `settings.json.lertaro-backup`。无法解析原配置或参数无效时，不会覆盖它。已有不同版本运行时，先退出再切换，不会自动结束用户的进程。

本插件通过公开配置文件与独立安装的 TranslucentTB 联动，不包含或复制该项目的任务栏引擎和二进制文件。TranslucentTB 自身的许可证、系统依赖和更新仍由原项目管理。

参考：[项目与下载](https://github.com/TranslucentTB/TranslucentTB)、[配置结构](https://github.com/TranslucentTB/TranslucentTB/blob/release/settings.schema.json)、[中文资源](https://github.com/TranslucentTB/TranslucentTB/tree/release/TranslucentTB/resources/language)。
