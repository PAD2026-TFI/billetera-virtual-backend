(() => {
    "use strict";
    // Una clave por intención. Execute conserva la clave; Nueva clave inicia otra.
    const keys = new Map();
    const watched = new WeakSet();
    function fill(path, value) {
        // Actualizo el estado de Swagger, no sólo el valor visible del input.
        window.ui.getSystem().specActions.changeParam(
            [path, "post"], "Idempotency-Key", "header", value);
    }
    function enhance() {
        if (typeof document === "undefined" || !document.documentElement) return;
        for (const path of ["/api/transfers", "/api/payments"]) enhanceOperation(path);
    }
    function enhanceOperation(path) {
        const summary = document.querySelector(`.opblock-summary-path[data-path="${path}"]`);
        const operation = summary?.closest(".opblock");
        if (!keys.has(path)) keys.set(path, crypto.randomUUID());
        if (!operation) return;
        const row = Array.from(operation.querySelectorAll("tr")).find(r =>
            r.dataset.paramName === "Idempotency-Key" ||
            r.querySelector(".parameter__name")?.textContent.includes("Idempotency-Key"));
        const input = row?.querySelector("input");
        if (!input || input.disabled) return;
        if (!watched.has(input)) {
            watched.add(input);
            input.addEventListener("input", () => { if (input.value) keys.set(path, input.value); });
        }
        if (!input.value) fill(path, keys.get(path));
        if (row.querySelector("[data-wallet-key-tools]")) return;
        const tools = document.createElement("div");
        tools.dataset.walletKeyTools = "true";
        tools.style.marginTop = "8px";
        const button = document.createElement("button");
        button.type = "button";
        button.className = "btn";
        button.textContent = "Nueva clave";
        button.addEventListener("click", () => {
            keys.set(path, crypto.randomUUID());
            const current = row.querySelector("input");
            if (current && !current.disabled) fill(path, keys.get(path));
        });
        const hint = document.createElement("p");
        hint.textContent = "Conservá la clave para reintentar. Generá una nueva para otra operación.";
        tools.append(button, hint);
        (row.querySelector("td:last-child") || input.parentElement).append(tools);
    }
    new MutationObserver(enhance).observe(document.documentElement, {
        childList: true, subtree: true, attributes: true, attributeFilter: ["disabled"]
    });
    enhance();
})();
