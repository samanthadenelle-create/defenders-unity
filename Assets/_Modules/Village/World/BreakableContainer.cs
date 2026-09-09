// =============================================================================
// BreakableContainer - a dungeon/outpost loot CHEST the hero OPENS (WO-1132).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// OWNER RULING 2026-08-21: "can we make it a chest?" / "open chest" / "not
// attackable item" / "can only open outside of combat" / "prevents player from
// trying to run in collect and go".
//
// ── WHAT THIS TYPE USED TO BE, AND WHY IT CHANGED (read before "restoring" any of it)
// It was a STATIC HOSTILE: it implemented IDamageable + IDamageableStructure,
// declared Faction => CombatFaction.Hostile, and rewrote its own layer to "Enemy"
// so the hero's enemy-mask OverlapSphere would find it and TakeDamage() it. That
// was deliberate - it is how smashing worked - but it also made every crate a
// valid TARGET for the hostile reticle, which is the ENTIRE defect logged as
// WO-1047 ("a dungeon prop is registering as a HOSTILE target").
//
// Two concerns were sharing one flag: *may the hero damage this?* and *is this a
// thing to lock onto?*. This file removes the FIRST concern outright, so the
// second stops being ambiguous. The bug is not filtered out - it stops existing.
// ⛔ DO NOT "fix" a future prop-targeting report by adding an exclusion list to
// HeroTargetIndicator; that is the inferior fix this ruling replaced.
//
// ⛔ THE CLASS NAME IS LOAD-BEARING AND MUST NOT BE RENAMED. Every composed
// dungeon on disk (DungeonBaker.PlaceComposeChests) and every KayKit outpost
// baked this component into its .unity by script GUID + class name. "Breakable"
// is now a misnomer - the chest is opened, not broken - but a rename would orphan
// the component on every baked scene in the tree. The misnomer is the cheap half.
//
// ── WHAT IT IS NOW
// A proximity interactable, the SAME shape as DungeonTreasureCache and
// DungeonExitInteractable (ActivateRadius 4.5 so the shared Interact button arms
// before the player is standing on the prop; proximity TEST on an interval, the
// button Request every frame because Request is a per-frame claim).
//
// ── THE OUT-OF-COMBAT GATE
// DeNelle.Core.Combat.BattleLock.IsInBattle() is THE combat-state authority and
// the ONLY one consulted here. It is live in a composed dungeon: composed scenes
// stage no BattleArena and carry no WaveManager, but HeroCombatEngagement
// registers a BattleLock probe that is raised by every hero-aggro hollow whose
// aggro band the hero is standing in (Enemy.UpdateHeroCombatEngagement). A second
// authority is NOT invented here, and the HUD's hostile(activebattle) posture is
// deliberately NOT used: it is a laggy (0.20s poll) derivative of this same lock
// and it lives in DeNelle.HUD, which DeNelle.Village may not reach anyway.
//
// A refused open is NEVER a dead tap - a dead tap reads as a bug. The prompt
// itself changes to the refusal sentence, and tapping it surfaces that sentence
// as a toast. The words come from canon-strings.json (VillageStrings.Canon, the
// same seam DungeonSealedDoorPanel uses), never a hardcoded literal.
//
// ── MIGRATION: NO RE-BAKE REQUIRED
// Chests are placed at BAKE time and saved into the .unity, so scenes on disk
// still carry the legacy primitive cube, the "Enemy" layer, and no chest art.
// EnsureChest() normalises all three on Awake, so every already-baked dungeon is
// corrected at load with no re-bake - the same runtime-bootstrap idiom
// DungeonExitSpawner and ComposedPropVisuals use for exactly this reason. It is
// idempotent, and it runs from Create() too so a bake-time placement is built
// correctly in the first place (Awake does not fire on AddComponent in edit mode).
//
// REUSES (does NOT reinvent, CLAUDE.md sec.9):
//   - DeNelle.Village.Items.ItemDropSystem.RollLines / RollAndDeposit (loot roll)
//   - DeNelle.Village.Items.ItemPickupSpawner.Spawn (world drop mote)
//   - DeNelle.Village.Buildings.MobileInteractButton (the shared Interact button)
//   - DeNelle.Core.Combat.BattleLock (the combat-state authority)
//   - DeNelle.Core.UI.ElarionUiKit.ShowToast (the ONE transient toast seam)
//   - DeNelle.Village.VillageStrings.Canon (canon-strings.json reader)
//
// COLOURBLIND LAW (owner is red/green colourblind): the chest's state is carried
// by its LID ANGLE and its prompt WORDS. Closed vs open is a silhouette change,
// never a hue change. Tints reinforce; they never carry the meaning alone.
//
// ASCII strings only. Canon: the village is Elarion.
// =============================================================================

