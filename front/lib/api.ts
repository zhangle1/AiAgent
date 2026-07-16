import type { Catalog, ServiceName, SettingsResponse, TestResult, UiSettings } from "@/lib/settings-types";

async function parseJson<T>(response: Response): Promise<T> {
  const text = await response.text();
  let payload: unknown = null;
  if (text) {
    try {
      payload = JSON.parse(text) as unknown;
    } catch {
      throw new Error(
        `Request returned non-JSON response: HTTP ${response.status} ${text.slice(0, 160)}`,
      );
    }
  }
  if (!response.ok) {
    const message = typeof payload === "object" && payload && "message" in payload
      ? String((payload as { message?: string }).message)
      : `Request failed with HTTP ${response.status}`;
    throw new Error(message);
  }
  return payload as T;
}

export async function getSettings(): Promise<SettingsResponse> {
  return parseJson<SettingsResponse>(await fetch("/api/v1/settings", { cache: "no-store" }));
}

export async function getUiSettings(): Promise<UiSettings> {
  return parseJson<UiSettings>(await fetch("/api/v1/settings/ui", { cache: "no-store" }));
}

export async function updateUiSettings(ui: Partial<UiSettings>): Promise<UiSettings> {
  return parseJson<UiSettings>(
    await fetch("/api/v1/settings/ui", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(ui),
    }),
  );
}

export async function saveCatalog(catalog: Catalog): Promise<{ catalog: Catalog }> {
  return parseJson<{ catalog: Catalog }>(
    await fetch("/api/v1/settings/catalog", {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ catalog }),
    }),
  );
}

export async function applyCatalog(catalog: Catalog): Promise<{ catalog: Catalog; message: string }> {
  return parseJson<{ catalog: Catalog; message: string }>(
    await fetch("/api/v1/settings/apply", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ catalog }),
    }),
  );
}

export async function testService(service: ServiceName, catalog: Catalog): Promise<TestResult> {
  return parseJson<TestResult>(
    await fetch(`/api/v1/settings/tests/${service}/start`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ catalog }),
    }),
  );
}
