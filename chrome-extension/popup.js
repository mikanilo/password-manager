const NATIVE_HOST_NAME = "com.pwman.host";

const authView = document.getElementById("authView");
const vaultView = document.getElementById("vaultView");
const masterPasswordInput = document.getElementById("masterPasswordInput");
const unlockButton = document.getElementById("unlockButton");
const lockButton = document.getElementById("lockButton");
const statusEl = document.getElementById("status");
const entriesEl = document.getElementById("entries");

let port = null;
let pendingResolvers = [];

function connect() {
  port = chrome.runtime.connectNative(NATIVE_HOST_NAME);

  port.onMessage.addListener((response) => {
    const resolve = pendingResolvers.shift();
    if (resolve) resolve(response);
  });

  port.onDisconnect.addListener(() => {
    // The native host process exits when the port disconnects (e.g. Chrome
    // couldn't find/start it, or it crashed). Reflect that in the UI rather
    // than leaving the popup silently non-functional.
    const err = chrome.runtime.lastError;
    if (err) {
      setStatus(
        "Couldn't reach the pwman native host. Is it installed and registered? " +
          err.message
      );
    }
    port = null;
  });
}

function sendRequest(request) {
  return new Promise((resolve, reject) => {
    if (!port) connect();
    pendingResolvers.push(resolve);
    try {
      port.postMessage(request);
    } catch (e) {
      reject(e);
    }
  });
}

function setStatus(message) {
  statusEl.textContent = message || "";
}

unlockButton.addEventListener("click", async () => {
  const password = masterPasswordInput.value;
  if (!password) return;

  unlockButton.disabled = true;
  setStatus("");

  try {
    const result = await sendRequest({ action: "unlock", password });
    if (result.success) {
      masterPasswordInput.value = "";
      await loadEntries();
      authView.classList.add("hidden");
      vaultView.classList.remove("hidden");
    } else {
      setStatus(result.error || "Unlock failed.");
    }
  } catch (e) {
    setStatus("Error contacting native host: " + e.message);
  } finally {
    unlockButton.disabled = false;
  }
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
  setStatus("");
});

async function loadEntries() {
  const result = await sendRequest({ action: "list" });
  entriesEl.innerHTML = "";

  if (!result.success) {
    setStatus(result.error || "Couldn't load entries.");
    return;
  }

  if (!result.services || result.services.length === 0) {
    entriesEl.innerHTML = '<div style="color:#888;font-size:12px;">No saved entries yet.</div>';
    return;
  }

  for (const service of result.services) {
    const entryDiv = document.createElement("div");
    entryDiv.className = "entry";

    const serviceLabel = document.createElement("div");
    serviceLabel.className = "service";
    serviceLabel.textContent = service;

    const actions = document.createElement("div");
    actions.className = "actions";

    const fillBtn = document.createElement("button");
    fillBtn.textContent = "Fill on page";
    fillBtn.addEventListener("click", () => handleFill(service));

    const copyBtn = document.createElement("button");
    copyBtn.textContent = "Copy password";
    copyBtn.className = "secondary";
    copyBtn.addEventListener("click", () => handleCopy(service));

    actions.appendChild(fillBtn);
    actions.appendChild(copyBtn);
    entryDiv.appendChild(serviceLabel);
    entryDiv.appendChild(actions);
    entriesEl.appendChild(entryDiv);
  }
}

async function handleFill(service) {
  setStatus("");
  const result = await sendRequest({ action: "getPassword", service });
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
  const result = await sendRequest({ action: "getPassword", service });
  if (!result.success) {
    setStatus(result.error || "Couldn't retrieve credential.");
    return;
  }

  await navigator.clipboard.writeText(result.password);
  setStatus(`Password for '${service}' copied.`);
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
