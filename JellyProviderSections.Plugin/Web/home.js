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

        /* The class and these two attributes are the contract Jellyfin
           Enhanced's HSS discovery handler listens for. Matching it is what
           makes a click open its detail modal. */
        card.classList.add('discover-card');
        card.setAttribute('data-tmdb-id', String(info.tmdbId));
        card.setAttribute('data-media-type', info.mediaType);

        applyPoster(card, posterUrl(info.tmdbId));

        /* Jellyfin Enhanced listens on document in the capture phase, so when it
           is installed it stops the event before this ever runs. When it is not,
           this keeps the card from navigating to a details page for an item the
           server does not have. */
        card.addEventListener('click', function (event) {
            event.preventDefault();
        });
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
    }

    function start() {
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
