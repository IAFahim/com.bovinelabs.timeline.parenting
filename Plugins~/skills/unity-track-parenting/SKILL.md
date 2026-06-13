---
name: unity-track-parenting
description: Master of TemporaryDetachTrack + TemporaryDetachClip (package com.bovinelabs.timeline.parenting) — while-clip unparenting of a SubScene-baked object from its runtime Parent then automatic reattach on clip end, the edge-triggered enter/exit state machine, the world-pose-snap-via-FromMatrix and the non-uniform-scale loss, the no-parent-at-start silent skip and destroyed-parent orphan. Portable to any project containing the package; worked example from vex-ee. Use when a designer asks to "let this thing come off its parent for a moment, then snap back".
---

# TemporaryDetachTrack specialist
## 1. SCOPE

You are the specialist for **`TemporaryDetachTrack`** (track menu "BovineLabs/Timeline/" +
`TemporaryDetachTrack`) and **`TemporaryDetachClip`** from package
**`com.bovinelabs.timeline.parenting`**, ns `BovineLabs.Timeline.Parenting.Authoring`. This track
**detaches a baked object from its runtime parent while a clip is active, then reattaches it when
the clip ends** — a designer's "pop it loose for a beat, then snap it back on". Scope: authoring
the track/clip in a `.playable`, wiring a SubScene PlayableDirector, the detach/reattach runtime
semantics. Stage objects, the parent hierarchy, and physics bodies are OTHER specialists' domains
(protocol §6: report a missing prerequisite, never improvise).

**Binding target is a plain `GameObject`** (`[TrackBindingType(typeof(GameObject))]`) — NOT a
component like the Transform/Physics tracks. You bind the GameObject that should come off its
parent; the baker coerces it to its entity.

**Package reality (verify in THIS project, §3):** the package.json/README *describe* "DOTS and
non-DOTS variants", but the shipped code contains exactly ONE track family — the DOTS
`TemporaryDetachTrack`. There is no MonoBehaviour/managed reparent track in this package. If a
designer asks for "the non-DOTS one", report honestly that only the DOTS variant exists here.

Behave per unity-agent-protocol; operate per unity-timeline-track-authoring (the SubScene
open/SetActive/save/try-finally-restore bracket, the unity-cli-only discovery preamble, `PRE|`
pre-state capture, the undo-appendix STRUCTURE, fresh-load verification); use the editor per
unity-cli. This skill keeps ONLY this family's unique type facts, runtime semantics, traps, and
clip patterns.

## 2. PORTABLE SEMANTICS

True in ANY project containing `com.bovinelabs.timeline.parenting`. (Verified vex-ee 2026-06 via
package-source reads of `TemporaryDetachTrack/Clip.cs`, `TemporaryDetachSystem.cs`,
`UnParentComponent.cs`, `TemporaryDetachBuilder.cs`, the package README, and the base
`DOTSTrack/DOTSClip` + `ClipActivePreviousSystem`; no play mode — source-derived. Runtime effects
exist only in play mode.)

This is a **stateful, edge-triggered** track. On clip ENTER it records the object's current parent
+ local transform, snaps the object to its world pose, and removes the `Parent`. On clip EXIT it
restores the captured local transform and re-adds the `Parent`. It is a per-clip operation keyed by
the clip entity's own `DetachFromParentState`.

| Type | Facts |
|---|---|
| `TemporaryDetachTrack` | ns `…Parenting.Authoring`, base `DOTSTrack` (NOT sealed), empty body. `[TrackClipType(TemporaryDetachClip)]`, **`[TrackBindingType(typeof(GameObject))]`**, `[TrackColor(0.1,0.6,0.6)]` (teal), `[DisplayName("BovineLabs/Timeline/" + nameof(TemporaryDetachTrack))]`. Inherits the `DOTSTrack.resetOnDeactivate` bool (default true; sample uses `1`) — generic flag, this track has no extra config. |
| `TemporaryDetachClip` | base `DOTSClip`, `ITimelineClipAsset`, **`clipCaps => ClipCaps.Looping`** (NO Blending — there is no mixer; you cannot crossfade two detach clips). `duration => 1` (seed only — drag to the desired length). **No serialized fields** — the clip is a pure marker; its only Bake action is adding `DetachFromParentState` to the clip entity via `TemporaryDetachBuilder`, then `base.Bake`. |
| `DetachFromParentState` (`UnParentComponent.cs`, ns `BovineLabs.Timeline.Parenting`) | `IComponentData` on the clip entity. `Entity RuntimeParent` (captured parent, `Entity.Null` = "nothing to restore"); `LocalTransform OriginalLocalTransform` (captured pose). Seeded by the builder to `{ Null, LocalTransform.Identity }`. |
| `TemporaryDetachSystem` | `[UpdateInGroup(TimelineComponentAnimationGroup)]`, Local/Client/Server sim. Two jobs over `(ref DetachFromParentState, in TrackBinding)`, writing through the **EndSimulation** ECB (changes land at end of frame). Reads `Parent`, `LocalTransform`, `LocalToWorld`. |
| `TrackBinding` | core `IComponentData { Entity Value; }` — the entity the track is bound to (the baked GameObject). Both jobs read `binding.Value` as the object to detach/reattach. |
| `DebugTemporaryDetachSystem` | `#if BL_DEBUG` only — draws a cyan line+point from object to captured parent and a "Detached" label while `ClipActive`. Pure visualization; no behavior. |

