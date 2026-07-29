Jellyfin Provider Sections v0.1.0.0 - first installable release

Creates home screen sections by streaming provider ("Popular en Crunchyroll", "Novedades en Prime Video"), built from TMDb Discover, resolved against the local library, with requests through Seerr.

Verified against Jellyfin 10.11.11 with Home Screen Sections 2.5.11.0 and File Transformation 2.5.11.0, both required, and Seerr 3.4.0, optional.

- Each row shows the provider logo to the left of its title.
- Titles already in the library open their real Jellyfin page and keep watch state; the rest are drawn as portrait cards with their TMDb artwork, served and cached by the plugin itself so the browser never talks to image.tmdb.org.
- With Jellyfin Enhanced installed, clicking one of those cards opens its Jellyseerr detail modal. Without it the card still renders and the click is inert, rather than navigating to a page for an item the server does not have.
- The rows do not show Jellyfin's hover overlay: on an external card play, mark as played and favourite all act on something that is not there. Every other home row keeps its own overlay untouched.
- Administration page with the provider picker (region, content type, filters), connection tests, diagnostics, and a per-section preview.
- Degrades cleanly: if TMDb or Seerr are unreachable the home screen still loads, and the failure is reported instead of being swallowed.
