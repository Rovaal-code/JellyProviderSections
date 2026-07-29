/* ================================================================
   Jellyfin Provider Sections - configuration page logic

   Vanilla JS, no bundler, served as an embedded resource through
   WebAssetsController (same pattern as JellyNotify).

   Two hard rules followed throughout this file:
     1. All user/server-provided strings go in via textContent or
        setAttribute, never innerHTML. The plugin's whole security
        story rests on never trusting a section name as markup
        (see docs/research/10-security-and-licensing.md).
     2. Every async region renders a spinner while pending and an
        explicit empty/error state otherwise. A blank panel reads as
        a hung page.

   The backend is written in parallel with this file, so every call is
   individually guarded: a missing endpoint degrades that one panel to
   a readable error instead of taking down the page.
   ================================================================ */

(function () {
    'use strict';

    const API_BASE = '/JellyProviderSections';
    const PAGE_ID = 'jps-config-page';

    /** Sections currently loaded, keyed by id for quick lookup on actions. */
    let sections = [];
    /** Ids of cards the admin has expanded. Several may be open at once. */
    const expandedIds = new Set();
    /** Cached TMDb reference data so switching content type does not refetch. */
    const cache = { regions: null, providers: new Map() };
    /** Section being edited in the form, or null when creating a new one. */
    let editingId = null;
    /** Provider chosen in the form (id, name, logoPath). */
    let chosenProvider = null;

    const CONTENT_TYPES = [
        { value: 'Movie', label: 'Películas' },
        { value: 'Series', label: 'Series' }
    ];

    const SORT_OPTIONS = [
        { value: 'Popularity', label: 'Popularidad' },
        { value: 'RatingDesc', label: 'Mejor valoradas' },
        { value: 'ReleaseDateDesc', label: 'Más recientes' },
        { value: 'TitleAsc', label: 'Título (A-Z)' }
    ];

    const SYNC_RESULT_LABELS = {
        NeverRun: 'Sin ejecutar todavía',
        Success: 'Correcta',
        PartialFailure: 'Parcial, con incidencias',
        Failure: 'Fallida'
    };

    // ─── API helpers ──────────────────────────────────────────────

    /**
     * Authenticated request against this plugin's API using the current
     * Jellyfin session token. Throws with a readable message so callers can
     * surface the real reason instead of a generic failure.
     */
    async function api(path, options = {}) {
        const response = await fetch(API_BASE + path, {
            ...options,
            headers: {
                'Content-Type': 'application/json',
                'X-MediaBrowser-Token': window.ApiClient ? ApiClient.accessToken() : '',
                ...(options.headers || {})
            }
        });

        if (!response.ok) {
            let detail = '';
            try {
                const body = await response.text();
                if (body) {
                    try {
                        const parsed = JSON.parse(body);
                        detail = parsed.message || parsed.title || body;
                    } catch (e) {
                        detail = body;
                    }
                }
            } catch (e) {
                // Body already consumed or unreadable, status alone will do.
            }
            const suffix = detail ? ': ' + detail.slice(0, 300) : '';
            throw new Error('Error ' + response.status + suffix);
        }

        if (response.status === 204) {
            return null;
        }

        const text = await response.text();
        return text ? JSON.parse(text) : null;
    }

    // ─── Small DOM helpers ────────────────────────────────────────

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined && text !== null) {
            node.textContent = String(text);
        }
        return node;
    }

    function icon(name, extraClass) {
        const span = el('span', 'material-icons' + (extraClass ? ' ' + extraClass : ''), name);
        span.setAttribute('aria-hidden', 'true');
        return span;
    }

    function clear(node) {
        while (node.firstChild) {
            node.removeChild(node.firstChild);
        }
    }

    function byId(id) {
        return document.getElementById(id);
    }

    /** Replaces a container's contents with a centered spinner state. */
    function showLoading(container, message) {
        clear(container);
        const state = el('div', 'jps-state');
        state.appendChild(el('div', 'jps-spinner'));
        state.appendChild(el('p', 'jps-state-text', message || 'Cargando...'));
        container.appendChild(state);
    }

    /** Replaces a container's contents with an error state plus a retry button. */
    function showError(container, message, onRetry) {
        clear(container);
        const state = el('div', 'jps-state');
        state.appendChild(icon('error_outline', 'jps-state-icon'));
        state.appendChild(el('div', 'jps-state-title', 'No se pudo cargar'));
        state.appendChild(el('p', 'jps-state-text', message));
        if (onRetry) {
            const retry = el('button', 'jps-btn jps-btn-primary');
            retry.type = 'button';
            retry.appendChild(icon('refresh', 'jps-btn-icon'));
            retry.appendChild(el('span', null, 'Reintentar'));
            retry.addEventListener('click', onRetry);
            state.appendChild(retry);
        }
        container.appendChild(state);
    }

    /** Writes a short inline result message (ok / error / warn) into a slot. */
    function setResult(node, message, kind) {
        if (!node) {
            return;
        }
        node.className = 'jps-result' + (kind ? ' jps-result-' + kind : '');
        node.textContent = message;
        node.classList.remove('jps-hidden');
    }

    function hide(node) {
        if (node) {
            node.classList.add('jps-hidden');
        }
    }

    /** Enum values may arrive as strings or ordinals depending on serializer setup. */
    function enumName(value, options) {
        if (typeof value === 'number') {
            const match = options[value];
            return match ? match.value : options[0].value;
        }
        return value || options[0].value;
    }

    function labelFor(value, options) {
        const name = enumName(value, options);
        const found = options.find(o => o.value === name);
        return found ? found.label : name;
    }

    function formatDate(iso) {
        if (!iso) {
            return 'Nunca';
        }
        const date = new Date(iso);
        if (isNaN(date.getTime())) {
            return String(iso);
        }
        return date.toLocaleString();
    }

    function logoUrl(section) {
        if (!section || !section.tmdbProviderId) {
            return null;
        }
        return API_BASE + '/Logo/' + encodeURIComponent(section.tmdbProviderId);
    }

    // ─── Section card rendering ───────────────────────────────────

    function renderSections() {
        const list = byId('jps-sections-list');
        if (!list) {
            return;
        }

        clear(list);

        const counter = byId('jps-section-count');
        if (counter) {
            counter.textContent = sections.length === 1
                ? '1 sección configurada'
                : sections.length + ' secciones configuradas';
        }

        if (!sections.length) {
            const state = el('div', 'jps-state');
            state.appendChild(icon('video_library', 'jps-state-icon'));
            state.appendChild(el('div', 'jps-state-title', 'Todavía no hay secciones'));
            state.appendChild(el('p', 'jps-state-text',
                'Crea tu primera sección para que aparezca en la página principal de Jellyfin. Por ejemplo: "Popular en Crunchyroll" o "Novedades en Prime Video".'));
            const create = el('button', 'jps-btn jps-btn-primary');
            create.type = 'button';
            create.appendChild(icon('add', 'jps-btn-icon'));
            create.appendChild(el('span', null, 'Crear sección'));
            create.addEventListener('click', () => openForm(null));
            state.appendChild(create);
            list.appendChild(state);
            return;
        }

        sections.forEach(section => list.appendChild(renderCard(section)));
    }

    function renderCard(section) {
        const card = el('div', 'jps-section-card' + (section.enabled ? '' : ' jps-disabled'));
        card.dataset.sectionId = section.id;

        const bodyId = 'jps-body-' + section.id;
        const isOpen = expandedIds.has(section.id);

        // ── Closed state header (a real button: keyboard support for free) ──
        const head = el('button', 'jps-card-head');
        head.type = 'button';
        head.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
        head.setAttribute('aria-controls', bodyId);

        const url = logoUrl(section);
        if (url) {
            const img = el('img', 'jps-card-logo');
            img.src = url;
            img.alt = '';
            img.setAttribute('aria-hidden', 'true');
            img.addEventListener('error', () => {
                const fallback = el('span', 'jps-card-logo-fallback');
                fallback.appendChild(icon('movie'));
                img.replaceWith(fallback);
            });
            head.appendChild(img);
        } else {
            const fallback = el('span', 'jps-card-logo-fallback');
            fallback.appendChild(icon('movie'));
            head.appendChild(fallback);
        }

        const name = el('span', 'jps-card-name', section.displayName || 'Sección sin nombre');
        name.title = section.displayName || '';
        head.appendChild(name);

        const metaParts = [
            section.providerDisplayName || ('Proveedor ' + section.tmdbProviderId),
            section.region || 'Sin región',
            labelFor(section.contentType, CONTENT_TYPES),
            labelFor(section.sortBy, SORT_OPTIONS),
            (section.maxItems || 0) + ' elementos'
        ];
        const meta = el('span', 'jps-card-meta', metaParts.join(' · '));
        meta.title = metaParts.join(' · ');
        head.appendChild(meta);

        const status = el('span', 'jps-card-status');
        status.appendChild(statusItem(
            'Home Sections',
            section.homeSectionsRegistered ? 'registrado' : 'no registrado',
            section.homeSectionsRegistered ? 'ok' : 'warn'));
        status.appendChild(statusItem(
            'Seerr',
            section.seerrConnected ? 'conectado' : 'no conectado',
            section.seerrConnected ? 'ok' : 'warn'));
        if (section.lastError) {
            status.appendChild(statusItem('Último error', 'revisar detalle', 'error'));
        }
        head.appendChild(status);

        const actions = el('span', 'jps-card-actions');
        const badge = el('span',
            'jps-badge ' + (section.enabled ? 'jps-badge-active' : 'jps-badge-inactive'),
            section.enabled ? 'Activa' : 'Inactiva');
        actions.appendChild(badge);
        actions.appendChild(icon('expand_more', 'jps-chevron'));
        head.appendChild(actions);

        head.addEventListener('click', () => toggleCard(section.id));
        card.appendChild(head);

        // ── Expanded body ──
        const wrap = el('div', 'jps-card-body-wrap' + (isOpen ? ' jps-open' : ''));
        wrap.id = bodyId;
        const inner = el('div', 'jps-card-body-inner');
        inner.appendChild(renderCardBody(section));
        wrap.appendChild(inner);
        card.appendChild(wrap);

        return card;
    }

    function statusItem(label, value, kind) {
        const item = el('span', 'jps-status-item');
        item.appendChild(el('span', 'jps-dot jps-dot-' + kind));
        item.appendChild(el('span', null, label + ': ' + value));
        return item;
    }

    function field(label, value, mono) {
        const wrapper = el('div', 'jps-field');
        wrapper.appendChild(el('span', 'jps-field-label', label));
        wrapper.appendChild(el('span', 'jps-field-value' + (mono ? ' jps-mono' : ''),
            value === null || value === undefined || value === '' ? '–' : value));
        return wrapper;
    }

    function subhead(label) {
        const head = el('div', 'jps-subhead');
        head.appendChild(el('span', 'jps-subhead-label', label));
        return head;
    }

    function renderCardBody(section) {
        const body = el('div', 'jps-card-body');

        // 1. Identidad
        body.appendChild(subhead('Identidad'));
        const identity = el('div', 'jps-detail-grid');
        const idField = el('div', 'jps-field');
        idField.appendChild(el('span', 'jps-field-label', 'Identificador interno'));
        const idRow = el('div', 'jps-copy-row');
        idRow.appendChild(el('span', 'jps-field-value jps-mono', section.id));
        const copyBtn = el('button', 'jps-icon-btn');
        copyBtn.type = 'button';
        copyBtn.setAttribute('aria-label', 'Copiar identificador');
        copyBtn.title = 'Copiar identificador';
        copyBtn.appendChild(icon('content_copy'));
        copyBtn.addEventListener('click', ev => {
            ev.stopPropagation();
            copyText(section.id, copyBtn);
        });
        idRow.appendChild(copyBtn);
        idField.appendChild(idRow);
        identity.appendChild(idField);
        identity.appendChild(field('Nombre', section.displayName));
        identity.appendChild(field('Creada', formatDate(section.createdUtc)));
        identity.appendChild(field('Modificada', section.modifiedUtc ? formatDate(section.modifiedUtc) : 'Sin cambios'));
        body.appendChild(identity);

        // 2. Proveedor y alcance
        body.appendChild(subhead('Proveedor y alcance'));
        const scope = el('div', 'jps-detail-grid');
        scope.appendChild(field('Proveedor', section.providerDisplayName));
        scope.appendChild(field('ID TMDb del proveedor', section.tmdbProviderId, true));
        scope.appendChild(field('Región', section.region));
        scope.appendChild(field('Idioma de metadatos', section.metadataLanguage));
        scope.appendChild(field('Tipo de contenido', labelFor(section.contentType, CONTENT_TYPES)));
        body.appendChild(scope);

        // 3. Filtros
        body.appendChild(subhead('Filtros'));
        const filters = el('div', 'jps-detail-grid');
        filters.appendChild(field('Ordenación', labelFor(section.sortBy, SORT_OPTIONS)));
        filters.appendChild(field('Máximo de elementos', section.maxItems));
        filters.appendChild(field('Géneros incluidos', (section.includeGenreIds || []).join(', ')));
        filters.appendChild(field('Géneros excluidos', (section.excludeGenreIds || []).join(', ')));
        filters.appendChild(field('Idioma original', section.originalLanguage));
        filters.appendChild(field('País de origen', section.originCountry));
        filters.appendChild(field('Fecha mínima', section.minDate));
        filters.appendChild(field('Fecha máxima', section.maxDate));
        filters.appendChild(field('Valoración mínima', section.minRating));
        filters.appendChild(field('Votos mínimos', section.minVoteCount));
        filters.appendChild(field('Contenido adulto', section.includeAdult ? 'Incluido' : 'Excluido'));
        body.appendChild(filters);

        // 4. Consulta y resultados
        body.appendChild(subhead('Consulta y resultados'));
        const queryBox = el('div', 'jps-query-box jps-hidden');
        queryBox.id = 'jps-query-' + section.id;
        body.appendChild(queryBox);
        const queryResult = el('div', 'jps-result jps-hidden');
        queryResult.id = 'jps-queryresult-' + section.id;
        body.appendChild(queryResult);
        const previewBox = el('div', 'jps-preview-grid jps-hidden');
        previewBox.id = 'jps-preview-' + section.id;
        body.appendChild(previewBox);

        // 5. Caché
        body.appendChild(subhead('Caché y sincronización'));
        const cacheGrid = el('div', 'jps-detail-grid');
        cacheGrid.appendChild(field('Duración de caché', (section.cacheDurationMinutes || 0) + ' minutos'));
        cacheGrid.appendChild(field('Última sincronización', formatDate(section.lastSyncUtc)));
        cacheGrid.appendChild(field('Resultado', SYNC_RESULT_LABELS[enumName(section.lastSyncResult, [
            { value: 'NeverRun' }, { value: 'Success' }, { value: 'PartialFailure' }, { value: 'Failure' }
        ])] || 'Desconocido'));
        cacheGrid.appendChild(field('Solicitudes', section.requestsEnabled ? 'Activadas' : 'Desactivadas'));
        body.appendChild(cacheGrid);

        // 6. Diagnóstico
        if (section.lastError) {
            body.appendChild(subhead('Diagnóstico'));
            const err = el('div', 'jps-result jps-result-error', section.lastError);
            body.appendChild(err);
        }

        // 7. Acciones
        body.appendChild(subhead('Acciones'));
        const actionRow = el('div', 'jps-btn-row');
        actionRow.appendChild(actionButton('edit', 'Editar', () => openForm(section.id)));
        actionRow.appendChild(actionButton('content_copy', 'Duplicar', () => duplicateSection(section.id)));
        actionRow.appendChild(actionButton(
            section.enabled ? 'toggle_off' : 'toggle_on',
            section.enabled ? 'Desactivar' : 'Activar',
            () => toggleEnabled(section)));
        actionRow.appendChild(actionButton('play_arrow', 'Probar consulta', btn => testQuery(section.id, btn)));
        actionRow.appendChild(actionButton('visibility', 'Previsualizar', btn => previewSection(section.id, btn)));
        actionRow.appendChild(actionButton('cleaning_services', 'Limpiar caché', btn => clearCache(section.id, btn)));

        const del = actionButton('delete', 'Eliminar', () => deleteSection(section));
        del.classList.add('jps-btn-danger');
        actionRow.appendChild(del);

        body.appendChild(actionRow);
        return body;
    }

    /**
     * Buttons inside the expanded body must not bubble their click up to the
     * card header, which would collapse the card the admin just acted on.
     */
    function actionButton(iconName, label, handler) {
        const btn = el('button', 'jps-btn');
        btn.type = 'button';
        btn.appendChild(icon(iconName, 'jps-btn-icon'));
        btn.appendChild(el('span', null, label));
        btn.addEventListener('click', ev => {
            ev.stopPropagation();
            handler(btn);
        });
        return btn;
    }

    function copyText(text, btn) {
        const done = () => {
            const original = btn.querySelector('.material-icons');
            if (original) {
                original.textContent = 'check';
                setTimeout(() => { original.textContent = 'content_copy'; }, 1500);
            }
        };
        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done).catch(() => {});
        }
    }

    function toggleCard(id) {
        const card = document.querySelector('[data-section-id="' + CSS.escape(id) + '"]');
        if (!card) {
            return;
        }
        const head = card.querySelector('.jps-card-head');
        const wrap = card.querySelector('.jps-card-body-wrap');
        const open = expandedIds.has(id);

        if (open) {
            expandedIds.delete(id);
            wrap.classList.remove('jps-open');
            head.setAttribute('aria-expanded', 'false');
        } else {
            expandedIds.add(id);
            wrap.classList.add('jps-open');
            head.setAttribute('aria-expanded', 'true');
        }
    }

    function collapseAll() {
        expandedIds.clear();
        document.querySelectorAll('.jps-card-body-wrap.jps-open').forEach(w => w.classList.remove('jps-open'));
        document.querySelectorAll('.jps-card-head[aria-expanded="true"]')
            .forEach(h => h.setAttribute('aria-expanded', 'false'));
    }

    // ─── Section actions ──────────────────────────────────────────

    async function loadSections() {
        const list = byId('jps-sections-list');
        showLoading(list, 'Cargando secciones...');
        try {
            const data = await api('/Admin/sections');
            sections = Array.isArray(data) ? data : (data && data.items) || [];
            renderSections();
        } catch (err) {
            showError(list, err.message, loadSections);
        }
    }

    async function toggleEnabled(section) {
        const path = '/Admin/sections/' + encodeURIComponent(section.id) + (section.enabled ? '/disable' : '/enable');
        try {
            await api(path, { method: 'POST' });
            await loadSections();
        } catch (err) {
            alert('No se pudo cambiar el estado de la sección. ' + err.message);
        }
    }

    async function duplicateSection(id) {
        try {
            await api('/Admin/sections/' + encodeURIComponent(id) + '/duplicate', { method: 'POST' });
            await loadSections();
        } catch (err) {
            alert('No se pudo duplicar la sección. ' + err.message);
        }
    }

    async function deleteSection(section) {
        const name = section.displayName || 'esta sección';
        if (!window.confirm('¿Eliminar "' + name + '"? Esta acción no se puede deshacer y la sección dejará de aparecer en la página principal.')) {
            return;
        }
        try {
            await api('/Admin/sections/' + encodeURIComponent(section.id), { method: 'DELETE' });
            expandedIds.delete(section.id);
            await loadSections();
        } catch (err) {
            alert('No se pudo eliminar la sección. ' + err.message);
        }
    }

    async function testQuery(id, btn) {
        const box = byId('jps-query-' + id);
        const result = byId('jps-queryresult-' + id);
        btn.disabled = true;
        setResult(result, 'Consultando TMDb...', null);
        try {
            const data = await api('/Admin/sections/' + encodeURIComponent(id) + '/test-query', { method: 'POST' });
            if (data && data.query) {
                box.textContent = data.query;
                box.classList.remove('jps-hidden');
            }
            // The server answers { query, count, items }, where items is capped
            // at 12 for the preview: count is the real total.
            const total = data && (data.count !== undefined ? data.count : (data.items || []).length);
            const pages = data && data.pagesFetched !== undefined ? data.pagesFetched : null;
            let message = 'Consulta correcta. ' + (total !== undefined && total !== null ? total : 0) + ' resultados';
            if (pages !== null) {
                message += ' · ' + pages + ' páginas consultadas';
            }
            setResult(result, message, total ? 'ok' : 'warn');
        } catch (err) {
            setResult(result, 'La consulta falló. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    async function previewSection(id, btn) {
        const grid = byId('jps-preview-' + id);
        const result = byId('jps-queryresult-' + id);
        btn.disabled = true;
        clear(grid);
        grid.classList.remove('jps-hidden');
        const spinner = el('div', 'jps-state');
        spinner.appendChild(el('div', 'jps-spinner'));
        spinner.appendChild(el('p', 'jps-state-text', 'Generando previsualización...'));
        grid.appendChild(spinner);

        try {
            const data = await api('/Admin/sections/' + encodeURIComponent(id) + '/preview');
            const items = (data && (data.items || data.Items)) || [];
            clear(grid);
            if (!items.length) {
                grid.classList.add('jps-hidden');
                setResult(result, 'La previsualización no devolvió ningún elemento. Revisa los filtros de la sección.', 'warn');
                return;
            }
            items.forEach(item => {
                const cell = el('div', 'jps-preview-item');
                const poster = el('img', 'jps-preview-poster');
                poster.src = item.posterUrl || item.PosterUrl || '';
                poster.alt = '';
                poster.setAttribute('aria-hidden', 'true');
                poster.addEventListener('error', () => { poster.style.visibility = 'hidden'; });
                cell.appendChild(poster);
                cell.appendChild(el('span', 'jps-preview-title', item.name || item.Name || 'Sin título'));
                const isLocal = item.isLocal !== undefined ? item.isLocal : item.IsLocal;
                cell.appendChild(el('span', 'jps-preview-tag' + (isLocal ? ' jps-local' : ''),
                    isLocal ? 'En la biblioteca' : 'Externo'));
                grid.appendChild(cell);
            });
            hide(result);
        } catch (err) {
            clear(grid);
            grid.classList.add('jps-hidden');
            setResult(result, 'No se pudo previsualizar. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    async function clearCache(id, btn) {
        const result = byId('jps-queryresult-' + id);
        btn.disabled = true;
        try {
            await api('/Admin/sections/' + encodeURIComponent(id) + '/clear-cache', { method: 'POST' });
            setResult(result, 'Caché limpiada. La próxima carga volverá a consultar TMDb.', 'ok');
        } catch (err) {
            setResult(result, 'No se pudo limpiar la caché. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    // ─── Section form ─────────────────────────────────────────────

    function openForm(id) {
        editingId = id;
        chosenProvider = null;

        const section = id ? sections.find(s => s.id === id) : null;
        const panel = byId('jps-form-panel');
        const title = byId('jps-form-title');

        title.textContent = section ? 'Editar sección' : 'Nueva sección';

        setValue('jps-f-name', section ? section.displayName : '');
        setValue('jps-f-contenttype', section ? enumName(section.contentType, CONTENT_TYPES) : 'Series');
        setValue('jps-f-region', section ? section.region : 'ES');
        setValue('jps-f-language', section ? section.metadataLanguage : 'es-ES');
        setValue('jps-f-sortby', section ? enumName(section.sortBy, SORT_OPTIONS) : 'Popularity');
        setValue('jps-f-maxitems', section ? section.maxItems : 20);
        setValue('jps-f-includegenres', section ? (section.includeGenreIds || []).join(',') : '');
        setValue('jps-f-excludegenres', section ? (section.excludeGenreIds || []).join(',') : '');
        setValue('jps-f-origlang', section ? section.originalLanguage || '' : '');
        setValue('jps-f-origcountry', section ? section.originCountry || '' : '');
        setValue('jps-f-mindate', section ? section.minDate || '' : '');
        setValue('jps-f-maxdate', section ? section.maxDate || '' : '');
        setValue('jps-f-minrating', section ? (section.minRating !== null && section.minRating !== undefined ? section.minRating : '') : '');
        setValue('jps-f-minvotes', section ? section.minVoteCount : 50);
        setValue('jps-f-cache', section ? section.cacheDurationMinutes : 360);
        setChecked('jps-f-adult', section ? section.includeAdult : false);
        setChecked('jps-f-requests', section ? section.requestsEnabled : true);
        setChecked('jps-f-enabled', section ? section.enabled : true);

        if (section && section.tmdbProviderId) {
            chosenProvider = {
                provider_id: section.tmdbProviderId,
                provider_name: section.providerDisplayName,
                logo_path: section.providerLogoPath
            };
        }
        renderChosenProvider();

        hide(byId('jps-form-result'));
        panel.classList.remove('jps-hidden');
        panel.scrollIntoView({ behavior: 'smooth', block: 'start' });

        loadRegions().then(() => loadProviders());
    }

    function closeForm() {
        editingId = null;
        chosenProvider = null;
        byId('jps-form-panel').classList.add('jps-hidden');
    }

    function setValue(id, value) {
        const node = byId(id);
        if (node) {
            node.value = value === null || value === undefined ? '' : value;
        }
    }

    function getValue(id) {
        const node = byId(id);
        return node ? node.value.trim() : '';
    }

    function setChecked(id, value) {
        const node = byId(id);
        if (node) {
            node.checked = !!value;
        }
    }

    function isChecked(id) {
        const node = byId(id);
        return node ? node.checked : false;
    }

    function parseIntList(raw) {
        if (!raw) {
            return [];
        }
        return raw.split(',')
            .map(part => parseInt(part.trim(), 10))
            .filter(n => !isNaN(n));
    }

    async function loadRegions() {
        const select = byId('jps-f-region');
        if (!select) {
            return;
        }
        if (cache.regions) {
            fillRegions(cache.regions);
            return;
        }
        try {
            const data = await api('/Admin/tmdb/regions');
            cache.regions = Array.isArray(data) ? data : (data && data.results) || [];
            fillRegions(cache.regions);
        } catch (err) {
            // Region list is a convenience: keep whatever value is typed/saved so
            // the admin is not blocked by a TMDb outage.
            const current = select.value;
            clear(select);
            const opt = el('option', null, current || 'ES');
            opt.value = current || 'ES';
            select.appendChild(opt);
            setResult(byId('jps-form-result'),
                'No se pudo cargar la lista de regiones de TMDb. Revisa la conexión en la pestaña Conexiones. ' + err.message,
                'warn');
        }
    }

    function fillRegions(regions) {
        const select = byId('jps-f-region');
        const previous = select.value;
        clear(select);
        // GET /Admin/tmdb/regions projects TMDb's snake_case into { code, name,
        // englishName }, so read that and not the raw TMDb field names.
        regions.forEach(r => {
            const label = r.name || r.englishName || r.code;
            const opt = el('option', null, label + ' (' + r.code + ')');
            opt.value = r.code;
            select.appendChild(opt);
        });
        if (previous) {
            select.value = previous;
        }
        if (!select.value && regions.length) {
            select.value = regions[0].code;
        }
    }

    async function loadProviders() {
        const container = byId('jps-provider-list');
        if (!container) {
            return;
        }
        const region = getValue('jps-f-region') || 'ES';
        const contentType = getValue('jps-f-contenttype') || 'Series';
        const key = contentType + '|' + region;

        if (cache.providers.has(key)) {
            renderProviders(cache.providers.get(key));
            return;
        }

        showLoading(container, 'Cargando proveedores...');
        try {
            const data = await api('/Admin/tmdb/providers?region=' + encodeURIComponent(region) +
                '&contentType=' + encodeURIComponent(contentType));
            const list = Array.isArray(data) ? data : (data && data.results) || [];
            cache.providers.set(key, list);
            renderProviders(list);
        } catch (err) {
            showError(container, 'No se pudieron cargar los proveedores. ' + err.message, loadProviders);
        }
    }

    function renderProviders(providers) {
        const container = byId('jps-provider-list');
        clear(container);

        // GET /Admin/tmdb/providers projects TMDb's snake_case into { id, name,
        // logoPath }, so read that and not the raw TMDb field names.
        const filter = getValue('jps-f-providersearch').toLowerCase();
        const visible = filter
            ? providers.filter(p => (p.name || '').toLowerCase().includes(filter))
            : providers;

        if (!visible.length) {
            const state = el('div', 'jps-state');
            state.appendChild(icon('search_off', 'jps-state-icon'));
            state.appendChild(el('p', 'jps-state-text',
                providers.length
                    ? 'Ningún proveedor coincide con la búsqueda.'
                    : 'No hay proveedores disponibles para esta región y tipo de contenido.'));
            container.appendChild(state);
            return;
        }

        visible.slice(0, 200).forEach(p => {
            const id = p.id;
            const name = p.name || ('Proveedor ' + id);
            const logo = p.logoPath;

            const option = el('button', 'jps-provider-option' +
                (chosenProvider && chosenProvider.provider_id === id ? ' jps-selected' : ''));
            option.type = 'button';
            option.setAttribute('aria-label', 'Elegir proveedor ' + name);

            const img = el('img');
            img.src = API_BASE + '/Logo/' + encodeURIComponent(id);
            img.alt = '';
            img.setAttribute('aria-hidden', 'true');
            img.addEventListener('error', () => { img.style.visibility = 'hidden'; });
            option.appendChild(img);
            option.appendChild(el('span', null, name));

            option.addEventListener('click', () => {
                chosenProvider = { provider_id: id, provider_name: name, logo_path: logo };
                renderProviders(providers);
                renderChosenProvider();
            });

            container.appendChild(option);
        });
    }

    function renderChosenProvider() {
        const box = byId('jps-provider-chosen');
        clear(box);
        if (!chosenProvider) {
            box.classList.add('jps-hidden');
            return;
        }
        box.classList.remove('jps-hidden');
        const img = el('img');
        img.src = API_BASE + '/Logo/' + encodeURIComponent(chosenProvider.provider_id);
        img.alt = '';
        img.setAttribute('aria-hidden', 'true');
        img.addEventListener('error', () => { img.style.visibility = 'hidden'; });
        box.appendChild(img);
        box.appendChild(el('span', null,
            'Proveedor elegido: ' + (chosenProvider.provider_name || chosenProvider.provider_id)));
    }

    function collectFormPayload() {
        const payload = {
            displayName: getValue('jps-f-name'),
            enabled: isChecked('jps-f-enabled'),
            tmdbProviderId: chosenProvider ? chosenProvider.provider_id : 0,
            providerDisplayName: chosenProvider ? chosenProvider.provider_name : '',
            providerLogoPath: chosenProvider ? (chosenProvider.logo_path || '') : '',
            contentType: getValue('jps-f-contenttype'),
            region: getValue('jps-f-region'),
            metadataLanguage: getValue('jps-f-language'),
            sortBy: getValue('jps-f-sortby'),
            maxItems: parseInt(getValue('jps-f-maxitems'), 10) || 20,
            includeGenreIds: parseIntList(getValue('jps-f-includegenres')),
            excludeGenreIds: parseIntList(getValue('jps-f-excludegenres')),
            originalLanguage: getValue('jps-f-origlang') || null,
            originCountry: getValue('jps-f-origcountry') || null,
            minDate: getValue('jps-f-mindate') || null,
            maxDate: getValue('jps-f-maxdate') || null,
            minRating: getValue('jps-f-minrating') ? parseFloat(getValue('jps-f-minrating')) : null,
            minVoteCount: parseInt(getValue('jps-f-minvotes'), 10) || 0,
            includeAdult: isChecked('jps-f-adult'),
            requestsEnabled: isChecked('jps-f-requests'),
            cacheDurationMinutes: parseInt(getValue('jps-f-cache'), 10) || 360
        };
        if (editingId) {
            payload.id = editingId;
        }
        return payload;
    }

    function validateForm(payload) {
        if (!payload.displayName) {
            return 'El nombre de la sección es obligatorio.';
        }
        if (payload.displayName.length > 80) {
            return 'El nombre es demasiado largo (máximo 80 caracteres).';
        }
        if (!payload.tmdbProviderId) {
            return 'Elige un proveedor de la lista.';
        }
        if (!payload.region) {
            return 'La región es obligatoria.';
        }
        if (payload.maxItems < 1 || payload.maxItems > 200) {
            return 'El número de elementos debe estar entre 1 y 200.';
        }
        return null;
    }

    async function saveForm(btn) {
        const result = byId('jps-form-result');
        const payload = collectFormPayload();

        const problem = validateForm(payload);
        if (problem) {
            setResult(result, problem, 'error');
            return;
        }

        btn.disabled = true;
        setResult(result, 'Guardando...', null);
        try {
            if (editingId) {
                await api('/Admin/sections/' + encodeURIComponent(editingId), {
                    method: 'PUT',
                    body: JSON.stringify(payload)
                });
            } else {
                await api('/Admin/sections', {
                    method: 'POST',
                    body: JSON.stringify(payload)
                });
            }
            closeForm();
            await loadSections();
        } catch (err) {
            setResult(result, 'No se pudo guardar. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    // ─── Connections tab ──────────────────────────────────────────

    async function loadConfig() {
        const panel = byId('jps-connections-body');
        try {
            // GET /Admin/config answers { schemaVersion, tmdb: { enabled,
            // hasApiKey }, seerr: { enabled, serverUrl, ignoreSslErrors,
            // allowIgnoreQuota, hasApiKey } }.
            const config = await api('/Admin/config');
            const tmdb = config.tmdb || {};
            const seerr = config.seerr || {};

            setChecked('jps-tmdb-enabled', tmdb.enabled);
            setChecked('jps-seerr-enabled', seerr.enabled);
            setValue('jps-seerr-url', seerr.serverUrl || '');
            setChecked('jps-seerr-ssl', seerr.ignoreSslErrors);
            setChecked('jps-seerr-quota', seerr.allowIgnoreQuota);

            // Secrets are never returned by the server. The placeholder tells the
            // admin one is already stored without ever holding its value here.
            applySecretPlaceholder('jps-tmdb-key', tmdb.hasApiKey);
            applySecretPlaceholder('jps-seerr-key', seerr.hasApiKey);

            panel.classList.remove('jps-hidden');
            hide(byId('jps-connections-loading'));
        } catch (err) {
            hide(byId('jps-connections-loading'));
            panel.classList.remove('jps-hidden');
            setResult(byId('jps-config-result'),
                'No se pudo cargar la configuración guardada. ' + err.message, 'error');
        }
    }

    function applySecretPlaceholder(id, configured) {
        const node = byId(id);
        if (!node) {
            return;
        }
        node.value = '';
        node.placeholder = configured
            ? 'Guardado, escribe uno nuevo para cambiarlo'
            : 'Sin configurar';
    }

    async function saveConfig(btn) {
        const result = byId('jps-config-result');
        btn.disabled = true;
        setResult(result, 'Guardando...', null);

        // Secret fields are only sent when the admin actually typed something.
        // An empty string means "keep what is stored" (PreserveSecrets server-side).
        // PUT /Admin/config binds a flat SaveConfigRequest, not nested objects.
        const payload = {
            tmdbEnabled: isChecked('jps-tmdb-enabled'),
            tmdbApiKey: getValue('jps-tmdb-key'),
            seerrEnabled: isChecked('jps-seerr-enabled'),
            seerrServerUrl: getValue('jps-seerr-url'),
            seerrApiKey: getValue('jps-seerr-key'),
            seerrIgnoreSslErrors: isChecked('jps-seerr-ssl'),
            seerrAllowIgnoreQuota: isChecked('jps-seerr-quota')
        };

        try {
            await api('/Admin/config', { method: 'PUT', body: JSON.stringify(payload) });
            setResult(result, 'Configuración guardada.', 'ok');
            byId('jps-tmdb-key').value = '';
            byId('jps-seerr-key').value = '';
            await loadConfig();
        } catch (err) {
            setResult(result, 'No se pudo guardar. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    async function testConnection(service, btn, resultId) {
        const result = byId(resultId);
        btn.disabled = true;
        setResult(result, 'Probando conexión...', null);
        try {
            const data = await api('/Admin/test/' + service, { method: 'POST' });
            const ok = data === null || data.success === undefined ? true : data.success;
            const message = (data && (data.message || data.Message)) ||
                (ok ? 'Conexión correcta.' : 'La conexión falló.');
            setResult(result, message, ok ? 'ok' : 'error');
        } catch (err) {
            setResult(result, 'La conexión falló. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    // ─── Diagnostics tab ──────────────────────────────────────────

    async function loadDiagnostics() {
        const container = byId('jps-diagnostics-body');
        showLoading(container, 'Comprobando estado...');
        try {
            const d = await api('/Admin/diagnostics');
            clear(container);

            // GET /Admin/diagnostics answers with a nested shape:
            // { homeScreenSections: { available, version }, fileTransformation:
            // { available }, tmdb: { configured, enabled }, seerr: { … },
            // sections: { total, enabled }, pluginVersion }. It reports how the
            // plugin is configured, not live connectivity, so the labels say so.
            const hss = d.homeScreenSections || {};
            const ft = d.fileTransformation || {};
            const tmdb = d.tmdb || {};
            const seerr = d.seerr || {};
            const secs = d.sections || {};

            const integration = (state, optional) => {
                if (!state.configured) {
                    return optional
                        ? { text: 'Sin configurar (opcional)', kind: 'warn' }
                        : { text: 'Sin configurar', kind: 'error' };
                }
                return state.enabled
                    ? { text: 'Configurado y activo', kind: 'ok' }
                    : { text: 'Configurado, desactivado', kind: 'warn' };
            };

            const tmdbState = integration(tmdb, false);
            const seerrState = integration(seerr, true);

            // The endpoint carries no sync history, so it is derived from the
            // sections already loaded in the other tab.
            const synced = sections.filter(s => s.lastSyncUtc);
            const lastSync = synced.length
                ? synced.map(s => s.lastSyncUtc).sort().slice(-1)[0]
                : null;
            const failing = sections.filter(s => s.lastError);

            const grid = el('div', 'jps-diag-grid');
            grid.appendChild(diagItem('TMDb', tmdbState.text, tmdbState.kind));
            grid.appendChild(diagItem('Seerr', seerrState.text, seerrState.kind));
            grid.appendChild(diagItem('Home Screen Sections',
                hss.available
                    ? ('Detectado' + (hss.version ? ' (v' + hss.version + ')' : ''))
                    : 'No detectado',
                hss.available ? 'ok' : 'error'));
            grid.appendChild(diagItem('File Transformation',
                ft.available ? 'Detectado' : 'No detectado',
                ft.available ? 'ok' : 'warn'));
            grid.appendChild(diagItem('Secciones activas',
                (secs.enabled !== undefined ? secs.enabled : 0) + ' de ' +
                (secs.total !== undefined ? secs.total : sections.length),
                'ok'));
            grid.appendChild(diagItem('Última sincronización',
                lastSync ? formatDate(lastSync) : 'Nunca',
                lastSync ? 'ok' : 'warn'));
            if (d.pluginVersion) {
                grid.appendChild(diagItem('Versión del plugin', d.pluginVersion, 'ok'));
            }
            container.appendChild(grid);

            if (!hss.available) {
                const warn = el('div', 'jps-result jps-result-error',
                    'Home Screen Sections no está instalado o no se pudo detectar. Sin ese plugin, las secciones creadas aquí no aparecerán en la página principal de Jellyfin.');
                container.appendChild(warn);
            } else if (!ft.available) {
                const warn = el('div', 'jps-result jps-result-warn',
                    'File Transformation no se detectó. Home Screen Sections lo necesita para dibujar las filas en la página principal: las secciones pueden registrarse correctamente y aun así no verse.');
                container.appendChild(warn);
            }

            failing.forEach(s => {
                container.appendChild(el('div', 'jps-result jps-result-error',
                    'Último error en "' + (s.displayName || s.id) + '": ' + s.lastError));
            });

            const row = el('div', 'jps-btn-row');
            const sync = el('button', 'jps-btn');
            sync.type = 'button';
            sync.appendChild(icon('sync', 'jps-btn-icon'));
            sync.appendChild(el('span', null, 'Sincronizar ahora'));
            sync.addEventListener('click', () => runDiagAction('/Admin/sync-now', sync, 'Sincronización lanzada.'));
            row.appendChild(sync);

            const reg = el('button', 'jps-btn');
            reg.type = 'button';
            reg.appendChild(icon('app_registration', 'jps-btn-icon'));
            reg.appendChild(el('span', null, 'Registrar secciones ahora'));
            reg.addEventListener('click', () => runDiagAction('/Admin/register-sections-now', reg, 'Secciones registradas de nuevo.'));
            row.appendChild(reg);

            const refresh = el('button', 'jps-btn');
            refresh.type = 'button';
            refresh.appendChild(icon('refresh', 'jps-btn-icon'));
            refresh.appendChild(el('span', null, 'Actualizar'));
            refresh.addEventListener('click', loadDiagnostics);
            row.appendChild(refresh);

            container.appendChild(row);

            const diagResult = el('div', 'jps-result jps-hidden');
            diagResult.id = 'jps-diag-result';
            container.appendChild(diagResult);
        } catch (err) {
            showError(container, err.message, loadDiagnostics);
        }
    }

    function diagItem(label, value, kind) {
        const item = el('div', 'jps-diag-item');
        item.appendChild(el('span', 'jps-dot jps-dot-' + kind));
        const text = el('div', 'jps-diag-text');
        text.appendChild(el('span', 'jps-diag-label', label));
        text.appendChild(el('span', 'jps-diag-value', value));
        item.appendChild(text);
        return item;
    }

    async function runDiagAction(path, btn, okMessage) {
        const result = byId('jps-diag-result');
        btn.disabled = true;
        setResult(result, 'Ejecutando...', null);
        try {
            await api(path, { method: 'POST' });
            setResult(result, okMessage, 'ok');
            await loadSections();
        } catch (err) {
            setResult(result, 'La acción falló. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    // ─── Tabs ─────────────────────────────────────────────────────

    function initTabs() {
        const buttons = document.querySelectorAll('.jps-tab-btn');
        buttons.forEach(btn => {
            btn.addEventListener('click', () => {
                const target = btn.dataset.tab;
                buttons.forEach(b => {
                    const active = b === btn;
                    b.classList.toggle('active', active);
                    b.setAttribute('aria-selected', active ? 'true' : 'false');
                });
                document.querySelectorAll('.jps-tab-panel').forEach(panel => {
                    panel.classList.toggle('jps-hidden', panel.dataset.panel !== target);
                });
                if (target === 'diagnostics') {
                    loadDiagnostics();
                }
            });
        });
    }

    // ─── Wiring ───────────────────────────────────────────────────

    function bindEvents() {
        byId('jps-new-section').addEventListener('click', () => openForm(null));
        byId('jps-collapse-all').addEventListener('click', collapseAll);
        byId('jps-refresh-sections').addEventListener('click', loadSections);

        byId('jps-form-save').addEventListener('click', ev => saveForm(ev.currentTarget));
        byId('jps-form-cancel').addEventListener('click', closeForm);
        byId('jps-form-test').addEventListener('click', ev => testFormQuery(ev.currentTarget));

        byId('jps-f-region').addEventListener('change', loadProviders);
        byId('jps-f-contenttype').addEventListener('change', loadProviders);
        byId('jps-f-providersearch').addEventListener('input', () => {
            const key = getValue('jps-f-contenttype') + '|' + getValue('jps-f-region');
            if (cache.providers.has(key)) {
                renderProviders(cache.providers.get(key));
            }
        });

        byId('jps-config-save').addEventListener('click', ev => saveConfig(ev.currentTarget));
        byId('jps-tmdb-test').addEventListener('click', ev =>
            testConnection('tmdb', ev.currentTarget, 'jps-tmdb-result'));
        byId('jps-seerr-test').addEventListener('click', ev =>
            testConnection('seerr', ev.currentTarget, 'jps-seerr-result'));
    }

    /**
     * Runs the discover query for the values currently in the form, before the
     * section is saved, so filters can be iterated without publishing anything.
     */
    async function testFormQuery(btn) {
        const result = byId('jps-form-result');
        const payload = collectFormPayload();
        const problem = validateForm(payload);
        if (problem) {
            setResult(result, problem, 'error');
            return;
        }

        btn.disabled = true;
        setResult(result, 'Consultando TMDb...', null);
        try {
            const data = await api('/Admin/test-query', {
                method: 'POST',
                body: JSON.stringify(payload)
            });
            // The server answers { query, count, items }, where items is capped
            // at 12 for the preview: count is the real total.
            const total = data && (data.count !== undefined ? data.count : (data.items || []).length);
            setResult(result,
                'Consulta correcta. ' + (total || 0) + ' resultados con estos filtros.',
                total ? 'ok' : 'warn');
        } catch (err) {
            setResult(result, 'La consulta falló. ' + err.message, 'error');
        } finally {
            btn.disabled = false;
        }
    }

    // ─── Init ─────────────────────────────────────────────────────

    function init() {
        if (!byId(PAGE_ID)) {
            return;
        }
        initTabs();
        bindEvents();
        loadConfig();
        loadSections();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
