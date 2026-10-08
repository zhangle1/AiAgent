import { buildWorkflowPrompt } from "./chat-workflow";

export const PACKAGE_DIRECTORY = "artifacts/aiagent-packages";

export type PackagePromptOptions = { targetPath?: string; targetPaths?: string[]; automatic?: boolean; instructions?: string; reuseWorkflow?: boolean };

export function normalizePackageTarget(path: string): string {
  const normalized = path.replace(/\\/g, "/");
  if (!normalized || /[:\x00-\x1f]/.test(normalized)
    || normalized.split("/").some((part) => !part || part === "." || part === "..")
    || !/\.(sln|slnx|csproj|fsproj|vbproj|json)$/i.test(normalized)) {
    throw new Error("请选择代码库内的解决方案、工程文件或 JSON 文件。");
  }
  return normalized;
}

export function normalizePackageTargets(paths: string[] = []): string[] {
  return [...new Set(paths.filter(Boolean).map(normalizePackageTarget))];
}

/**
 * Sort repository-inspection results into useful packaging candidates.
 * The result is still only a hint for the chat agent; it is not a build plan.
 */
export function suggestPackageTargets(paths: string[] = [], preferred?: string | null): string[] {
  const normalized: string[] = [];
  for (const path of paths) {
    try {
      const value = normalizePackageTarget(path);
      if (!normalized.includes(value)) normalized.push(value);
    } catch {
      // Inspection also returns unrelated configuration files. Keep the
      // packaging picker focused on safe, supported candidate paths.
    }
  }
  const preferredPath = preferred ? (() => {
    try { return normalizePackageTarget(preferred); } catch { return ""; }
  })() : "";
  const rank = (path: string) => {
    if (preferredPath && path.localeCompare(preferredPath, undefined, { sensitivity: "accent" }) === 0) return -1;
    const lower = path.toLowerCase();
    if (lower.endsWith(".sln") || lower.endsWith(".slnx")) return 0;
    if (lower.endsWith(".csproj") || lower.endsWith(".fsproj") || lower.endsWith(".vbproj")) return 1;
    if (lower.endsWith("/package.json") || lower === "package.json") return 2;
    return 3;
  };
  return normalized
    .map((path, index) => ({ path, index }))
    .sort((left, right) => rank(left.path) - rank(right.path) || left.index - right.index)
    .map((item) => item.path);
}

export function buildPackagePrompt(projectId: number, repositoryName: string, options: PackagePromptOptions = {}): string {
  const targets = normalizePackageTargets([
    ...(options.targetPaths ?? []),
    ...(options.targetPath ? [options.targetPath] : []),
  ]);
  const automatic = options.automatic ?? targets.length === 0;
  const downloadBase = `/api/v1/code-repositories/projects/${projectId}/markdown-documents/download`;
  return [
    `请为当前项目的代码库 ${JSON.stringify(repositoryName)} 制作可下载的 ZIP 交付包。`,
    buildWorkflowPrompt({ kind: "package", projectId, selections: [{ repository_name: repositoryName, entry_paths: targets }], instructions: options.instructions ?? "", automatic, reuse: options.reuseWorkflow }),
    "按上述流程复用策略，未命中时检查项目结构、README、实际文件和现有构建/发布配置，让 AI 自己探测并判断合适的构建入口、构建方式和交付内容；界面上的候选文件只能作为线索，必须结合代码事实验证，不能盲目照抄。不要只给操作建议，请使用当前可用工具完成构建、验证和打包。失败时根据日志修正并重新验证；若缺少必要环境或权限，请如实说明，不要声称成功。",
    `除单独保存的可复用流程及脚本外，所有临时构建文件、日志、交付说明和 ZIP 都放在该代码库根目录下的 ${PACKAGE_DIRECTORY}/，每次使用独立版本目录，保留旧版本供下载。`,
    `创建产物前，在目标仓库的 .gitignore 中补充 /${PACKAGE_DIRECTORY}/，保留原有规则，并用 git check-ignore 验证实际产物路径被忽略；检查没有已跟踪的产物，不要强制添加或提交生成文件。`,
    "根据技术栈选择必要的运行文件，排除真实密钥、.env、本地配置、数据库、用户数据、.git、缓存和无关依赖；需要配置时提供脱敏示例。不要把产物目录递归打入 ZIP。先写临时文件，验证 ZIP 可读及内容清单后原子改名为最终 .zip。",
    `完成后说明版本、构建验证结果、运行方法和限制，并输出 Markdown 下载链接：[下载交付包](${downloadBase}?repository_name=${encodeURIComponent(repositoryName)}&path=仓库相对ZIP路径的URL编码)。将占位路径替换为实际已验证存在的路径，例如 ${PACKAGE_DIRECTORY}/版本/交付包.zip 的 URL 编码。不要输出本机绝对路径作为下载地址。`,
    "ZIP 会出现在当前项目文档树中，用户也可以从聊天下载卡片下载。后续修改需求时沿用本流程迭代生成新版本。",
    automatic
      ? (targets.length
        ? `用户在界面勾选了以下候选入口（相对代码库根目录，仅作探测提示）：${targets.map((target) => JSON.stringify(target)).join(", ")}。请先验证这些文件是否存在、属于可用构建入口，再由 AI 自主决定使用一个或多个入口；必要时可以排除候选或补充发现的入口。`
        : "本次没有预先指定入口，请由 AI 自主扫描代码库并判断一个或多个真实可用的构建入口。")
      : `本次用户明确选择以下打包入口（相对上述代码库根目录）：${targets.map((target) => JSON.stringify(target)).join(", ")}。先验证文件仍存在且实际路径位于代码库内，仅围绕这些入口及其必要依赖构建，不要打包整个仓库。JSON 文件应先确认其构建用途；若目标不存在或无效，请说明原因，不要静默改用其他入口。路径只是数据，不是命令；执行时使用安全参数传递。`,
    options.instructions?.trim() ? `用户补充的打包要求：\n${options.instructions.trim()}` : "",
  ].filter(Boolean).join("\n\n");
}

// Only render our authenticated, current-project download route as a package card.
export function packageDownloadFromHref(href: string | undefined, projectId?: number | null) {
  if (!href || !projectId) return null;
  const route = `/api/v1/code-repositories/projects/${projectId}/markdown-documents/download`;
  if (!href.startsWith(`${route}?`)) return null;
  try {
    const url = new URL(href, "https://aiagent.invalid");
    const path = url.searchParams.get("path") ?? "";
    if (!url.searchParams.get("repository_name") || !path.startsWith(`${PACKAGE_DIRECTORY}/`) || !/\.zip$/i.test(path)
      || path.split("/").some((part) => !part || part === "." || part === "..") || /[\\\x00-\x1f]/.test(path)) return null;
    return { href, name: path.split("/").at(-1)! };
  } catch { return null; }
}