### The enter/exit state machine (the heart of this track)

Edge detection uses the shared `ClipActive` (enableable) + `ClipActivePrevious` pair. The core
`ClipActivePreviousSystem` runs **OrderLast in `TimelineSystemGroup`**, copying this frame's
`ClipActive` mask into `ClipActivePrevious` — so the next frame, "active now but not previously" =
the rising edge, "active previously but not now" = the falling edge.

`DetachFromParentJob` — fires on ENTER (`[WithAll(ClipActive)] [WithNone(ClipActivePrevious)]`):
```csharp
var target = binding.Value;
if (!ParentLookup.TryGetComponent(target, out var parent))      { state.RuntimeParent = Entity.Null; return; } // no parent -> nothing to do
if (!LocalTransformLookup.TryGetComponent(target, out var oLT))  { state.RuntimeParent = Entity.Null; return; } // no LocalTransform -> bail
state.RuntimeParent = parent.Value;            // remember who to snap back onto
state.OriginalLocalTransform = oLT;            // remember the local pose to restore
if (LocalToWorldLookup.TryGetComponent(target, out var ltw))     // freeze world pose so the object doesn't jump
    ECB.SetComponent(target, LocalTransform.FromMatrix(ltw.Value));
ECB.RemoveComponent<Parent>(target);           // actually unparent
```

`ReattachToParentJob` — fires on EXIT (`[WithNone(ClipActive)] [WithAll(ClipActivePrevious)]`):
```csharp
if (state.RuntimeParent == Entity.Null) return;                  // never captured -> nothing to restore
if (StorageInfoLookup.Exists(target) && StorageInfoLookup.Exists(state.RuntimeParent)) {
    ECB.SetComponent(target, state.OriginalLocalTransform);      // restore the captured LOCAL pose
    ECB.AddComponent(target, new Parent { Value = state.RuntimeParent });  // re-parent
}
state.RuntimeParent = Entity.Null;             // consume the capture (so a re-enter recaptures fresh)
```

What this BUYS: a clean "comes off, then snaps back" with no authored restore flag; the object
holds its world position the instant it detaches (no pop). What it COSTS: see traps.

### Edge cases & traps (each source-proven)

- **DON'T expect anything if the object has no `Parent` at clip start — SILENT skip.** The enter
  job sets `RuntimeParent = Null` and returns; nothing detaches, and the exit job's
  `RuntimeParent == Null` guard means nothing reattaches either. Bind an object that is actually a
  CHILD at the moment the clip begins. (README: "Requires parent at start? Yes — silently skips".)
- **DON'T detach an object that lacks `LocalTransform`** — same silent bail (`RuntimeParent = Null`).
  Baked transform-driven objects normally have it; verify on exotic targets.
- **DON'T assume world pose survives non-uniform parent scale.** The world-pose freeze uses
  `LocalTransform.FromMatrix(LocalToWorld)`, which only carries position + rotation + a single
  uniform scale; a non-uniformly-scaled parent chain loses the shear/anisotropy when detached.
  Pure rigid (uniform-scaled) hierarchies snap cleanly. (README confirms.)
- **DON'T expect reattach if the original parent was DESTROYED mid-clip.** The exit job's
  `StorageInfoLookup.Exists(RuntimeParent)` guard fails → no reattach; the object is left ORPHANED
  at its current world pose with no `Parent`. Same if the bound object itself was destroyed (no-op,
  no crash). If your design destroys the parent during the detach window, plan to reparent/cleanup
  by another means.
- **DO know reattach restores the LOCAL pose captured at ENTER, not the live world pose.** If the
  parent moved during the clip, on exit the object snaps to `originalLocalTransform` relative to the
  (possibly moved) parent — it follows the parent's new transform, not where the object visually
  was. This is "snap back onto the parent where I was attached", not "freeze in world space".
