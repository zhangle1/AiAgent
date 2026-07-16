"use client";

import { useEffect, useMemo, useState } from "react";
import { CheckCircle2, FlaskConical, KeyRound, Loader2, Plus, Save, Trash2 } from "lucide-react";
import {
  createGitAccount,
  deleteGitAccount,
  listGitAccounts,
  testGitAccount,
  updateGitAccount,
  type GitAccount,
  type GitAccountPayload,
  type GitAccountTestResult,
  type GitProvider,
} from "@/lib/git-account-api";
import { SettingsPageHeader } from "@/components/settings/layout/SettingsShell";

const words = {
  title: "Git \u8d26\u53f7",
  description: "\u7ba1\u7406\u4ee3\u7801\u6258\u7ba1\u5e73\u53f0\u7684\u8d26\u53f7\u8fde\u63a5\u4e0e\u8bbf\u95ee\u4ee4\u724c\u3002\u9879\u76ee\u7ed1\u5b9a\u3001\u540c\u6b65\u4e0e\u4ea4\u4ed8\u529f\u80fd\u4f1a\u5728\u201c\u4ee3\u7801\u5e93\u201d\u4e2d\u63d0\u4f9b\u3002",
  configurations: "\u914d\u7f6e\u5217\u8868",
  providerConnection: "\u4f9b\u5e94\u5546\u8fde\u63a5",
  accountConfiguration: "\u8d26\u53f7\u914d\u7f6e",
  diagnostics: "\u8bca\u65ad",
  runTest: "\u8fd0\u884c\u6d4b\u8bd5",
  running: "\u6d4b\u8bd5\u4e2d...",
  save: "\u4fdd\u5b58\u914d\u7f6e",
  saving: "\u4fdd\u5b58\u4e2d...",
  add: "\u65b0\u5efa\u8d26\u53f7",
  name: "\u663e\u793a\u540d\u79f0",
  username: "\u5e73\u53f0\u7528\u6237\u540d",
  email: "\u63d0\u4ea4\u90ae\u7bb1\uff08\u53ef\u9009\uff09",
  token: "\u4e2a\u4eba\u8bbf\u95ee\u4ee4\u724c",
  tokenEdit: "\u65b0\u7684\u8bbf\u95ee\u4ee4\u724c\uff08\u7559\u7a7a\u5219\u4e0d\u4fee\u6539\uff09",
  noAccounts: "\u8fd8\u6ca1\u6709\u8d26\u53f7\u914d\u7f6e\u3002",
  configured: "\u4ee4\u724c\u5df2\u914d\u7f6e",
  notConfigured: "\u672a\u914d\u7f6e\u4ee4\u724c",
  saved: "\u8d26\u53f7\u914d\u7f6e\u5df2\u4fdd\u5b58\u3002",
  noSavedAccount: "\u8bf7\u5148\u4fdd\u5b58\u8d26\u53f7\u3002",
  noToken: "\u8bf7\u5148\u914d\u7f6e\u5e76\u4fdd\u5b58\u8bbf\u95ee\u4ee4\u724c\uff0c\u518d\u8fd0\u884c\u6d4b\u8bd5\u3002",
  active: "\u5f53\u524d\u4f7f\u7528",
};

const blank = (provider: GitProvider = "gitee"): GitAccountPayload => ({
  provider,
  display_name: "",
  username: "",
  email: "",
  access_token: "",
  is_active: true,
});

