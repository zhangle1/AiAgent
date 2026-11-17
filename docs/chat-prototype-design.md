# 聊天原型设计

输入区「+ → 设计模式」在普通聊天提交中显式添加完整 HTML、内联 CSS 与 data-psd-name 区域提示；模式标签可退出，切换项目／会话清除。目标请求不应用此提示；重试使用已有请求或保存后的用户消息，不重复包装。

顶部「项目程序运行」旁的「+」新增「原型设计」，右侧空白工作区及页签新增菜单也可打开。完成的聊天 HTML 代码块提供直接预览按钮。面板列出当前会话完整 HTML 和当前项目的已保存原型，不将生成中、失败、停止、残缺代码或普通说明识别成产出。

HTML 文件卡片先经过现有项目引用解析与归属检查，再读取受控原文件；切换项目／会话卸载面板并取消读取。前端文件大小限制为 2 MB。外部链接不进入原型文件读取接口。

面板支持桌面／平板／移动预览、HTML 源码编辑、注解、HTML 下载、原型与注解保存。保存复用 PromptTemplate API，stage=design、tag=prototype-html，正文沿用 prototype-html:v1 编码，注解保存在 description；新产物默认 personal。编辑现有模板保留其变量、标签、可见性，写权限由既有服务端接口验证。原始项目文件不会被面板覆盖；保存的是原型工作台副本。切换下拉中的产出前提醒保存。

「带入聊天调整原型」仅填充草稿，包含当前 HTML 和注解，用户发送后得到新一轮产出，原版本保留在会话。PSD 三个入口沿用 PsdFiles，以当前编辑 HTML 生成桌面、平板、移动端分组像素图层；调整预览尺寸可查看对应 HTML 排版。PSD 不写入服务器，也不提供任意已有 PSD 的上传解析和原生图层编辑；已有 PSD 的后续修改通过对应 HTML 来源重新导出。

预览 iframe 使用空 sandbox，不执行脚本、不获得同源 DOM 权限；PSD 导出继续使用禁止脚本的同源静态 iframe。生成 HTML 不写 public，不执行模型命令。

参考调研：

- [GrapesJS Components](https://github.com/GrapesJS/grapesjs/blob/dev/docs/modules/Components.md)：以组件模型作为导出源。当前保留既有 HTML，未引入模型转换和依赖。
- [ag-psd](https://github.com/Agamnentzar/ag-psd)：沿用项目已安装的 PSD 写出能力与既有像素图层边界。

验证使用合成会话、项目和 HTTP 模板替身，不连接真实用户数据。`node --test tests/chat-prototype.test.mjs` 校验多产出、残缺、超限、CRLF 和说明边界；`node tests/chat-prototype.browser.mjs` 校验项目过滤、脚本隔离、源码修改、注解持久化、调整草稿，以及真实生成的移动端 PSD 签名和宽度。浏览器用例复用现有 CHAT_LIBRARY_TEST_TOOLS 工具目录。另运行 `npx tsc --noEmit` 与 `git diff --check`。
