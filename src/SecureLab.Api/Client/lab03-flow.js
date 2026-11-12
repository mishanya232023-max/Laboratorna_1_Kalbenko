function apiFetch(path, options = {}) {
    const url = new URL(path, location.origin);
    return fetch(url, { ...options, credentials: "same-origin" });
}

const byId = (id) => document.getElementById(id);
const setState = (text) => { byId("state").textContent = text; };
const say = (text) => { byId("log").textContent = text; };

function clearPrivate() {
    byId("profile").textContent = "—";
    byId("created").textContent = "—";
}

async function refreshState() {
    try {
        const response = await apiFetch("/api/auth/session-check");
        if (response.status === 204) {
            setState("signed-in");
        } else if (response.status === 401) {
            setState("anonymous");
            clearPrivate();
        } else {
            setState("unknown");
        }
    } catch {
        setState("unknown");
    }
}

async function postJson(path, payload) {
    return apiFetch(path, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
    });
}

byId("register-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = new FormData(event.target);
    const response = await postJson("/api/auth/register", {
        userName: form.get("userName"),
        email: form.get("email"),
        displayName: form.get("displayName"),
        password: form.get("password")
    });
    event.target.elements.password.value = "";
    say(`register: ${response.status}`);
});

byId("login-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = new FormData(event.target);
    const response = await postJson("/api/auth/login", {
        userName: form.get("userName"),
        password: form.get("password")
    });
    event.target.elements.password.value = "";
    say(`login: ${response.status}`);
    await refreshState();
});

byId("profile-btn").addEventListener("click", async () => {
    const response = await apiFetch("/api/me");
    if (!response.ok) {
        byId("profile").textContent = `status ${response.status}`;
        if (response.status === 401) await refreshState();
        return;
    }
    const profile = await response.json();
    byId("profile").textContent = `userName: ${profile.userName}, role: ${profile.role}`;
});

byId("incident-form").addEventListener("submit", async (event) => {
    event.preventDefault();
    const form = new FormData(event.target);
    const response = await postJson("/api/incidents", {
        title: form.get("title"),
        description: form.get("description"),
        severity: form.get("severity"),
        occurredAtUtc: new Date(form.get("occurredAtUtc")).toISOString()
    });
    if (response.status === 201) {
        const created = await response.json();
        byId("created").textContent = `201 created: id=${created.id}, status=${created.status}`;
    } else {
        byId("created").textContent = `status ${response.status}`;
        if (response.status === 401) await refreshState();
    }
});

byId("logout-btn").addEventListener("click", async () => {
    const response = await apiFetch("/api/auth/logout", { method: "POST" });
    say(`logout: ${response.status}`);
    clearPrivate();
    await refreshState();
});

const occurred = document.querySelector('[name="occurredAtUtc"]');
occurred.value = new Date(Date.now() - 10 * 60 * 1000).toISOString().slice(0, 16);
refreshState();