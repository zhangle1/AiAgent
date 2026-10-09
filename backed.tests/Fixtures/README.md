# 聊天 Excel 测试样本

所有样本均为合成数据，不含用户资料。

- `chat-legacy.xls`：xlwt 生成的 BIFF8 工作簿，含“库存”“说明”工作表，以及中文、数字 12 和空 B 列；同一二进制样本用于 XLS/XLT 测试。
- `chat-modern.xlsx/.xlsm/.xlsb`：使用 SheetJS 0.18.5 的 `utils.aoa_to_sheet` 和对应 `bookType` 生成，第一表为 `[["物料", null, "数量"], ["测试零件", null, 12]]`，第二表为 `[["中文正常"]]`，启用 `bookSST`。
- `chat-modern.xltx/.xltm`：将合成 XLSX 包中 `[Content_Types].xml` 的工作簿主类型分别改为 `application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml` 和 `application/vnd.ms-excel.template.macroEnabled.main+xml`。宏格式样本不包含 VBA。
- `chat-biff2.xls`：SheetJS 0.18.5 的 `biff2` 输出，内容为 `[["Item", null, "Count"], ["Part", null, 12]]`，用于验证更早期无 OLE 容器的 XLS。

依赖仅用于生成静态样本，测试和应用运行不依赖 SheetJS/xlwt。回归测试覆盖上传、提取、会话保存、重启重读、权限、取消、空列、非法文件清理与长度限制；文件大小和 ZIP 解压限制沿用服务端校验。
