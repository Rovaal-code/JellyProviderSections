/* ================================================================
   Jellyfin Provider Sections - home screen card decoration

   Injected into Jellyfin Web's index.html by File Transformation.

   Why this exists: Home Screen Sections renders artwork-carrying cards only
   for its own three built-in Jellyseerr sections, chosen by section key, so
   third-party sections always go through Jellyfin's standard card builder.
   That builder derives the image URL from the item id, and a title that is not
   in the library has a synthetic id no image endpoint can resolve, so the card
   comes out as a flat placeholder. This fills those cards in from the plugin's
   own poster route.

   It also tags them the way the ecosystem already expects, so clicking one
   opens Jellyfin Enhanced's Jellyseerr detail modal instead of navigating to a
   details page that cannot exist. Jellyfin Enhanced is optional: without it the
   click is simply inert.

   Everything needed is already in the DOM. The synthetic id encodes the TMDb id
   and the content type, so no extra request is made to find out what a card is.
   ================================================================ */

(function () {
    'use strict';

    /* The synthetic id is a GUID built from the TMDb id plus the ASCII marker
       'JPS'. Guid.ToString("N") reorders those bytes, so the marker always lands
       on characters 8 to 16 and the TMDb id on the first 8. See
       LibraryResolver.BuildDeterministicId. */
    var MARKER = '504a0053';
    var MARKER_START = 8;
    var MARKER_END = 16;
    var DECORATED = 'jpsDecorated';
    var RATED = 'jpsRated';
    var ROW_CLASS = 'jps-section-row';
    var STYLE_ID = 'jps-home-style';

    /* Jellyfin's card builder puts a hover overlay on every card: play, mark as
       played, favourite, and a details menu. On an external card all of those
       act on something the server does not have, and on a catalogue row they are
       visual noise either way, so the whole overlay goes. The rows are marked
       from the cards that carry our marker, which also covers the local titles
       sitting next to them, so a row never hovers two different ways.

       Jellyfin Enhanced neutralises the same overlay on the cards it builds
       itself, for the same reason. */
    function injectStyle() {
        if (document.getElementById(STYLE_ID)) {
            return;
        }

        var style = document.createElement('style');
        style.id = STYLE_ID;
        // The container itself stays: it is what hosts the request button. Only
        // the buttons Jellyfin put there go, which on a local title in one of
        // these rows means the hover ends up empty, as intended.
        style.textContent =
            '.' + ROW_CLASS + ' .cardOverlayButton:not(.discover-requestbutton) { display: none !important; }';
        document.head.appendChild(style);
    }

    function markRow(card) {
        var row = card.closest ? card.closest('.verticalSection') : null;
        if (row && !row.classList.contains(ROW_CLASS)) {
            row.classList.add(ROW_CLASS);
        }
    }

    function externalItemInfo(id) {
        if (!id || id.length !== 32 || id.slice(MARKER_START, MARKER_END) !== MARKER) {
            return null;
        }

        var tmdbId = parseInt(id.slice(0, MARKER_START), 16);
        if (!tmdbId) {
            return null;
        }

        // Last byte of the id: 1 for a movie, 2 for a series.
        return {
            tmdbId: tmdbId,
            mediaType: id.charAt(id.length - 1) === '1' ? 'movie' : 'tv'
        };
    }

    function posterUrl(tmdbId) {
        var path = 'JellyProviderSections/Poster/' + tmdbId;

        // ApiClient knows the server's base path; the plain relative URL is only
        // a fallback for the brief window before it exists.
        if (window.ApiClient && typeof window.ApiClient.getUrl === 'function') {
            return window.ApiClient.getUrl(path);
        }

        return '/' + path;
    }

    function applyPoster(card, url) {
        var container = card.querySelector('.cardImageContainer');
        if (!container) {
            return;
        }

        // Load it first: a title TMDb has no poster for answers 404, and the
        // placeholder Jellyfin already drew looks better than an empty box.
        var probe = new Image();
        probe.onload = function () {
            container.style.backgroundImage = 'url(\'' + url + '\')';
            container.classList.add('coveredImage');
            container.classList.remove(
                'defaultCardBackground',
                'defaultCardBackground0',
                'defaultCardBackground1',
                'defaultCardBackground2',
                'defaultCardBackground3',
                'defaultCardBackground4',
                'defaultCardBackground5');

            var placeholder = container.querySelector('.cardImageIcon, .material-icons');
            if (placeholder) {
                placeholder.remove();
            }
        };
        probe.src = url;
    }

    function decorate(card) {
        if (card.dataset[DECORATED]) {
            return;
        }

        var info = externalItemInfo(card.getAttribute('data-id'));
        if (!info) {
            return;
        }

        card.dataset[DECORATED] = '1';
        markRow(card);

        /* The class and these two attributes are the contract Jellyfin
           Enhanced's HSS discovery handler listens for. Matching it is what
           makes a click open its detail modal. */
        card.classList.add('discover-card');
        card.setAttribute('data-tmdb-id', String(info.tmdbId));
        card.setAttribute('data-media-type', info.mediaType);

        // Same per-type hook Home Screen Sections puts on its own discover
        // cards, so anything styling those styles these too.
        var scalable = card.querySelector('.cardScalable');
        if (scalable) {
            scalable.classList.add('discoverCard-' + info.mediaType);
        }

        /* The card builder pointed every link at a details page for an item the
           server does not have. The click is handled elsewhere (the modal, or
           nothing), but the href would still show a bogus URL on hover and open
           a broken page on middle click. */
        card.querySelectorAll('a[href]').forEach(function (anchor) {
            anchor.removeAttribute('href');
            anchor.style.cursor = 'pointer';
        });

        applyPoster(card, posterUrl(info.tmdbId));
        addRequestButton(card, info);

        /* Jellyfin Enhanced listens on document in the capture phase, so when it
           is installed it stops the event before this ever runs. When it is not,
           this keeps the card from navigating to a details page for an item the
           server does not have. */
        card.addEventListener('click', function (event) {
            event.preventDefault();
        });
    }

    /** The owning section's id, which the card builder leaves on the row. */
    function sectionIdFor(card) {
        var row = card.closest ? card.closest('.verticalSection') : null;
        if (!row) {
            return null;
        }

        for (var i = 0; i < row.classList.length; i++) {
            if (/^(jps)?[0-9a-f]{32}$/.test(row.classList[i])) {
                return row.classList[i];
            }
        }

        return null;
    }

    function setButtonIcon(button, name, title) {
        var glyph = button.querySelector('.material-icons');
        if (glyph) {
            // Class only, never text: both together render the glyph twice.
            glyph.classList.remove('add', 'check', 'error', 'hourglass_empty');
            glyph.classList.add(name);
        }
        if (title) {
            button.title = title;
        }
    }

    /**
     * Adds the request button to the hover overlay, with the same markup Home
     * Screen Sections uses on its own discover cards so the two can be styled
     * by one rule. The buttons Jellyfin's card builder put there are dropped by
     * the injected stylesheet: on a title the server does not have, play and
     * mark-as-played act on nothing.
     *
     * The click goes to this plugin's own request endpoint rather than HSS's,
     * which would need Jellyseerr configured inside HSS as well.
     */
    function addRequestButton(card, info) {
        var overlay = card.querySelector('.cardOverlayContainer');
        if (!overlay || overlay.querySelector('.discover-requestbutton')) {
            return;
        }

        var holder = document.createElement('div');
        holder.className = 'cardOverlayButton-br flex';

        var button = document.createElement('button');
        button.setAttribute('is', 'discover-requestbutton');
        button.type = 'button';
        button.setAttribute('data-action', 'none');
        button.className = 'discover-requestbutton cardOverlayButton cardOverlayButton-hover '
            + 'itemAction paper-icon-button-light emby-button';
        button.setAttribute('data-id', String(info.tmdbId));
        button.setAttribute('data-media-type', info.mediaType);
        button.title = 'Solicitar';

        // Left empty on purpose, exactly as Home Screen Sections writes it: the
        // glyph comes from the icon class, and setting the text as well draws
        // the plus twice.
        var glyph = document.createElement('span');
        glyph.className = 'material-icons cardOverlayButtonIcon cardOverlayButtonIcon-hover add';
        glyph.setAttribute('aria-hidden', 'true');
        button.appendChild(glyph);

        button.addEventListener('click', function (event) {
            // Must not reach the card, or the detail modal opens on top of it.
            event.preventDefault();
            event.stopPropagation();

            if (button.disabled) {
                return;
            }

            button.disabled = true;
            setButtonIcon(button, 'hourglass_empty', 'Solicitando...');

            var url = (window.ApiClient && typeof window.ApiClient.getUrl === 'function')
                ? window.ApiClient.getUrl('JellyProviderSections/request')
                : '/JellyProviderSections/request';

            fetch(url, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-MediaBrowser-Token': window.ApiClient ? window.ApiClient.accessToken() : ''
                },
                body: JSON.stringify({
                    tmdbId: info.tmdbId,
                    contentType: info.mediaType === 'movie' ? 'Movie' : 'Series',
                    sectionId: sectionIdFor(card),
                    allSeasons: true
                })
            })
                .then(function (r) { return r.json().then(function (b) { return { ok: r.ok, body: b }; }); })
                .then(function (r) {
                    if (r.ok) {
                        setButtonIcon(button, 'check', r.body.message || 'Solicitado');
                        return;
                    }
                    button.disabled = false;
                    setButtonIcon(button, 'error', r.body.message || 'No se pudo solicitar');
                })
                .catch(function () {
                    button.disabled = false;
                    setButtonIcon(button, 'error', 'No se pudo solicitar');
                });
        });

        holder.appendChild(button);
        overlay.appendChild(holder);
    }

    /**
     * Puts the TMDb rating in front of the year, as a gold star plus the score,
     * matching the discover cards Home Screen Sections builds for its own rows.
     *
     * Fetched one row at a time rather than one card at a time: a row can hold
     * two hundred of them. The server only knows the titles it has already
     * served, so anything it does not recognise simply keeps the plain year.
     */
    function applyRatings(cards) {
        var pending = cards.filter(function (c) { return !c.dataset[RATED]; });
        if (!pending.length) {
            return;
        }

        var ids = pending.map(function (c) { return c.getAttribute('data-tmdb-id'); });
        pending.forEach(function (c) { c.dataset[RATED] = '1'; });

        var url = (window.ApiClient && typeof window.ApiClient.getUrl === 'function')
            ? window.ApiClient.getUrl('JellyProviderSections/Ratings')
            : '/JellyProviderSections/Ratings';

        fetch(url + '?ids=' + encodeURIComponent(ids.join(',')))
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (ratings) {
                if (!ratings) {
                    return;
                }

                pending.forEach(function (card) {
                    var rating = ratings[card.getAttribute('data-tmdb-id')];
                    var line = card.querySelector('.cardText-secondary bdi');
                    if (!rating || !line || line.querySelector('.jps-card-star')) {
                        return;
                    }

                    var star = document.createElement('span');
                    star.className = 'material-icons jps-card-star';
                    star.setAttribute('aria-hidden', 'true');
                    star.textContent = 'star';
                    star.style.cssText = 'font-size:14px;vertical-align:middle;color:#FFD700;';

                    var score = document.createElement('span');
                    score.textContent = ' ' + rating.toFixed(1) + ' • ';

                    line.insertBefore(score, line.firstChild);
                    line.insertBefore(star, line.firstChild);
                });
            })
            .catch(function () { /* the year alone is a fine fallback */ });
    }

    function scan(root) {
        if (!root || typeof root.querySelectorAll !== 'function') {
            return;
        }

        if (root.classList && root.classList.contains('card')) {
            decorate(root);
        }

        var cards = root.querySelectorAll('.card[data-id]');
        for (var i = 0; i < cards.length; i++) {
            decorate(cards[i]);
        }

        // One request for everything this pass decorated, not one per card. The
        // root itself counts: the observer hands over one card at a time, and
        // querySelectorAll only ever looks at descendants.
        var decorated = [].slice.call(root.querySelectorAll('.card.discover-card[data-tmdb-id]'));
        if (root.classList && root.classList.contains('discover-card') && root.hasAttribute('data-tmdb-id')) {
            decorated.push(root);
        }

        applyRatings(decorated);
    }

    function start() {
        injectStyle();
        scan(document.body);

        var Observer = window.MutationObserver || window.WebKitMutationObserver;
        if (!Observer) {
            return;
        }

        // Rows arrive asynchronously and re-render when the user changes tabs,
        // so a single pass at load time would miss almost everything.
        new Observer(function (mutations) {
            for (var i = 0; i < mutations.length; i++) {
                var added = mutations[i].addedNodes;
                for (var j = 0; j < added.length; j++) {
                    if (added[j].nodeType === 1) {
                        scan(added[j]);
                    }
                }
            }
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start);
    } else {
        start();
    }
})();
