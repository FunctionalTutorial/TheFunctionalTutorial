# Tutorial Server — Project Scope Plan

**Status:** Active tracking document  
**Last updated:** 2026-07-27  
**Owner:** Wizden fork  
**Server name:** The Functional Tutorial Server  
**Goal:** A dedicated SS14 server mode where players join, pick any job (including antagonists), complete a short personal tutorial in isolation, then die and respawn to try again (including mid-round joins).

Use this document to keep design and implementation on track. Update it when scope changes.

---

## 1. Vision

Players should be able to join a **tutorial server**, pick **any role they want to learn** (crew jobs and antagonists alike), and immediately play a **5–10 minute, purpose-built tutorial** for that role.

Each tutorial runs in an **isolated personal map** with:

- **No communication with other players at all** (no OOC, LOOC, IC, radio, ghost chat, etc.) — zero moderation burden
- No shared station pressure or round economy
- Clear guided steps for that role (stubs are fine if they represent real gameplay closely enough)
- Clean completion → death → respawn loop so they can try another role or repeat
- Immediate map despawn when the owning player leaves the body that spawned it (death or ghost)

This is **not** a full round. It is a **practice / onboarding sandbox** that feels like the real game for one role at a time.

**Fidelity rule:** Mechanics can be stubs as long as they represent real gameplay closely enough. Example: spawning as a Nuclear Operative does **not** require a full station — spawn a small section of the nukie map, let the player outfit themselves, and try different loadouts.

---

## 2. Player Flow (Happy Path)

```text
Connect
  → Lobby / character setup (player profile appearance retained)
  → Join round (or mid-round join / respawn)
  → Role picker: any job, including antagonists
  → Server loads/spawns a private tutorial map for that player + role
  → Player spawns into isolated zone with starting gear / antag tools as needed
  → Guided tutorial steps (5–10 minutes) — not skippable
  → Tutorial complete → player dies (scripted / forced)
     OR player /ghosts (or dies mid-tutorial) → map despawns immediately
  → Respawn → back to role picker to try the same or a new tutorial
```

Mid-round joins and deaths use the same path: **every spawn gets a fresh personal tutorial map**.

**Exit paths:** Completion is not skippable. The only early exit is `/ghost` (or death), which despawns the map and lets the player respawn into a new tutorial.

---

## 3. Goals (In Scope)

| ID | Goal | Notes |
|----|------|--------|
| G1 | Dedicated game mode / game rule for tutorial server | Prefer game-rule driven, not hard-coded server hacks |
| G2 | **All jobs** selectable immediately at spawn | Including restricted jobs and antagonists; no curated gate |
| G3 | Antagonist selection at spawn (traitor, nukie, etc.) | Explicit opt-in; stub maps/mechanics OK |
| G4 | Per-player isolated tutorial map instance | Hand-authored maps/grids loaded per spawn |
| G5 | **Zero player-to-player communication** | No OOC/LOOC/IC/radio/ghost chat — no moderation needed |
| G6 | Short guided tutorials (target 5–10 min) | Steps can be stubs; fidelity over completeness |
| G7 | Completion → death → respawn loop | `/ghost` or death also exits; map despawns immediately |
| G8 | Mid-round join and mid-round respawn always allowed | Deathmatch/respawn-style behavior |
| G9 | Hybrid round lifecycle | Long self-recycling round + **daily auto-restart into tutorial mode** |
| G10 | Integration tests for core flow | Required for new gameplay features |
| G11 | Server browser branding | Name / MOTD / rules locked (see §11) |

---

## 4. Non-Goals (Out of Scope for v1)

- Full multiplayer station rounds on this server
- Persistent progression / unlock trees / XP (may revisit later)
- Perfect 1:1 recreation of full maps (small stub sections are intended)
- Live traitor objectives tied to real mobs/players (plain-text placeholders only)
- Skip-tutorial button (use `/ghost` + respawn instead)
- New admin/mentor spectate tools (existing admin tools are enough)
- Any player chat that requires moderation
- Cross-player PvP tutorials in shared spaces (solo isolation only)
- Replacing the in-game guidebook (tutorials should complement it)
- Engine (`RobustToolbox`) changes — work around in content only

---

## 5. Design Principles