using UnityEngine;
using DeNelle.Core.Combat;        // BattleLock - the combat-state authority
using DeNelle.Core.Diagnostics;   // FlowTrace / Guard (CLAUDE.md sec.12)
using DeNelle.Core.UI;            // ElarionUiKit.ShowToast
using DeNelle.Village.Items;      // ItemDropSystem / ItemPickupSpawner (same assembly)
// MobileInteractButton and ChestInteractionText are namespace DeNelle.Village - no using needed.

namespace DeNelle.Village
{
    /// <summary>
    /// A loot chest the hero walks up to and OPENS. It is inert furniture: it is
    /// NOT an <c>IDamageable</c>, it declares no combat faction, and it does not sit
    /// on the Enemy layer, so nothing can attack it and the hostile reticle cannot
    /// lock onto it. Opening is refused while combat is active.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BreakableContainer : MonoBehaviour
    {
        private const string Sys = "Loot";

        /// <summary>Name of the child the chest body hangs under - the idempotency marker.</summary>
        private const string BodyName = "ChestVisual";

        // Matched to DungeonTreasureCache / DungeonExitInteractable so every dungeon
        // affordance feels identical: the button arms before the hero is on the prop.
        private const float ActivateRadius = 4.5f;
        private const float CheckInterval = 0.15f;

        [Tooltip("Loot table rolled on open (loot-tables.json id, e.g. crate-common / barrel-common / chest-rare).")]
        [SerializeField] private string lootTableId = "crate-common";

        // WO-1132: maxHp is RETIRED (a chest has no hit points) but the field is kept as a
        // private serialized leftover-free class - nothing reads it and nothing writes it.
        // It is intentionally absent rather than kept-and-ignored: a live-looking hp field
        // on an unattackable prop is precisely the ambiguity this ticket removed.

        private Transform _hero;
        private bool _heroFound;
        private bool _isInRange;
        private bool _opened;
        private float _nextProximityCheck;
        private Transform _lid;          // rotated back on open - the silhouette IS the state

        /// <summary>Loot table id rolled when this chest is opened.</summary>
        public string LootTableId
        {
            get => lootTableId;
            set => lootTableId = value;
        }

        /// <summary>True once this chest has paid out. An opened chest keeps its
        /// open-lid body so the room reads as looted rather than the prop vanishing.</summary>
        public bool IsOpened => _opened;

        private void Awake()
        {
            // Covers every ALREADY-BAKED scene on disk: strips the legacy primitive cube,
            // moves the object off the Enemy layer, and builds the chest body. Idempotent.
            EnsureChest(gameObject);
            _lid = FindLid(gameObject);
        }

        // ── Prompt + the out-of-combat gate ─────────────────────────────────────

        private void Update()
        {
            if (_opened) return;

            if (!_heroFound)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");   // canon sec.7: one tag
                if (tagged != null) { _hero = tagged.transform; _heroFound = true; }
                if (!_heroFound) return;
            }
            // The hero rig can be replaced (body-swap) after we cached it - re-resolve rather
            // than dereferencing a destroyed Transform (the DungeonPortal DEF-40 lesson).
            if (_hero == null) { _heroFound = false; return; }

            // Build/authoring mode only - this is NOT a combat flag (see MobileInteractButton).
            if (MobileInteractButton.Suppressed) { ReleasePrompt(); return; }

            if (Time.unscaledTime >= _nextProximityCheck)
            {
                _nextProximityCheck = Time.unscaledTime + CheckInterval;
                Vector3 d = _hero.position - transform.position;
                d.y = 0f;
                _isInRange = d.sqrMagnitude <= ActivateRadius * ActivateRadius;
            }

            if (!_isInRange) { MobileInteractButton.Release(this); return; }

            // THE GATE. One authority, consulted live so the prompt flips the instant the
            // last hollow disengages - the player is told WHY, in words, and the tap is
            // never dead. Owner ruling: this "prevents player from trying to run in
            // collect and go" - loot rewards CLEARING a room, not sprinting past it.
            if (BattleLock.IsInBattle())
                MobileInteractButton.Request(this, ChestInteractionText.BlockedByEnemies.Resolve(), RefuseOpen);
            else
                MobileInteractButton.Request(this, ChestInteractionText.Open.Resolve(), Open);
        }

