JellyProvider Sections v0.1.4.0 - the real TMDb and Seerr marks

The integration state in the Secciones tab now shows each service's own logo instead of a stand-in glyph.

Both are bundled with the plugin and served from it, rather than linked from a CDN: the administration page keeps working on a server with no route to the internet, and opening it does not tell a third party who is looking at it. Same reason the provider logos and the card artwork are already cached and served locally.