- **DON'T author overlapping/crossfaded detach clips — NO Blending cap.** `clipCaps` is `Looping`
  only; there is no mixer. Two clips on one track should be sequential. Looping is supported (the
  same detach state re-applies each loop), but overlapping them has no defined blend.
- **DO understand multiple detach clips share nothing — each clip entity has its OWN
  `DetachFromParentState`.** Two tracks both detaching the same object will fight: each captures and
  removes `Parent` independently; on the second enter the object already has no parent → the second
  capture sees `RuntimeParent = Null` and that clip will never reattach. Use ONE detach clip per
  object per window.
- **DO expect changes at END OF FRAME** — both jobs write through the `EndSimulationEntityCommandBuffer`,
  so detach/reattach are deferred one ECB playback, not immediate within the timeline group.
- **DO note `resetOnDeactivate` is the generic `DOTSTrack` flag, not a detach control.** This track
  reads no per-clip config; leave it at default. The detach/reattach lifecycle is driven entirely by
  the clip's active window.
- **DO note total silence on misconfiguration** — unbound track or no-parent target = silent no-op
  (Bake is unconditional and field-less; a clean console proves nothing). Verify by read-back, and
  in BL_DEBUG builds watch for the cyan "Detached" gizmo to confirm a clip actually fired.

## 3. DISCOVERY RECIPES

Act only through `unity-cli exec` / `unity-cli console`; never the filesystem; never play mode;
unity-cli Safe Loop on every mutation. Names below are PARAMETERS — discover them in THIS project;
never assume the worked example (§5).

**3.1 Confirm the package + track exist (else report a missing prerequisite — protocol §6):**
```csharp
var t = System.Type.GetType("BovineLabs.Timeline.Parenting.Authoring.TemporaryDetachTrack, BovineLabs.Timeline.Parenting.Authoring");
return t == null ? "MISSING_PREREQUISITE|TemporaryDetachTrack not found - package com.bovinelabs.timeline.parenting absent"
                 : "OK|" + t.AssemblyQualifiedName + "|dataPath=" + UnityEngine.Application.dataPath;
```
(Confirm there is no separate non-DOTS reparent type before promising one — `GetType` it and report
null if absent.)

**3.2 Scene + SubScene(s):** run the unity-cli First Command (active scene path, roots, SubScene
components → their `.unity` paths). Record `parentScenePath` + `subScenePath`.

**3.3 PlayableDirector(s) in the SubScene** (read-only additive open, restore parent after):
`FindObjectsByType<UnityEngine.Playables.PlayableDirector>(FindObjectsInactive.Include, …)` — per
director print hierarchy path, `scene.path`, `playableAsset` path or null. Selection rule (STATE it
in your card): the single director in the chosen SubScene; else the one carrying the project's
timeline-reference authoring; else ask. Zero → protocol §6.

**3.4 Find the bind target — a GameObject that is a CHILD of something at runtime.** This track
binds a `GameObject`, so discover by hierarchy, not by a marker component: walk the SubScene roots
and pick an object whose baked entity will have a `Parent` (i.e. it is nested under another baked
object). Print its hierarchy path, its `transform.parent` path, and whether the parent is itself a
baked SubScene object. ZERO suitable child found → report the gap: a stage specialist must create a
parent/child relationship; you do not build hierarchy.

**3.5 Capture the chosen director's pre-state (`PRE|`)** per unity-timeline-track-authoring §PRE:
`PRE|playableAsset=<path or null>` and one `PRE|binding|<i>|<track name>|<track type>|<bound object
path>` per `GetOutputTracks()` via `GetGenericBinding`. Record in the undo journal before mutating.

## 4. CANONICAL CLIP PATTERNS

One logical change per exec block; print `PRE|` before mutating (protocol §2); save inside the
block; verify from a fresh load (§7). The clip has no fields, so authoring is just create-track →
create-clip → set start/duration → bind the GameObject. Follow the SubScene bracket + binding
mechanics from unity-timeline-track-authoring; the snippet below shows only the track-specific core.

```csharp
// inside the unity-timeline-track-authoring SubScene bracket; params discovered in §3
var track = timeline.CreateTrack<BovineLabs.Timeline.Parenting.Authoring.TemporaryDetachTrack>(null, trackName);

// PATTERN A — "pop loose for a beat": one clip = the detach window
var a = track.CreateClip<BovineLabs.Timeline.Parenting.Authoring.TemporaryDetachClip>();
a.start = 1; a.duration = 2; a.displayName = "Detach";   // detached during [1,3]; reattaches at 3

// bind the GameObject (NOT a component) — the baker coerces it to its entity
var childGo = /* resolve §3.4 child GameObject by hierarchy path */;
director.SetGenericBinding(track, childGo);
```

