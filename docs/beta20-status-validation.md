# Beta20：恢复搜索状态反馈

ViewModel 已提供加载、建立索引和就绪状态，但 StatusBarControl 内层固定为零高度、Collapsed，文字还固定绑定 Ready。本版移除内层限制，绑定现有 StatusText，保留外层原有可见性控制和普通界面样式。

恢复显示后，320 DIP 窄窗口中的长状态文字实测超出窗口（右边界约 670 DIP）。改用 Auto/* 列和字符省略后，右边界约 308 DIP，保持在窗口内。

真实 WPF 回归构建 QuickSearchWindow，填入六条结果，检查 loading/indexing/ready 文本更新、可见／折叠／再次可见，以及状态栏非零高度和窗口底部边界；另检查窄窗口长文本。没有为测试启动重索引，也没有修改用户索引或设置。
