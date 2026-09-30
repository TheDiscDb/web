# Bundled Optical Disc Manifest schema

`optical-disc-manifest-v1.schema.json` is the pinned copy of the draft v1 schema
from the private `TheDiscDb/optical-disc-manifest` repository.

This is the **only** copy in this repository. `TheDiscDb.Contributions` embeds
the same file by link rather than keeping its own, so a manifest the browser
generator produces is validated against exactly the bytes the server enforces on
upload. Two copies would let the generator emit a manifest the upload path then
rejects.

Refresh it whenever the published v1 schema changes, and rerun the generator
tests: they assert generated manifests satisfy it.