1. **Total communication blackout** — No way for players to talk to each other. If any chat path exists between players, that is a bug. Goal: zero moderation.
2. **Isolation first** — Separate maps; no shared interaction between tutorials.
3. **All jobs, immediately** — Every job/antag is pickable from day one; content quality can improve over time via stubs.
4. **Stubs are OK** — Represent real gameplay closely enough; do not require full stations, live objectives, or perfect systems.
5. **One role, one map** — Each spawn is tailored to the chosen job/antag package (small map section is fine).
6. **Map dies with the body** — If the owning player is not in the body that spawned the map (death or ghost), despawn that map immediately. Same on disconnect.
7. **Short and repeatable** — Prefer focused drills over long narratives; completion is not skippable.
8. **Reuse upstream patterns** — Game rules, map loading, respawn trackers, antag APIs; put new code under Wizden paths.
9. **Authorable content** — Maps + YAML/prototype-driven tutorial steps so content can grow without code rewrites.
10. **Fail soft** — Missing polished content for a role still spawns a stub practice map, never softlocks join.
11. **Profile retained** — Use the player's character appearance and profile settings on spawn.

---

## 6. Architecture Overview

### 6.1 High-level components

```text
┌─────────────────────────────────────────────────────────────┐
│ TutorialServer Game Rule (always active on this preset)     │
│  - Forces mid-round joins / respawns                        │
│  - Owns player session → tutorial instance mapping          │
│  - Suppresses normal station antag selection / round end    │
└───────────────────────────┬─────────────────────────────────┘
                            │
        ┌───────────────────┼───────────────────┐
        ▼                   ▼                   ▼
 Role Picker UI      Map Instance Pool     Tutorial Director
 (job + antag)       (load/unload grids)   (steps, complete)
        │                   │                   │
        └───────────────────┴───────────────────┘
                            │
                            ▼
                 Player spawned in private map
                 + isolation components
                 + role gear / antag setup
```

### 6.2 Systems (`_Functional`)

| Area | Location |
|------|----------|
| Shared components / events | `Content.Shared/_Functional/TutorialServer/` |
| Server systems / game rule | `Content.Server/_Functional/TutorialServer/` |
| Client UI (role picker, step HUD) | `Content.Client/_Functional/TutorialServer/` |
| Prototypes (rules, tutorials, spawn gear) | `Resources/Prototypes/_Functional/TutorialServer/` |
| Locale | `Resources/Locale/en-US/_Functional/tutorial-server.ftl`, `tutorial-server-deep.ftl` |
| Maps / grids | `Resources/Maps/_Functional/TutorialServer/` (+ `Roles/` per non-stub role) |
| Deep role curricula | `Resources/Prototypes/_Functional/TutorialServer/roles/*.yml` |
| Integration tests | `Content.IntegrationTests/Tests/_Functional/TutorialServer/` |

Upstream files should only be edited when unavoidable; mark every change with `//Functional: <why>`.

### 6.3 Upstream systems to reuse

| System | Why it matters |
|--------|----------------|
| `GameRule` / `GameTicker` | Mode lifecycle, round start, join hooks |
| `RespawnRuleSystem` / `RespawnTrackerComponent` | Mid-round death → respawn queue |
| `LoadMapRuleSystem` / map loader APIs | Loading personal maps/grids |
| `AntagSelectionSystem` / antag APIs | Applying traitor (etc.) kits & objectives carefully (likely customized, not random) |
| Job / starting gear prototypes | Outfit the chosen job |
| Station jobs / late-join flow | Hook or bypass for unlimited job picks |

Deathmatch-style respawn is a useful reference (`DeathMatchRuleSystem` + `RespawnTrackerComponent`), but tutorial mode needs **role re-selection** and **per-player maps**, not a shared arena.

---

## 7. Feature Specs

### 7.1 Game preset / rule

- New game preset, e.g. `TutorialServer`, that starts a **long-lived, self-recycling round** with the tutorial rule active.
- **Daily auto-restart** for hygiene (memory / orphan cleanup). Restart **must** put the server back into tutorial mode (same preset/rule — never fall through to a normal station round).
- Within a day, prefer recycling in-place (map unload, session cleanup) over round restarts.
- Disable or no-op normal round-ending pressure (shuttle call, nuke win conditions, etc.).
- Disable random antagonist assignment from standard rules.
- Always allow late join; never close join window for this preset.

