document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-filter-list]").forEach((list) => {
        const key = list.dataset.filterList;
        const search = document.querySelector(`[data-list-search="${key}"]`);
        const status = document.querySelector(`[data-list-status="${key}"]`);
        const count = document.querySelector(`[data-list-count="${key}"]`);
        const empty = document.querySelector(`[data-list-empty="${key}"]`);
        const cards = [...list.querySelectorAll("[data-filter-card]")];

        const applyFilters = () => {
            const query = (search?.value || "").trim().toLocaleLowerCase();
            const selectedStatus = (status?.value || "").trim().toLocaleLowerCase();
            let visible = 0;

            cards.forEach((card) => {
                const matchesQuery = (card.dataset.searchText || "").toLocaleLowerCase().includes(query);
                const matchesStatus = !selectedStatus || (card.dataset.status || "").toLocaleLowerCase() === selectedStatus;
                card.hidden = !(matchesQuery && matchesStatus);
                if (!card.hidden) visible++;
            });

            if (count) count.textContent = `${visible} ${key === "offers" ? "offer" : "invitation"}${visible === 1 ? "" : "s"}`;
            if (empty) empty.hidden = visible !== 0;
        };

        search?.addEventListener("input", applyFilters);
        status?.addEventListener("change", applyFilters);
    });
});
