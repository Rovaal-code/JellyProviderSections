Jellyfin Provider Sections v0.1.2.0 - the cards now look like the ones they sit next to

The external cards were carrying the artwork but little else. They now match the discover cards Home Screen Sections builds for its own rows, attribute for attribute, so a stylesheet written for one styles the other.

- The TMDb rating sits in front of the year, as a gold star plus the score (★ 8.5 • 2021). It is fetched once per row rather than once per card, since a row can hold two hundred of them.
- A request button in the hover overlay, same markup as the discover cards: `is="discover-requestbutton"`, the same class list, `data-id` and `data-media-type`. The click goes to this plugin's own request endpoint, so it works off the Seerr connection configured here rather than needing Jellyseerr set up inside Home Screen Sections as well. The icon turns to a tick when the request lands and reports the reason when it does not.
- `discoverCard-movie` / `discoverCard-tv` on the card body, the same per-type hook.
- Card links no longer point at a details page for an item the server does not have. The click was already handled, but the address still showed on hover and opened a broken page on middle click.
- Everything else Jellyfin's card builder puts in the hover overlay is still hidden: on a title that is not in the library, play and mark-as-played act on nothing.
- The integration state in the Secciones tab is now a small card per service with a pulsing green dot, matching JellyNotify rather than approximating it.
