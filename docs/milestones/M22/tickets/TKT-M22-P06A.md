# TKT-M22-P06A — Early Windows Companion Feasibility Probe

GitHub issue: #182

M22; R2. Implementation GPT-6.1 Sol High; NORMAL independent GPT-6.1 Sol High.
Prepared, ENVIRONMENT-GATED: actual interactive Windows target access has not
been confirmed. Do not call this Ready until that entry condition is evidenced.
Queue predecessor #181 must merge; native feasibility must be assessed
before settlement/continuation expansion. No alternate/skipping route is implied.

## Outcome and boundaries

Run a minimal nonshipping tray/native-notification probe against the existing
loopback host to discover Windows lifecycle and packaging limitations early.
Recommend a bounded production companion approach; do not silently select or
install a new production runtime/package, autostart or public distribution route.

## Entry conditions

- Current develop includes C04 and B3 predecessors with final-head CI/review.
- A Windows device with interactive desktop, version/build/architecture recorded,
  can run the probe and manually exercise lock/sleep/notification controls.
  Hosted Windows build-only CI cannot prove delivery in an interactive session.
- Probe dependencies are free/local and isolated from the shipping host/React
  stack. A production dependency/ADR or paid-runner choice remains an owner gate.

## Contract and acceptance

1. Compare a minimal .NET native tray approach and documented native app
   notifications/registration requirements. Cite primary Microsoft sources, pin
   actual probe versions and record packaged/unpackaged constraints. Relevant
   starting references: [NotifyIcon](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-windows-forms)
   and [.NET app notifications](https://learn.microsoft.com/windows/apps/develop/notifications/app-notifications/app-notifications-dotnet).
   Documentation is feasibility input, never delivery proof.
2. Build a bounded opt-in synthetic probe, with French open/test/quit labels, that
   preserves the existing same-origin React/loopback API and read-only gateway.
   No real account secrets, game actions or native financial calculations. Browser
   close/minimize leaves the probe-owned host alive; explicit Quit stops only
   its owned process. Second launch cannot duplicate hosts/alerts or kill an
   unrelated existing host. Keep loopback binding/Host/anti-forgery protections.
3. On actual Windows record: UI open/minimized/closed native test delivery;
   focus/notification suppression and permission disabled; lock/unlock;
   sleep/resume/offline; explicit quit/relaunch and second launch; click activation
   to a safe local current route. Permission/suppression may legitimately prevent
   delivery and must return truthful status, never a false success.
4. Synthetic expired/generation-invalid messages do not burst after resume or
   open stale private routes. Clicks only open/revalidate; they never execute an
   action. Persisted production alert dedupe/preferences are later P06B scope.
5. Produce an actual Windows result matrix with version, probe head, steps,
   timestamps, screenshots/video where useful and observed failures. Neither a
   cross-compiled binary nor Linux/macOS screenshots satisfy native vectors.
   Remove probe registrations/artifacts explicitly; preserve credentials/data.
6. Recommend production host/process ownership, notification identity and packaging
   direction with security/privacy/update/uninstall limits. If architecture or
   production dependencies change, present evidence and concise owner choices;
   record pending decision and keep P06B/C unready. Probe delivery alone does
   not approve installation/release or close P06/#150.

## Validation and exclusions

Probe unit/process-ownership tests, current host security and generation tests,
actual Windows matrix and reproducible setup/cleanup, workflow suite and CI.
UI/web edits require original/actual 1920×1080 evidence; a diagnostic tray is
not a replacement for the six-screen baseline. No real urgent financial alert,
durable queue, startup registration, installer/updater or paid cloud dependency.

Without interactive Windows evidence preserve Draft and the exact environment
handoff; do not substitute a build-only runner. Commit `[TKT-M22-P06A]`; PR develop,
Closes #182, actual milestone 11. Independent review/CI before Ready;
owner owns production architecture selection and merges. Stop after this probe.