        private void ReleasePrompt()
        {
            _isInRange = false;
            MobileInteractButton.Release(this);
        }

        private void OnDisable() => ReleasePrompt();

        /// <summary>
        /// The refused tap. It SAYS SO - a silent no-op would read as a broken button.
        /// The sentence is the same one the prompt is already showing, so the toast
        /// confirms the reason rather than introducing a second vocabulary.
        /// </summary>
        private void RefuseOpen()
        {
            string sentence = ChestInteractionText.BlockedByEnemies.Resolve();
            FlowTrace.Step(Sys, $"chest '{name}' open REFUSED - combat active (BattleLock.IsInBattle). " +
                $"Player told: \"{sentence}\"");
            Guard.Try(Sys, "surface chest in-combat refusal",
                () => ElarionUiKit.ShowToast(sentence, ElarionUiKit.ToastTone.Info, 2.4f));
        }

        // ── Open -> roll -> drop ────────────────────────────────────────────────

        /// <summary>
        /// Opens the chest: rolls <see cref="LootTableId"/> and drops survival materials.
        /// Re-checks the battle lock so a tap that lands on the frame combat STARTS cannot
        /// slip through the gate.
        /// </summary>
        private void Open()
        {
            if (_opened) return;

            if (BattleLock.IsInBattle()) { RefuseOpen(); return; }

            _opened = true;
            ReleasePrompt();

            string label = gameObject != null ? gameObject.name : "chest";
            Vector3 at = transform != null ? transform.position : Vector3.zero;

            // Roll the loot. Prefer a WORLD pickup mote (walk-over to collect); if the roll
            // produced nothing OR pickups are disabled, fall back to a direct larder deposit
            // so the open is still paid. ItemPickupSpawner.Spawn no-ops when the lane is off.
            bool bossTable = string.Equals(LootTableCatalog.Find(lootTableId)?.Source, "boss",
                System.StringComparison.OrdinalIgnoreCase);
            var lines = ItemDropSystem.RollLines(lootTableId, includeBossOnly: bossTable);
            if (ItemDropSystem.UseWorldPickups && lines != null && lines.Count > 0)
            {
                // WO-1589: the "chest" token is TRACE ONLY - it rides on the mote so the
                // reward line printed at PICKUP names the chest as its origin, which is what
                // makes "one CHEST REWARD TOAST per chest" readable in a device log at all.
                // Nothing is said HERE: the loot is on the floor, not in the player's hands.
                ItemPickupSpawner.Spawn(at, lines, "chest");
                FlowTrace.Step(Sys, $"{label} opened -> dropped {lines.Count} loot line(s) as a world mote (table '{lootTableId}')");
            }
            else
            {
                int deposited = ItemDropSystem.DepositLines(lines);
                FlowTrace.Step(Sys, $"{label} opened -> deposited {deposited} item(s) to larder " +
                    $"from the ONE captured roll (table '{lootTableId}', bossLines={bossTable})");

                // WO-1589: this branch IS a bank - the roll went straight into the larder, so
                // the player holds it the instant the chest opens. Say it here, through the
                // SAME producer the mote pickup uses. The world-mote branch above deliberately
                // says NOTHING yet: nothing has been granted until the mote is walked over,
                // and ItemPickupMarker.Collect owns that toast.
                if (deposited > 0)
                    LootRewardToast.Announce(lines, at + Vector3.up * 1.0f, "chest-deposit");
            }

            // The prop STAYS, wearing its open lid. An empty room with a vanished chest
            // cannot be told apart from a room whose chest never spawned; an open lid can.
            ShowOpened();
        }

