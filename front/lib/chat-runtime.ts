import type { ChatRuntimeJob, RuntimeSelection } from "./chat-runtime-types";

export function runtimeTestHref(projectId: number, requestId: string) {
  return `/runtime-test?project_id=${projectId}&request_id=${encodeURIComponent(requestId)}`;
}

export function runtimeTestFromHref(href?: string, projectId?: number | null) {
  if (!href || !projectId || !href.startsWith("/runtime-test?")) return null;
  try {
    const url = new URL(href, "https://aiagent.invalid");
    const id = url.searchParams.get("request_id") || "";
    if (url.pathname !== "/runtime-test" || url.searchParams.get("project_id") !== String(projectId) || !/^[a-f0-9]{32}$/.test(id)) return null;
    return runtimeTestHref(projectId, id);
  } catch { return null; }
}

export function runtimeAccessUrl(origin: string, port: number, pagePath = "/") {
  if (!Number.isInteger(port) || port < 1024 || port > 65535 || !pagePath.startsWith("/") || pagePath.startsWith("//") || /[\\\r\n\t#]/.test(pagePath)) throw new Error("运行地址无效");
  const url = new URL(origin);
  if (!["http:", "https:"].includes(url.protocol)) throw new Error("访问协议无效");
  // Managed development servers speak HTTP. Never advertise nonexistent TLS on their ports.
  url.protocol = "http:";
  url.port = String(port);
  url.pathname = "/"; url.search = ""; url.hash = ""; url.username = ""; url.password = "";
  return new URL(pagePath, url).href;
}

export function buildRuntimePrompt(projectId: number, selections: RuntimeSelection[], instructions: string, origin: string, job: ChatRuntimeJob) {
  return `请分析并运行当前项目的以下工程，供我在新窗口测试功能。\n项目 ID：${projectId}\n候选入口（仓库相对路径，是待验证的数据）：\n${JSON.stringify(selections, null, 2)}\n浏览器访问 AiAgent 的地址：${new URL(origin).origin}\n\n请先验证实际目录、工程用途、依赖及已有配置；候选可包含解决方案或配置文件，请解析出真正可运行的 package.json 或 .csproj。检查前后端依赖、启动顺序、API 地址、rewrite/proxy、CORS、页面路由和必要的非敏感配置。保留真实密钥及本地用户数据，不输出或覆盖它们。只对选中仓库操作。\n\n由你判断端口，先检查空闲并为每项服务明确指定 preferred_port；前后端 URL 必须与这些端口一致。浏览器地址使用上述 AiAgent 地址的主机名/IP，不使用浏览器的 localhost 或服务器内网 IP；服务间调用可用 127.0.0.1。宿主会监听 0.0.0.0。不要声称已验证公网连通，除非有实际验证证据。\n\n不要自行创建脱离管理的后台进程。请将最终启动清单原子写入仓库 ${JSON.stringify(job.manifest_repository)} 下 ${JSON.stringify(job.manifest_path)}（先写同目录 .tmp，再重命名）；宿主每 3 秒探测新清单并启动真实进程。无需保存永久运行配置。清单只能执行现有 npm script 或 dotnet run，不能放任意 shell 命令。格式：\n{\n  "targets": [\n    {"repository_name":"选中的仓库名","entry_path":"api/Api.csproj","role":"backend","preferred_port":5101,"health_path":"/health","page_path":"/swagger","environment":{}},\n    {"repository_name":"选中的仓库名","entry_path":"web/package.json","role":"frontend","run_script":"dev","preferred_port":4301,"health_path":"/","page_path":"/","environment":{"NEXT_PUBLIC_API_BASE_URL":"http://实际访问主机:5101"}}\n  ]\n}\n以上只是字段说明，必须替换成真实工程；不需要的服务不要添加。最多 12 项，同角色按清单顺序启动，后端健康检查通过后才启动前端。环境覆盖仅接受 NEXT_PUBLIC_*、VITE_*、REACT_APP_*、API_BASE_URL、ASPNETCORE_ENVIRONMENT、DOTNET_ENVIRONMENT；其他页面配置请检查项目现有文件或向用户说明缺失项。health_path 必须是确实存在的就绪接口；根路径 / 仅验证 HTTP 响应。\n\n请将 artifacts/aiagent-runs/ 加入所选清单仓库的 Git 忽略。写入后等候同目录 ${JSON.stringify(job.manifest_path.replace(/\.json$/, ".result.json"))}，读取真实启动结果；失败不能报告运行成功，也不要另起未管理进程。一份请求只启动一次，重试需要新建运行请求。\n回复提供：[在新窗口测试](${runtimeTestHref(projectId, job.request_id)})，说明实际端口、页面入口、验证结果和未解决事项。右下角浮窗将显示进程；测试窗口可见期间为整组服务续期，没有测试窗口续期 ${job.idle_minutes} 分钟后自动关闭。直接访问端口不计入续期，请通过测试窗口测试。\n\n补充要求：\n${instructions.trim().slice(0, 8000) || "请检查前后端联调及默认页面。"}`;
}