### 7.2 Role selection

On spawn / respawn, show a **Tutorial Role Picker**:

- **All crew jobs** available immediately (no curated unlock gate)
- **All antagonist / special roles** available immediately (traitor, nukie, etc.) — stub tutorials acceptable
- Spawn uses the player's **character appearance and profile settings**
- Selection creates a `TutorialSession` for that player (job id, antag id, map prototype, progress)

If a role lacks a polished tutorial yet, still offer it with a **stub map / stub steps** that approximate the real role closely enough (e.g. small nukie outfitting room, not a full nuke ops round).

### 7.3 Personal tutorial maps

- Hand-made maps/grids under `Resources/Maps/_Wizden/TutorialServer/`.
- Small stub sections of larger maps are preferred over full stations.
- On player spawn:
  1. Load (or clone) the map/grid for the chosen tutorial
  2. Record ownership (`NetUserId` → map/grid uids + owning body uid)
  3. Spawn player at designated spawn point with their profile appearance
  4. **Despawn immediately** when the owning player is no longer in that body (death, `/ghost`, or disconnect), or when the tutorial completes
- Any map with **no active owning player in the spawning body** must despawn as soon as that condition is true — no lingering empty tutorial maps.
- Maps should be **self-contained enough** for the drill (power/atmos/tools/loadout vendors as needed); full station simulation is not required.
- Prefer one map (or small map section) per tutorial package; shared “hub” maps are out of scope for v1.

### 7.4 Isolation (hard requirement)

Players must have **no way to communicate with or conflict with other players**. This server is intentionally unmoderated.

**Block all player-to-player communication**, including:

| Channel / vector | Approach |
|------------------|----------|
| Local / whisper / emote | Separate maps (different `MapId`) — physical isolation |
| Radio / common / department | Disabled / stripped while in tutorial mode |
| OOC | **Disabled** for players on this server/mode |
| LOOC | **Disabled** |
| Dead / ghost chat | **Disabled** (no chatting while ghosted either) |
| PDA messaging / binary / hivemind / other special channels | Disabled or sandboxed with no cross-player delivery |
| Teleport / shared landmarks | No shared playspace; existing admin tools remain for admins only |

**Preferred technical approach:** each tutorial instance on its **own map**, plus global chat blackout for non-admins in this preset.

### 7.5 Tutorial director (steps)

Each tutorial is a prototype, roughly:

```yaml
# Conceptual shape — final schema TBD during implementation
- type: TutorialPrototype
  id: TutorialTraitorBasic
  job: Passenger          # or null if antag-only package
  antag: Traitor
  map: Maps/_Wizden/TutorialServer/traitor_basic.yml
  durationHint: 8
  steps:
  - id: get-uplink
    text: tutorial-traitor-step-uplink
  - id: buy-item
    text: tutorial-traitor-step-buy
  - id: complete-objective
    text: tutorial-traitor-step-objective
```

Runtime responsibilities:

- Show current step (popup / UI / prompt — not cross-player chat)
- Detect completion conditions where implemented; stubs may use simple interact/reach markers
- Advance steps; on final step → `CompleteTutorial`
- `CompleteTutorial` → brief success feedback → kill body → despawn map → enqueue respawn
- **No skip button.** Early exit is only via `/ghost` or death, then respawn to pick a new (or same) tutorial.

Keep step detection **event-driven** where possible (SS14 ECS conventions), not heavy `Update()` polling.

### 7.6 Completion, ghost, death, and respawn

- On success: mark complete, play feedback, force death, **despawn map immediately**, respawn to role picker.
- On `/ghost` or death mid-tutorial: treat as exit → **despawn map immediately** (player is no longer in the spawning body) → allow respawn to picker.
- On disconnect: despawn private map immediately.
- Rule of thumb: **no active player in the spawning body ⇒ map is gone.**
- Respawn delay: short (0–5s); this is a practice server.
- MOTD guidance: `Type /ghost and respawn to try a new tutorial`.

### 7.7 Antagonists

All antagonist roles are pickable. Stubs are expected early.

