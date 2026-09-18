const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');
const read = (file) => fs.readFileSync(path.join(root, file), 'utf8');

test('clan chat is open (WO-1851) and both player entry points still consult the same gate', () => {
  // WO-1265 shipped this suite pinning the gate FALSE (the local-only PlayerPrefs
  // prototype had no server backing). WO-1851 (clan WO-8, 2026-09-17) flips the gate
  // to TRUE now that WO-1265's server/moderation/two-wallet acceptance is complete
  // (WO-1848's two-wallet integration gate is green). What must survive the flip is
  // not the VALUE but the WIRING: both entry points still read the one gate constant
  // rather than each carrying their own copy.
  const gate = read('Assets/_Modules/Core/Services/ClanFeatureGate.cs');
  const dock = read('Assets/_Modules/HUD/Kit/HudKitController.cs');
  const bootstrap = read('Assets/_Modules/HUD/ClanChatPanelBootstrap.cs');
  assert.match(gate, /PlayerFacingEnabled = true/);
  assert.match(dock, /if \(DeNelle\.Core\.Services\.ClanFeatureGate\.PlayerFacingEnabled\)[\s\S]*"Chat"/);
  assert.match(bootstrap, /if \(!ClanFeatureGate\.PlayerFacingEnabled\) return;/);
});

