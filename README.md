# FileManager.App

A cross-platform file manager built on Avalonia and ReactiveUI, with a
unified interface for local and cloud storage. Early development —
see Roadmap below.

## About

This is a ground-up rewrite of a WinForms file explorer I built in high
school. The original worked, but it was tied to Windows shell APIs from
top to bottom and had no real separation between "what a file is" and
"how you get one." This version is designed around a single abstraction
— `IStorageProvider` — so the UI never has to know whether it's looking
at the local disk, Azure Blob Storage, or OneDrive.

## Why this project

A learning vehicle, deliberately:

- Async programming end-to-end — not bolted on, part of the core design
- MVVM with ReactiveUI
- Cross-platform desktop development with Avalonia
- Designing an abstraction that has to hold up against a technology
  (cloud storage) that didn't exist when the first line was written
- Docker, for running a local Azure Blob emulator during development

## Architecture

```
FileManager.sln
├── FileManager.App/            Avalonia UI + ReactiveUI view models, composition root
├── FileManager.Core/           models + interfaces, zero dependencies
└── FileManager.Core.Tests/     xUnit tests for Core
└── FileManager.Infrastructure/ Local Storage definitions based on Core 
└── FileManager.Infrastructure.Tests/ xUnit tests for Infrastructure 
```

Dependencies only point inward, toward `Core`. `Core` depends on
nothing else in the solution. `Infrastructure` (local storage), `Cloud`
(Azure/OneDrive), and a split-out `UI` project get added as their
milestones start — see Roadmap.

## Tech stack

- **.NET** — .NET 10.0
- **Avalonia UI** — cross-platform desktop UI
- **ReactiveUI** — MVVM
- **xUnit** — testing
- **Docker + Azurite** — local Azure Blob emulation for development *(planned)*

## Roadmap

- [x] Phase 0 — Setup & tooling
- [x] Phase 1 — Core & async basics
- [ ] Phase 2 — Local storage & first UI
- [ ] Phase 3 — Transfer manager
- [ ] Phase 4 — Docker & Azurite
- [ ] Phase 5 — Azure provider
- [ ] Phase 6 — Search, preview & polish

## Getting started

_TODO: build/run instructions go here once Phase 2 makes the app
actually runnable._

## Legacy code

Legacy code might be added later because i would need to translate it first.