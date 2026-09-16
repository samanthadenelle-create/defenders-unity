// =============================================================================
// ItemDropWatcher - the hidden MonoBehaviour that subscribes to every enemy/boss
// Died event (READ-ONLY) and rolls drops via ItemDropSystem. Spawned only when
// the feature flag is ON (ItemDropSystem.StartNow). Inert otherwise (never built).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village.Items
//
// ISOLATION: it only SUBSCRIBES to existing public events:
//   - DeNelle.Village.Enemy.Died        (Action<Enemy>)
//   - DeNelle.Village.DragonBoss.Died   (Action<DragonBoss>)
// It mutates nothing on the enemy. On death it picks the right loot table id
// (enemy def id if a table exists, else the default) and asks ItemDropSystem to
// roll + deposit the materials into the village larder.
//
// Rescan model (mirrors how camp/zone watchers pick up dynamically-spawned
// enemies): a light periodic FindObjectsByType pass subscribes any newly-spawned
// enemy ONCE (tracked by instance id) so wave spawns are covered without touching
// the spawner. Unsubscribing happens implicitly when the enemy is destroyed - the
// tracked-set is pruned of dead/destroyed entries each scan.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

namespace DeNelle.Village.Items
{
    [DisallowMultipleComponent]
    public sealed class ItemDropWatcher : MonoBehaviour
    {
        private float _scanInterval = 1.5f;
        private float _nextScan;

        // Enemies we've already wired (by instance id) so we never double-subscribe.
        private readonly HashSet<int> _wiredEnemies = new HashSet<int>();
        private readonly HashSet<int> _wiredBosses = new HashSet<int>();

        public void Configure(float scanInterval)
        {
            _scanInterval = Mathf.Max(0.25f, scanInterval);
            _nextScan = 0f; // scan on first Update
        }

        private void Update()
        {
            if (!ItemDropSystem.Enabled) return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + _scanInterval;
            Scan();
        }

        private void Scan()
        {
            // Subscribe any new enemies.
            var enemies = Object.FindObjectsByType<Enemy>();
            if (enemies != null)
            {
                foreach (var e in enemies)
                {
                    if (e == null) continue;
                    int id = e.GetInstanceID();
                    if (_wiredEnemies.Contains(id)) continue;
                    _wiredEnemies.Add(id);
                    e.Died += OnEnemyDied;
                }
            }

            // Subscribe any new bosses.
            var bosses = Object.FindObjectsByType<DragonBoss>();
            if (bosses != null)
            {
                foreach (var b in bosses)
                {
                    if (b == null) continue;
                    int id = b.GetInstanceID();
                    if (_wiredBosses.Contains(id)) continue;
                    _wiredBosses.Add(id);
                    b.Died += OnBossDied;
                }
            }

            // Prune the tracked sets so they don't grow unbounded across waves.
            // (Destroyed Unity objects compare == null; their ids are simply dropped.)
            PruneDestroyed(_wiredEnemies, enemies);
        }

        private static void PruneDestroyed(HashSet<int> tracked, Enemy[] live)
        {
            if (tracked.Count < 256) return; // cheap guard; only prune when it grows
            var liveIds = new HashSet<int>();
            if (live != null)
                foreach (var e in live) if (e != null) liveIds.Add(e.GetInstanceID());
            tracked.RemoveWhere(id => !liveIds.Contains(id));
        }

        private void OnEnemyDied(Enemy enemy)
        {
            if (!DeNelle.Core.Combat.PracticeCombatPolicy.AllowsProgression(enemy)) return;
            string tableId = ResolveEnemyTable(enemy.EnemyDefId);
            // WO-556: data-driven boss-ness — if the resolved table is a BOSS table, its boss-only
            // gem/gear lines roll. This covers a boss-tier Enemy (e.g. orc-warlord, source:"boss")
            // without any per-instance flag.
            bool isBoss = IsBossTable(tableId);
            DropFor(tableId, enemy.transform != null ? enemy.transform.position : Vector3.zero, isBoss);
        }

        private void OnBossDied(DragonBoss boss)
        {
            if (!DeNelle.Core.Combat.PracticeCombatPolicy.AllowsProgression(boss)) return;
            string tableId = LootTableCatalog.DefaultBossTableId;
            Vector3 at = (boss != null && boss.transform != null) ? boss.transform.position : Vector3.zero;
            DropFor(tableId, at, includeBossOnly: true);   // the dedicated boss path always allows gem/gear
        }

        /// <summary>
        /// Route a roll either to a WORLD pickup mote at <paramref name="at"/> (the
        /// hero collects it) or straight to the larder, per ItemDropSystem.UseWorldPickups.
        /// WO-556: <paramref name="includeBossOnly"/> opens the gem/gear-gated lines on a boss kill.
        /// </summary>
        private static void DropFor(string tableId, Vector3 at, bool includeBossOnly)
        {
            // #55: per-kill WORLD motes are CreatePrimitive objects spawned at each death spot; they
            // are unparented, survive arena teardown, and litter the field ("3 little blocks" for a
            // 3-orc pack). Inside an ACTIVE BattleArena, route the roll straight to the larder so loot
            // is still credited but NO physical mote is left behind. World motes stay in the open
            // village/overworld where walking over them to collect is the intended interaction.
            bool arenaLive = DeNelle.Village.Arena.BattleArena.Existing != null;
            if (ItemDropSystem.UseWorldPickups && !arenaLive)
            {
                var lines = ItemDropSystem.RollLines(tableId, includeBossOnly);
                if (lines != null && lines.Count > 0)
                    ItemPickupSpawner.Spawn(at, lines);
            }
            else
            {
                ItemDropSystem.RollAndDeposit(tableId, includeBossOnly);
            }
        }

        /// <summary>WO-556: true when the table exists and is declared a boss table (source "boss").</summary>
        private static bool IsBossTable(string tableId)
        {
            var t = LootTableCatalog.Find(tableId);
            return t != null && !string.IsNullOrEmpty(t.Source)
                && t.Source.Equals("boss", System.StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Prefer a table whose id matches the enemy def id; else the default.</summary>
        private static string ResolveEnemyTable(string enemyDefId)
        {
            if (!string.IsNullOrEmpty(enemyDefId) && LootTableCatalog.Find(enemyDefId) != null)
                return enemyDefId;
            return LootTableCatalog.DefaultEnemyTableId;
        }
    }
}
