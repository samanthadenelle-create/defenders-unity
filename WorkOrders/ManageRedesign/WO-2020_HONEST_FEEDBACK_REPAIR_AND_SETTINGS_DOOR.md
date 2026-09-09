# WO-2020 - Honest Feedback repair and permanent Settings door

**Status:** FIXED - checked in; awaiting owner device match on the next build

**Date:** 2026-09-08

## Owner observation

The device's Tell Us Honestly panel overlaps its body, input, reward, and buttons. It is difficult to
trigger again after dismissal, so Settings needs a permanent manual door and a saved proof image.

## Scope and acceptance

- Separate prose/input and actions into non-overlapping lanes at the Seeker's 2670x1200 surface.
- Keep the automatic offer one-time, but always install the panel and submit service in a valid hub.
- Add Settings > Feedback > Send Feedback and route it through PanelId.HonestFeedback.
- Keep the thank-you claim one-time even when feedback can be sent again.
- Externalize all feedback-flow player copy under feedback.* keys.
- Submit normalized language and device language with the player's natural-language note.
- Add source regression coverage, render a proof PNG, run the full regression fleet, build a tester APK,
  sync remote content before install, and verify the installed version on Seeker.
