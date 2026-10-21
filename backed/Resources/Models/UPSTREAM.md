# Model metadata snapshot

`models.dev.json` is copied byte-for-byte from
`client/apps/desktop/resources/models.dev/api.json`, imported with PI-Desktop
0.20.1 at commit `872ad335f4580c223738a8ab5538e7b00c17c9ed`.
Its upstream public data endpoint is https://models.dev/api.json.

SHA-256: `77eebbf630c949935ffbda6a368d8f5391a107e1628ddebe26da7265c8adcb87`.

The server embeds this seed and creates `data/models/model.json` on first use.
It contains public model metadata, not provider API keys or user login tokens.
Endpoint model lists determine availability; this snapshot only enriches the
returned ids. Keep the original client provenance and license notices.
