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
        input.value = value;
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

        const row = Array.from(operation.querySelectorAll("tr")).find(r =>
            r.dataset.paramName === "Idempotency-Key" ||
            r.querySelector(".parameter__name")?.textContent.includes("Idempotency-Key"));

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
        button.style.padding = "4px 8px";
        button.textContent = "🔄 Generar nueva clave";
        button.title = "Generar una nueva clave de idempotencia automáticamente";
        button.addEventListener("click", (e) => {
            e.preventDefault();
            const freshKey = crypto.randomUUID();
            setEndpointKey(path, method, freshKey);
            const currentInput = row.querySelector("input");
            if (currentInput && !currentInput.disabled) {
                fillParam(path, method, currentInput, freshKey);
            }
        });

        const hint = document.createElement("span");
        hint.style.fontSize = "12px";
        hint.style.color = "#666";
        hint.textContent = "Clave generada automáticamente. Hacé clic para renovarla.";

        tools.append(button, hint);
        (row.querySelector("td:last-child") || input.parentElement).append(tools);
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
