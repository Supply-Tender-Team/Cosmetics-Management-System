const calendar = document.querySelector('[data-closing-calendar]');
const dayButtons = [...calendar.querySelectorAll('button[aria-controls]')];

dayButtons.forEach(button => {
    button.addEventListener('click', () => {
        for (const day of dayButtons) {
            const selected = day === button;
            day.setAttribute('aria-pressed', String(selected));
            document.getElementById(day.getAttribute('aria-controls')).hidden = !selected;
        }
    });
});
