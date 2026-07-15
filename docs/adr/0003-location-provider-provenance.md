# ADR 0003: Persist optional location-provider provenance

## Status

Accepted — 2026-07-14

## Decision

Add optional `provider` metadata to schema-version-1 `IncidentLocation` records. Coordinates remain authoritative; provider text identifies the online adapter that was attempted or supplied an editable suggestion. Older records without the property remain valid, so no schema migration is required.

The Nominatim cache remains disposable project data and is capped at the 2,000 most recently resolved rounded coordinates. Lookups keep the existing explicit user action, process-wide one-request-per-second gate, custom User-Agent, and configurable endpoint.

## Consequences

- JSON and HTML evidence preserve the origin of machine-generated location text.
- A failed lookup may still record the attempted provider while retaining coordinates and manual fields.
- Cache growth is bounded for long projects without placing cache data in the authoritative project document.
