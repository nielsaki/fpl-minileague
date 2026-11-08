// Published (static) copy only. Locally Blazor handles these; online the pages are plain HTML, so
// this does the two things that need a browser: following the gameweek picker, and highlighting a
// manager in both graphs when their name is clicked.

document.addEventListener('change', (event) => {
    const select = event.target.closest('select[data-nav]');
    if (select) {
        location.href = new URL(select.value, document.baseURI).href;
    }
});

document.addEventListener('click', (event) => {
    const item = event.target.closest('.legend-item[data-key]');
    const clear = event.target.closest('[data-legend-clear]');
    if (!item && !clear) {
        return;
    }

    // Clicking the highlighted name again, or "Show all", clears the highlight.
    const key = item && item.getAttribute('aria-pressed') !== 'true' ? item.dataset.key : null;

    document.querySelectorAll('.legend-item[data-key]').forEach((button) => {
        const on = button.dataset.key === key;
        button.classList.toggle('pressed', on);
        button.setAttribute('aria-pressed', on ? 'true' : 'false');
    });

    document.querySelectorAll('svg.line-chart').forEach((svg) => {
        svg.classList.toggle('has-focus', key !== null);
        const firstBand = svg.querySelector('.cross');
        svg.querySelectorAll('.series').forEach((series) => {
            const on = series.dataset.key === key;
            series.classList.toggle('focused', on);
            if (on) {
                // Draw the highlighted line on top, but still under the hover bands.
                svg.insertBefore(series, firstBand);
            }
        });
    });

    document.querySelectorAll('[data-legend-clear]').forEach((button) => {
        button.hidden = key === null;
    });
});