        /// <summary>Swings the lid back so the chest reads as looted by SILHOUETTE, not hue.</summary>
        private void ShowOpened()
        {
            if (_lid == null) _lid = FindLid(gameObject);
            if (_lid != null) _lid.localRotation = Quaternion.Euler(-110f, 0f, 0f);
        }

        private static Transform FindLid(GameObject host)
        {
            if (host == null) return null;
            var body = host.transform.Find(BodyName);
            return body != null ? body.Find("LidPivot") : null;
        }

        // ── Runtime / bake-time factory ─────────────────────────────────────────

        /// <summary>
        /// Build a chest prop carrying a configured <see cref="BreakableContainer"/>.
        /// <paramref name="visualToken"/> ("crate" / "barrel" / "chest") only picks the
        /// wood tint - the silhouette is the same chest either way.
        /// <para>
        /// ⛔ THE SIGNATURE IS LOAD-BEARING: DungeonBaker.PlaceComposeChests invokes this
        /// by REFLECTION with exactly these four arguments
        /// (Assets/Editor/RoomForge/DungeonBaker.cs), as does DungeonChainBuilder. Changing
        /// the name or the arity silently stops placing chests in every composed dungeon -
        /// it fails at bake with a warning, not at compile.
        /// </para>
        /// </summary>
        public static BreakableContainer Create(Transform parent, Vector3 pos, string lootTableId, string visualToken)
        {
            string token = string.IsNullOrEmpty(visualToken) ? "crate" : visualToken;
            var go = new GameObject($"Chest_{token}");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            // Deliberately NOT the "Enemy" layer (WO-1132). The chest is furniture: leaving it
            // on Enemy is what let the hostile reticle lock onto it (WO-1047) and what made the
            // combat camera frame a crate. Default layer, solid collider - it blocks lightly,
            // like any other piece of world furniture, and nothing hostile-hunting can see it.
            go.layer = 0;

            // A solid body box so the chest reads as a physical object. Sized to the built
            // body below; not a trigger - the interaction is a distance check, not a physics
            // event, so the collider's only job is to stop the hero walking through it.
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = false;
            box.center = new Vector3(0f, 0.33f, 0f);
            box.size = new Vector3(0.95f, 0.66f, 0.62f);

            EnsureChest(go, token);

            var bc = go.AddComponent<BreakableContainer>();
            bc.lootTableId = string.IsNullOrEmpty(lootTableId) ? "crate-common" : lootTableId;
            return bc;
        }

        // ── Chest body + legacy normalisation ───────────────────────────────────

