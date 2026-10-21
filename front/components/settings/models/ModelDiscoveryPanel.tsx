"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { RefreshCw } from "lucide-react";
import { discoverModels, type DiscoveredModel } from "@/lib/model-discovery-api";
import type { CatalogProfile } from "@/lib/settings-types";
import { modelProfileSignature } from "@/lib/model-discovery-state";
import { useSettings } from "../SettingsProvider";

export function ModelDiscoveryPanel({ profile, service }: { profile: CatalogProfile; service: "llm" | "embedding" }) {
  const { importDiscoveredModels } = useSettings();
  const [models, setModels] = useState<DiscoveredModel[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const sequence = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const lastResult = useRef<{ connection: string; models: DiscoveredModel[]; message: string; error: string } | null>(null);
  const profileRef = useRef(profile);
  profileRef.current = profile;
  const signature = modelProfileSignature(profile);
  const run = useCallback(async () => {
    controller.current?.abort();
    const abort = new AbortController();
    controller.current = abort;
    const request = ++sequence.current;
    const target = profileRef.current;
    setBusy(true); setError(""); setMessage("");
    try {
      let detected: DiscoveredModel[] = [];
      let discoveryError = "";
      try {
        const result = await discoverModels(target, service, abort.signal);
        detected = result.models;
      } catch (failure) {
        if (abort.signal.aborted) return;
        discoveryError = failure instanceof Error ? failure.message : "模型检测失败";
      }
      if (request !== sequence.current) return;
      setModels(detected);
      // The connection must be persisted even when the provider cannot list models.
      const saved = await importDiscoveredModels(service, target.id, detected.map((item, index) => ({
        id: `discovered-${Date.now()}-${index}`, name: item.name, model: item.model,
        ...(item.context_window ? { context_window: String(item.context_window), context_window_source: item.context_window_source ?? undefined, context_window_detected_at: new Date().toISOString() } : {}),
        ...(item.max_output_tokens ? { max_output_tokens: String(item.max_output_tokens), max_output_tokens_source: item.max_output_tokens_source ?? undefined } : {}),
        ...(item.supports_images !== null && item.supports_images !== undefined ? { supports_images: item.supports_images } : {}),
        ...(item.supports_tools !== null && item.supports_tools !== undefined ? { supports_tools: item.supports_tools } : {}),
      })), modelProfileSignature(target));
      if (saved && request === sequence.current) {
        const message = discoveryError ? "连接配置已保存。模型检测失败，未更新模型列表；请检查服务地址、Key 和模型列表权限。"
          : detected.length ? `已检测并保存 ${detected.length} 个模型。容量优先使用服务返回值，其次读取服务端 model.json；未知值留空。`
          : "连接配置已保存。服务返回了空模型列表，已有模型保留，可继续手动添加模型。";
        lastResult.current = { connection: modelProfileSignature({ ...target, api_key: "" }), models: detected, message, error: discoveryError };
        setMessage(message);
        setError(discoveryError);
      }
    } catch (failure) {
      if (request === sequence.current && !abort.signal.aborted) setError(`连接配置保存失败，输入仍保留在当前页面：${failure instanceof Error ? failure.message : "请重试保存"}`);
    } finally { if (request === sequence.current) setBusy(false); }
  }, [importDiscoveredModels, service]);

  useEffect(() => {
    controller.current?.abort(); sequence.current++;
    setError(""); setBusy(false);
    // Saving masks the key; retain the detection outcome, including upstream errors.
    if (profileRef.current.api_key === "********" && lastResult.current?.connection === modelProfileSignature({ ...profileRef.current, api_key: "" })) {
      setModels(lastResult.current.models);
      setMessage(lastResult.current.message);
      setError(lastResult.current.error);
      return;
    }
    setModels([]); setMessage("");
    // Saved, masked keys do not trigger writes merely by opening Settings.
    if (!profileRef.current.api_key || profileRef.current.api_key === "********" || !profileRef.current.base_url) return;
    const timer = setTimeout(() => void run(), 900);
    return () => { clearTimeout(timer); controller.current?.abort(); sequence.current++; };
  }, [signature, run]);

  return <div className="mt-4 rounded-lg border border-[var(--border)] p-3 text-[12px]">
    <div className="flex items-center justify-between gap-3">
      <span className="font-medium">自动检测模型与上下文容量</span>
      <button type="button" onClick={() => void run()} disabled={busy || !profile.base_url}
        className="inline-flex items-center gap-1.5 rounded-md border border-[var(--border)] px-2.5 py-1.5 disabled:opacity-50">
        <RefreshCw size={13} className={busy ? "animate-spin" : ""} />{busy ? "检测并保存中…" : "检测并保存"}
      </button>
    </div>
    <p className="mt-2 text-[var(--muted-foreground)]">输入密钥后自动检测并保存连接配置。检测失败或列表为空时也会保存，已有模型和手动容量保留；不发送聊天请求探测上限。</p>
    {error && <p role="alert" className="mt-2 text-red-600">{error}</p>}
    {message && <p role="status" className="mt-2 text-[var(--muted-foreground)]">{message}</p>}
    {models.length > 0 && <div className="mt-2 max-h-48 overflow-auto divide-y divide-[var(--border)]">
      {models.map(item => <div key={item.model} className="flex justify-between gap-3 py-2">
        <span className="min-w-0 truncate" title={item.model}>{item.name}</span>
        <span className="shrink-0 text-[var(--muted-foreground)]">{item.context_window?.toLocaleString() ?? "容量未知"}{item.max_output_tokens ? ` · 输出 ${item.max_output_tokens.toLocaleString()}` : ""}</span>
      </div>)}
    </div>}
  </div>;
}
