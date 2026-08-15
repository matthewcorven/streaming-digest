window.streamingDigestModelEvents = (() => {
    const sources = new Map();

    function start(dotNetRef, url) {
        const handle = `model-events-${crypto.randomUUID()}`;
        const source = new EventSource(url, { withCredentials: true });

        source.onopen = () => {
            dotNetRef.invokeMethodAsync('HandleBrowserSseOpenedAsync');
        };

        source.onmessage = event => {
            dotNetRef.invokeMethodAsync('HandleBrowserSseMessageAsync', 'message', event.data ?? '');
        };

        source.onerror = () => {
            dotNetRef.invokeMethodAsync('HandleBrowserSseErrorAsync', source.readyState);
            if (source.readyState === EventSource.CLOSED) {
                stop(handle);
            }
        };

        sources.set(handle, source);
        return handle;
    }

    function stop(handle) {
        const source = sources.get(handle);
        if (!source) {
            return;
        }

        source.close();
        sources.delete(handle);
    }

    return { start, stop };
})();