        /// <summary>
        /// Makes <paramref name="host"/> a proper chest: off the Enemy layer, legacy primitive
        /// cube stripped, chest body built. IDEMPOTENT - a host that already carries a
        /// <see cref="BodyName"/> child is left alone, so a future bake-time art pass wins
        /// over this with no code change (the ComposedPropVisuals.HasBody idiom).
        /// </summary>
        private static void EnsureChest(GameObject host, string visualToken = null)
        {
            if (host == null) return;

            Guard.Try(Sys, $"normalise chest '{host.name}'", () =>
            {
                // 1. Off the Enemy layer. Baked scenes carry layer=Enemy because Create() USED
                //    to set it and DungeonChainBuilder / KayKitChallengeOutpostBuilder set it
                //    directly. This is the line that retires the WO-1047 defect class on
                //    content already on disk, with no re-bake.
                int enemyLayer = LayerMask.NameToLayer("Enemy");
                if (enemyLayer >= 0 && host.layer == enemyLayer)
                {
                    host.layer = 0;
                    FlowTrace.Step(Sys, $"chest '{host.name}' migrated OFF the Enemy layer " +
                        "(WO-1132: a chest is furniture, not a hostile target).");
                }

                if (host.transform.Find(BodyName) != null) return;   // already bodied by us

                // 2. Strip the legacy primitive cube that Create() used to build ON THE ROOT.
                //    Left in place it would sit inside the chest and read as a grey block.
                //    Only the ROOT mesh is legacy - child meshes are real authored art (below).
                var oldFilter = host.GetComponent<MeshFilter>();
                var oldRend = host.GetComponent<MeshRenderer>();
                bool hadLegacyVisual = oldRend != null || oldFilter != null;
                SafeDestroy(oldRend);
                SafeDestroy(oldFilter);
                if (hadLegacyVisual)
                    FlowTrace.Step(Sys, $"chest '{host.name}': legacy primitive-cube visual stripped (WO-1132).");

                // 3. A host that ALREADY carries real art keeps it - the ComposedPropVisuals
                //    .HasBody law: a bake-time art pass silently wins over the runtime
                //    primitive with no code change here. This is not hypothetical:
                //    KayKitChallengeOutpostBuilder.PlaceBreakables instantiates real KayKit
                //    chest/barrel models as CHILDREN, and building our coffer on top of one
                //    would render two chests inside each other.
                if (host.GetComponentInChildren<Renderer>(true) != null)
                {
                    FlowTrace.Step(Sys, $"chest '{host.name}': authored art already present - " +
                        "runtime chest body SKIPPED (bake-time art wins).");
                    return;
                }

                BuildChestBody(host, visualToken ?? TokenFromName(host.name));
            });
        }

        /// <summary>
        /// The chest art: a banded wooden coffer with a hinged lid, built from URP-lit
        /// primitives. No prefab, no material asset, no Addressables key - the same
        /// runtime-primitive idiom ComposedPropVisuals uses for the composed-dungeon
        /// pillars, so this needs no content build and cannot 404 off the R2 CDN
        /// (CLAUDE.md sec.16). Every child's own collider is STRIPPED: the root
        /// BoxCollider is the one body, and stray primitive colliders would shadow it.
        /// </summary>
        private static void BuildChestBody(GameObject host, string visualToken)
        {
            var body = new GameObject(BodyName);
            body.transform.SetParent(host.transform, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;

            Color wood = WoodTint(visualToken);
            Color iron = new Color(0.30f, 0.30f, 0.34f);
            Color gold = new Color(0.85f, 0.70f, 0.28f);

            // Base coffer.
            var baseBox = Prim(body.transform, "Base", PrimitiveType.Cube, wood);
            baseBox.transform.localPosition = new Vector3(0f, 0.22f, 0f);
            baseBox.transform.localScale = new Vector3(0.90f, 0.44f, 0.58f);

            // Two iron bands around the base - the detail that makes the silhouette read as
            // a CHEST and not a crate, which matters far more than the tint in a dark room.
            var bandL = Prim(body.transform, "BandL", PrimitiveType.Cube, iron);
            bandL.transform.localPosition = new Vector3(-0.28f, 0.22f, 0f);
            bandL.transform.localScale = new Vector3(0.10f, 0.47f, 0.61f);

            var bandR = Prim(body.transform, "BandR", PrimitiveType.Cube, iron);
            bandR.transform.localPosition = new Vector3(0.28f, 0.22f, 0f);
            bandR.transform.localScale = new Vector3(0.10f, 0.47f, 0.61f);

            // The LID, on a pivot at the BACK edge so it swings open like a real hinge.
            // The pivot is what ShowOpened rotates - closed vs open is a silhouette change,
            // never a colour change (owner is red/green colourblind).
            var pivot = new GameObject("LidPivot");
            pivot.transform.SetParent(body.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.44f, -0.29f);
            pivot.transform.localRotation = Quaternion.identity;

            var lid = Prim(pivot.transform, "Lid", PrimitiveType.Cube, wood);
            lid.transform.localPosition = new Vector3(0f, 0.06f, 0.29f);
            lid.transform.localScale = new Vector3(0.94f, 0.14f, 0.62f);

            var lidBand = Prim(pivot.transform, "LidBand", PrimitiveType.Cube, iron);
            lidBand.transform.localPosition = new Vector3(0f, 0.07f, 0.29f);
            lidBand.transform.localScale = new Vector3(0.20f, 0.17f, 0.64f);

            // Latch - a small bright plate at the front. Emissive so the chest is findable
            // in an unlit dungeon at low lantern oil (the ComposedPropVisuals lesson).
            var latch = Prim(body.transform, "Latch", PrimitiveType.Cube, gold, emissiveMul: 0.9f);
            latch.transform.localPosition = new Vector3(0f, 0.40f, 0.30f);
            latch.transform.localScale = new Vector3(0.16f, 0.14f, 0.06f);

            FlowTrace.Step(Sys, $"chest body built on '{host.name}' (token '{visualToken}') - " +
                "openable prop, no damage contracts, not on the Enemy layer (WO-1132).");
        }

        private static GameObject Prim(Transform parent, string name, PrimitiveType type,
                                       Color tint, float emissiveMul = 0.28f)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            // STRIP the primitive's own collider - decoration hung under the root body box.
            SafeDestroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            Paint(go, tint, emissiveMul);
            return go;
        }

