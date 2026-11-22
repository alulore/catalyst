---
name: explicit-state
description: >
  Use when writing, changing or reviewing code that holds state over time:
  loading, saving or error flags, async requests, autosave, uploads, retries,
  queues, cancellation, or anything a user can start, cancel, retry or close.
---

# Explicit state

Apply this while writing, at the moment state appears, not in a later review
pass. The aim is to make impossible states unrepresentable before they become
bugs.

## The tell

You are about to write two or more values that describe one thing:

```ts
const [loading, setLoading] = useState(false);
const [saving, setSaving] = useState(false);
const [error, setError] = useState<string | null>(null);
const [ready, setReady] = useState(false);
```

Every combination is now representable, including `ready && error && saving`.
Each new flag adds a state nobody designed, and each consumer guesses which
combination means what. That is an implicit state machine. Make it explicit.

One boolean that cannot contradict anything is fine: a popover's `open`, a
one-shot `didCopy`. The line is contradiction and time. If another flag, a
timer, a retry or a late response can make it untrue, it is a machine.

## Write the state card first

For anything with more than two phases or any async, write this in your plan
before the code. It takes a minute and prevents most of the bugs below.

```
Thing:   note document
Phases:  loading | ready | dirty | saving | saved | conflict | failed | gone
Events:  loadOk, loadFail, edit, saveStart, saveOk, saveFail, conflict, retry, archive
Illegal: edit while gone; saveStart while saving (coalesce); close while saving (await)
Guards:  load epoch; save revision; retry deadline; teardown awaits save
```

Then build to the card:

- **One field holds the phase.** Derive `isLoading`, `canClose`, `readOnlyReason`
  and every label from it. Never store those beside it.
- **Phases are states, not actions.** `saving`, not `isSaving`; `conflict`, not
  `hasConflict`. A phase describes what is true now, so two phases cannot both
  be true.
- **Every non-terminal phase has a way out.** Name the resting and terminal
  ones (`ready`, `saved`, `failed`, `gone`) so spinners, retries and timers
  stop.
- **Carry the reason, not just the block.** If a phase is read-only, or close is
  refused, put `readOnlyReason` / `closeNeeds` in the snapshot. One reason, one
  requirement, one place that decides. Otherwise the UI shows a dead button and
  every surface invents its own explanation.
- **Events, not setters.** Expose `edit`, `saveStart`, `saveOk`, not
  `setPhase`. If any caller can assign any phase, the machine is decorative.
- **Illegal transitions are a decision.** For each event, choose: ignore,
  coalesce, refuse with a reason, or treat as a bug. Silence is a decision too.

```ts
type Note =
  | { phase: 'loading'; epoch: number }
  | { phase: 'ready'; text: string; revision: number }
  | { phase: 'dirty'; text: string; revision: number }
  | { phase: 'saving'; sent: string; draft: string; revision: number; op: number }
  | { phase: 'saved'; text: string; revision: number }
  | { phase: 'conflict'; mine: string; theirs: string; revision: number }
  | { phase: 'failed'; text: string; problem: string; revision: number }
  | { phase: 'gone' };

function reduce(state: Note, event: NoteEvent): Note {
  switch (event.type) {
    case 'edit':
      // Typing during a save is kept, not dropped.
      if (state.phase === 'saving') return { ...state, draft: event.text };
      if (
        state.phase === 'ready' ||
        state.phase === 'dirty' ||
        state.phase === 'saved' ||
        state.phase === 'failed'
      ) {
        return { phase: 'dirty', text: event.text, revision: state.revision };
      }
      return state;
    case 'saveStart':
      return state.phase === 'dirty'
        ? { phase: 'saving', sent: state.text, draft: state.text, revision: state.revision, op: event.op }
        : state;
    case 'saveOk':
      if (state.phase !== 'saving' || event.op !== state.op) return state;
      return state.draft === state.sent
        ? { phase: 'saved', text: state.sent, revision: event.revision }
        : { phase: 'dirty', text: state.draft, revision: event.revision };
    default:
      return state;
  }
}
```

Keep the reducer pure and put effects at the edge. Return the next snapshot
plus the effects to run (`save`, `scheduleRetry`, `close`), so the machine is
testable without a server, a timer or a screen. In languages without sum types,
use a sealed class or enum plus a payload with one invariant: payload fields
change only inside the transition function.

## Async is where implicit machines leak

