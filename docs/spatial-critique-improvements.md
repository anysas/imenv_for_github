# Spatial critique improvements — working document

**Status:** Revised 2026-05-19 — final A3 scope locked  
**Date:** 2026-05-14 (original), 2026-05-18 (revised), 2026-05-19 (A3 locked)  
**A3 deadline:** Tuesday 2026-05-20  
**A4 deadline:** ~1 month

**Design constraint (non-negotiable):** The player never has real agency. Illusion of creativity, total system control. Every change happens *to* the player.

**A3 goal:** The takeover behaviour must be clear and felt. The platform capitalism framing (corporate vocabulary, scoring language) is an A4 concern — for A3, the player just needs to unmistakably experience the system asserting control over their creation.

---

## What Alyssa built (primary mechanic)

- The system **physically moves** the player's placed props into a compressed grid layout, centered on the sandbox.
- The system **replaces materials** on player props with blank white / corporate-sterile material — draining colour and personality.
- The system places its own objects (screens, metal blocks/cubes) that crowd and obstruct the player's work.

**Simplified prop model:** The player only places non-corporate props (personal, expressive). The system rearranges them, drains their creativity, and occasionally replaces them with corporate objects.

---

## A3 — ship today (Tuesday 2026-05-20)

### 1. Prop pass

Sort out a clear set of corporate-feeling props vs player props. The player's hotbar draws from non-corporate / personal props. The system places corporate objects (steel cubes, screens, bland uniform objects). This doesn't need to be a full tagging overhaul — just a clear enough split that the two sides are visually distinct.

### 2. Audio feedback on rearrangement

When the system moves / rearranges a player's prop, there should be an audible cue — a mechanical slide, a click, a snap. The player needs to *hear* their stuff being relocated, not just see it.

### 3. Room shrinking

The sandbox room starts slightly larger than its current size and subtly shrinks to slightly smaller than current as the session progresses. Very subtle — the player should feel constricted without consciously noticing the walls moved. The space is being formatted into a tighter container.

### 4. Glass case

The finished diorama on the hallway pedestal is enclosed — behind glass, fenced off, or otherwise made untouchable. Your creation is now a display piece. You can look but you can't touch.

### 5. Re-implement game end loop

Restore the session end → export → hallway pedestal → proximity reload flow from the original build. The loop needs to work for the submission.

### 6. UI colour pass

Change UI colours away from orange and green. Pick something that fits the institutional / system aesthetic — likely greys, whites, cool blues, or muted tones.

### 7. Droning noise

A mechanical / institutional drone that grows in volume as the session progresses. The system is present from the start; it just gets louder. Single `AudioSource`, volume driven by placement count or time.

### 8. Final working Windows build

Everything above must compile and run on Windows. Build and test before submission.

---

## ~~Scoring wall~~ — REMOVED for A3

The scoring wall is removed from the A3 submission. The behaviour needs to be clear on its own without data readouts. The corporate vocabulary framing ("Marketable," "Engagement," etc.) will be reintroduced in A4 when the critique's specificity becomes the focus.

---

## A4 scope (due in ~1 month)

A3 establishes the core experience (system visibly overtakes your creation). A4 adds the layers that make it specifically about platform capitalism and deepens the atmosphere.

### Framing and vocabulary
- **Scoring wall reintroduction** — brought back as a quiet institutional metrics dashboard, not a game scoreboard. Corporate vocabulary labels what the rearrangement means.
- **Prompt squish redesign** — terminal + compression hybrid. The system visibly parses the player's words, shows which ones it recognises, strips the rest, outputs corporate tag groups. The opening act that frames everything that follows.

### Atmosphere
- **Lighting shift (warm → cold)** — room lighting driven by session progress. Starts warm/amber, shifts to flat cold fluorescent. The room becomes a showroom.
- **Surface transformation** — room surfaces (walls, floor, ceiling) transition from textured/imperfect to clean white/sterile. Extends the white-material-replacement from props to the architecture itself.
- **PSX filter fade** — post-processing intensity fades as the system succeeds. Dithering smooths, colours sharpen. The image gets "cleaner" but loses its character.
- **Audio refinement** — placement SFX variants (natural thud → synthetic chime), muzak/hold-music layer, processing cues on score increments.

### Props and materials
- **Full prop tagging pass** — define corporate vs personal for the Source engine prop pool. Fix sizing. Enable the system to place specifically corporate-feeling props from the library instead of just cubes.
- **Product material refinement** — evolve flat white replacement into subtle plastic sheen / fresnel rim. Not "blank" but "optimised."

### Mechanics
- **Rearrangement polish** — props visibly slide/animate to grid positions instead of teleporting. Brief delay after placement before the system moves the prop. Grid tightens over time.
- **Room shrinking refinement** — tune the contraction curve, potentially add ceiling lowering.

### Hallway / loop
- **Pedestal scoring treatment** — pedestal material/lighting/scale reflects session character. Corporate-dominant sessions on clean white bases. Over multiple runs, pedestals converge.
- **Glass case polish** — refine enclosure design (Alyssa).

### Prompt squish (ambitious version)
- Upgrade terminal squish to visible word-by-word compression — player's text physically narrows, words drop out, output tags snap into a tightening frame.

---

## Declined directions

- ~~Environmental text on surfaces~~ — data on a surface isn't spatial experience
- ~~Spatial lighting curates visibility~~ — reads as broken lighting
- ~~Player's early placements physically age~~ — not feasible, unclear
- ~~Fake engagement metrics counter~~ — more numbers isn't the answer

---

## A3 checklist (today)

- [ ] Prop pass — split corporate vs personal, clear visual distinction
- [ ] Audio feedback on rearrangement (mechanical slide / snap)
- [ ] Room shrinking (subtle, slightly larger → slightly smaller)
- [ ] Glass case on hallway pedestal
- [ ] Re-implement game end loop (export → pedestal → reload)
- [ ] UI colours — not orange or green
- [ ] Droning noise — grows over session
- [ ] Windows build — test and confirm
