/**
 * api.js — klien untuk backend C# (ASP.NET Core + SQLite).
 * Semua progres perjalanan disimpan di server lewat modul ini.
 */

async function request(path, options = {}) {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  const text = await response.text();
  let body = null;
  if (text) {
    try {
      body = JSON.parse(text);
    } catch {
      body = { raw: text };
    }
  }
  if (!response.ok) {
    const error = new Error(body?.error ?? `${response.status} ${response.statusText}`);
    error.status = response.status;
    error.body = body;
    throw error;
  }
  return body;
}

export const api = {
  health: () => request("/api/health"),

  listProfiles: () => request("/api/profiles").then((r) => r.profiles ?? []),

  createProfile: (name) =>
    request("/api/profiles", { method: "POST", body: JSON.stringify({ name }) }).then((r) => r.profile),

  getProfile: (id) => request(`/api/profiles/${id}`),

  deleteProfile: (id) => request(`/api/profiles/${id}`, { method: "DELETE" }),

  listSlots: (id) => request(`/api/profiles/${id}/slots`).then((r) => r.slots ?? []),

  resume: (id) => request(`/api/profiles/${id}/resume`),

  loadSlot: (id, slot) => request(`/api/profiles/${id}/slots/${slot}`),

  saveSlot: (id, slot, state) =>
    request(`/api/profiles/${id}/slots/${slot}`, { method: "PUT", body: JSON.stringify(state) }),

  deleteSlot: (id, slot) => request(`/api/profiles/${id}/slots/${slot}`, { method: "DELETE" }),

  journey: (id) => request(`/api/profiles/${id}/journey`),

  logEvent: (id, kind, detail) =>
    request(`/api/profiles/${id}/events`, { method: "POST", body: JSON.stringify({ kind, detail }) }),
};
