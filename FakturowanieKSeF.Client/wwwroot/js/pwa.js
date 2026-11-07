// Rejestracja service workera (PWA, D45) - w osobnym pliku, bo polityka CSP blokuje skrypty wstawione w stronę (D47)
if ('serviceWorker' in navigator) navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
