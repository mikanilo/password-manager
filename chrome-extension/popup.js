const NATIVE_HOST_NAME = "com.pwman.host";

const authView = document.getElementById("authView");
const vaultView = document.getElementById("vaultView");
const pinInput = document.getElementById("pinInput");
const authLabel = document.getElementById("authLabel");
const unlockButton = document.getElementById("unlockButton");
const lockButton = document.getElementById("lockButton");
const statusEl = document.getElementById("status");
const entriesEl = document.getElementById("entries");

const newServiceInput = document.getElementById("newServiceInput");
const newUsernameInput = document.getElementById("newUsernameInput");
const newPasswordInput = document.getElementById("newPasswordInput");
const generatePasswordButton = document.getElementById("generatePasswordButton");
const saveAccountButton = document.getElementById("saveAccountButton");
const detectedHint = document.getElementById("detectedHint");

let port = null;

// Requests waiting on a reply, oldest first. The native host handles one
// message at a time and answers in order, so matching replies by position
// is enough.
//
// Every entry MUST be settled when the port goes away. Previously only the
// resolve half was kept, so if the host died -- or didn't recognise the
// action, or was never reachable -- the promise stayed pending forever and
// the button that triggered it just did nothing, with no error anywhere.
let pending = [];

const HOST_UNREACHABLE =
  "Couldn't reach the pwman native host. Make sure it's published and " +
  "registered (run register-native-host.ps1), and re-publish it after " +
  "changing the C# code.";

function connect() {
  port = chrome.runtime.connectNative(NATIVE_HOST_NAME);

  port.onMessage.addListener((response) => {
    const entry = pending.shift();
    if (entry) entry.resolve(response);
  });

  port.onDisconnect.addListener(() => {
    // The native host process exits when the port disconnects (e.g. Chrome
    // couldn't find/start it, or it crashed).
    const err = chrome.runtime.lastError;
    const message = err ? `${HOST_UNREACHABLE} (${err.message})` : HOST_UNREACHABLE;

    const inFlight = pending;
    pending = [];
    for (const entry of inFlight) {
      entry.reject(new Error(message));
    }

    port = null;
  });
}

function sendRequest(request) {
  return new Promise((resolve, reject) => {
    let queued = false;
    try {
      if (!port) connect();
      pending.push({ resolve, reject });
      queued = true;
      port.postMessage(request);
    } catch (e) {
      // Drop the entry we just queued, otherwise it would consume some
      // later request's reply and desynchronise the whole queue.
      if (queued) {
        pending = pending.filter((entry) => entry.resolve !== resolve);
      }
      reject(e);
    }
  });
}

function setStatus(message, ok = false) {
  statusEl.textContent = message || "";
  statusEl.classList.toggle("ok", ok && !!message);
}

const MONTH_NAMES = [
  "Jan", "Feb", "Mar", "Apr", "May", "Jun",
  "Jul", "Aug", "Sept", "Oct", "Nov", "Dec",
];

// 11th, 12th and 13th are the exceptions -- they take "th" despite ending
// in 1, 2 and 3.
function ordinalSuffix(day) {
  if (day >= 11 && day <= 13) return "th";
  switch (day % 10) {
    case 1: return "st";
    case 2: return "nd";
    case 3: return "rd";
    default: return "th";
  }
}

// Mirrors TimestampFormat.Format in PasswordManager.Core, so the extension
// shows dates identically to the CLI and desktop app: "Sept 1st 2026, 11:06 am".
function formatDate(iso) {
  if (!iso) return "unknown";
  const d = new Date(iso);
  if (isNaN(d.getTime())) return "unknown";

  const hour = d.getHours() % 12 || 12;
  const minute = String(d.getMinutes()).padStart(2, "0");
  const meridiem = d.getHours() < 12 ? "am" : "pm";
  const day = d.getDate();

  return (
    `${MONTH_NAMES[d.getMonth()]} ${day}${ordinalSuffix(day)} ` +
    `${d.getFullYear()}, ${hour}:${minute} ${meridiem}`
  );
}

// The active tab's hostname, used to pre-fill the "add account" service
// field. activeTab grants us the URL once the user opens the popup.
async function getActiveTab() {
  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  return tab || null;
}

function hostnameOf(url) {
  try {
    const h = new URL(url).hostname;
    return h.replace(/^www\./, "");
  } catch {
    return "";
  }
}

