// Published (static) copy only. Locally Blazor handles these; online the pages are plain HTML, so
// this does the two things that need a browser: following the gameweek picker, and picking managers
// in the legend to compare them in every graph. (The weekly/cumulative switch is CSS only.)

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

    // Clicking a name adds it to the comparison or drops it; "Show all" drops everyone.
    document.querySelectorAll('.legend-item[data-key]').forEach((button) => {
        const on = item
            ? (button === item) !== (button.getAttribute('aria-pressed') === 'true')
            : false;
        button.classList.toggle('pressed', on);
        button.setAttribute('aria-pressed', on ? 'true' : 'false');
    });

    const keys = new Set(
        [...document.querySelectorAll('.legend-item[aria-pressed="true"]')].map((button) => button.dataset.key));

    // Every graph marks its managers with data-key; the ones not picked fade.
    document.querySelectorAll('.focus-scope').forEach((scope) => {
        scope.classList.toggle('has-focus', keys.size > 0);
        scope.querySelectorAll('[data-key]').forEach((element) => {
            element.classList.toggle('focused', keys.has(element.dataset.key));
        });
    });

    // Draw the picked lines on top, but still under the hover bands.
    document.querySelectorAll('svg.line-chart').forEach((svg) => {
        const firstBand = svg.querySelector('.cross');
        svg.querySelectorAll('.series.focused').forEach((series) => svg.insertBefore(series, firstBand));
    });

    document.querySelectorAll('[data-legend-clear]').forEach((button) => {
        button.hidden = keys.size === 0;
    });
});
