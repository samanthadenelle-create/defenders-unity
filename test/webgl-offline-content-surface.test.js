const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');

const root = path.resolve(__dirname, '..');

test('WebGL hides and runtime-blocks the app offline-download flow', () => {
  const settings = fs.readFileSync(
    path.join(root, 'Assets/_Modules/Settings/SettingsController.cs'), 'utf8');
  const panel = fs.readFileSync(
    path.join(root, 'Assets/_Modules/Core/UI/OfflineOptInPanel.cs'), 'utf8');
  const service = fs.readFileSync(
    path.join(root, 'Assets/_Modules/Core/Addressables/OfflineContentService.cs'), 'utf8');

  // Check the actual button's preprocessor scope, independently of localized copy.
  // Staying within a single directive block prevents an unrelated earlier #if
  // from making an unguarded button appear protected.
  const offlineSection = settings.match(
    /#if !UNITY_WEBGL\s*\r?\n(?:(?!^\s*#)[\s\S])*?OnOfflineClicked\);(?:(?!^\s*#)[\s\S])*?^\s*#endif/gm);
  assert.ok(offlineSection, 'the Settings offline-download entry must be excluded from WebGL');

  const showMethod = panel.match(
    /public static void Show\(\)[\s\S]*?Application\.platform == RuntimePlatform\.WebGLPlayer[\s\S]*?return;/);
  assert.ok(showMethod, 'OfflineOptInPanel.Show must fail closed when called in WebGL');

  const resolverGuard = service.match(
    /ResolveContentSource\(Action<ContentSource>[\s\S]*?Application\.platform == RuntimePlatform\.WebGLPlayer[\s\S]*?Source = ContentSource\.Online;[\s\S]*?yield break;/);
  assert.ok(resolverGuard,
    'WebGL boot must use its shipped catalog without entering the native offline-content gate');
});
