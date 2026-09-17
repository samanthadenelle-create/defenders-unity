# VFX Played Inventory — Raid + Fireball Windows (2026-09-16 19:55-20:52)

**Source logs:** `logs/device/raid-window-1955.txt`, `logs/device/owner-fireball-20260916/logcat.txt` (20:49-20:52)

| Window | VFX Key | Prefab Path | Raid Count | Fireball Count | Total |
|--------|---------|-------------|------------|----------------|-------|
| Raid | ArcaneTower_Aura | Assets/Hovl Studio/AOE Magic spells Vol.1/Prefabs/Lightning strike.prefab (Hovl bridge) | 1 | — | 1 |
| Raid | Elite_Spawn | Assets/Resources/VFX/Portal/Elite_Spawn.prefab | 1 | — | 1 |
| Both | Cast_FireCharge | Assets/Resources/VFX/Projectiles/Casting_Fire.prefab | 5 | 6 | 11 |
| Both | Cast_MuzzleFlash | Assets/Resources/VFX/Weapon/Cast_MuzzleFlash.prefab | 9 | 4 | 13 |
| Both | Explosion_Arcane | Assets/Spells Pack/Particles/Prefabs/Projectiles/Explosion/Explosion_Arcane.prefab | 10 | 12 | 22 |
| Both | Impact_Flame | Assets/Resources/VFX/Status/BigExplosion.prefab | 7 | 9 | 16 |
| Raid | Env_DestructionDust | Assets/Lana Studio/Casual RPG VFX/Prefabs/Burst/Poof_generic.prefab | 5 | — | 5 |
| Raid | Damage_Smolder | Assets/Resources/VFX/Damage/Damage_Smolder.prefab | 1 | — | 1 |
| Raid | Lightningspellmaybe_Cast | Assets/Hovl Studio/AOE Magic spells Vol.1/Prefabs/Lightning strike.prefab | 3 | 1 | 4 |
| Raid | lighteningOnSpellLand_Impact | Assets/Hovl Studio/RPG VFX Bundle/Random effect prefabs/Electro splash.prefab | 3 | 1 | 4 |
| Raid | Impact_Aether | Assets/Lana Studio/Casual RPG VFX/Prefabs/Range_attack/Hit_magic.prefab | 2 | 3 | 5 |
| Raid | Damage_Fire | Assets/Resources/VFX/Damage/Damage_Fire.prefab | 1 | — | 1 |
| Raid | Damage_CriticalBeacon | Assets/Resources/VFX/Damage/Damage_CriticalBeacon.prefab | 1 | — | 1 |
| Raid | ArcherTower_Projectile | Assets/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Projectile VFX loop/Projectile 13 red laser.prefab | 4 | — | 4 |
| Raid | PP_MuzzleFlash | Assets/Resources/VFX/Weapon/Cast_MuzzleFlash.prefab | 4 | — | 4 |
| Both | Impact_Physical | Assets/Lana Studio/Casual RPG VFX/Prefabs/Slash/Slash_stone_once.prefab | 6 | 4 | 10 |
| Raid | Spear_Impact | Assets/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 11 orange arrow.prefab | 4 | — | 4 |
| Raid | Damage_BreakBurst | Assets/Resources/VFX/Damage/Damage_BreakBurst.prefab | 1 | — | 1 |
| Raid | Damage_Ruin | Assets/Resources/VFX/Damage/Damage_Ruin.prefab | 1 | — | 1 |
| Fireball | WaveClear_Celebration | Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Level_up.prefab | — | 3 | 3 |
| Fireball | Juice_LevelUp | Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Level_up.prefab | — | 3 | 3 |
| Fireball | Juice_WaveClear | Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Level_up.prefab | — | 3 | 3 |
| Fireball | Poi_NodeAura | Assets/Resources/VFX/Aura/Aura_TalentNode.prefab | — | 4 | 4 |
| Fireball | PosionCloud_Cast | Assets/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Flash and hits/Hit 24 green explosion.prefab | — | 1 | 1 |
| Fireball | Sleep_Impact | Assets/Lana Studio/Casual RPG VFX/Prefabs/States/Character_status_sleep.prefab | — | 1 | 1 |
| Fireball | Impact_ShockwaveRing | Assets/Lana Studio/Casual RPG VFX/Prefabs/Burst/Burst_rings.prefab | — | 1 | 1 |
| Fireball | Cast_MageCharge | Assets/Lana Studio/Casual RPG VFX/Prefabs/Orbs/Orbs_electric.prefab | — | 2 | 2 |
| Fireball | Death_Skeleton | Assets/Lana Studio/Casual RPG VFX/Prefabs/Burst/Poof_generic.prefab | — | 6 | 6 |
| Fireball | Combo_Tier1 | Assets/Lana Studio/Casual RPG VFX/Prefabs/Burst/Flash_circle.prefab | — | 2 | 2 |
| Fireball | Combo_Tier2 | Assets/Lana Studio/Casual RPG VFX/Prefabs/Burst/Flash_dubble_circle.prefab | — | 1 | 1 |
| Fireball | ArcherTowerLevel2_Projectile | Assets/Hovl Studio/AAA Projectiles Vol 1/Prefabs/Projectiles 2D/2D Projectile 20 pink arrow.prefab | — | 1 | 1 |

**Total unique VFX:** 32 distinct keys across both windows. **Total plays:** 171 (raid: 86, fireball: 85).

**Source citations (sample):**
- Raid `Cast_FireCharge`: Log line 09-16 19:55:51.465 — `PlayOneshot('Cast_FireCharge')`
- Fireball `Juice_LevelUp`: Log line 09-16 20:49:03.707 — `PlayOneshot('Juice_LevelUp')`
- Raid `Lightningspellmaybe_Cast`: Log line 09-16 19:56:04.138 — `PlayKey('Lightningspellmaybe_Cast') -> prefab 'Lightning strike'`