- **Late results.** Every load or request carries an epoch, request id or
  revision. Compare it when the result arrives and drop it if stale. Setting
  `cancelled = true` is not enough if one response path forgets to check it.
- **Overlapping writes.** Per entity, either serialize (one queue) or
  compare-and-set (`ifUpdatedAt`, revision, ETag). "Last response wins" is not
  a policy; it is how an older write erases a newer one.
- **Double entry.** Guard the transition itself. A disabled button is a hint,
  not a guard: keyboard, deep links, retries, a second window and an agent all
  bypass it.
- **Deadlines and retries.** Every wait has a deadline and a terminal failure.
  Retries are bounded, visible and stop in a terminal phase. A spinner with no
  deadline is a stuck state waiting to happen.
- **Teardown.** Close, unmount, sign-out and shutdown stop timers, invalidate
  in-flight work and await what writes to storage before closing it. Fire and
  forget teardown is how a save lands after the database closes.
- **One-shot resources.** Consume a token, code or lock only after the side
  effect succeeds. Consuming it first turns every failure into a dead retry.
- **Optimistic UI.** Keep the unconfirmed value separate from the confirmed
  one. Mark saved only on acknowledgement; on failure, revert or re-queue and
  keep the draft dirty. Never let the optimistic value become the baseline.
- **Several producers.** If three places write the same field, one will erase
  another. Give the field one owner, or a merge rule and a revision. Coalesce
  duplicate work by key instead of letting two equal operations race.
- **Locks and leases.** A lock has an owner and a stale window. A lock without
  expiry turns one crash into a permanent outage.

## Derive once

Two places computing `ready`, `readOnly` or `canClose` will disagree. Make one
function that switches over the phase and returns everything a surface needs:
`{ label, tone, canClose, readOnlyReason }`. Every banner, footer, button and
agent surface reads that object.

If a blocked action needs a reason, return the reason from the same projection.
Prefer refusing with an explanation over hiding the control; a missing button
teaches the user nothing and hides a state the machine knows about.

## The shapes this usually takes

| Smell | Explicit form |
| --- | --- |
| `isLoading` + `isSaving` + `hasError` + `isReady` | one `phase` |
| `pending` plus `setTimeout` | a phase with a deadline and a terminal timeout |
| `cancelled` checked in some callbacks | epoch or request id compared on resolve |
| last response wins | per-entity queue or revision guard |
| disabled button as the only guard | transition guard plus a reason |
| banner and footer each compute status | one `describe(snapshot)` |
| "saved" set optimistically | confirmed value separate; saved only on ack |
| retry forever | bounded retries and a `failed` phase with a reason |
| close or unmount just clears state | teardown awaits the pending write |
| one-shot token consumed before the effect | consume on success, release on failure |
| batch applies half, then fails | per-item tracking plus rollback or undo |
| agent write during a debounce | revision on every write; conflict is a phase |

## Write the tests that would have caught it

Test transitions, not just the happy path. One test per bug you are preventing:

- Each phase: the event that must be refused, and what the user sees.
- A late response arriving after a newer request.
- A second click or second event while the first is in flight.
- Failure, then retry; retry after success; retry that never succeeds.
- Close, sign-out or shutdown mid-flight, then the pending write.
- Two writers on one record: the stale one is refused, not applied.

Name the test after the failure, for example "refuses a stale save after an
agent edit". The name is how the next person learns the rule.

## Before you call it done

- [ ] One phase field; no two flags can contradict.
- [ ] Every non-terminal phase has an exit; every wait has a deadline.
- [ ] Every async result is dropped if stale; every racing write is queued or
      revision-guarded.
- [ ] Double entry is refused at the transition, not just by a disabled control.
- [ ] Teardown stops and awaits what it owns.
- [ ] UI state and blocked-action reasons come from one projection.
- [ ] Tests cover refusal, staleness, double entry, failure/retry and teardown.

## Keep it proportionate

Do not build a twelve-state machine for a checkbox. Use the smallest machine
that removes the contradiction: often one enum and one reducer is enough, and
XState is not required. When you meet an existing implicit machine, do not
rewrite the world: introduce the phase at the boundary you are touching, keep
the old surface as a derived view, migrate callers, and add the transition
tests. Leave unrelated state alone.

## If you are reviewing instead

Ask four questions: which flags describe one thing, which can be true together,
which async result can arrive late, and which action can be entered twice.
Report the contradictions and the smallest machine that removes them. Do not
turn a review into a rewrite.
