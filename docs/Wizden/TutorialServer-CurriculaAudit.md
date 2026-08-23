# TutorialServer Curricula Audit

**Date:** 2026-07-28  
**Scope:** All **ready** (`stub: false`) TutorialServer job/antag tutorials under `Resources/Prototypes/_Functional/TutorialServer/roles/`.  
**References:**

- Steam guide inventory: [`Build/_Functional/steam-guides-job-tutorial-findings.md`](../../Build/_Functional/steam-guides-job-tutorial-findings.md)
- Room theming: `Resources/Prototypes/_Functional/TutorialServer/tutorial_rooms.yml`
- Official wiki (where reachable): [wiki.spacestation14.com Jobs](https://wiki.spacestation14.com/wiki/Category:Jobs) / [fandom Category:Jobs](https://space-station-14.fandom.com/wiki/Category:Jobs) (fandom often Cloudflare-blocked; wiki.spacestation14.com used as primary)

Stubs (Lawyer, Security Cadet, remaining antags, etc.) are **out of ranking** — they are Acknowledge-only sandboxes.

---

## Scoring rubric (1–5)

| Metric | 1 | 3 | 5 |
|---|---|---|---|
| **Layout** | Generic chamber; no dept cues beyond door color | Correct floor/wall/door theming; sparse furniture; practice kit on floor | Reads as a miniature of the real department (zones, machines, counters where expected) |
| **Guide / wiki** | Hold/vend/walk; little overlap with documented happy path | Partial happy path + Acknowledge tips for missing loops | Core Steam/wiki shift-start loop is playable in-tutorial |
| **Interactivity** | Mostly Acknowledge + HoldItem | Mix of Hold/Interact/Reach; few real sensors | Multiple sensor-backed outcomes (place cable, mix reagent, heal dummy, dock shuttle, etc.) |
| **Length fit** | ≪3 min or ≫15 min (non-eng) / ≫35 min (eng) | Roughly in band but padded or thin | ~5–10 min non-eng; eng quartet ~5–30 min with progressive depth |

**Composite** = average of the four metrics (rounded to 1 decimal).

**Length estimate** uses sub-goal counts + sensor complexity (not stopwatch data):

- Acknowledge / Hold / Interact ≈ 30–60s each  
- Real sensor loops (chem mix, cable place, anomaly, shuttle dock, mop puddle) ≈ 1–4 min each  

---

## Leaderboard (all ready roles)

| Rank | Job | Layout | Guide | Interact | Length | Composite | Est. time | Sub-goals (ACK/active) |
|---:|---|---:|---:|---:|---:|---:|---|---|
| 1 | Chef | 5 | 5 | 5 | 5 | **5.0** | 6–10 min | 13 (2/11) |
| 2 | Scientist | 4 | 5 | 5 | 5 | **4.8** | 8–12 min | 15 (4/11) |
| 3 | Cargo Technician | 5 | 5 | 5 | 4 | **4.8** | 12–20 min | 17 (10/7) |
| 4 | Janitor | 4 | 5 | 4 | 5 | **4.5** | 5–8 min | 11 (3/8) |
| 5 | Chemist | 4 | 5 | 4 | 5 | **4.5** | 6–10 min | 11 (3/8) |
| 6 | Research Director | 4 | 4 | 5 | 5 | **4.5** | 8–12 min | 14 (4/10) |
| 7 | Salvage Specialist | 5 | 4 | 5 | 4 | **4.5** | 8–14 min | 12 (3/9) |
| 8 | HoP | 4 | 5 | 5 | 4 | **4.5** | 8–12 min | 12 (4/8) |
| 9 | Bartender | 4 | 4 | 4 | 5 | **4.3** | 5–8 min | 10 (3/7) |
| 10 | Paramedic | 3 | 5 | 4 | 5 | **4.3** | 5–9 min | 10 (3/7) |
| 11 | Botanist | 4 | 4 | 4 | 5 | **4.3** | 5–8 min | 8 (2/6) |
| 12 | Station Engineer | 3 | 4 | 5 | 4 | **4.0** | 10–20 min | 18 (7/11) |
| 13 | Atmospheric Technician | 3 | 4 | 5 | 4 | **4.0** | 10–20 min | 17 (5/12) |
| 14 | Technical Assistant | 3 | 4 | 4 | 5 | **4.0** | 6–12 min | 14 (5/9) |
| 15 | Passenger | 3 | 4 | 4 | 5 | **4.0** | 4–7 min | 12 (2/10) |
| 16 | Security Officer | 3 | 4 | 4 | 5 | **4.0** | 5–8 min | 11 (3/8) |
| 17 | Warden | 3 | 4 | 4 | 5 | **4.0** | 5–8 min | 10 (3/7) |
| 18 | Captain | 3 | 4 | 5 | 5 | **4.3** | 5–9 min | 11 (3/8) |
| 19 | Chaplain | 3 | 4 | 4 | 5 | **4.0** | 5–8 min | 8 (3/5) |
| 20 | HoS | 3 | 4 | 4 | 5 | **4.0** | 5–8 min | 10 (3/7) |
| 21 | Quartermaster | 3 | 4 | 4 | 5 | **4.0** | 5–8 min | 12 (3/9) |
| 22 | CMO | 3 | 4 | 4 | 5 | **4.0** | 5–9 min | 13 (4/9) |
| 23 | Medical Doctor | 3 | 4 | 4 | 5 | **4.0** | 5–9 min | 11 (3/8) |
| 24 | Detective | 3 | 4 | 4 | 5 | **4.0** | 5–8 min | 10 (3/7) |
| 25 | Clown | 2 | 4 | 5 | 5 | **4.0** | 5–8 min | 10 (2/8) |
| 26 | Chief Engineer | 3 | 4 | 4 | 4 | **3.8** | 8–15 min | 14 (4/10) |
| 27 | Mime | 2 | 4 | 4 | 5 | **3.8** | 5–8 min | 8 (3/5) |
| 28 | Traitor | 2 | 4 | 4 | 5 | **3.8** | 5–9 min | 11 (4/7) |

> Ties broken by guide fidelity, then interactivity. Cargo Tech length can run long because of piloting retries.
> **CentCom Official tutorial removed** (2026-07-28) — upstream `CentralCommandOfficial` job remains.
> **2026-07-28 deepen passes:** (1) Chaplain / Mime / Traitor / Captain; (2) Clown / Paramedic / HoS / QM / Botanist; (3) CentCom removed; HoP visitor ID desk; Salvage magnet arena; Cargo sell; Eng repair; Atmos TEG; Doctor epi; CMO crew monitor; Detective pad; Clown slip.

---

## Metric deep-dives

### 1. Layout similarity (department feel)

**Strong**

| Job | Why |
|---|---|
| **Chef** | Kitchen tile, counters, sink furniture; microwave/grill/vendors in practice kit — closest to a real kitchen bay. |
| **Cargo Tech** | Full shuttle arena (cargo shuttle + bay + ATS) — highest layout fidelity in the project. |
| **Bartender / Botanist / Janitor** | Bar wood/bar tiles, hydro floors, janitor room theming match dept identity. |
| **Salvage** | Dedicated lattice/asteroid room (`TutorialRoomSalvage`) — intentional EVA aesthetic. |

**Weak / shared-chamber tax**

| Job | Gap |
|---|---|
| **All Engineering** | TA / Engineer / Atmos / CE share `TutorialRoomEngineering` (lime steel + reinforced walls). No distinct atmos pipe room, engine chamber geometry, or CE office. Engines (Singulo/Tesla/AME/TEG) are floor props in 7×7 chambers — not containment rings. |
| **All Medical** | Doctor / Chemist / Para / CMO share `TutorialRoomMedical`. No chem lab island vs treatment bay vs lobby split; ChemDispenser sits in “med room.” |
| **All Security** | Officer / Detective / Warden / HoS share `TutorialRoomSecurity`. No brig cells, perma, or armory geometry — only timer/safe props. |
| **Command** | Captain / HoP share `TutorialRoomCommand` — no bridge consoles, no ID desk furniture beyond spawned computer. |
| **Clown / Mime** | Generic `TutorialRoomService` with almost no theming props. |

**Systemic layout gap:** chambers are goal-count-driven 7×7 boxes. They cue department *materials*, not real map topology (chem window, brig line of sight, eng SMES bay, cargo warehouse aisles).

---

### 2. Guide / wiki similarity

Sources: Steam findings doc + wiki.spacestation14.com job pages (Chemist, Station Engineer, Cargo Technician sampled; others inferred from Steam RU/EN handbooks).

| Job | Guide sources | Match | Gaps vs guides/wiki |
|---|---|---|---|
| **Scientist** | RnD handbook; anomaly pedagogy | Excellent | Lathe / research console “first 10 minutes” still missing (Steam takeaway). |
| **Chef** | How to Chef | Excellent | Recipe breadth intentionally omitted (correct). |
| **Janitor** | Janitor Guide (Wizden) | Excellent | Light replacer / cleanades / holosign / mousetrap not taught. |
| **Chemist** | Joey Strumm; wiki Chemist | Strong | Only Inaprovaline; no Bic/Derm/Dylo ladder, no ChemMaster bottle/pill output assert, no plasma path. |
| **Cargo Tech** | Wiki Cargo + RU cargo | Strong for **piloting** | Weak on sell/bounty/appraisal loop (wiki’s primary money path). |
| **Bartender** | Bartending 4 Newbies | Strong | Missing “stock glasses into Booze+Soda dispensers”; shotgun/exits is Acknowledge only. |
| **Technical Assistant** | Station Power + RU Eng | Strong for doors/LV/hull | No insulated gloves emphasis; no T-ray; pry vs hack both not fully distinct. |
| **Station Engineer** | Wiki Eng + Station Power | Partial | Wiki: AME first every shift — tutorial moved AME to CE and teaches Singulo/Tesla as **inspect + tip**, not start. Substation/SMES are interact, not construct. |
| **Atmos** | Station Power TEG chapter | Partial | Distro/filter/TEG are inspect + theory; no hot/cold loop run, no pipe wrench network. |
| **CE** | Station Power + leadership | Partial | Good priority/AME-backup framing; little real delegation gameplay. |
| **Security Officer** | RU Sec handbook | Good arrest slice | No brig delivery timer, no Space Law menu, no stun→cuff timing drill. |
| **Warden** | RU Sec | Good lite | No sentence lengths, no perma, no evidence locker workflow. |
| **Detective** | RU Sec detective | Good lite | Scanner interact is tag-click, not real forensics readout; no print pad use on mob. |
| **Paramedic** | Intro to Paramedic | Strong | Heal + `PracticeMobBuckled` transport; crew monitor still missing. |
| **Doctor / CMO** | Medical handbook | Partial | Heal dummy good; no bloodloss/chem hypos, no surgery/clone, CMO lacks crew monitor. |
| **Botanist** | How to Botany | Strong | Harvest + ObtainItem wheat handoff; still skips water/nutrient lights / Robust Harvest. |
| **Salvage** | Cargo salvage + Sadler | Partial | Magboots + ore good; no magnet, wreck, or expedition. |
| **QM** | RU Cargo QM | Partial | Orders + package/cart floor loop; no budget/approve order, no sell. |
| **Captain / HoP** | Weak Steam; wiki command | Partial | Captain: comms + disk stow; HoP: ID console click — no real alert/ID write. |
| **Clown / Mime** | Clown/Mime guides | Strong | Mime WallInvisible; Clown cream-pies dummy; slip still tip-only. |
| **Chaplain / CentCom** | Gap / CentCom RU | Partial | Chaplain bible heal; CentCom fax open — no full service/fax-send. |
| **Traitor** | New Player antag | Partial | Emag door + cuff drill; uplink store UI still missing. |

---

### 3. Interactivity

**Tier A — real outcome sensors (best)**

| Job | Sensors used |
|---|---|
| Scientist / RD | SpawnAnomaly, ScanAnomaly, StabilizeAnomaly, RemoveAnomaly |
| Chef | Obtain/Hold finished FoodBurgerPlain / FoodCakePlain (+ microwave interact) |
| Chemist | SolutionContains Inaprovaline |
| Janitor | PuddleCleared |
| Cargo Tech | PilotShuttle, ShuttleThrottle, Dock/Undock |
| TA / Engineer | WiresPanelOpen, MapHasEntity (LV/MV/HV cables) |
| CE | AmeInjecting |
| Sec / Warden / HoS / Detective | PracticeMobCuffed |
| Doctor / CMO / Paramedic | PracticeMobDamageBelow |
| Botanist | HydroHarvest |
| Bartender | SolutionContains ScrewdriverCocktail |
| Chaplain | PracticeMobDamageBelow (bible) |
| Mime | MapHasEntity WallInvisible |
| Captain | StowItem NukeDiskFake |
| Traitor | PracticeMobCuffed (+ emag InteractTargetTag) |
| Clown | PracticeMobCreamPied |
| Paramedic | PracticeMobBuckled (+ heal) |
| Botanist | HydroHarvest + ObtainItem WheatBushel |

**Tier B — interact/hold scaffolding (medium)**

Most heads, QM, Salvage, Clown — HoldItem + InteractTargetTag + UseInHand + ReachMarker.

**Tier C — Acknowledge-heavy (weak)**

| Job | Issue |
|---|---|
| **Mime** | 50% Acknowledge; wall tip is text-only |
| **Cargo Tech** | Many ACK explainers between real dock steps (necessary pedagogy, lowers “active %”) |
| **Engineer engines goals** | Singulo/Tesla are InteractTargetTag + Acknowledge — look interactive, don’t start engines |
| **HoP / Salvage** | Still thin vs deepened peers (ID write / wreck still open) |

---

### 4. Length fit

| Band | Jobs | Notes |
|---|---|---|
| **Sweet spot (~5–10 min)** | Chef, Chemist, Janitor, Bartender, Sec Officer, Warden, Doctor, CMO, Detective, HoP, QM, Passenger, Clown | Sub-goal counts 9–13 with 1–2 real loops |
| **Eng band (~5–30 min)** | TA (~6–12), Engineer (~10–20), Atmos (~10–22), CE (~8–15) | Progressive; Engineer/Atmos tip toward theory padding rather than build time |
| **Can run long** | Cargo Tech (10–18), Scientist/RD (8–12) | Docking retries / anomaly stabilize can stretch |
| **Too short** | HoP, Salvage | Thin vs peers after second deepen pass |

---

## Per-job analysis (detail)

### Engineering quartet

#### Technical Assistant — composite 4.0
- **Layout (3):** Eng room; hack airlock + breach marker + APC are props, not a maint corridor with cut cable underfloor.
- **Guide (4):** Matches Steam/RU door panel + LV repair + hull patch themes. Good progression blurb to Engineer.
- **Interact (4):** `WiresPanelOpen` + `MapHasEntity` CableApcExtension are real; spacing is tag-click with steel (not plating construction).
- **Length (5):** Fits junior eng lesson.
- **Gaps:** No insulated gloves / shock lesson; no crowbar-pry vs hack contrast chamber; APC doesn’t actually re-power from placed cable; breach doesn’t change tiles.

#### Station Engineer — composite 3.8
- **Layout (3):** Substation/SMES/monitor/gens crammed into chambers — not an engine bay.
- **Guide (4):** Wiki wants AME-first then permanent power; tutorial teaches MV/HV place + inspect Singulo/Tesla. Directionally right for the quartet split, weaker vs wiki shift-start.
- **Interact (4):** Cable place is strong; SMES/substation/monitor/gens are click/Acknowledge.
- **Length (4):** OK for eng band; engine tips add minutes without play.
- **Gaps:** No PA/containment build; no working SMES charge; no solar; no real MV/HV *repair of pre-cut* run (player places new cable anywhere); AME moved entirely to CE.

#### Atmospheric Technician — composite 3.8
- **Layout (3):** Same eng room; vent/scrubber/filter/TEG props without pipe manifold.
- **Guide (4):** Hits distro / waste filter / TEG theory from Station Power; no Frezon (correctly out of scope).
- **Interact (4):** Highest active count; still mostly InteractTargetTag.
- **Length (4):** Theory Acknowledges fill time; little “wait for pressure.”
- **Gaps:** No wrench-rotate pipe network; TEG never generates; no air alarm; no canister mix into distro.

#### Chief Engineer — composite 3.8
- **Layout (3):** Shared eng room + AME pack + token TEG/Singulo.
- **Guide (4):** Leadership / AME-as-backup / priority tip aligns with Station Power + CE role; weak on real oversight tools (alerts, cameras, crew monitor).
- **Interact (4):** AmeInjecting is the only heavy sensor; rest is inspect + magboots.
- **Length (4):** Fine as capstone if player already did TA→Eng→Atmos; alone it feels like “hold headset + start AME.”
- **Gaps:** No unique CE office; no RCD/project; no atmos coordination mechanic beyond tips.

---

### Science

#### Scientist — composite 4.8
- **Layout (4):** Science room + anomaly pad / CHIMP / APE suite — best science fidelity.
- **Guide (5):** Full spawn→scan→stabilize→remove matches pedagogical anomaly path.
- **Interact (5):** Best sensor stack in the project.
- **Length (5):** Solid 8–12 min.
- **Gaps:** No research console / lathe / anomaly vessel; Steam “first 10 min RnD” still open.

#### Research Director — composite 4.5
- Same anomaly spine as Scientist + command gear.
- **Gaps:** Duplicate of Scientist gameplay; little unique RD (server priorities, robotics, AI). Composite slightly below Scientist because leadership is Acknowledge-only.

---

### Medical

#### Chemist — composite 4.5
- **Layout (4):** Machines present; still in shared med room (no chem window).
- **Guide (5):** Dispenser → Inaprovaline → ChemMaster → grind produce matches Steam “machine loop before encyclopedia.”
- **Interact (4):** SolutionContains is real; ChemMaster step is only open-UI interact.
- **Gaps:** No pill/bottle ObtainItem assert; no second basic med; no labeler.

#### Medical Doctor — composite 3.8
- Heal dummy with packs/ointment is the right wiki core; analyzer + vend scaffolding.
- **Gaps:** No blood loss, no chemicals, no crit→defib/stasis, no surgery.

#### Paramedic — composite 4.3
- Crit dummy heal (`maxDamage` 25) + **`PracticeMobBuckled`** on `TutorialRollerBed` + pull to marker.
- **Gaps:** No crew monitor UI; epi still not required by sensor.

#### CMO — composite 3.8
- Shares doctor heal + hypo UseInHand + triage tip.
- **Gaps:** No crew monitor UI, no chem priority, no cloning/cryo leadership beat.

---

### Security

#### Security Officer — composite 4.0
- Nonlethals hold + cuff dummy + law tip matches RU arrest slice.
- **Gaps:** No stun-then-cuff requirement; no brig cell walk with timer; baton/disabler never fired for sensor.

#### Warden — composite 4.0
- Cuff + brig timer + gun safe interact + armory tip — good lite.
- **Gaps:** No sentence table; no locker search; no permabrig.

#### Detective — composite 3.8
- Scanner → evidence tag → pad → cuff optional.
- **Gaps:** Evidence is a tagged napkin (no fiber UI); no print-from-mob; no case folder.

#### HoS — composite 4.0
- Comms console open + cuff dummy + SecTech + Space Law tip (differentiated from Officer).
- **Gaps:** No real alert-level change; still shares security room geometry.

---

### Cargo

#### Cargo Technician — composite 4.5
- **Layout (5):** Unique shuttle arena.
- **Guide (4):** Piloting/docking is excellent; wiki money loop (bounty/ATS sell) absent.
- **Interact (5):** Real dock/undock/throttle.
- **Length (4):** Can exceed 10 min for new pilots; ACK-heavy between steps.
- **Gaps:** No crate sell, no order console, no mail.

#### Quartermaster — composite 4.0
- Appraisal + orders console + package/manifest holds + cart vend + priority/shuttle tips.
- **Gaps:** No approve-order/budget sensor; no ATS sell (Cargo Tech still owns piloting).

#### Salvage Specialist — composite 3.5
- Lattice room + magboots/PKA/ore — good EVA vibe.
- **Gaps:** No magnet wreck, no expedition, no ore processor; guidebook is Cargo not Salvage-specific.

---

### Service

#### Chef — composite 5.0 (highest)
- Layout + burger/cake machine loops + vendors — reference implementation.
- **Gaps:** No botany handoff item requirement; no menu/service window RP.

#### Janitor — composite 4.5
- Puddle→drain + potassium tip matches Wizden Janitor Guide closely.
- **Gaps:** Lights, cleanades, holosign, trash run beyond holding bag.

#### Bartender — composite 4.3
- Screwdriver cocktail SolutionContains is real mixing.
- **Gaps:** No dispenser stocking; no shotgun equip; metamorphic glass not required.

#### Botanist — composite 4.3
- Plant → HydroHarvest → **ObtainItem WheatBushel** handoff tip.
- **Gaps:** No nutrient/water sensors; no Robust Harvest / clippers.

#### Clown — composite 3.8
- HONK + **cream pie `PracticeMobCreamPied`** + soap hold + slip tip.
- **Gaps:** Generic service room; slip-someone sensor still deferred.

#### Mime — composite 3.8
- Vow + pen + **`MapHasEntity` WallInvisible** (invisible wall action) + crayon/paper.
- **Gaps:** Shared service room; no themed mime chamber; paper write not required.

#### Chaplain — composite 4.0
- `TutorialBible` heal on `TutorialPracticeMobLightDamage` (`PracticeMobDamageBelow`) + smite tip + wine.
- **Gaps:** No full service RP; no null rod antag beat.

---

### Command / CentCom / Antag

#### Passenger — composite 4.0
- Best onboarding for controls (move, inventory, drink, pry).
- **Gaps:** Layout is civilian lounge, not arrivals; no ID/PDA lesson; no “ask HoP” interact.

#### Captain — composite 4.0
- Comms console open (`InteractTargetTag`) + fake disk hold → **`StowItem`** + shuttle tip.
- **Gaps:** No real alert-level change (rooms lack station AlertLevel); no fax; no nuke authenticity beyond fake disk.

#### HoP — composite 3.5
- ID console click + stamp/paper.
- **Gaps:** No actual access write; no job change; no line/queue.

#### CentCom Official — composite 3.5
- Stamp/headset + **`TutorialFaxCentcom` open** + inspection walk + crew-role tip.
- **Gaps:** Fax is open-UI only (no send); no paired fax network; Steam CentCom culture still lite.

#### Traitor — composite 3.8
- Objectives ACK + emag airlock click + flash/cuff dummy + uplink tip.
- **Gaps:** No uplink store UI; emag step is InteractTargetTag (may not apply real emag); antag room not infiltration slice.

---

## Cross-cutting gaps

1. **Shared rooms (partially fixed)** — Chemist → `TutorialRoomChem`, Warden → `TutorialRoomBrig`, Atmos → `TutorialRoomAtmos`, CE → `TutorialRoomCE`. Doctor/Para/CMO still share Medical; Detective/HoS/Officer still share Security; TA/Engineer still share Engineering.
2. **Inspect ≠ operate** — Singulo/Tesla/SMES/filter often still advance on `InteractTargetTag` + Acknowledge. TEG power + pipe place improved Atmos.
3. **Wiki shift-start vs quartet pedagogy** — Engineer wiki says AME first; Wizden quartet teaches AME at CE. Document that choice in-picker blurbs so players aren’t confused vs wiki.
4. **~~Cargo money loop missing~~** — **Done** (Cargo Tech sell + bounty; QM approve + sell pad).
5. **Heads / soft-skills** — Captain alert Blue + disk; HoP ID-write; Traitor uplink buy; Scientist/RD point to RA for lathe.
6. **~~Paramedic transport~~** — **Done** (`PracticeMobBuckled` on `TutorialRollerBed`).
7. **Science lathe gap** — Intentional split: RA owns `ResearchUnlocked` / `LathePrinted`; Scientist/RD Acknowledge tip points there.
8. **Length imbalance** — Bottom quartile can still finish quickly; eng theory can pad without adding mastery.

---

## Recommended priority (post P0–P2 deepen, 2026-07-28)

| Priority | Work | Why |
|---|---|---|
| ~~P0~~ | ~~QM approve + sell~~ | **Done** (`CargoOrderApproved` + room sell pad `CargoSold`) |
| ~~P0~~ | ~~Chem / Brig / Atmos rooms~~ | **Done** (`TutorialRoomChem` / `Brig` / `Atmos`) |
| ~~P1~~ | ~~Sec stun→cuff + brig timer~~ | **Done** (`PracticeMobStunned`, `BrigTimerStarted`) |
| ~~P1~~ | ~~Traitor uplink buy~~ | **Done** (`StorePurchased` + AddUplink bootstrap) |
| ~~P1~~ | ~~Atmos pipe place~~ | **Done** (`MapHasEntity` GasPipeStraight) |
| ~~P2~~ | ~~Cargo bounty sell~~ | **Done** (`CargoBountyFulfilled`) |
| ~~P2~~ | ~~Captain alert Blue~~ | **Done** (`AlertLevelChanged`) |
| ~~P2~~ | ~~Chemist Dylovene + filled PillCanister~~ | **Done** |
| ~~P2~~ | ~~Scientist/RD → RA blurbs~~ | **Done** (Acknowledge tips; lathe stays on RA) |
| P3 | Shared Med/Sec room variants for Doctor/Para/CMO/Detective/HoS | Layout polish |
| P3 | Chemist bottle-labeler / plasma; Atmos connected manifold | Breadth |
| P3 | Real emag door access; Captain fax-send | Edge fidelity |

---

## Appendix A — Ready role counts

| Category | Ready roles |
|---|---|
| Civilian | Passenger |
| Service | Bartender, Botanist, Chaplain, Chef, Clown, Janitor, Mime |
| Cargo | Cargo Technician, Quartermaster, Salvage Specialist |
| Medical | Chemist, Medical Doctor, Paramedic, CMO |
| Science | Scientist, RD |
| Engineering | Technical Assistant, Station Engineer, Atmos Tech, CE |
| Security | Security Officer, Detective, Warden, HoS |
| Command | Captain, HoP |
| CentCom | Central Command Official |
| Antag | Traitor |
| **Total ready** | **29** |

Stub remainder: see `tutorial_roles.yml` (crew + antag stubs; Security Cadet intentionally stub).

---

## Appendix B — Method notes

- Interactivity “active” = any `TutorialStepComplete` other than `Acknowledge`.
- Guide scores weigh **Steam happy paths** heavily where a dedicated guide exists; otherwise wiki job page duties; RU White Dream handbooks used for Sec/Cargo/Med structure.
- Layout scores judge **themed practice rooms + practiceSpawns**, not hand-authored full station maps (roles with `room:` build procedural chambers; Cargo Tech uses `shuttleArena`).
- This audit is qualitative; re-run after major role YAML rewrites.
