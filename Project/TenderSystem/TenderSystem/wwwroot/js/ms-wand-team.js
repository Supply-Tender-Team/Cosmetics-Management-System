const team = document.querySelector('.wand-team');
const initials = [...team.querySelectorAll('button[aria-controls]')];
const cards = [...team.querySelectorAll('.wand-team__card')];

initials.forEach(button => {
    button.addEventListener('click', () => {
        if (button.getAttribute('aria-expanded') === 'true') return;
        initials.forEach(initial => initial.setAttribute('aria-expanded', String(initial === button)));
        cards.forEach(card => { card.hidden = card.id !== button.getAttribute('aria-controls'); });
    });
});
