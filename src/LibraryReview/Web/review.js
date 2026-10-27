define(['loading', 'toast'], function (loading, toast) {
    'use strict';
    var pluginId = '9eba6ec4-c190-40c2-961c-71e92174680e';
    return function (view) {
        var state, preview, busy = false;
        function el(id) { return view.querySelector('#' + id); }
        function message(text) { el('lrMessage').textContent = text; }
        function configKey(config, name) { return Object.keys(config).find(function (key) { return key.toLowerCase() === name.toLowerCase(); }) || name; }
        function configGet(config, name) { return config[configKey(config, name)]; }
        function configSet(config, name, value) { config[configKey(config, name)] = value; }
        function api(path, body) {
            return ApiClient.ajax({ type: body === undefined ? 'GET' : 'POST',
                url: ApiClient.getUrl('LibraryReview' + path), dataType: 'json',
                contentType: 'application/json', headers: { 'Authorization': 'MediaBrowser Token="' + encodeURIComponent(ApiClient.accessToken()) + '"' },
                data: body === undefined ? undefined : JSON.stringify(body), timeout: 1800000 });
        }
        async function action(work) {
            if (busy) return;
            busy = true; el('lrScan').disabled = true;
            try { await work(); }
            catch (error) {
                var detail = 'Operation stopped. Refresh the report and inspect activity before retrying.';
                try { detail = JSON.parse(error.responseText).message || detail; } catch (_) { }
                message(detail);
            } finally { busy = false; el('lrScan').disabled = false; }
        }
        function button(text, handler) {
            var b = document.createElement('button'); b.type = 'button';
            b.className = 'raised emby-button'; b.textContent = text;
            b.style.margin = '4px 8px 4px 0'; b.addEventListener('click', handler); return b;
        }
        function render() {
            var findings = state.items.filter(function (i) { return i.findings.length && i.status === 'open'; });
            el('lrSummary').textContent = state.items.length + ' movies checked · ' + findings.length + ' new review items · Last scan: ' + (state.lastScan ? new Date(state.lastScan).toLocaleString() : 'Not run');
            if (state.lastError) message(state.lastError);
            var container = el('lrItems'); container.replaceChildren();
            state.items.filter(function (i) { return el('lrShowAll').checked || (i.findings.length && ['open', 'problem'].includes(i.status)); }).forEach(function (item) {
                var card = document.createElement('article'); card.style.cssText = 'padding:20px;margin:14px 0;border:1px solid #56616d;border-radius:10px';
                var h = document.createElement('h2'); h.textContent = item.title; card.appendChild(h);
                var p = document.createElement('p'); p.textContent = 'Status: ' + item.status + ' · Runtime: ' + item.actualMinutes.toFixed(1) + ' minutes'; card.appendChild(p);
                var source = document.createElement('p'); source.textContent = item.referenceSource; card.appendChild(source);
                var list = document.createElement('ul');
                item.findings.forEach(function (finding) { var li = document.createElement('li'); li.textContent = finding.severity + ': ' + finding.message; list.appendChild(li); });
                card.appendChild(list);
                var path = document.createElement('p'); path.textContent = item.file.path; path.style.overflowWrap = 'anywhere'; card.appendChild(path);
                [['Accept this edition', 'accepted'], ['Dismiss', 'dismissed'], ['Mark as a problem', 'problem'], ['Reopen', 'open']].forEach(function (pair) {
                    card.appendChild(button(pair[0], function () { action(async function () { await api('/' + item.id + '/Decision', { status: pair[1] }); await load(); }); }));
                });
                if (item.status === 'problem') card.appendChild(button('Block release and find replacement…', function () {
                    action(async function () {
                        message('Checking exact file and Radarr identity; hashing the video may take several minutes…');
                        preview = await api('/' + item.id + '/Preview', {});
                        el('lrPreview').textContent = 'Title: ' + preview.title + '\nExact file: ' + preview.path + '\nSize: ' + (preview.size / 1073741824).toFixed(2) + ' GiB\nRelease: ' + preview.release + '\nSHA-256: ' + preview.sha256 + '\nExpires: ' + new Date(preview.expires).toLocaleString();
                        el('lrConfirmTitle').value = ''; el('lrConfirmDelete').disabled = true;
                        el('lrConfirm').showModal(); message('Preview ready. No files changed.');
                    });
                }));
                container.appendChild(card);
            });
            if (!container.children.length) { var p = document.createElement('p'); p.textContent = 'No unresolved findings. This is not a guarantee of correct content.'; container.appendChild(p); }
            el('lrActivity').textContent = state.actions.slice(-50).map(function (a) { return a.at + ' · ' + a.stage + ' · ' + a.message; }).join('\n') || 'No replacement actions.';
        }
        async function load() { state = await api(''); render(); }
        el('lrShowAll').addEventListener('change', function () { if (state) render(); });
        el('lrScan').addEventListener('click', function () { action(async function () { message('Checking movie metadata…'); await api('/Scan', {}); message('Read-only scan completed.'); await load(); }); });
        el('lrCancel').addEventListener('click', function () { el('lrConfirm').close(); preview = null; });
        el('lrConfirmTitle').addEventListener('input', function () { el('lrConfirmDelete').disabled = !preview || el('lrConfirmTitle').value !== preview.title; });
        el('lrConfirmDelete').addEventListener('click', function () {
            if (!preview || el('lrConfirmTitle').value !== preview.title) return;
            var p = preview; preview = null; el('lrConfirm').close();
            action(async function () {
                message('Verifying and processing the exact confirmed file…');
                var result = await api('/' + p.itemId + '/Replace', { token: p.token, confirmTitle: p.title });
                message(result); await load();
            });
        });
        el('lrConfig').addEventListener('submit', function (event) {
            event.preventDefault(); action(async function () {
                var config = await ApiClient.getPluginConfiguration(pluginId);
                configSet(config, 'DesiredLanguage', el('lrLanguage').value); configSet(config, 'RadarrUrl', el('lrUrl').value);
                configSet(config, 'RadarrApiKey', el('lrKey').value); configSet(config, 'JellyfinMovieRoot', el('lrJRoot').value);
                configSet(config, 'RadarrMovieRoot', el('lrRRoot').value); configSet(config, 'EnableReplacement', el('lrReplace').checked);
                await ApiClient.updatePluginConfiguration(pluginId, config); message('Settings saved. Replacement only runs after a separate explicit confirmation.');
            });
        });
        view.addEventListener('viewshow', function () { action(async function () {
            var c = await ApiClient.getPluginConfiguration(pluginId);
            el('lrLanguage').value = configGet(c, 'DesiredLanguage') || 'eng'; el('lrUrl').value = configGet(c, 'RadarrUrl') || '';
            el('lrKey').value = configGet(c, 'RadarrApiKey') || ''; el('lrJRoot').value = configGet(c, 'JellyfinMovieRoot') || '';
            el('lrRRoot').value = configGet(c, 'RadarrMovieRoot') || ''; el('lrReplace').checked = configGet(c, 'EnableReplacement') || false;
            await load();
        }); });
    };
});
