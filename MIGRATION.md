# Proof-of-concept -> PrismCast migration

The original proof of concept was implemented by patching a separate synchronization plugin directly. PrismCast 0.2 replaces that experiment with a standalone Dalamud plugin and its own session transport.

## Removed

- external sync-plugin source checkout/build step
- patched API enums and hosted-service registration
- external sync-plugin DLL identity
- old `/mediacast` and `/mcast` commands

## Replaced with

- standalone `PrismCast.dll`
- `/prismcast` and `/prism`
- PrismCast's own authenticated temporary host transport
- Cloudflare Quick Tunnel for PrismCast state/media
- direct invite codes
- optional trusted-host rendezvous and automatic joining
- normal side-by-side operation with other Dalamud plugins

## Why

A viewer still needs PrismCast's renderer and media engine locally. Keeping that code in its own plugin gives PrismCast an independent identity while leaving every unrelated synchronization plugin untouched.