        /// <summary>
        /// Destroy that is safe on BOTH sides of the editor boundary. Create() is invoked at
        /// BAKE time (DungeonBaker, edit mode) as well as at runtime, and plain Destroy()
        /// THROWS in edit mode ("Destroy may not be called from edit mode") - which would
        /// abort the bake mid-chest and leave the composed dungeon half-populated.
        /// </summary>
        private static void SafeDestroy(UnityEngine.Object victim)
        {
            if (victim == null) return;
            if (Application.isPlaying) Destroy(victim);
            else DestroyImmediate(victim);
        }

        /// <summary>
        /// URP-safe painting. CreatePrimitive ships the built-in Standard shader, which URP
        /// cannot render -> it falls back to Hidden/InternalErrorShader (MAGENTA). Build the
        /// material explicitly - the same class of fix as the "pink floor" URP/Lit lesson.
        /// </summary>
        private static void Paint(GameObject go, Color tint, float emissiveMul)
        {
            var r = go.GetComponent<Renderer>();
            if (r == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                // NOT silent (sec.12): no shader means the chest is built but unpainted, which
                // looks like "the art did not land" rather than "the shader is missing".
                FlowTrace.Warn(Sys, $"no URP/Lit or Standard shader for '{go.name}' - chest part keeps the default material.");
                return;
            }
            var mat = new Material(shader) { color = tint };
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            mat.SetColor("_EmissionColor", tint * emissiveMul);
            r.sharedMaterial = mat;
        }

        /// <summary>Recovers the visual token from a legacy baked name ("Breakable_chest").</summary>
        private static string TokenFromName(string goName)
        {
            if (string.IsNullOrEmpty(goName)) return "crate";
            string lower = goName.ToLowerInvariant();
            if (lower.Contains("chest")) return "chest";
            if (lower.Contains("barrel")) return "barrel";
            return "crate";
        }

        private static Color WoodTint(string token)
        {
            token = (token ?? "crate").ToLowerInvariant();
            if (token.Contains("chest")) return new Color(0.52f, 0.36f, 0.20f);  // rich chest wood
            if (token.Contains("barrel")) return new Color(0.42f, 0.28f, 0.16f); // dark wood
            return new Color(0.55f, 0.40f, 0.24f);                               // crate wood
        }
    }
}
