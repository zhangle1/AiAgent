"use client";

import { useEffect, useState } from "react";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";
import { getAdminUsage, getAdminUsers, type AdminUser } from "@/lib/admin-api";
import { getAuthStatus } from "@/lib/auth-api";
import type { AdminUsageReport } from "@/lib/admin-usage-types";

const field = "h-10 rounded-lg border border-slate-200 bg-white px-3 text-sm text-slate-800";
const panel = "mt-5 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm";
const today = () => new Date().toISOString().slice(0, 10);

export function AdminUsagePage() {
  const [allowed, setAllowed] = useState<boolean | null>(null);
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [period, setPeriod] = useState<AdminUsageReport["period"]>("day");
  const [range, setRange] = useState("30");
  const [month, setMonth] = useState(() => today().slice(0, 7));
  const [start, setStart] = useState(today);
  const [end, setEnd] = useState(today);
  const [userId, setUserId] = useState("");
  const [projectId, setProjectId] = useState("");
  const [projects, setProjects] = useState<AdminUsageReport["projects"]>([]);
  const [report, setReport] = useState<AdminUsageReport | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const [refresh, setRefresh] = useState(0);
  const [metric, setMetric] = useState<"sessions" | "turns" | "frequency" | "tokens">("sessions");
  useEffect(() => { let active = true; void getAuthStatus().then(status => { if (active) setAllowed(status.is_admin === true); }).catch(() => { if (active) setAllowed(false); }); return () => { active = false; }; }, []);
  useEffect(() => { if (!allowed) return; let active = true; void getAdminUsers().then(value => { if (active) setUsers(value); }).catch(() => { if (active) setError("用户列表加载失败，请刷新重试。"); }); return () => { active = false; }; }, [allowed, refresh]);
  useEffect(() => {
    if (!allowed) return;
    const controller = new AbortController();
    setReport(null); setError(""); setLoading(false);
    let startDate: string | undefined; let endDate: string | undefined;
    if (range === "custom") { startDate = start; endDate = end; }
    if (range === "month") {
      if (!/^\d{4}-\d{2}$/.test(month)) { setError("请选择月份。"); return; }
      startDate = `${month}-01`;
      const [year, m] = month.split("-").map(Number);
      endDate = `${month}-${new Date(Date.UTC(year, m, 0)).getUTCDate()}`;
    }
    if (startDate !== undefined && (!startDate || !endDate || startDate > endDate || (Date.parse(endDate) - Date.parse(startDate)) / 86400000 >= 3650)) { setError("请选择有效日期范围（最多 3650 天）。"); return; }
    setLoading(true);
    void getAdminUsage(period, Number(range) || 30, userId || undefined, {
      ...(startDate ? { start_date: startDate, end_date: endDate } : {}), ...(projectId ? { project_id: projectId } : {}),
    }, controller.signal).then(value => { if (!controller.signal.aborted) { setReport(value); setProjects(value.projects); } })
      .catch(reason => { if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "读取统计失败。"); })
      .finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [allowed, range, month, start, end, period, userId, projectId, refresh]);
  if (allowed === null) return <p role="status">正在检查权限…</p>;
  if (!allowed) return <p role="alert">没有管理权限。</p>;
  const metricName = { sessions: "活跃会话数", turns: "完成轮次", frequency: "日均轮次", tokens: "Token" }[metric];
  return <section>
    <SettingsPageHeader title="用户流量" description="按日期、用户和项目查看聊天使用情况与 Token 消耗。" action={<button className={field} onClick={() => setRefresh(value => value + 1)}>刷新</button>} />
    <div className="flex flex-wrap items-end gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <label className="grid gap-1 text-xs">聚合维度<select className={field} value={period} onChange={e => setPeriod(e.target.value as typeof period)}><option value="day">按日</option><option value="week">按周</option><option value="month">按月</option><option value="year">按年</option></select></label>
      <label className="grid gap-1 text-xs">时间范围<select className={field} value={range} onChange={e => setRange(e.target.value)}><option value="30">近 30 天</option><option value="90">近 90 天</option><option value="365">近 365 天</option><option value="month">指定月份</option><option value="custom">自定义日期</option></select></label>
      {range === "month" && <label className="grid gap-1 text-xs">月份<input type="month" className={field} value={month} onChange={e => setMonth(e.target.value)} /></label>}
      {range === "custom" && <><label className="grid gap-1 text-xs">开始日期<input type="date" className={field} value={start} onChange={e => setStart(e.target.value)} /></label><label className="grid gap-1 text-xs">结束日期（含）<input type="date" className={field} value={end} onChange={e => setEnd(e.target.value)} /></label></>}
      <label className="grid gap-1 text-xs">用户<select className={field} value={userId} onChange={e => setUserId(e.target.value)}><option value="">全部用户</option>{users.map(user => <option key={user.id} value={user.id}>{user.alias || user.username}</option>)}</select></label>
      <label className="grid gap-1 text-xs">项目<select className={`${field} max-w-64`} value={projectId} onChange={e => setProjectId(e.target.value)}><option value="">全部项目</option><option value="0">未关联 / 无法归属</option>{projects.map(project => <option key={project.id} value={project.id}>{project.name}</option>)}</select></label>
    </div>
    <p className="mt-3 text-xs leading-6 text-slate-500">UTC 日期；仅统计已记录用量的成功完成轮次。活跃会话按用户和会话去重（无会话 ID 的轮次不计会话数），跨周期会话可重复出现；日均轮次 = 完成轮次 ÷ 所选范围自然天数（含零活动日）。项目按会话当前归属统计，无法关联单列；不代表新建会话数。Token 含供应商原始用量及缺失时的估算值。</p>
    {error && <p role="alert" className="mt-4 rounded-lg bg-rose-50 p-4 text-rose-700">{error}</p>}
    {loading && <p role="status" className="py-12 text-center text-slate-500">正在汇总统计…</p>}
    {report && <>
      <p className="my-4 text-xs text-slate-500">{report.from.slice(0, 10)} 至 {new Date(Date.parse(report.to) - 86400000).toISOString().slice(0, 10)} · {report.users.length} 位参与用户</p>
      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">{[["活跃会话数", report.metrics.session_count], ["完成轮次", report.turn_count], ["活跃天数", report.metrics.active_days], ["日均轮次", report.metrics.daily_turns], ["累计 Token", report.total_tokens]].map(([label, value]) => <div key={label} className="rounded-2xl border border-slate-200 bg-white p-5"><p className="text-xs text-slate-500">{label}</p><p className="mt-2 text-2xl font-semibold">{value.toLocaleString()}</p></div>)}</div>
      <section className={panel}><div className="flex flex-wrap items-center justify-between gap-3"><h2 className="font-semibold">{metricName}趋势</h2><select aria-label="图表指标" className={field} value={metric} onChange={e => setMetric(e.target.value as typeof metric)}><option value="sessions">活跃会话数</option><option value="turns">完成轮次</option><option value="frequency">日均轮次</option><option value="tokens">Token</option></select></div>
        <Bars items={report.buckets.map(bucket => ({ id: bucket.key, label: bucket.label, value: metric === "sessions" ? bucket.metrics.session_count : metric === "turns" ? bucket.turn_count : metric === "frequency" ? bucket.metrics.daily_turns : bucket.total_tokens }))} unit={metricName} />
      </section>
      <section className={panel}><h2 className="font-semibold">项目使用对比 · 完成轮次</h2><Bars items={Object.values(report.user_projects.reduce<Record<string, { id: string; label: string; value: number }>>((result, row) => { const id = String(row.project_id ?? 0); result[id] ??= { id, label: row.project_name, value: 0 }; result[id].value += row.turn_count; return result; }, {}))} unit="轮次" /></section>
      <section className={panel}><h2 className="font-semibold">用户 × 项目明细</h2><div className="mt-4 max-h-[480px] overflow-auto"><table className="w-full min-w-[720px] text-left text-sm"><thead className="sticky top-0 bg-white text-slate-500"><tr>{["用户", "项目", "活跃会话", "完成轮次", "活跃天数", "日均轮次", "Token"].map(label => <th key={label} className="px-3 py-3">{label}</th>)}</tr></thead><tbody>{report.user_projects.map(row => <tr key={`${row.user_id}:${row.project_id}`} className="border-t border-slate-100">{[row.username, row.project_name, row.metrics.session_count, row.turn_count, row.metrics.active_days, row.metrics.daily_turns, row.total_tokens].map((value, index) => <td key={index} className="px-3 py-3">{value.toLocaleString()}</td>)}</tr>)}{!report.user_projects.length && <tr><td colSpan={7} className="py-10 text-center text-slate-400">当前筛选范围暂无已完成的聊天用量</td></tr>}</tbody></table></div></section>
    </>}
  </section>;
}

function Bars({ items, unit }: { items: { id: string; label: string; value: number }[]; unit: string }) {
  const max = Math.max(1, ...items.map(item => item.value));
  if (!items.some(item => item.value > 0)) return <p className="py-14 text-center text-sm text-slate-400">当前筛选范围暂无{unit}数据</p>;
  return <div className="mt-6 overflow-x-auto pb-3" tabIndex={0} aria-label={`${unit}柱状图，可横向滚动`}><div className="flex h-60 min-w-full w-max items-end gap-3 border-b border-slate-200">{items.map(item => <div key={item.id} tabIndex={0} title={`${item.label}：${item.value.toLocaleString()} ${unit}`} aria-label={`${item.label}：${item.value.toLocaleString()} ${unit}`} className="flex w-16 shrink-0 flex-col items-center justify-end gap-2"><span className="text-xs text-slate-500">{item.value.toLocaleString()}</span><div className="w-8 rounded-t-md bg-blue-500 hover:bg-blue-600" style={{ height: item.value / max * 170 }} /><span className="w-full truncate text-center text-[10px] text-slate-500">{item.label}</span></div>)}</div></div>;
}
