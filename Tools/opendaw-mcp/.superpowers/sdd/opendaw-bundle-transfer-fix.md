# Fix: export_bundle transfers .odb as base64, not a JSON number array

## Reproduction

Wrote `test/integration/bundle-export.test.ts` against the *unfixed* code first: imports a
~26 MB WAV of pseudo-random noise (a PRNG, not `Math.random()`, so it's deterministic) as a
sample into a Nano instrument, builds a one-region arrangement referencing it, then calls
`export_bundle`.

First attempt used silence instead of noise, following the task's suggestion — that failed to
reproduce anything: `ProjectBundle.encode` DEFLATE-compresses the zip (compression level 6), and
digital silence collapses to ~60 KB regardless of duration, so the array-of-numbers payload that
crosses `page.evaluate` never got large. Switched the sample to noise (each byte close to
incompressible) so the compressed bundle stays large (~26 MB), which is what actually stresses the
old code path.

Against the unfixed code, ran:
```
npx vitest run --config vitest.integration.config.ts test/integration/bundle-export.test.ts
```
with a 170s wall clock timeout. The Vitest worker process's heap grew to ~4 GB constructing
`Array.from(new Uint8Array(encoded))` plus its JSON serialization, then crashed:
```
FATAL ERROR: Ineffective mark-compacts near heap limit Allocation failed - JavaScript heap out of memory
```
This happened at ~61s into the run (before the process died). It didn't hang forever so much as
kill the worker with an OOM — same underlying cause (multiplying a ~26 MB buffer into tens of
millions of JS number elements plus JSON text) as the real MCP client's reported "the call simply
never returned" — the client's own process would either stall similarly long past its 60s timeout
or hit the same memory wall, depending on available RAM. Reproduction confirmed.

## Fix

- `host/src/assets.ts`: `exportBundle` now returns `Promise<string>` — a base64 encoding of the
  zip bytes — instead of `Promise<number[]>`.
- `host/src/base64.ts` (new): extracted `bytesToBase64(bytes: Uint8Array): string`, the same
  chunked `String.fromCharCode` + `btoa` loop that `render.ts` already used for PCM (chunk size
  `0x8000` to stay under the spread-argument limit). `render.ts`'s local `toBase64(frames:
  Float32Array)` now just views the frames as bytes and calls the shared helper — no
  duplicated chunking logic on the browser side.
- `src/pcm.ts`: extracted a private `decodeBytes(base64): Uint8Array` (the existing
  buffer-copy-not-view logic, unchanged) and added `decodeBundle(base64): Uint8Array` as its
  sibling next to `decodeChannel`, which now also calls `decodeBytes` internally. Both share the
  same care around Node's pooled `Buffer` (copy into a fresh `Uint8Array` rather than viewing
  `buffer.buffer` directly, which would read/write neighbouring pooled memory).
- `src/server.ts`: `export_bundle` now calls `host().call<string>("bundle")`, decodes with
  `decodeBundle`, and writes the resulting `Uint8Array` directly.

### Sharing with the PCM path
Fully shared where the shapes matched:
- Encode side: both `render.ts` and `assets.ts` now go through the one `bytesToBase64` helper in
  `host/src/base64.ts` — no second chunking implementation.
- Decode side: both `decodeChannel` and `decodeBundle` go through the one `decodeBytes` helper in
  `src/pcm.ts` — the Buffer-pooling-safe copy logic exists exactly once.

Not shared: the final wrap differs by design (`decodeChannel` returns `Float32Array` typed over
the copied bytes; `decodeBundle` returns the `Uint8Array` itself) since a bundle is opaque bytes,
not interleaved PCM — same reasoning the task description already called out.

### Fixing the existing tests broken by the return-type change
`test/integration/assets.test.ts` had two tests calling `window.__odaw.bundle()` directly and
asserting on a `number[]` result (`bytes.slice(0, 4)` etc). Updated both to decode the returned
base64 string via `Buffer.from(base64, "base64")` before checking the zip magic bytes.

## New test

`test/integration/bundle-export.test.ts`, test name: **"экспортирует .odb, содержащий большой
импортированный сэмпл, не вешая Node"** (inside describe block "экспорт большого бандла").

It: writes a ~26 MB WAV of PRNG noise to the OS temp dir, imports it via `import_asset` as
`big_noise`, deletes the temp WAV immediately after import (`unlink`), builds a one-track/
one-region Nano arrangement referencing it, calls `export_bundle` to a path under the test's own
`outputDir` temp dir, then asserts:
- the write succeeds and returns the expected path,
- the output file's size is between 15 MB and 40 MB (ballpark for a ~26 MB noisy sample plus zip
  overhead, allowing for both zip container overhead and case-by-case DEFLATE variance),
- the file's first 4 bytes are `PK\x03\x04` (zip magic).

No large binaries are committed — the input WAV and output `.odb` both live under `os.tmpdir()`
and the input is deleted at the end of the import step; `afterAll` removes the whole scratch
directory.

## Verification

1. `npx vitest run`
```
 RUN  v4.1.11 C:/Unity Projects/DungeonGirls/Tools/opendaw-mcp

 Test Files  10 passed (10)
      Tests  70 passed (70)
   Start at  03:25:55
   Duration  765ms (transform 631ms, setup 0ms, import 1.65s, tests 245ms, environment 2ms)
```

2. `npm run build`
```
...
dist/wasm-engine/wasm/engine.wasm                            1,053.90 kB │ gzip: 341.58 kB
dist/assets/jszip.min-D7KnG0-e.js                                97.32 kB │ gzip:  30.12 kB
dist/assets/index-D2JtugoD.js                                 1,502.44 kB │ gzip: 345.91 kB

(!) Some chunks are larger than 500 kB after minification. Consider:
- Using dynamic import() to code-split the application
- Use build.rollupOptions.output.manualChunks to improve chunking: https://rollupjs.org/configuration-options/#output-manualchunks
- Adjust chunk size limit for this warning via build.chunkSizeWarningLimit.

✓ built in 6.42s
```
(the chunk-size warning is pre-existing/unrelated to this change; build succeeded)

3. `npx vitest run --config vitest.integration.config.ts`
```
 RUN  v4.1.11 C:/Unity Projects/DungeonGirls/Tools/opendaw-mcp

 Test Files  11 passed (11)
      Tests  39 passed (39)
   Start at  03:26:16
   Duration  41.04s (transform 218ms, setup 0ms, import 4.90s, tests 34.34s, environment 1ms)
```
(38 previously existing + 1 new = 39, matches expected growth)

4. `npx tsc --noEmit`
```
(no output — zero errors)
```

## Files touched
- `host/src/assets.ts`
- `host/src/render.ts`
- `host/src/base64.ts` (new)
- `src/pcm.ts`
- `src/server.ts`
- `test/integration/assets.test.ts`
- `test/integration/bundle-export.test.ts` (new)