**Traitor objectives:** plain-text placeholders only. They do **not** need to hook into real mobs, steal targets, or completable objective entities — just enough UI/text to illustrate what objectives look like (e.g. “Assassinate John Doe”, “Steal the captain's antique laser gun” as display strings).

**Nuclear Operative (example stub):** spawn a small section of the nukie map; let the player outfit themselves and try different loadouts. No real station, no team, no nuke win condition required.

**General antag needs:**

- Explicit antag grant without round population ratios
- Gear / uplink / loadout where relevant (can be stubbed)
- No dependency on other players existing
- Existing admin tools are sufficient for spectating/help — no new admin UI

---

## 8. Content Plan (Maps & Tutorials)

**All jobs are available from the start.** Ship a stub for every role early; deepen content in waves. Stubs beat hiding jobs.

### Wave A — Vertical slice + stub coverage (must ship first)

1. End-to-end pipeline: pick any job → private map → steps or stub sandbox → die/ghost → map despawns → respawn → pick again  
2. Polished-enough tutorials for a few showcase roles (e.g. Passenger/Janitor + Traitor with plain-text objectives)  
3. **Stub map fallback** for every other job/antag so nothing is locked  
4. Communication blackout verified  
5. Daily auto-restart returns to tutorial preset  

### Wave B — Deepen core crew drills

Improve stubs into real guided tutorials for high-demand roles:

- [x] Medical (doctor heal dummy, chemist Inaprovaline, paramedic rescue, CMO triage)
- [x] Engineering (AME inject, atmos analyzer/TEG theory, CE leadership)
- [x] Security (cuff dummy, detective forensics, warden brig/armory, HoS law)
- [x] Cargo / Service (bartender Screwdriver, botanist harvest, janitor mop, salvage EVA, QM orders)

### Wave C — Deepen antags & special roles

- Traitor: richer plain-text objective set + uplink practice
- Nukie: small map section + loadout practice (already a stub model)
- Other antags with dedicated small maps

Each role package should eventually include:

- Map (or small map section) file
- Prototype steps + locale strings (stubs allowed initially)
- Starting gear / loadout access as needed
- Integration or smoke coverage for spawn/despawn; fuller step tests where non-trivial

---

## 9. Implementation Phases

### Phase 0 — Foundations (this doc + decisions)

- [x] Project scope plan written
- [x] Role policy: all jobs/antags available immediately (stubs OK)
- [x] Isolation policy: no player chat of any kind
- [x] Round lifecycle: self-recycling round + daily auto-restart into tutorial mode
- [x] Profile appearance retained; no skip button; `/ghost` + respawn to switch
- [x] Traitor objectives = plain-text placeholders; no new admin tools
- [x] Server name / MOTD / rules locked (see §11)
- [x] Lock map naming / folder conventions (`Maps/_Functional/TutorialServer/`)

### Phase 1 — Mode skeleton

- [x] `TutorialServer` game rule + preset prototypes (`_Functional`)
- [x] Always-on late join + respawn hooks
- [x] Suppress normal antag/round-end rules for this preset
- [x] Daily auto-restart that **always** relaunches tutorial preset
- [x] Player session tracking on `TutorialServerRuleComponent`
- [x] Server browser name / MOTD / rules sample config
- [x] Integration test: preset starts and player can late-join

### Phase 2 — Private maps

- [x] Load map/grid per player spawn
- [x] Ownership tracking (player + spawning body)
- [x] **Immediate despawn** when owner leaves body (death / ghost) or disconnects
- [x] Spawn point convention on tutorial maps
- [x] Integration test: two private maps get different `MapId`s

### Phase 3 — Role picker + gear

- [x] Client UI listing **all** jobs and antagonist roles (stubs greyed + confirm)
- [x] Apply job outfit / antag setup (stub-friendly)
- [x] Retain character appearance from profile
- [x] Stub map fallback when a role has no polished tutorial yet
- [x] Integration coverage for stub marking / wiki roles

### Phase 4 — Tutorial director

- [x] Tutorial prototypes + step progression system (stubs allowed)
- [x] HUD / prompts for current step
- [x] No skip control; `/ghost` or death exits to respawn/picker
- [x] Completion → map despawn → respawn → picker
- [x] Traitor plain-text objective placeholders (Character UI)
- [x] Core integration tests green

### Phase 5 — Isolation hardening (zero chat)

- [x] Disable OOC, LOOC, dead chat for players in this mode (rule CVars)
- [x] Radio send/receive cancelled while rule active
- [ ] Exploit pass (shared entities, warps) — ongoing
- [x] Integration asserts chat CVars off with rule

### Phase 6 — Content Wave A

- [x] Showcase maps + stub fallback coverage for all roles (`_Functional`)
- [x] Locale + MOTD/rules polish
- [x] Smoke test (server + client connect) after Content/Resources changes

### Phase 7 — Expand content (Wave B/C)

- [x] Deep multi-goal curricula for all 24 non-stub roles (goals + sub-goals + sensors)
- [x] Wave B deepenings: Chemist, Janitor, Station Engineer, Bartender, Botanist, Medical Doctor, Security Officer + CE/CMO/RD/QM/Warden/HoS/Captain/HoP/Clown/Mime
- [x] Engineering quartet progression: Technical Assistant → Station Engineer → Atmos Tech → Chief Engineer (doors/hull/LV → MV/HV/SMES/Singulo/Tesla → distro/TEG/waste → CE leadership)
- [x] Promoted stubs to deep curricula: Paramedic, Atmospheric Technician, Salvage Specialist, Detective, Technical Assistant (Security Cadet remains stub)
- [x] Shared sensors: `SolutionContains`, `PuddleCleared`, `PracticeMobCuffed`, `PracticeMobDamageBelow`, `AmeInjecting`, `HydroHarvest`
- [x] Dedicated per-role practice maps under `Maps/_Functional/TutorialServer/Roles/`
- [x] Enclosed department-styled rooms via `tutorialRoom` + `TutorialPracticeRoomSystem` (Box/Bagel floor/wall/light cues; always-powered lights; breathable atmos)
- [x] Station-section room templates (`tutorialRoomTemplate`): crop from Saltern/Packed/other stations when possible, salvage chunks as interim crops, procedural `fallbackRoom` last resort; stamp N identical copies with goal-gated doors (`TutorialRoomTemplateSystem`)

**Section template sources** (`Resources/Prototypes/_Functional/TutorialServer/tutorial_room_templates.yml`):

| Template | Map crop | Fallback room |
|---|---|---|
| Medbay / Chem / Science / Command / Bar / Hydro / Arrivals | Saltern AABB (`tutorial_section_crops.yml`) | matching `tutorialRoom` |
| Surgery / Atmos / Janitor / Theatre | Packed AABB (`tutorial_section_crops.yml`) | Medical / Atmos / Janitor / Clown |
| Engineering | Salvage `engineering-chunk` | Engineering |
| Security / Brig | Salvage `security-chunk` | Security / Brig |
| Kitchen / Chapel / CargoOffice | Salvage small-chef / small-chapel / small-cargo | Kitchen / Chapel / Cargo |
| MaintAntag | Salvage small-syndicate | Antag |

Re-export station crops: set `TUTORIAL_EXPORT_CROPS=1` and run `ExportTutorialSectionCrops`, or host command `tutorialcropsections`.

- [x] Cargo Tech shuttle arena (`tutorialShuttleArena`) teaching helm, undock, flight keys, and docking
- [x] Practice kits via `practiceSpawns` (vendors/machines/props; always-powered tutorial machine protos)
- [x] Goal HUD checklist (`TutorialGoalSensorSystem` + client goal HUD)
- [ ] Hand-craft antagonist tutorials beyond Traitor
- [ ] Replace remaining stub roles with deep curricula role-by-role

---

## 10. Testing Strategy

Per Wizden rules, new gameplay features need integration tests under `Content.IntegrationTests/Tests/_Functional/TutorialServer/`.

**Minimum required tests:**

1. `TutorialPreset_AllowsMidRoundJoinAndRespawn`
2. `TutorialSpawn_CreatesPrivateMapPerPlayer`
3. `TutorialGhostOrDeath_DespawnsOwnedMapImmediately`
4. `TutorialComplete_UnloadsMapAndReturnsToRespawnFlow`
5. `TutorialIsolation_PlayersOnSeparateMaps` (MapId / transform assert)
6. `TutorialChat_NoPlayerToPlayerChannels` (OOC/LOOC/dead/radio as applicable)
7. `TutorialRolePicker_AllJobsAvailable_AppliesProfileAppearance`
8. `TutorialDailyRestart_ReloadsTutorialPreset` (or config/unit coverage equivalent)

Also run the `ss14-smoke-test` skill after meaningful Content/Resources changes.

---

## 11. Resolved Decisions

| # | Question | Decision |
|---|----------|----------|
| Q1 | Round lifecycle? | **Hybrid:** one long self-recycling round that cleans up as aggressively as possible, plus a **daily auto-restart** that always returns the server to **tutorial mode**. |
| Q2 | Job availability? | **All jobs immediately.** This server exists to teach every job; stubs fill gaps until polished tutorials exist. |
| Q3 | Chat policy? | **No OOC, no LOOC, no IC/radio/ghost chat with other players.** No player-to-player communication at all — no moderation burden. |
| Q4 | Appearance? | **Retain character appearance and profile settings.** |
| Q5 | Skip completion? | **Not skippable.** Players may `/ghost` and respawn to try a different tutorial. |
| Q6 | Admin tools? | **No new tools.** Existing admin tooling is enough. |
| Q7 | Traitor objectives? | **Plain-text placeholders only.** No real mob hooks or completable objective entities required — just enough to illustrate what they are. |
| Q8 | Branding / MOTD / rules? | See below. |

### Server browser / lobby copy (locked)

| Field | Text |
|-------|------|
| **Server name** | `The Functional Tutorial Server` |
| **MOTD** | `Join as any job to play a tutorial. Type /ghost and respawn to try a new tutorial` |
| **Rules** | `Don't try to brick the server.` |

---

## 12. Risks & Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Map leaks (orphaned maps) | Memory growth, lag | Despawn as soon as owner leaves spawning body; disconnect cleanup; daily restart hygiene |
| Antag systems assume multiplayer round | Broken objectives / null targets | Stub setups; traitor objectives are plain text only |
| Any remaining chat path | Forces moderation / conflict | Hard-disable all player chat channels; test matrix |
| Stub quality too low | Players confused | Fidelity rule: represent real gameplay closely enough; deepen Wave B/C |
| Daily restart lands on wrong preset | Server stops being a tutorial server | Restart config must pin `TutorialServer` preset |
| Upstream job/antag changes break tutorials | Maintenance burden | Keep tutorials in `_Wizden`; thin adapters; stub fallbacks |
| Players try to brick the server | Outages | Rules text + admin tools; keep maps ephemeral |

---

## 13. Success Criteria (v1 Done)

v1 is done when:

1. `The Functional Tutorial Server` boots the `TutorialServer` preset and stays joinable mid-round.
2. A player can pick **any job or antagonist**, spawns with their profile appearance into a private map (polished or stub).
3. Another player joining at the same time gets a **different** map and has **no chat path** to the first (including OOC/ghost).
4. Completing, dying, `/ghost`ing, or disconnecting **immediately despawns** that player's map; respawn returns them to role select.
5. Traitor shows plain-text objective placeholders; nukie-style roles can use small map-section stubs.
6. Daily auto-restart brings the server back into tutorial mode.
7. Wave A pipeline + stub coverage works; showcase tutorials exist for at least one crew job + Traitor.
8. Integration tests above pass locally.
9. Smoke test (server + client connect) passes.

---

## 14. Reference — Related Upstream Code

Useful starting points when implementing (read-only guidance; prefer Wizden wrappers):

- `Content.Server/GameTicking/GameTicker.Spawning.cs` — late join / respawn
- `Content.Server/GameTicking/Rules/RespawnRuleSystem.cs` — death → respawn queue
- `Content.Server/GameTicking/Rules/DeathMatchRuleSystem.cs` — always-on respawn mode patterns
- `Content.Server/GameTicking/Rules/LoadMapRuleSystem.cs` — map/grid loading for rules
- `Content.Server/Antag/AntagSelectionSystem.cs` — antag assignment hooks
- `Content.Server/Station/Components/StationJobsComponent.cs` — mid-round job totals (likely bypass)

---

## 15. Changelog (plan revisions)

| Date | Change |
|------|--------|
| 2026-07-27 | Initial project scope plan created |
| 2026-07-27 | Resolved Q1–Q8; added stub fidelity rule, map-despawn-on-leave-body, zero-chat policy, branding/MOTD/rules, hybrid daily restart |
| 2026-07-27 | Paths/markers → `_Functional` / `//Functional`; v1 implementation landed (Phases 1–6) |

When implementing, update checkboxes in §9 and the decision table in §11 rather than inventing parallel docs.
