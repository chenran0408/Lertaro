# TranslucentTB component

This directory contains the unmodified TranslucentTB 2026.2 portable engine, distributed under GNU GPL version 3. Copyright remains with TranslucentTB's contributors. Lertaro's settings adapter and process supervisor are separate programs. The upstream engine has not been relabeled as Lertaro's own code.

- Project: https://github.com/TranslucentTB/TranslucentTB
- Exact release: https://github.com/TranslucentTB/TranslucentTB/releases/tag/2026.2
- License: LICENSE.md in this directory.
- Corresponding upstream source: TranslucentTB-2026.2-source.zip in this directory. The source includes the Visual Studio solution, package declarations and build configuration. Follow its README and CONTRIBUTING instructions to build; Lertaro does not modify the native engine.
- The pinned source and release download checksums are recorded in assets.json. Lertaro's reproducible download/package procedure is Tools/PrepareTaskbarBundle.ps1 in the Lertaro source.

Microsoft runtime framework packages are supplied separately under their own applicable Microsoft terms, not under TranslucentTB's GPL. Windows verifies their package signatures during per-user installation. Existing newer framework versions are retained. Installation occurs only when the user enables taskbar appearance and a required framework is missing.

- WinUI project and licenses: https://github.com/microsoft/microsoft-ui-xaml
- Visual C++ framework distribution: https://learn.microsoft.com/en-us/troubleshoot/developer/visualstudio/cpp/libraries/c-runtime-packages-desktop-bridge

简体中文：Lertaro 随包提供未修改的 TranslucentTB 2026.2，无需另行安装 TranslucentTB。首次启用时会检查所需的微软运行组件；缺少时为当前用户安装。上游许可证与对应源码随包提供。组件仅在启用任务栏美化时运行；Windows 11 为本版内置组件的目标系统。
