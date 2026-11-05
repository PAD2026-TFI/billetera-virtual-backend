(() => {
    "use strict";

    const watchedInputs = new WeakSet();
    const keysByEndpoint = new Map();

    function getEndpointKey(path, method) {
        const id = `${method}:${path}`;
        if (!keysByEndpoint.has(id)) {
            keysByEndpoint.set(id, crypto.randomUUID());
        }
        return keysByEndpoint.get(id);
    }

    function setEndpointKey(path, method, key) {
        keysByEndpoint.set(`${method}:${path}`, key);
    }

    function fillParam(path, method, input, value) {
        try {
            const nativeSetter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, "value")?.set;
            if (nativeSetter) {
                nativeSetter.call(input, value);
            } else {
                input.value = value;
            }
        } catch {
            input.value = value;
        }

        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));

        try {
            if (window.ui?.getSystem?.()?.specActions?.changeParam) {
                window.ui.getSystem().specActions.changeParam(
                    [path, method], "Idempotency-Key", "header", value);
            }
        } catch (err) {
            console.warn("Could not update Swagger UI spec state:", err);
        }
    }

    function enhanceOperation(operation) {
        const pathEl = operation.querySelector(".opblock-summary-path");
        const methodEl = operation.querySelector(".opblock-summary-method");
        if (!pathEl || !methodEl) return;

        const path = pathEl.getAttribute("data-path") || pathEl.textContent.trim();
        const method = methodEl.textContent.trim().toLowerCase();

        const row = Array.from(operation.querySelectorAll("tr")).find(r => {
            const paramName = (r.dataset.paramName || "").toLowerCase();
            const textContent = (r.querySelector(".parameter__name")?.textContent || "").toLowerCase();
            return paramName.includes("idempotency") || textContent.includes("idempotency");
        });

        if (!row) return;

        const input = row.querySelector("input");
        if (!input || input.disabled) return;

        const currentKey = getEndpointKey(path, method);

        if (!watchedInputs.has(input)) {
            watchedInputs.add(input);
            input.addEventListener("input", () => {
                if (input.value) {
                    setEndpointKey(path, method, input.value);
                }
            });
        }

        if (!input.value) {
            fillParam(path, method, input, currentKey);
        }

        if (row.querySelector("[data-wallet-key-tools]")) return;

        const tools = document.createElement("div");
        tools.dataset.walletKeyTools = "true";
        tools.style.marginTop = "8px";
        tools.style.display = "flex";
        tools.style.alignItems = "center";
        tools.style.gap = "8px";

        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn";
        button.style.fontSize = "12px";
        button.style.padding = "4px 10px";
        button.style.backgroundColor = "#2563eb";
        button.style.color = "#ffffff";
        button.style.border = "none";
        button.style.borderRadius = "4px";
        button.style.cursor = "pointer";
        button.style.fontWeight = "600";
        button.textContent = "🔄 Generar nueva Idempotency-Key";
        button.title = "Genera un nuevo UUID para enviar una nueva transacción sin colisión";

        const hint = document.createElement("span");
        hint.style.fontSize = "12px";
        hint.style.color = "#666";
        hint.textContent = "Clave generada automáticamente. Clic para renovar.";

        button.addEventListener("click", (e) => {
            e.preventDefault();
            const freshKey = crypto.randomUUID();
            setEndpointKey(path, method, freshKey);
            const currentInput = row.querySelector("input");
            if (currentInput && !currentInput.disabled) {
                fillParam(path, method, currentInput, freshKey);
                hint.textContent = "✅ Nueva clave asignada";
                hint.style.color = "#16a34a";
                setTimeout(() => {
                    hint.textContent = "Clave generada automáticamente. Clic para renovar.";
                    hint.style.color = "#666";
                }, 2500);
            }
        });

        tools.append(button, hint);
        (input.parentElement || row.querySelector("td:last-child")).append(tools);
    }

    function enhance() {
        const operations = document.querySelectorAll(".opblock");
        operations.forEach(enhanceOperation);
    }

    new MutationObserver(enhance).observe(document.documentElement, {
        childList: true, subtree: true, attributes: true, attributeFilter: ["disabled"]
    });
    enhance();
})();
