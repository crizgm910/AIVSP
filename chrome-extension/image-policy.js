(() => {
  'use strict';
  const frameOrigins = Object.freeze([
    'https://forms.cloud.microsoft', 'https://forms.office.com',
    'https://teams.microsoft.com', 'https://teams.cloud.microsoft'
  ]);
  function parse(value) {
    if (typeof value !== 'string' || !value.length || value.length > 4096) return null;
    try {
      const url = new URL(value);
      if (url.protocol !== 'https:' || url.username || url.password || url.port) return null;
      const host = url.hostname;
      // Domain names only. URL canonicalization also catches hexadecimal/short IPv4.
      if (host.length > 253 || host.endsWith('.') || !host.includes('.') ||
          !host.split('.').every(label => /^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$/i.test(label)) ||
          !/^(?:[a-z]{2,63}|xn--[a-z0-9-]+)$/i.test(host.split('.').at(-1)) ||
          /(?:^|\.)(?:localhost|local|internal|intranet|lan|home|onion|invalid|test|example)$/i.test(host) ||
          /(?:^|\.)(?:localtest\.me|nip\.io|sslip\.io)$/i.test(host)) return null;
      url.hash = '';
      return Object.freeze({ url: url.href, origin: url.origin, pattern: url.origin + '/*' });
    } catch { return null; }
  }
  globalThis.AivspImagePolicy = Object.freeze({ parse, frameOrigins });
})();