unlockButton.addEventListener("click", async () => {
  const secret = pinInput.value;
  if (!secret) return;

  unlockButton.disabled = true;
  setStatus("");

  try {
    const result = await sendRequest({ action: "unlock", pin: secret });
    if (result.success) {
      pinInput.value = "";
      await loadEntries();
      await prepareAddForm();
      authView.classList.add("hidden");
      vaultView.classList.remove("hidden");
    } else {
      setStatus(result.error || "Unlock failed.");
    }
  } catch (e) {
    setStatus(e.message);
  } finally {
    unlockButton.disabled = false;
  }
});

pinInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") unlockButton.click();
});

lockButton.addEventListener("click", () => {
  // Disconnecting the port kills the native host process (Chrome's native
  // messaging lifecycle), which is what actually clears the key from memory
  // -- same effect as clicking "Lock" in the desktop app.
  if (port) {
    port.disconnect();
    port = null;
  }
  vaultView.classList.add("hidden");
  authView.classList.remove("hidden");
  newServiceInput.value = "";
  newUsernameInput.value = "";
  newPasswordInput.value = "";
  detectedHint.classList.add("hidden");
  setStatus("");
});

async function loadEntries() {
  const result = await sendRequest({ action: "list" });
  entriesEl.innerHTML = "";

  if (!result.success) {
    setStatus(result.error || "Couldn't load entries.");
    return;
  }

  const entries =
    result.entries ||
    (result.services || []).map((s) => ({ service: s, createdUtc: null }));

  if (entries.length === 0) {
    entriesEl.innerHTML =
      '<div style="color:#888;font-size:12px;">No saved entries yet.</div>';
    return;
  }

  for (const entry of entries) {
    const entryDiv = document.createElement("div");
    entryDiv.className = "entry";

    const serviceLabel = document.createElement("div");
    serviceLabel.className = "service";
    serviceLabel.textContent = entry.service;

    const addedLabel = document.createElement("div");
    addedLabel.className = "added";
    addedLabel.textContent = "Added " + formatDate(entry.createdUtc);

    const actions = document.createElement("div");
    actions.className = "actions";

    const fillBtn = document.createElement("button");
    fillBtn.textContent = "Fill on page";
    fillBtn.addEventListener("click", () => handleFill(entry.service));

    const copyBtn = document.createElement("button");
    copyBtn.textContent = "Copy password";
    copyBtn.className = "secondary";
    copyBtn.addEventListener("click", () => handleCopy(entry.service));

    actions.appendChild(fillBtn);
    actions.appendChild(copyBtn);
    entryDiv.appendChild(serviceLabel);
    entryDiv.appendChild(addedLabel);
    entryDiv.appendChild(actions);
    entriesEl.appendChild(entryDiv);
  }
}

// Pre-fill the "add account" form with the current site, and offer to save
// any credentials already typed into the page's login form.
async function prepareAddForm() {
  const tab = await getActiveTab();
  const host = tab ? hostnameOf(tab.url || "") : "";
  if (host && !newServiceInput.value) newServiceInput.value = host;

  if (!tab?.id) return;

  try {
    const [injection] = await chrome.scripting.executeScript({
      target: { tabId: tab.id },
      func: detectCredentialsOnPage,
    });
    const found = injection?.result;
    if (found && found.password) {
      newPasswordInput.value = found.password;
      if (found.username) newUsernameInput.value = found.username;
      detectedHint.textContent =
        "Found a login typed on this page — review and save it below.";
      detectedHint.classList.remove("hidden");
    }
  } catch {
    // executeScript throws on restricted pages (chrome://, the web store,
    // etc.). Nothing to detect there — the manual form still works.
  }
}

generatePasswordButton.addEventListener("click", async () => {
  setStatus("");
  generatePasswordButton.disabled = true;

  try {
    const result = await sendRequest({ action: "generate" });
    if (result.success) {
      // Revealed deliberately: a generated password is no use if you can't
      // read it, and it's about to be saved to the vault anyway.
      newPasswordInput.type = "text";
      newPasswordInput.value = result.password;
      setStatus("Generated a new password.", true);
    } else {
      setStatus(result.error || "Couldn't generate a password.");
    }
  } catch (e) {
    setStatus(e.message);
  } finally {
    generatePasswordButton.disabled = false;
  }
});

saveAccountButton.addEventListener("click", async () => {
  const service = newServiceInput.value.trim();
  const username = newUsernameInput.value.trim();
  const password = newPasswordInput.value;

  if (!service || !password) {
    setStatus("A service name and a password are both required.");
    return;
  }

  saveAccountButton.disabled = true;
  setStatus("");

  try {
    const result = await sendRequest({
      action: "addEntry",
      service,
      username,
      password,
    });

    if (!result.success) {
      setStatus(result.error || "Couldn't save the account.");
      return;
    }

    newServiceInput.value = "";
    newUsernameInput.value = "";
    newPasswordInput.value = "";
    newPasswordInput.type = "password";
    detectedHint.classList.add("hidden");

    let msg = result.replaced
      ? `Updated the saved account for '${service}'.`
      : `Saved a new account for '${service}'.`;
    if (result.strength && result.strength !== "Strong") {
      msg += ` Note: password strength ${result.strength}.`;
    }
    setStatus(msg, true);

    await loadEntries();
  } catch (e) {
    setStatus(e.message);
  } finally {
    saveAccountButton.disabled = false;
  }
});