export function GitAccountsSettingsPage() {
  const [accounts, setAccounts] = useState<GitAccount[]>([]);
  const [draft, setDraft] = useState<GitAccountPayload>(blank());
  const [editingId, setEditingId] = useState<number | null>(null);
  const [tokenConfigured, setTokenConfigured] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [error, setError] = useState("");
  const [testResult, setTestResult] = useState<GitAccountTestResult | null>(null);

  const selectedAccount = useMemo(() => accounts.find((account) => account.id === editingId) ?? null, [accounts, editingId]);

  const selectAccount = (account: GitAccount) => {
    setEditingId(account.id);
    setTokenConfigured(account.token_configured);
    setDraft({
      provider: account.provider,
      display_name: account.display_name,
      username: account.username,
      email: account.email ?? "",
      access_token: "",
      is_active: account.is_active,
    });
    setError("");
    setTestResult(null);
  };

  const load = async () => {
    setLoading(true);
    try {
      const items = await listGitAccounts();
      setAccounts(items);
      const first = items.find((item) => item.is_active) ?? items[0];
      if (first) selectAccount(first);
      setError("");
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Unable to load Git accounts.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { void load(); }, []);

  const createNew = () => {
    setEditingId(null);
    setTokenConfigured(false);
    setDraft(blank());
    setError("");
    setTestResult(null);
  };

  const save = async (event: React.FormEvent) => {
    event.preventDefault();
    setSaving(true);
    setError("");
    try {
      const saved = editingId ? await updateGitAccount(editingId, draft) : await createGitAccount(draft);
      setAccounts((items) => {
        const withoutSaved = items.filter((item) => item.id !== saved.id).map((item) => saved.is_active ? { ...item, is_active: false } : item);
        return [saved, ...withoutSaved];
      });
      selectAccount(saved);
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Unable to save the Git account.");
    } finally {
      setSaving(false);
    }
  };

  const remove = async (target = selectedAccount) => {
    if (!target || !window.confirm("Delete this Git account?")) return;
    try {
      await deleteGitAccount(target.id);
      const items = accounts.filter((item) => item.id !== target.id);
      setAccounts(items);
      const next = items.find((item) => item.is_active) ?? items[0];
      if (next) selectAccount(next); else createNew();
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Unable to delete the Git account.");
    }
  };

  const runTest = async () => {
    if (!editingId) { setError(words.noSavedAccount); return; }
    if (!tokenConfigured) { setError(words.noToken); return; }
    setTesting(true);
    setError("");
    try {
      setTestResult(await testGitAccount(editingId));
    } catch (exception) {
      setError(exception instanceof Error ? exception.message : "Unable to run the Git connection test.");
    } finally {
      setTesting(false);
    }
  };

  return (
    <section className="min-w-0">
      <SettingsPageHeader title={words.title} description={words.description} action={null} />
      <div className="grid gap-5 lg:grid-cols-[180px_minmax(0,1fr)]">
        <aside className="h-fit rounded-xl border border-[var(--border)] bg-white p-3">
          <div className="mb-3 flex items-center justify-between"><h2 className="text-[13px] font-semibold">{words.configurations}</h2><button type="button" onClick={createNew} aria-label={words.add} className="rounded-md p-1 text-zinc-500 hover:bg-zinc-100"><Plus size={15} /></button></div>
          {loading ? <div className="flex justify-center py-8 text-zinc-400"><Loader2 size={16} className="animate-spin" /></div> : accounts.length === 0 ? <p className="rounded-lg border border-dashed border-[var(--border)] px-2 py-4 text-center text-[11px] text-zinc-500">{words.noAccounts}</p> : <div className="space-y-1">{accounts.map((account) => <div key={account.id} className={`group flex items-center rounded-lg ${account.id === editingId ? "bg-zinc-100" : "hover:bg-zinc-50"}`}><button type="button" onClick={() => selectAccount(account)} className="min-w-0 flex-1 px-2.5 py-2 text-left"><span className="block truncate text-[12px] font-medium">{account.display_name}</span><span className="mt-0.5 block truncate text-[10px] text-zinc-500">{account.provider === "gitee" ? "Gitee" : "GitHub"} · @{account.username}</span></button><button type="button" onClick={() => void remove(account)} className="mr-1 rounded p-1.5 text-zinc-400 opacity-0 hover:bg-red-50 hover:text-red-600 group-hover:opacity-100 focus:opacity-100" aria-label={`Delete ${account.display_name}`}><Trash2 size={13} /></button></div>)}</div>}
        </aside>

        <form onSubmit={save} className="space-y-4">
          <section className="rounded-xl border border-[var(--border)] bg-white p-5">
            <div className="mb-5 flex flex-wrap items-center justify-between gap-3"><div><h2 className="text-[15px] font-semibold">{words.providerConnection}</h2><p className="mt-1 text-[12px] text-zinc-500">Gitee is the default provider; GitHub can be selected when needed.</p></div><button type="button" onClick={createNew} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-[var(--border)] px-2.5 text-[12px] hover:bg-zinc-50"><Plus size={14} />{words.add}</button></div>
            <label className="block text-[12px] font-medium">Provider<select value={draft.provider} onChange={(event) => setDraft((item) => ({ ...item, provider: event.target.value as GitProvider }))} className="mt-1.5 h-10 w-full rounded-md border border-[var(--border)] bg-white px-3 text-[13px] outline-none focus:border-emerald-500"><option value="gitee">Gitee</option><option value="github">GitHub</option></select></label>
          </section>

          <section className="rounded-xl border border-[var(--border)] bg-white p-5">
            <div className="mb-4 flex items-center justify-between gap-3"><div><h2 className="text-[15px] font-semibold">{words.accountConfiguration}</h2><p className="mt-1 text-[12px] text-zinc-500">Access tokens are encrypted on the server and never returned to the browser.</p></div>{selectedAccount && <button type="button" onClick={() => void remove()} className="inline-flex h-8 items-center gap-1.5 rounded-md px-2 text-[12px] text-red-600 hover:bg-red-50"><Trash2 size={14} />Delete</button>}</div>
            <div className="grid gap-4 md:grid-cols-2"><Field label={words.name} value={draft.display_name} onChange={(value) => setDraft((item) => ({ ...item, display_name: value }))} placeholder="My Gitee account" /><Field label={words.username} value={draft.username} onChange={(value) => setDraft((item) => ({ ...item, username: value }))} placeholder={draft.provider === "gitee" ? "Gitee username" : "GitHub username"} /><Field label={words.email} value={draft.email ?? ""} onChange={(value) => setDraft((item) => ({ ...item, email: value }))} placeholder="you@example.com" type="email" /><Field label={editingId ? words.tokenEdit : words.token} value={draft.access_token ?? ""} onChange={(value) => setDraft((item) => ({ ...item, access_token: value }))} placeholder="Personal access token" type="password" /></div>
            <label className="mt-4 flex items-center gap-2 text-[12px]"><input type="checkbox" checked={draft.is_active} onChange={(event) => setDraft((item) => ({ ...item, is_active: event.target.checked }))} />{words.active}</label>
            {error && <p className="mt-4 rounded-md bg-red-50 px-3 py-2 text-[12px] text-red-700">{error}</p>}
            <button disabled={saving} className="mt-5 inline-flex h-9 items-center gap-2 rounded-md bg-emerald-600 px-4 text-[13px] font-medium text-white hover:bg-emerald-700 disabled:bg-zinc-300">{saving ? <Loader2 size={15} className="animate-spin" /> : <Save size={15} />}{saving ? words.saving : words.save}</button>
          </section>

          <section className="rounded-xl border border-[var(--border)] bg-white p-5"><div className="flex flex-wrap items-center justify-between gap-3"><div><h2 className="text-[15px] font-semibold">{words.diagnostics}</h2><p className="mt-1 text-[12px] text-zinc-500">Verify the saved account and access token with the selected Git provider.</p></div><button type="button" disabled={testing || !editingId || !tokenConfigured} onClick={() => void runTest()} className="inline-flex h-8 items-center gap-1.5 rounded-md border border-[var(--border)] px-3 text-[12px] hover:bg-zinc-50 disabled:cursor-not-allowed disabled:text-zinc-400"><FlaskConical size={14} />{testing ? words.running : words.runTest}</button></div>{!editingId ? <p className="mt-3 text-[12px] text-zinc-500">{words.noSavedAccount}</p> : !tokenConfigured ? <p className="mt-3 text-[12px] text-zinc-500">{words.noToken}</p> : null}{testResult && <div className={`mt-4 rounded-lg border px-3 py-2 text-[12px] ${testResult.status === "success" ? "border-emerald-200 bg-emerald-50 text-emerald-800" : "border-red-200 bg-red-50 text-red-700"}`}><div className="flex items-center gap-2 font-medium">{testResult.status === "success" ? <CheckCircle2 size={15} /> : <KeyRound size={15} />}{testResult.summary}</div><p className="mt-1">{testResult.detail}</p></div>}</section>
        </form>
      </div>
    </section>
  );
}

function Field({ label, value, onChange, placeholder, type = "text" }: { label: string; value: string; onChange: (value: string) => void; placeholder: string; type?: string }) {
  return <label className="block text-[12px] font-medium">{label}<input type={type} value={value} onChange={(event) => onChange(event.target.value)} placeholder={placeholder} className="mt-1.5 h-10 w-full rounded-md border border-[var(--border)] px-3 text-[13px] outline-none focus:border-emerald-500 focus:ring-2 focus:ring-emerald-100" /></label>;
}