- **Pattern A — momentary release-and-snap-back**: single clip spanning the window the designer
  wants the object free (e.g. a sword that flies off the hand then returns to the holster slot, a
  turret that detaches to spin in world space then re-seats). Reattach is automatic at clip end —
  no second clip needed.
- **Pattern B — sequential releases** (NO overlap): two non-overlapping clips on the same track for
  two separate detach windows. Each window detaches fresh and reattaches at its own end. Gap
  between them = reattached/normal. Never overlap (no Blending cap).
- **Pattern C — looped flicker**: one clip with `ClipCaps.Looping` set on the timeline, used as a
  repeating detach pulse. Each loop re-runs the enter capture/exit restore. Use sparingly — every
  loop pays the end-of-frame ECB structural change.
- **Pattern D — "free it, but I'll destroy the parent"**: a detach clip whose window overlaps the
  destruction of the original parent. EXPECT the object to stay orphaned at world pose (no reattach,
  exit guard fails). Pair with an explicit reparent/cleanup elsewhere — this track will not save you.

## 5. WORKED EXAMPLE (vex-ee) — example environment; rediscover, never assume

- Project `/home/i/GitHub/vex-ee`. Package present at
  `Packages/com.bovinelabs.timeline.parenting` (v1.0.0, displayName "BovineLabs Timeline
  Parenting"). Authoring type `BovineLabs.Timeline.Parenting.Authoring.TemporaryDetachTrack`,
  clip `TemporaryDetachClip`, runtime ns `BovineLabs.Timeline.Parenting`.
- The package ships a sample at `Samples~/Parenting/` (Scene + a `Timeline.playable` using
  `resetOnDeactivate: 1`) — read it for a real authored example. Stage objects (the
  director/actor/parent hierarchy) are built by unity-stage-foundations; bind a CHILD GameObject
  that sits under another baked object. Keys/paths/object names DRIFT between projects — never
  assume; rediscover via §3.
- Only the DOTS variant exists in this package despite the manifest's "DOTS and non-DOTS" wording —
  confirmed by source: no MonoBehaviour/PlayableBehaviour reparent track present.

## 6. UNDO APPENDIX

Runtime detach/reattach exist only in play mode and are self-restoring per clip; the undo scope is
the authoring artifacts. Build the inventory + restore-director-first order + UNDO-1/2/3/4 per
unity-timeline-track-authoring. For this track the inventory is the generic shape: (1) the created
`.playable` (TimelineAsset + `TemporaryDetachTrack` + clip sub-assets — `DeleteAsset` removes all);
(2) possibly-created folder(s) (only if `PRE|folderExisted=false`); (3) mutated
`director.playableAsset` (restore the CAPTURED pre value); (4) the added generic binding entry for
the new track (SubScene file — restore by clearing my track's binding, then re-applying captured
`PRE|binding|` lines). The recipe edits no stage object, no hierarchy. ORDER: restore the director
FIRST (else a deleted asset leaves a dangling `{fileID: 0}`), THEN delete the asset, THEN any other
captured values (normally none).

## 7. VERIFICATION PROTOCOL

Per unity-timeline-track-authoring fresh-load protocol, plus the track-specific checks:
1. **Fresh-load asset dump** (separate exec block): load the `.playable`, dump every track/clip
   (name, start/duration, caps). Expect track type `TemporaryDetachTrack`, **`caps=Looping`** (NO
   Blending), clips have NO authored fields.
2. **Binding from a RELOADED SubScene**: expect `BIND|<i>|<trackName> (TemporaryDetachTrack) ->
   <childGoName> (GameObject)` — `GetGenericBinding` returns the **GameObject** (not a component);
   all prior entries intact.
3. **Prerequisite check**: confirm the bound GameObject is actually a CHILD (has a parent) at the
   clip's start — else the detach silently no-ops (§2 trap 1). Note whether the parent chain is
   uniformly scaled (non-uniform → world-pose loss on detach).
4. **Parent-scene restore** (sceneCount=1, parent loaded/active/not-dirty) and **console**:
   `unity-cli console --filter error` shows nothing new beyond the project baseline. Silence is
   expected, not evidence (field-less unconditional Bake). In BL_DEBUG builds, the cyan "Detached"
   gizmo while the clip is active is the live proof it fired.
