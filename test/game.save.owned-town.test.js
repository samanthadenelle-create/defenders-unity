'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { buildState } = require('../api/game/save.js');

test('cloud mapping preserves owned construction and story layout separately', () => {
    const ownedBase = {
        schemaVersion: 1, baseId: 'personal-iron-bastion', revision: 9,
        captureReceiptId: 'capture-one', suppliesReceiptId: 'capture-one',
        repairSupplies: { wood: 0, iron: 0, stone: 7 },
        milestoneFlags: 7, reenteredLayoutRevision: 6,
        structures: [
            { instanceId: 'inherited-one', placement: { itemId: 'tower_arcane_spire', level: 2 },
                inheritedPose: { templateStructureId: 'stable-one', parentFrame: [1, 0, 0, 0] }, condition01: 0, retired: true },
            { instanceId: 'new-one', placement: { itemId: 'tower_ground_archer', level: 3, cellX: 12, cellZ: 14 }, condition01: 0.5 },
        ],
    };
    const body = {
        ownedBase, baseLayout: [{ itemId: 'wall_wood', cellX: 3, cellZ: 4, level: 2 }],
        obsidianQueue: { channels: { Builder: { active: [{ StructureId: 'owned-town@personal-iron-bastion|new-one', TargetTier: 3 }] } } },
        pendingTownCapture: { receipt: 'local-intent' }, PendingTownCapture: { receipt: 'legacy-local-intent' },
        resetEpoch: 4, schemaVersion: 41, playerId: 'transport-only',
    };
    const original = JSON.stringify(body);
    const persisted = JSON.parse(JSON.stringify(buildState(body)));
    assert.deepEqual(persisted.ownedBase, ownedBase);
    assert.deepEqual(persisted.baseLayout, body.baseLayout);
    assert.deepEqual(persisted.obsidianQueue, body.obsidianQueue);
    for (const key of ['pendingTownCapture', 'PendingTownCapture', 'resetEpoch', 'schemaVersion', 'playerId'])
        assert.equal(Object.hasOwn(persisted, key), false, key);
    assert.equal(JSON.stringify(body), original, 'mapping must not mutate the submitted snapshot');
});

test('older partial saves do not blank an existing owned town', () => {
    const delta = buildState({ wood: 12, ownedBase: null, pendingTownCapture: null });
    assert.equal(Object.hasOwn(delta, 'ownedBase'), false);
    assert.equal(Object.hasOwn(delta, 'pendingTownCapture'), false);
    assert.equal(delta.wood, 12);
});
