import http from "node:http";
import { readFile } from "node:fs/promises";
const root = new URL("../tests/forms-lab/", import.meta.url);
const files = new Map([["/", ["index.html", "text/html"]], ["/index.html", ["index.html", "text/html"]],
  ["/cases.html", ["cases.html", "text/html"]], ["/cases.js", ["cases.js", "text/javascript"]],
  ["/lab.js", ["lab.js", "text/javascript"]], ["/styles.css", ["styles.css", "text/css"]],
  ["/shared-chart.svg", ["shared-chart.svg", "image/svg+xml"]],
  ["/space-grid.svg", ["space-grid.svg", "image/svg+xml"]]]);
const servers = [];
for (const port of [8765, 8766]) {
  const server = http.createServer(async (request, response) => {
    const entry = files.get(new URL(request.url, "http://127.0.0.1").pathname);
    if (!["GET", "HEAD"].includes(request.method)) { response.writeHead(405); response.end(); return; }
    if (!entry) { response.writeHead(404); response.end("Not found"); return; }
    try {
      const bytes = await readFile(new URL(entry[0], root));
      response.writeHead(200, { "Content-Type": `${entry[1]}; charset=utf-8`, "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" });
      response.end(request.method === "HEAD" ? undefined : bytes);
    } catch { response.writeHead(500); response.end("Fixture unavailable"); }
  });
  server.on("error", error => { console.error(`Puerto ${port}: ${error.code}. Cierra el servidor anterior si ya existe.`); servers.forEach(active => active.close()); process.exitCode = 1; });
  servers.push(server); server.listen(port, "127.0.0.1", () => console.log(`Laboratorio: http://127.0.0.1:${port}`));
}
for (const signal of ["SIGINT", "SIGTERM"]) process.on(signal, () => { servers.forEach(server => server.close()); });
