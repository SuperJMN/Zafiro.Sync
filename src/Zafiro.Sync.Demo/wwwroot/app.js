const form = document.querySelector("#settings-form");
const content = document.querySelector("#content");
const reloadButton = document.querySelector("#reload");
const createIdentityButton = document.querySelector("#create-identity");
const exportIdentityButton = document.querySelector("#export-identity");
const importIdentityButton = document.querySelector("#import-identity");
const exportPassword = document.querySelector("#export-password");
const importPassword = document.querySelector("#import-password");
const importFile = document.querySelector("#import-file");
const identityStatus = document.querySelector("#identity-status");
const identityDevice = document.querySelector("#identity-device");
const identityPublicKey = document.querySelector("#identity-public-key");
const status = document.querySelector("#status");
const revision = document.querySelector("#revision");
const fileId = document.querySelector("#file-id");
const lastOperation = document.querySelector("#last-operation");

let baseRevision = null;

const setBusy = (busy) => {
  document.querySelectorAll("button").forEach((button) => {
    button.disabled = busy;
  });
};

const setStatus = (text, tone = "") => {
  status.textContent = text;
  status.className = `status ${tone}`.trim();
};

const showMetadata = (state, operation) => {
  baseRevision = state.revision;
  revision.textContent = state.revision?.toString() ?? "-";
  fileId.textContent = state.fileId ?? "-";
  lastOperation.textContent = operation;
};

const showIdentity = (identity) => {
  identityStatus.textContent = identity.exists ? "Lista" : "Sin identidad";
  identityDevice.textContent = identity.deviceId ?? "-";
  identityPublicKey.textContent = identity.publicKey ?? "-";
};

const loadIdentity = async () => {
  const response = await fetch("/api/identity");
  if (!response.ok) {
    throw new Error(await response.text());
  }

  const identity = await response.json();
  showIdentity(identity);
  return identity;
};

const createIdentity = async () => {
  setBusy(true);
  setStatus("Creando");

  try {
    const response = await fetch("/api/identity", { method: "POST" });
    if (!response.ok) {
      throw new Error(await response.text());
    }

    const identity = await response.json();
    showIdentity(identity);
    setStatus("Identidad lista", "ok");
    await loadSettings();
  } catch (error) {
    setStatus("Error", "error");
    lastOperation.textContent = error.message;
  } finally {
    setBusy(false);
  }
};

const exportIdentity = async () => {
  setBusy(true);
  setStatus("Exportando");

  try {
    const response = await fetch("/api/identity/export", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({ password: exportPassword.value }),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = "zafiro-sync-identity.json";
    link.click();
    URL.revokeObjectURL(url);
    setStatus("Exportada", "ok");
    lastOperation.textContent = "Identidad exportada";
  } catch (error) {
    setStatus("Error", "error");
    lastOperation.textContent = error.message;
  } finally {
    setBusy(false);
  }
};

const importIdentity = async () => {
  setBusy(true);
  setStatus("Importando");

  try {
    const file = importFile.files[0];
    if (!file) {
      throw new Error("identity file missing");
    }

    const response = await fetch("/api/identity/import", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        password: importPassword.value,
        exportJson: await file.text(),
      }),
    });

    if (!response.ok) {
      throw new Error(await response.text());
    }

    const identity = await response.json();
    showIdentity(identity);
    setStatus("Importada", "ok");
    await loadSettings();
  } catch (error) {
    setStatus("Error", "error");
    lastOperation.textContent = error.message;
  } finally {
    setBusy(false);
  }
};

const loadSettings = async () => {
  setBusy(true);
  setStatus("Cargando");

  try {
    const response = await fetch("/api/settings");
    if (response.status === 409) {
      content.value = `{
  "theme": "dark",
  "currency": "EUR",
  "autosave": true
}`;
      showMetadata({ revision: null, fileId: null }, "Identidad pendiente");
      setStatus("Sin identidad");
      return;
    }

    if (!response.ok) {
      throw new Error(await response.text());
    }

    const state = await response.json();
    content.value = state.content;
    showMetadata(state, state.exists ? "Carga remota" : "Plantilla local");
    setStatus(state.exists ? "Sincronizado" : "Nuevo", "ok");
  } catch (error) {
    setStatus("Error", "error");
    lastOperation.textContent = error.message;
  } finally {
    setBusy(false);
  }
};

const saveSettings = async () => {
  setBusy(true);
  setStatus("Guardando");

  try {
    const response = await fetch("/api/settings", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify({
        content: content.value,
        baseRevision,
      }),
    });

    const result = await response.json();
    if (response.status === 409) {
      baseRevision = result.revision;
      revision.textContent = result.revision.toString();
      fileId.textContent = result.fileId;
      lastOperation.textContent = "Conflicto remoto";
      setStatus("Conflicto", "error");
      return;
    }

    if (!response.ok) {
      throw new Error(result.detail ?? result.message ?? "Save failed");
    }

    showMetadata(result, "Guardado remoto");
    setStatus("Guardado", "ok");
  } catch (error) {
    setStatus("Error", "error");
    lastOperation.textContent = error.message;
  } finally {
    setBusy(false);
  }
};

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  await saveSettings();
});

reloadButton.addEventListener("click", loadSettings);
createIdentityButton.addEventListener("click", createIdentity);
exportIdentityButton.addEventListener("click", exportIdentity);
importIdentityButton.addEventListener("click", importIdentity);

await loadIdentity();
await loadSettings();