async function handleFill(service) {
  setStatus("");

  let result;
  try {
    result = await sendRequest({ action: "getPassword", service });
  } catch (e) {
    setStatus(e.message);
    return;
  }

  if (!result.success) {
    setStatus(result.error || "Couldn't retrieve credential.");
    return;
  }

  const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
  if (!tab?.id) {
    setStatus("No active tab to fill.");
    return;
  }

  await chrome.scripting.executeScript({
    target: { tabId: tab.id },
    func: fillFieldsOnPage,
    args: [result.username, result.password],
  });

  window.close();
}

async function handleCopy(service) {
  setStatus("");

  let result;
  try {
    result = await sendRequest({ action: "getPassword", service });
  } catch (e) {
    setStatus(e.message);
    return;
  }

  if (!result.success) {
    setStatus(result.error || "Couldn't retrieve credential.");
    return;
  }

  await navigator.clipboard.writeText(result.password);
  setStatus(`Password for '${service}' copied.`, true);
}

// Injected into the page to read any credentials already typed into a login
// form. Runs in the page's context and only returns plain strings back.
function detectCredentialsOnPage() {
  const passwordField = document.querySelector('input[type="password"]');
  const password = passwordField && passwordField.value ? passwordField.value : "";

  let username = "";
  if (passwordField) {
    const candidates = Array.from(
      document.querySelectorAll(
        'input[type="text"], input[type="email"], input:not([type])'
      )
    ).filter(
      (el) =>
        passwordField.compareDocumentPosition(el) &
        Node.DOCUMENT_POSITION_PRECEDING
    );
    const filled = candidates.reverse().find((el) => el.value);
    if (filled) username = filled.value;
  }

  return { username, password };
}

// Injected directly into the page via chrome.scripting.executeScript.
// Runs in the page's context, not the extension's -- so it can only receive
// plain arguments (username/password), not reach back into extension state.
function fillFieldsOnPage(username, password) {
  function setNativeValue(element, value) {
    // Many frameworks (React in particular) track input state via their own
    // value setter, not the raw DOM property, so a plain `.value = x` gets
    // silently overwritten. Using the native setter directly and dispatching
    // an 'input' event afterward makes the framework pick up the change.
    const nativeSetter = Object.getOwnPropertyDescriptor(
      window.HTMLInputElement.prototype,
      "value"
    ).set;
    nativeSetter.call(element, value);
    element.dispatchEvent(new Event("input", { bubbles: true }));
    element.dispatchEvent(new Event("change", { bubbles: true }));
  }

  const passwordField = document.querySelector('input[type="password"]');
  if (!passwordField) {
    alert("pwman: couldn't find a password field on this page.");
    return;
  }
  setNativeValue(passwordField, password);

  // Look for a plausible username/email field: any text/email input that
  // appears before the password field in the DOM.
  const candidates = Array.from(
    document.querySelectorAll('input[type="text"], input[type="email"]')
  );
  const usernameField = candidates.find(
    (el) => el.compareDocumentPosition(passwordField) & Node.DOCUMENT_POSITION_FOLLOWING
  );

  if (usernameField) {
    setNativeValue(usernameField, username);
  }
}

// Ask the host up front which secret this vault actually expects. A vault
// created before PIN support still opens with its master password, so the
// prompt follows the file instead of assuming. This also surfaces an
// unreachable native host the moment the popup opens, rather than leaving
// the user to discover it by pressing a button that appears to do nothing.
async function initAuthView() {
  try {
    const result = await sendRequest({ action: "vaultExists" });

    if (!result.success) {
      setStatus(result.error || "Couldn't read the vault.");
      return;
    }

    if (!result.exists) {
      authLabel.textContent =
        "No vault yet. Create one with the pwman desktop app or CLI.";
      pinInput.disabled = true;
      unlockButton.disabled = true;
      return;
    }

    if (result.authMode === "MasterPassword") {
      authLabel.textContent = "Enter your master password";
      pinInput.placeholder = "Master password";
      pinInput.removeAttribute("inputmode");
    }
  } catch (e) {
    setStatus(e.message);
  }
}

initAuthView();
