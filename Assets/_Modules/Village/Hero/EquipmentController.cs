// =============================================================================
// EquipmentController — visually equips real (KayKit) weapon meshes on a Humanoid
// hero by attaching them to the rig's hand bones, driven by the existing Gear-v1
// equip data (GearLoadout / WeaponDef). Armor is stubbed (entry point wired, no
// visual yet — assets incoming).
// -----------------------------------------------------------------------------
// Assembly: DeNelle.Village   Namespace: DeNelle.Village
//
// WHY THIS EXISTS / WHAT IT GENERALIZES:
//   Gear-v1 already attaches a *primitive cube* sword/staff/mace to the RightHand
//   bone via GearVisualApplier.AttachWeaponVisual (GearVisualApplier.cs:104-203,
//   parent resolved at :137 GetBoneTransform(HumanBodyBones.RightHand)), and the
//   Ranger gets a real KayKit bow via HeroBowAttachment (LeftHand bone, prop loaded
//   from Resources/Heroes/Props + bounds-normalized). This controller GENERALIZES
//   that pattern to ALL weapon classes using the real KayKit weapon meshes:
//     • resolve the equipped weapon id (from GearLoadout.EquippedWeapon, the SAME
//       data model — no new gear model invented),
//     • map the id -> a KayKit mesh + per-weapon grip offset/rotation,
//       (mesh attaches to RightHand; shields -> LeftHand),
//     • instantiate, parent, destroy the previous prop on swap (no stacking),
//     • re-attach whenever GearLoadout.OnGearChanged fires (the SAME event the
//       cube path already raises on equip-change).
//   The legacy cube GearVisualApplier stays as the no-mesh fallback (it null-guards
//   and is gated OFF by EnablePrimitiveGear), so nothing double-stacks: when a real
//   mesh resolves we use it; otherwise we keep the existing behaviour.
//
// MESH-LOADING GAP (important):
//   The KayKit weapon FBXs live under Assets/Models/KayKit/.../KayKit Fantasy
//   Weapons Bits 1.0/Assets/fbx(unity)/ — that folder is NOT a Resources folder
//   (and the pack is gitignored), so Resources.Load CANNOT reach them at runtime /
//   in a build. This mirrors the exact constraint HeroBowAttachment documents for
//   the bow. The build-safe convention already used for the bow is to COPY the
//   needed KayKit props into Assets/Resources/Heroes/Props/ (committed, Resources-
//   loadable). So this controller loads each weapon mesh from
//       Resources/Heroes/Props/Weapons/<meshName>
//   FIRST; if absent (mesh not yet copied), it falls back to a tinted primitive so
//   the hero still reads as armed. ACTION FOR ART/CLI: drop sword_A/D/G, staff_A,
//   wand_A, bow_A, dagger_A, axe_A, hammer_A, shield_A (as prefabs or fbx) into
//   Assets/Resources/Heroes/Props/Weapons/ to light up the real meshes. Until then
//   the primitive fallback renders.
// =============================================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using DeNelle.Core;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.Geometry;

namespace DeNelle.Village
{
    /// <summary>
    /// Component on a hero. Reads the hero's equipped weapon (via GearLoadout, the
    /// Gear-v1 data model) and attaches a real KayKit weapon mesh to the Humanoid
    /// rig's RightHand bone (shields -> LeftHand) with a per-weapon grip transform.
    /// Re-attaches on equip-change. Armor is a wired-but-no-op stub for now.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class EquipmentController : MonoBehaviour
    {
        // Resources sub-path the build-safe KayKit weapon props are copied into
        // (mirrors HeroBowAttachment's "Heroes/Props/Bow"). See file header / gap note.
        private const string WeaponPropResourceDir = "Heroes/Props/Weapons/";
        // Reference standing height for heldLength presets (GearVisualApplier / NavMeshAgent canon).
        private const float RefHeroHeightM = 1.8f;

        private const string PropName = "EquipmentProp_Weapon";

        // ── Weapon-id -> KayKit mesh + grip map ──────────────────────────────────
        // TODO data-driven: add a `visualMesh` (string) + `grip` (pos/euler/scale) to
        // weapons.json and read them off WeaponDef instead of this hardcoded table.
        // For now this maps the ACTUAL ids in weapons.json (mage_*, knight_*, ranger_*,
        // aegis_*) onto owned KayKit Fantasy Weapons Bits meshes. The grip values seat
        // the hilt/grip in the palm for a ~1.8m Humanoid hero; tune against a screenshot.
        // Weapon family — gates the grip-point algorithm. Sword/Blade uses the geometric
        // hilt-spike inference (grip the HANDLE below the crossguard). Everything else keeps
        // its proven centre/root grip but the same hook exists to extend later
        // (Staff -> mid-shaft, Bow -> riser/centre, etc.).
        private enum WeaponClass { Sword, Dagger, Axe, Hammer, Staff, Wand, Bow, Shield }

        // WO-435: "melee" = every hand-held shafted/bladed weapon that grips from its own
        // geometry (handle below the head/crossguard, primary axis pointing out of the fist).
        // ALL of these now share ONE seating path: bounds-normalize -> SeatByHandle (grip from
        // mesh) -> rig-hand-axis rotation + per-archetype nudge. Bow (own NormalizeInto + LeftHand
        // centre-grip) and Shield (centre-grip, LeftHand) are NOT melee and keep their own paths.
        private static bool IsMelee(WeaponClass k) =>
            k == WeaponClass.Sword || k == WeaponClass.Dagger || k == WeaponClass.Axe ||
            k == WeaponClass.Hammer || k == WeaponClass.Staff || k == WeaponClass.Wand;

        private sealed class WeaponVisual
        {
            public string mesh;          // KayKit mesh name under Resources/Heroes/Props/Weapons/
            public bool leftHand;        // shields -> LeftHand; everything else RightHand
            public Vector3 gripPos;      // local position on the hand bone
            public Vector3 gripEuler;    // local rotation on the hand bone
            public float heldLength;     // longest-axis target length (m) after bounds-normalize
            public Color tint;           // fallback-primitive tint when the mesh isn't present
            public WeaponClass kind;     // family — drives the grip-point inference path
            public bool native;          // prop is authored grip-at-origin + oriented (e.g. Blink) — trust it: skip normalize/hilt-seat
        }

        // Per-archetype grip presets (one place to tune each weapon family's seat).
        // GRIP-POINT (WO-435): gripPos for melee is now ZERO — the grip point is DERIVED from
        // the mesh by SeatByHandle (handle-from-geometry), not hand-typed. The old per-archetype
        // Y-offsets ("0.02/0.05 everywhere") were the §4 smell: a constant applied asset-agnostic
        // to every FBX regardless of its own handle pivot. Only Shield keeps a deliberate non-zero
        // gripPos (its centre-grip seat) and the bow stays zero (its own NormalizeInto path).
        private static WeaponVisual Sword(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = false, kind = WeaponClass.Sword,
            gripPos = Vector3.zero, gripEuler = new Vector3(0f, 0f, 0f),
            heldLength = 0.65f, tint = new Color(0.74f, 0.75f, 0.78f)   // ~36% of RefHeroHeightM (GearVisualApplier canon)
        };
        private static WeaponVisual Dagger(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = false, kind = WeaponClass.Dagger,
            gripPos = Vector3.zero, gripEuler = new Vector3(0f, 0f, 0f),
            heldLength = 0.40f, tint = new Color(0.70f, 0.72f, 0.76f)
        };
        private static WeaponVisual Axe(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = false, kind = WeaponClass.Axe,
            gripPos = Vector3.zero, gripEuler = new Vector3(0f, 0f, 0f),
            heldLength = 0.80f, tint = new Color(0.68f, 0.66f, 0.62f)
        };
        private static WeaponVisual Hammer(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = false, kind = WeaponClass.Hammer,
            gripPos = Vector3.zero, gripEuler = new Vector3(0f, 0f, 0f),
            heldLength = 0.85f, tint = new Color(0.66f, 0.66f, 0.68f)
        };
        private static WeaponVisual Staff(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = false, kind = WeaponClass.Staff,
            gripPos = Vector3.zero, gripEuler = new Vector3(0f, 0f, 0f),
            heldLength = 1.30f, tint = new Color(0.60f, 0.50f, 0.40f)
        };
        private static WeaponVisual Wand(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = false, kind = WeaponClass.Wand,
            gripPos = Vector3.zero, gripEuler = new Vector3(0f, 0f, 0f),
            heldLength = 0.45f, tint = new Color(0.55f, 0.45f, 0.62f)
        };
        private static WeaponVisual Bow(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = true, kind = WeaponClass.Bow,   // bow goes in the off/bow (LEFT) hand
            // owner spec: bow longest->Y, grip=center. NormalizeInto seats the bow to spec in the
            // GRIP ROOT's own frame: LONGEST axis (limbs/nock-to-nock) -> local +Y, NARROWEST -> +X,
            // curve depth -> +Z, grip at the root origin.
            //
            // ⚠ CORRECTED 2026-08-16 — see HeroBowAttachment.cs:55-65 for the full record.
            // This block used to argue that "gripEuler stays ZERO here — exactly the proven-correct
            // value HeroBowAttachment uses ... a prior +91 Z tweak rotated the already-correct bow
            // ~90 degrees sideways — that WAS the 'bow is turned' bug". THAT PREMISE WAS FALSE.
            // NormalizeInto has no knowledge of the BONE the grip root gets parented to, so a zero
            // euler maps the limb span onto the hand bone's raw +Y (the fist axis) and the bow lies
            // HORIZONTALLY across the body. HeroBowAttachment no longer relies on a zero euler at
            // all: it DERIVES the hand-local seat via WeaponBoundsOrient.ComputeBowHeldRotation.
            // This comment is preserved-and-marked rather than deleted because the wrong conclusion
            // recorded here is what caused the correct fix to be reverted once already.
            //
            // NOTE: on the HERO this preset is not reached — DeferBowToBowAttachment skips bows so
            // HeroBowAttachment owns the held bow. It still serves COMPANIONS / non-rangers, and
            // as of 2026-08-16 that path is DERIVED too: AttachLoadedProp routes kind==Bow through
            // WeaponBoundsOrient.ComputeBowHeldRotation and WITHHOLDS ApplyGlobalWeaponYaw from the
            // result, so a companion archer gets the hero's seat. gripEuler below is therefore a
            // felt-tune NUDGE composed on top of that derivation — it is NOT the seat. Leave it at
            // zero unless a screenshot says otherwise; a dialed constant here is the failure mode
            // this whole comment block exists to record (see RangedPrimaryRegression case 9).
            gripPos = new Vector3(0f, 0f, 0f), gripEuler = new Vector3(0f, 0f, 0f),
            heldLength = 0.92f, tint = new Color(0.36f, 0.22f, 0.10f)
        };
        private static WeaponVisual Shield(string mesh) => new WeaponVisual
        {
            mesh = mesh, leftHand = true, kind = WeaponClass.Shield,   // shields -> LeftHand per spec
            gripPos = new Vector3(-0.05f, 0f, 0f), gripEuler = new Vector3(-58f, 16f, -90f),  // Offset Forge + hand-bone nudge 2026-06-23: shield_A rot (-58,16,-90).
            heldLength = 0.45f, tint = new Color(0.58f, 0.60f, 0.64f)   // ~25% of RefHeroHeightM — torso-scale buckler
        };

        // Shallow copy of a preset so the Addressable/fallback paths can flip `native` WITHOUT
        // mutating the shared cached IdMap instance (Resolve may return a cached preset).
        private static WeaponVisual CopyOf(WeaponVisual v) => new WeaponVisual
        {
            mesh = v.mesh, leftHand = v.leftHand, gripPos = v.gripPos, gripEuler = v.gripEuler,
            heldLength = v.heldLength, tint = v.tint, kind = v.kind, native = v.native
        };

        // Mark a preset as a NATIVE prop — a grip-at-origin, correctly-oriented authored prefab
        // (e.g. a Blink weapon: Sword1h_01 sits at the origin, identity rotation). Equip() then
        // routes it through SeatNative (trust its pivot) instead of the bounds-normalize +
        // hilt-inference legacy path that reverse-engineers a grip for raw Tripo/KayKit FBX.
        // Use for any Blink/authored .prefab dropped into Resources/Heroes/Props/Weapons.
        private static WeaponVisual Native(WeaponVisual v) { v.native = true; return v; }

        // Exact-id overrides keyed by the ids actually present in weapons.json.
        // (Falls through to the keyword classifier below for anything not listed.)
        // TODO data-driven: delete this once weapons.json carries visualMesh/grip.
        private static readonly Dictionary<string, WeaponVisual> IdMap =
            new Dictionary<string, WeaponVisual>(System.StringComparer.OrdinalIgnoreCase)
        {
            // Mage — wand at low tier, staff higher.
            { "mage_starter",        Wand("wand_A")   },
            { "mage_oak",            Staff("staff_A") },
            { "mage_arcane",         Staff("staff_B") },
            { "mage_void",           Staff("staff_C") },
            { "aegis_aetherstaff",   Staff("staff_D") },

            // Knight — sword tiers -> sword_A / sword_D / sword_G by tier.
            { "knight_starter",      Native(Sword("sword_A")) },   // Blink Sword1h_01 prefab (grip-at-origin)
            { "knight_iron",         Sword("sword_D") },
            { "knight_oath",         Sword("sword_F") },
            { "knight_dawn",         Sword("sword_G") },
            { "aegis_emberbrand",    Sword("sword_G") },

            // Ranger — bows (LeftHand). NOTE: the Ranger's held bow is ALSO provided by
            // HeroBowAttachment; see EquipBestForHero() where we skip bows to avoid a
            // duplicate. Kept here so a non-ranger equipping a bow still gets one.
            { "ranger_starter",      Bow("bow_A") },
            { "ranger_yew",          Bow("bow_B") },
            { "ranger_storm",        Bow("bow_C") },
            { "ranger_eclipse",      Bow("bow_C") },
            { "aegis_heartwood_longbow", Bow("bow_C") },

            // Cleric — censer reads closest to a mace/hammer; use hammer_A stand-in.
            { "aegis_hallowed_censer", Hammer("hammer_A") },
        };

        // ── Runtime state ────────────────────────────────────────────────────────
        private Animator _animator;
        private float _cachedHeroHeightM;   // measured once per body; 0 = not yet measured
        private GearLoadout _loadout;

        // PACKAGE de-dupe (owner F8 2026-07-03 "holding two swords, shield 180°"): when the hero body
        // BAKES its own weapon/shield/helmet (Paladin package — HeroBodySwapper tags the SAME root with
        // PackageBakedGearMarker), the KayKit weapon-mesh + shield-mesh prop attach is SKIPPED so the
        // baked gear is the only gear visible. Cheap GetComponent on the root — equip is event-driven,
        // not a hot loop (and LateAttachRetry early-outs on it). Loadout/stat/armor-tint stay fully active.
        private bool PackageBakedGear => TryGetComponent(out PackageBakedGearMarker marker) && marker.enabled;
        private GameObject _currentWeaponProp;
        private string _currentWeaponId;
        private int _armorTier;

        // ── IN-GAME SEATING EDITOR support (WO-577, Offset Forge slice 2) ─────────────
        // The live on-screen Seating Editor (SeatingEditorOverlay) edits the offset of the
        // CURRENTLY equipped weapon/off-hand by eye. To preview live + reproduce the runtime
        // seat exactly, it needs the same inputs the attach path used. These are captured on
        // each attach (main-hand below; off-hand mirror further down) and consumed by the
        // public editor API at the bottom of this class. Inert when no editor is open.
        private string      _currentWeaponMeshKey;     // offset id (mesh name, e.g. "sword_A")
        private WeaponClass _currentWeaponKind;
        private bool        _currentWeaponMelee;
        private float       _currentWeaponHeldLength;
        private Vector3     _currentWeaponGripPos;
        private Vector3     _currentWeaponGripEuler;
        private bool        _currentWeaponNative;
        /// <summary>WO-1431: the archetype the MAIN-HAND seat was dispatched on, resolved ONCE at
        /// attach by <see cref="WeaponOrientHelper.Classify"/> and reused by the Seating Editor
        /// preview so the preview and the shipped seat can never dispatch on different families.
        /// (The shield's sheathed path was fixed for exactly this class of two-baseline drift.)</summary>
        private WeaponArchetype _currentWeaponArchetype;
        /// <summary>WO-1431: the main-hand mirror of <see cref="_currentOffHandDerivable"/> — this
        /// prop passed the WO-1123/WO-1215 precedence ladder at attach, so an archetype-derived
        /// grip may touch it. False = an authored Offset Forge row or a SUBSTANTIATED
        /// `manual: true` owns the seat and the derived rule must keep its hands off.</summary>
        private bool _currentWeaponDerivable;
        private string  _currentOffHandMeshKey;
        private float   _currentOffHandHeldLength;
        private Vector3 _currentOffHandGripPos;
        private Vector3 _currentOffHandGripEuler;
        private bool    _currentOffHandNative;
        // ── WO-1123 derived-seat state (mirrors _currentWeaponKind for the off hand) ──────────
        // Captured ONCE at attach so ApplyHoldPose — which re-asserts the pose EVERY FRAME — never
        // pays a catalog lookup per frame (the same discipline the throttled sheathed-offset traces
        // above were forced into after they blinded three F8 captures).
        private WeaponClass _currentOffHandKind;
        /// <summary>`manual: true` on this off-hand's catalog row: CANON, never auto-overwritten.</summary>
        private bool _currentOffHandManual;
        /// <summary>The off-hand passed every precedence gate at attach, so a derived pose may be
        /// computed for it (still re-checked per pose for an authored SHEATHED row).</summary>
        private bool _currentOffHandDerivable;
        /// <summary>The SHEATHED pose's own derivability, deliberately SEPARATE from
        /// <see cref="_currentOffHandDerivable"/> (owner ruling 2026-08-20). The drawn flag folds in
        /// the DRAWN authored row, and the capture proved that an authored drawn row
        /// ("source=AuthoredOffset ... authoredRow=True") silently switched off the sheathed
        /// derivation as well — a row dialled for the hand speaking for a pose on the hip. The
        /// sheathed pose has its own "&lt;meshKey&gt;@sheathed" channel, checked per pose in
        /// ComputeSheathedOffHandRotation; this flag is only "is it a shield we may derive at all".</summary>
        private bool _currentOffHandSheathDerivable;
        /// <summary>The measured half of the shield seat (thickness axis + which face has the
        /// handle), resolved ONCE at attach. Default/invalid = no derivation available.</summary>
        private WeaponOrientHelper.ShieldFrame _currentOffHandShieldFrame;
        /// <summary>The same measurement taken against the SEATING EDITOR's own preview seat (which
        /// re-seats through NormalizeInto even for a native prop). Separate from the runtime frame
        /// on purpose — sharing one would pose the preview off the wrong axis.</summary>
        private WeaponOrientHelper.ShieldFrame _previewShieldFrame;

        /// <summary>
        /// WO-1123: the ONE reader of `WeaponDef.manual` on the gear side. `manual: true` means the
        /// row's seat is owner-dialled and a derived pass leaves it EXACTLY as loaded
        /// (ARCHITECTURE_PRINCIPLES §4). Absent/unknown id => false => derivable, which is the
        /// correct answer for the 15 hand-authored rows (knight_shield_starter among them).
        /// </summary>
        private static bool IsManualOrientRow(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var def = GearCatalog.FindWeapon(id);
            return def != null && def.manual;
        }

        /// <summary>
        /// WO-1215: the ONE reader of <c>WeaponDef.generated</c>. True = the catalog row was
        /// machine-emitted by GearCatalogGenerator, so a <c>manual: true</c> on it cannot record an
        /// owner-dialled seat (the generator stamps <c>manual = false</c>; the pair only exists
        /// because a data-only balance pass wrote it). Fed to
        /// <see cref="WeaponOrientHelper.ManualSeatIsSubstantiated"/>, never consulted alone —
        /// being generated is not by itself a reason to re-seat anything.
        /// </summary>
        private static bool IsGeneratedCatalogRow(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var def = GearCatalog.FindWeapon(id);
            return def != null && def.generated;
        }
        // While a seating edit is live: suspend the auto idle/combat hold so the grip root
        // the editor drives is not stomped by ApplyHoldPose, and remember which slot is edited.
        private bool _seatingEditActive;
        private bool _seatEditOffHand;
        private int  _seatEditMode = -1;   // -1 unseated, 0 nudge(geometry), 1 vertical(fullOverride)
        // SHEATHED seating edit (2026-07-07): the owner dials the BACK (sheathed) pose live; the
        // edit runs in the back-socket frame and saves under "<meshKey>@sheathed" (see ApplyHoldPose).
        private bool _seatEditSheathed;

        // Registry key suffix for owner-authored SHEATHED poses. Registry keys are arbitrary
        // strings (verified: plain Dictionary + JsonUtility string field, no sanitization), so
        // '@' passes through save/load/remove untouched.
        private const string SheathedKeySuffix = "@sheathed";

        // AUTHORED SCALE per slot (2026-07-07 WYSIWYG scale-parity fix): the owner-dialed uniform
        // scale (offsets.json fo.scale, default 1). The rendered local scale for a compensated slot
        // is ALWAYS ParentScaleCompensation(parent) * authoredScale — one composition shared by the
        // attach path, ApplyHoldPose re-parents, and the Seating Editor preview, so what the owner
        // approves in the editor is byte-identical to every subsequent boot.
        private float _weaponAuthoredScale  = 1f;
        private float _offHandAuthoredScale = 1f;

        // ── ARMOR TINT (WO-567) ──────────────────────────────────────────────────────
        // The combat-pivot north star keeps ONE static hero model — armor is NOT a mesh swap
        // (Blink junked). To make "equipped armor" READ on the body, higher armor tiers tint the
        // hero BODY via a MaterialPropertyBlock accent (a base-color multiply, richer with tier).
        // CHEAP + LEAK-FREE: an MPB never instances a material (mirrors HeroArmorRimLight). It
        // COEXISTS with HeroArmorRimLight's emission MPB — both use the GetPropertyBlock-merge
        // pattern, so the base-color tint (this) and the rarity rim GLOW (rim light) stack.
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId     = Shader.PropertyToID("_Color");
        private MaterialPropertyBlock _armorMpb;
        private readonly List<SkinnedMeshRenderer> _bodyRenderers = new List<SkinnedMeshRenderer>();
        private readonly List<Color> _bodyBaseColors = new List<Color>();   // authored base color per renderer (multiply target)
        private bool _armorTintDirty;

        // OFF-HAND (shield) prop — attached to the OFF hand (LeftHand) alongside the main weapon.
        // Mirrors the main-hand prop lifecycle (destroy-on-swap, no stacking). Driven off
        // GearLoadout.EquippedOffHand on the SAME OnGearChanged event the main weapon uses.
        private const string OffHandPropName = "EquipmentProp_OffHand";
        // Mesh child under the grip root. MUST stay a different name: both used to be
        // EquipmentProp_OffHand, so the Inspector had two objects with the same label —
        // the child is identity; the root is the Offset Forge row.
        private const string OffHandMeshName = "EquipmentProp_OffHand_Mesh";
        private GameObject _currentOffHandProp;
        // PROD-019/D-SEAT instrumentation only. Records whether the current
        // off-hand attach left the Addressables success path. No runtime pose or
        // fallback decision reads this field.
        private bool _offHandAddressableFailed;
        private string _lastOffHandSeatProofSignature;
        /// <summary>PROD-019: last sheathe parent we ran EnsureWeaponRenderersVisible for —
        /// cleared when the off-hand leaves that seat so a reparent re-shows without per-frame spam.</summary>
        private Transform _offHandSheatheShownOn;
        private string _currentOffHandId;

        // OFF-HAND Addressables handle (Blink shields load via "gear/weapon/Shield1h_XX"). ONE owner
        // (this controller) — released on every off-hand swap / detach / OnDisable so a Blink shield
        // prefab never leaks. Its OWN generation counter rejects a stale async completion that lands
        // after the player swapped/unequipped the off-hand mid-load (no ghost shield).
        private AsyncOperationHandle<GameObject> _offHandHandle;
        private bool _offHandHandleOpen;
        private int _offHandGeneration;
        // Addressables owns the prefab dependency graph.  The live Seeker build proved that an
        // instantiated shield can keep its MeshRenderer while its sharedMesh later becomes null
        // (healthy at attach, <no renderer> when combat draws it).  Keep runtime-owned copies of
        // mesh/material dependencies for the lifetime of this slot so a handle/ref-count churn or
        // bundle eviction cannot hollow out an otherwise-live prop.
        private readonly List<Mesh> _offHandRuntimeMeshes = new List<Mesh>();
        private readonly List<Material> _offHandRuntimeMaterials = new List<Material>();

        // Addressables equip (WO-Item, Blink gear): when the equipped WeaponDef loads its
        // prefab via Addressables (loadVia=="addressable" or a "gear/" address in prefabPath),
        // we LoadAssetAsync the prefab and attach on completion. The handle is held here and
        // Addressables.Release'd on the next swap / unequip / OnDisable so a Blink prefab never
        // leaks (§2b.1 pooling discipline — ONE owner of the handle, the attach system). The
        // generation counter rejects a stale async completion that resolves AFTER the player
        // already swapped to a different weapon (no ghost prop from an out-of-date load).
        private AsyncOperationHandle<GameObject> _weaponHandle;
        private bool _weaponHandleOpen;
        private int _equipGeneration;

        // True while HeroBowAttachment owns the ranger's held bow, so a bow equip is SKIPPED here.
        // ⚠ EVALUATED LIVE, NEVER CACHED (fix 2026-08-16). This used to be a bool cached in Awake().
        // HeroBodySwapper adds the EquipmentController (running its Awake) at BuildBody long BEFORE
        // it calls HeroBowAttachment.AttachTo, so the cached value was permanently FALSE, the skip
        // below never fired, and the ranger got TWO bows: the attachment's (correctly seated) and
        // this controller's (seated from _baseGripEuler = zero euler). Component-add ORDER must not
        // be able to defeat the de-dupe.
        private bool DeferBowToBowAttachment => GetComponent<HeroBowAttachment>() != null;

        // ── Hold-state (idle lowered  vs  combat ready) ──────────────────────────────
        // Driven off the SAME combat signal the camera/locomotion use: HeroLocomotion
        // computes `engaged = WaveManager present || moving` and calls ActorAnimator
        // .SetCombatStance(engaged). We mirror that here (auto fallback below) and expose
        // SetCombatActive(bool) so any caller (HeroLocomotion / a future CombatState
        // registry) can drive it authoritatively. The pose is applied at the ATTACH level
        // — a local-rotation offset on the grip root — so no hero animation clip is needed
        // for the held-low vs held-ready read (the bonus; the GRIP is the priority).
        private Transform _gripRoot;          // current weapon's grip-root transform

        // WO-VFX-WEAPON-TRAILS: read-only accessor so WeaponTrailController can anchor the blade
        // trail on the actual held weapon's grip root (the moving prop transform) rather than the
        // bare hand bone. Null until a weapon is equipped/attached; the trail controller falls back
        // to the RightHand bone (then a synthetic child) when this is null.
        public Transform GripRoot => _gripRoot;
        private Vector3 _baseGripEuler;       // the weapon's neutral grip rotation
        private bool _combatActive;           // current hold state (false = idle/lowered)
        private bool _combatExplicit;         // a caller drove SetCombatActive -> stop auto-mirroring
        private WaveManager _waveManager;     // auto-fallback combat signal (same as locomotion)

        // ── CARRY STATE: sheathed (town / out-of-combat) ↔ drawn (in-combat) ─────────
        // OWNER DESIGN (2026-07-04): out of combat the weapon is SHEATHED on the BACK — NOT
        // gripped in the hand — and is DRAWN to the hand only in combat.
        //
        // WHY THIS FIXES THE ~60° OVERWORLD FLOAT (context-delta RCA): the weapon is parented to
        // the hand bone, so it FOLLOWS the animated hand identically in both contexts — the seat
        // itself is not globally broken (battle proves it correct). The ONLY per-context rotation
        // in the whole attach path was the retired IdleHoldOffsetEuler = (55,0,0), applied by the
        // old ApplyHoldPose ONLY when out of combat and ZERO in combat. Composed onto _baseGripRot
        // (which yaws the blade +90° via _swordGripEuler), that 55° local-X tilt reads as a ~55-60°
        // world YAW — exactly the owner's "~-60° in Y, correct in battle, wrong in town." Rather
        // than tuning that hack, we remove it: out of combat the hand grip is not shown AT ALL (the
        // weapon is on the back), and in combat the grip uses the SAME seat battle uses (pure
        // _baseGripRot). So the hand grip only ever renders in the context that seats it right.
        //
        // The in-combat signal is the CANONICAL one (HeroLocomotion.IsWaveInCombat, HeroLocomotion.cs
        // :552-563 — a live wave Countdown/Active OR BattleArena.AnyBattleInProgress); the Update()
        // auto-fallback below mirrors it so the draw matches the just-shipped calm/combat idle split.
        //
        // Sheathed pose is VISUAL (owner felt-tunes) — Inspector-exposed with deterministic defaults.
        //
        // ⛔ THE BACK CARRY IS RETIRED — SHEATHED PROPS HANG FROM THE HIPS (owner ruling 2026-08-20,
        // verbatim: "sheathed should sit inverted with the longest mesh (y) up and down attached to
        // hip bone"). The line that used to sit here read "The weapon rides a back socket (created
        // under the Chest/Spine bone by ResolveBackSocket) laid diagonally across the back; the
        // off-hand/shield rides the SAME socket, opposite side" — and BOTH halves of it were the
        // defect the owner reported, proven by the device capture (logs/device/2026-08-20-equip.log):
        //   • "ResolveBackSocket on 'Hero (Blaise)': sheathe anchor under bone 'CC_Base_Spine01'."
        //     GetBoneTransform(Chest) resolves to the LOW spine on this CC rig, so the "back" carry
        //     was already sitting at the waist — which is exactly the horizontal-across-the-waist
        //     sword in logs/device/sheathed-weapon.png.
        //   • "AttachOffHandProp MEASURED ... parent='SheatheSocket_Back' ... s(0.72, 0.92, 0.72)"
        //     the shield is NOT missing — it renders a real 0.72x0.92x0.72 m volume. It shared ONE
        //     socket transform with the sword at the same origin, so it was buried in the body mesh.
        //     "Rides the same socket, opposite side" was never true: both props got the same anchor
        //     and only their own local offsets separated them, which the bone's 1.67 lossyScale and
        //     the rig-arbitrary bone axes then scrambled.
        // So: TWO sockets, on OPPOSITE HIPS, and the long axis runs vertical. See ResolveSheatheSocket.
        //
        // ⚠ THESE OFFSETS ARE IN *BODY* SPACE NOW, NOT BONE-LOCAL (same ruling). x = OUTWARD along
        // the prop's own hip side (the per-side sign is applied for you, so both defaults are
        // POSITIVE), y = up, z = forward. ComputeSheathLocalPosition converts to the socket frame
        // with InverseTransformVector, which also divides out the bone's lossyScale — so a metre
        // here is a metre on the hero, on any rig, whatever axes CC_Base_Hips happens to carry.
        // A bone-local vector could not survive either (the old (-0.10, 0.12, -0.15) was authored
        // against a chest bone and inherited by a spine one).
        [SerializeField] private Vector3 _sheatheWeaponLocalPos   = new Vector3(0.15f, -0.02f, -0.04f);
        // SHEATHED SWORD ROTATION — DERIVED, not guessed (owner F8 fix 2026-07-04): the on-back sword
        // rotation is no longer a magic hand-typed euler (the old (8,0,158) had ZERO relationship to the
        // weapon geometry OR the chest bone's rig-specific axes — the §4 smell, exactly why it sat wrong
        // while the DRAWN seat was right). ApplyHoldPose now builds the base sheathe orientation from the
        // BODY's own axes with the SAME Quaternion.LookRotation(flat, blade) construction the correct
        // battle draw uses (ComputeMeleeGripRotation, the "secret") — see ComputeSheathRotation. This
        // field is the persisted AUTHORED NUDGE composed ON TOP of that derived base (Inspector/owner
        // felt-tune, never auto-overwritten), mirroring how _swordGripEuler nudges the drawn seat.
        // Default ZERO = pure geometric sheathe (component is always runtime AddComponent, so this code
        // default applies — no scene/prefab serializes an old value over it).
        [SerializeField] private Vector3 _sheatheWeaponLocalEuler = Vector3.zero;
        // Degrees the sheathed long axis leans off VERTICAL, toward the OFF (main-hand-opposite)
        // side. DEFAULT IS NOW 0 = straight up-and-down at the hip (owner ruling 2026-08-20: "the
        // longest mesh (y) up and down"). It used to default to 28 — the baldric diagonal of the
        // retired back carry, and the reason the capture's sword read as lying across the waist.
        // The FIELD IS KEPT, not deleted (§12: never strip a tuning seam), so the owner can lean
        // the hang a few degrees off the leg without a recompile. The regression asserts the
        // SHIPPED DEFAULT is vertical, so a re-introduced diagonal fails loudly rather than quietly.
        [SerializeField] private float _sheatheBladeDiagonalDeg = 0f;
        // ⛔ THIS FIELD IS NO LONGER THE AUTHORITY — IT IS THE FALLBACK (owner F8 2026-08-21).
        //
        // It used to be described here as "THE ONE NUMBER THAT FLIPS 'INVERTED' ... if it reads
        // upside down on the device, flip this ONE field". THAT INSTRUCTION IS RETIRED, AND
        // FOLLOWING IT IS THE BUG:
        //   • 2026-08-20, F8 on Blaise: -1 reads upside down.  → +1 shipped.
        //   • 2026-08-21, F8 on the owner's Flameblade: +1 reads upside down.
        // Both captures are correct. Which end of a prop is the TIP is a property of the MESH —
        // NormalizeInto + SeatHiltLowerHalf put the tip at prop-local +Y, while a NATIVE prop
        // (SeatNative, "trust grip-at-origin") keeps whatever the artist authored, which is often
        // the other way round. One global number therefore CANNOT be right for both heroes: every
        // flip of this field simply moves the defect to whoever carries the other mesh, which is
        // exactly the ping-pong the two captures above record.
        //
        // The authority is now PER MESH and DERIVED FROM GEOMETRY:
        // WeaponOrientHelper.TryResolveSheathedTipSign, resolved once at attach into
        // _sheatheTipSign (see ResolveSheathedTipSign below) and consumed by ComputeSheathRotation.
        //
        // THE FIELD IS KEPT, DELIBERATELY (§12: never strip a tuning seam). It is what stands when
        // a prop cannot answer — no renderer, a degenerate long axis, or a grip that sits mid-mesh
        // with Read/Write off so neither the taper nor the origin test can decide. Those cases log
        // a Warn naming themselves, so "the global fallback is driving this prop" is readable in a
        // capture instead of being inferred.
        //   -1  = tip DOWN / hilt up   (prop-local +Y maps onto -body.up)
        //   +1  = tip UP   / hilt down (prop-local +Y maps onto +body.up)
        // Both signs keep the long axis VERTICAL, which is the half of the 08-20 ruling that is not
        // a matter of taste and is not affected by any of this.
        [SerializeField] private float _sheatheLongAxisSign = 1f;
        // The per-mesh answer for the CURRENTLY EQUIPPED main-hand prop. 0 = nothing decidable was
        // measured, and only then does _sheatheLongAxisSign above speak. Resolved once per attach
        // (never per frame — ApplyHoldPose re-asserts the pose at frame rate) and cleared on
        // unequip so a stale sign can never outlive the mesh it was measured from.
        private float  _sheatheTipSign;
        private string _sheatheTipWhy;
        // Body-space offset for the SHEATHED off-hand (shield) — see _sheatheWeaponLocalPos for the
        // frame. It sits on the OPPOSITE hip from the weapon (ResolveSheatheSocket owns which).
        //
        // ⚠ WHAT THIS NUMBER MEANS CHANGED ON 2026-08-20: since ApplyOffHandCentreOnSocket it
        // places the plate's RENDERED CENTRE, not its grip origin. Before that it placed the origin,
        // which for a grip-at-origin shield is the plate's bottom edge — so the plate hung upward
        // and the same 0.26 put it at the hero's chest.
        // ⚠ AND THE OLD JUSTIFICATION WAS MEASURED ON A STALE FIGURE. It read: "pushed further out
        // (0.26) than the sword because the live default shield MEASURES 0.72 m across: at the
        // sword's 0.15 its inner half would be inside the leg." The live plate measures
        // 0.512 x 0.63 x 0.161 in mesh local and renders ~0.36 m wide on the hero (KnightGearProof
        // capture) — the 0.72 was the SHARED-SOCKET era's world AABB of a differently seated prop.
        // 0.26 still stands, but for the CURRENT reason: a ~0.36 m plate centred 0.26 m out has its
        // inner edge ~0.08 m from the body axis, i.e. resting against the thigh rather than floating
        // beside it. Re-measure before re-tuning; do not inherit either number on faith.
        [SerializeField] private Vector3 _sheatheOffHandLocalPos   = new Vector3(0.26f, 0f, -0.05f);
        // AUTHORED CORRECTION (§4 sanctioned manual nudge, owner live felt-tune 2026-07-04 — manual=true,
        // never auto-overwritten): sheathed shield-on-back rotation. Base (0,90,12); owner Z+=180 →
        // (0,90,192) for the face-on-back read. Y+=180 is NOT baked here — ApplyGlobalWeaponYaw
        // composes the same universal flip weapons use (owner 2026-07-05).
        [SerializeField] private Vector3 _sheatheOffHandLocalEuler = new Vector3(0f, 90f, 192f);
        // ── THE SHIELD IS STRAPPED TO THE ARM, NOT HUNG ON THE HIP (owner F8 2026-08-21) ─────────
        // Owner, verbatim: "shield is attaching to hip not wrist or arm" / "the shield on arm or arm
        // bone". The device capture is unambiguous about what she was looking at — every frame:
        //   key='ShieldWithItemLogic' ... parent='SheatheSocket_HipOff' parentLossy=(1.67,1.67,1.67)
        // Body-space offset for the shield at the FOREARM mount. It is small on purpose: the plate's
        // rendered CENTRE is placed here (ApplyOffHandCentreOnSocket), and a shield strapped to a
        // forearm is centred ON the arm, pushed out only far enough that the plate rides outside the
        // limb instead of intersecting it. The hip figure (0.26, above) is a HIP number — it is the
        // half-width of a hero's stance, and re-using it on the arm would float the shield a
        // quarter-metre off the elbow. Both fields survive: the hip one is still what a rig with no
        // mapped forearm falls back to (§12).
        [SerializeField] private Vector3 _armOffHandLocalPos = new Vector3(0.05f, 0f, 0f);

        // Resolved attach targets + the DRAWN local transform, so the carry-state can move each prop
        // between its hand (drawn) and the back socket (sheathed) with no re-equip. _baseGripRot holds
        // the drawn WEAPON rotation; the off-hand keeps its own drawn rotation (it has no rig-axis grip).
        private Transform  _weaponHand;
        private Vector3    _weaponDrawnLocalPos;
        private Transform  _offHandHand;
        private Vector3    _offHandDrawnLocalPos;
        private Quaternion _offHandDrawnLocalRot = Quaternion.identity;
        // TWO sheathe anchors, lazily created under the HIPS bone, one per slot (owner ruling
        // 2026-08-20). There used to be exactly one — `_backSocket`, "the SHARED sheathe anchor" —
        // and sharing it is what buried the shield inside the body: the capture shows both props
        // parented to the single 'SheatheSocket_Back' transform at the same origin. Two transforms
        // on opposite hips make that failure mode structurally impossible, not merely tuned around.
        private Transform  _sheatheSocketMain;   // main-hand weapon — the hip OPPOSITE the weapon hand
        private Transform  _sheatheSocketOff;    // off-hand / shield — the OFF-HAND FOREARM (see below)
        // True when _sheatheSocketOff actually landed on an ARM bone. False = this rig has no mapped
        // forearm and the socket fell back to the hips chain, in which case the HIP offset and the
        // HIP side are the correct pair to use — the pose numbers must follow the anchor that was
        // really resolved, never the anchor that was asked for. (docs/ARCHITECTURE.md: a derived
        // value can be arithmetically perfect and land one transform out.)
        private bool       _sheatheSocketOffIsArm;
        // Which way each slot's hip offset points, as a multiplier on body.right. The weapon hangs
        // on the hip OPPOSITE the drawing hand (a right-handed hero draws across the body from the
        // left hip) and the shield takes the other one. Constants, not fields: this is the invariant
        // "they are never on the same side", and a field could be set to make them collide again.
        private const float SheatheSideMain = -1f;   // -body.right → the hero's LEFT hip
        private const float SheatheSideOff  = +1f;   // +body.right → the hero's RIGHT hip (fallback mount)
        // ⚠ AND THE ARM MOUNT IS ON THE OTHER SIDE FROM THE HIP MOUNT — that is not an inconsistency,
        // it is the anatomy. The off-hand prop seats on the LEFT HAND in every drawn pose
        // (AttachOffHandProp resolves HumanBodyBones.LeftHand unconditionally), so its forearm is the
        // LEFT forearm and "outward, away from the player" there is the hero's LEFT: -body.right.
        // SheatheSideOff above stays +1 because it belongs to the HIP fallback, which is the
        // weapon-hand-side hip. Both constants are consumed through OffHandSheatheSide(), so no call
        // site can pick the wrong one for the anchor it actually got.
        private const float SheatheSideArmOff = -1f; // -body.right → the hero's LEFT (off-hand) arm

        // ── SWORD GRIP ORIENTATION (rig-relative) ────────────────────────────────────
        // THE FIX (task #36 follow-up): the grip POINT (handle below the crossguard) is
        // correct, but the blade DIRECTION was wrong — it lay across the torso. Cause:
        // the grip root was parented under the RightHand bone and its blade axis (prop
        // local +Y) was left aligned to the BONE's local +Y. On this rig the hand bone's
        // local axes do NOT world-align the way a generic prop frame assumes, so "blade
        // +Y" came out pointing sideways across the body instead of forward from the fist.
        //
        // We now build the grip-root's base rotation FROM the hand bone's own axes so the
        // blade extends along the way a fist naturally "points" when gripping a sword, and
        // the grip axis runs along the bone (palm→fingers). Which of the hand bone's local
        // axes is the "point" (forward-from-fist) vs the "grip" (along the bone) is
        // RIG-SPECIFIC, so both are EXPOSED below for an in-Inspector nudge without a
        // recompile. Defaults are the most plausible for a Humanoid RightHand (Unity's
        // Mecanim convention: the bone's local +Y runs down the forearm toward the
        // fingertips = the grip/point line; +Z is roughly the palm normal). Tune on the
        // real rig if the first pick reads off.

        // The hand-bone local axis the BLADE should extend along (forward from the fist).
        // Default +Y (along the finger line — a held blade continues the forearm/finger
        // direction). Flip sign or switch axis in the Inspector if the blade still reads
        // sideways/backward on this rig.
        [SerializeField] private Vector3 _handBladeAxis = new Vector3(0f, 1f, 0f);

        // The hand-bone local axis the weapon's GRIP/edge plane should align to (keeps the
        // flat of the blade oriented sanely — roughly the palm normal). Default +Z.
        [SerializeField] private Vector3 _handGripUpAxis = new Vector3(0f, 0f, 1f);

        // Final calibration nudge applied ON TOP of the rig-derived orientation, in the
        // grip root's local space (after it's been pointed down the hand axis). Lets the
        // owner perfect the read (e.g. blade forward-and-slightly-up from the fist) from
        // the Inspector against the real hand bone — no recompile. The idle/combat hold
        // offset composes on top of THIS (see ApplyHoldPose), so the hold tilt stays
        // relative to the corrected ready orientation (no double-apply).
        [SerializeField] private Vector3 _swordGripEuler = new Vector3(-25f, 90f, 0f);  // owner felt-test: "model" (sword prop) Y+90 (was (-25,0,0))

        // WO-435: per-archetype calibration nudges, the staff/mace/etc. equivalents of
        // _swordGripEuler. ALL melee now run the same rig-aware grip path (bounds-derived
        // SeatByHandle + ComputeMeleeGripRotation); these are the additive manual-correction
        // nudges applied ON TOP in the corrected local frame, treated as CANON (never auto-
        // overwritten, per WEAPON_ARMOR_ORIENT_LOGIC §4). Defaulted to ZERO so generalizing
        // the path does NOT regress the existing look — only the sword keeps its proven -25°
        // nudge above. Inspector-exposed for tuning each family against the real rig with no
        // recompile. (Dagger reuses _swordGripEuler — same bladed archetype.)
        // RC5 FIX (2026-07-04): these were ZERO, so an un-corrected staff/wand/axe/mace inherited the
        // bone's raw local axes and read SIDEWAYS across the torso (only sword/dagger had the proven
        // nudge). The SHARED rig-hand-axis correction on this rig (KnightV3 CC_Base RightHand) is
        // Y=+90 — it appears in EVERY felt-approved melee correction (sword_A via _swordGripEuler's
        // +90, sword_G offset (0,90,0), the old axe_A offset (-25,90,0)). So the SANE DEFAULT that
        // makes a NEW weapon inherit a working "points out of the fist" grip is +90 Y, plus the -25 X
        // forward-lean only for BLADED heads (sword/dagger/axe). "Corrections teach the default"
        // (WEAPON_ARMOR_ORIENT_LOGIC §4-step-5): axe now carries (-25,90,0) here and its redundant
        // offsets.json entry is REMOVED — net rotation for axe_A is IDENTICAL (was default(0)∘offset
        // (-25,90,0); now default(-25,90,0)∘no-offset), so no regression, but a new axe works.
        // NEEDS-CAPTURE: staff/wand/mace exact forward-lean (0 vs -25) confirmed by the build capture;
        // 0 tilt is the safe neutral for a symmetric shaft. These remain Inspector-tunable + CANON
        // (never auto-overwritten). Any per-mesh delta still layers on top via offsets.json.
        // WO-970 residual: the former +90Y compensated for the old yaw-only bounds solve.
        // The geometry authority now seats the staff long axis on +Y, so the safe default WAS neutral.
        //
        // ── ⭐ OWNER RULING 2026-08-26 — THE DRAWN STAFF STANDS VERTICAL ────────────────────────
        // Owner verbatim: *"staff drawn is showing horizontal"* / *"should be up and down vertical"*,
        // on top of her standing rule *"the pointed object is Y top, flat is bottom"*. So: in hand,
        // in combat, the shaft's long axis runs along the BODY'S UP AXIS, pointed end UP.
        //
        // WHY NEUTRAL WAS WRONG, and why the correction is exactly +90 about X — the arithmetic,
        // not a dialled guess (six prior fixes tuned the MEASUREMENT; the measurement was fine):
        //
        //   The drawn seat is  ApplyGlobalWeaponYaw(ComposeMeleeGripRotation(blade, up, N))
        //                   =  rigAligned * Euler(N) * RotY(180).
        //   With the shipped rig axes _handBladeAxis (0,1,0) / _handGripUpAxis (0,0,1),
        //   rigAligned = Quaternion.LookRotation((0,0,1),(0,1,0)) == IDENTITY, and RotY(180)
        //   leaves a +Y vector untouched — so the prop's long axis (prop-local +Y, put there by
        //   NormalizeInto) reaches the hand-bone frame as  Euler(N) * (0,1,0).
        //   With N = 0 that is (0,1,0) = the bone's BLADE axis, which on the hand bone points
        //   HORIZONTALLY out of the fist (world (0,0,-1) in the regression's synthetic bone) —
        //   the SWORD rule, applied to a staff. That is the defect, stated as a vector.
        //
        //   Required: the shaft must land on the axis that maps to the body's vertical, which in
        //   the corrected frame is the GRIP-UP axis, local +Z. Solve  Euler(N) * (0,1,0) = (0,0,1):
        //   a rotation about X by t sends (0,1,0) -> (0, cos t, sin t), so cos t = 0, sin t = +1,
        //   t = +90. => N = (90, 0, 0). The +SIGN is the ruling's "pointed end up": prop +Y (the
        //   tip, per "pointed object is Y top") maps to +Z, which the bone carries to world +Y.
        //   (-90 would stand it tip-DOWN and read identical to the undirected tilt oracle.)
        //
        // Note this is stated in the CORRECTED frame — "put the shaft on the grip-up axis, not the
        // blade axis" — so it stays right if the rig axes are re-picked in the Inspector; it is an
        // ARCHETYPE correction and covers every staff, present and future, not a per-mesh row.
        // ⛔ This is the DRAWN pose only. It does not touch the sheathed path, and it is NOT a
        // reason to flip _sheatheLongAxisSign (WO-1136).
        /// <summary>Owner-ruled drawn-staff archetype correction (2026-08-26) — see the derivation
        /// above. Single source of truth: the serialized default below initialises from it and the
        /// headless oracle drives the shipped composition with it, so the two can never disagree.</summary>
        public static readonly Vector3 StaffDrawnGripNudgeDefault = new Vector3(90f, 0f, 0f);
        [SerializeField] private Vector3 _staffGripEuler = StaffDrawnGripNudgeDefault;
        [SerializeField] private Vector3 _wandGripEuler  = new Vector3(0f, 90f, 0f);
        [SerializeField] private Vector3 _axeGripEuler   = new Vector3(-25f, 90f, 0f);
        [SerializeField] private Vector3 _maceGripEuler  = new Vector3(0f, 90f, 0f);

        // Owner 2026-07-05: universal held-weapon Y flip (all families, hero + enemy).
        private const float WeaponGlobalYawDeg = 180f;

        /// <summary>Composes the global held-weapon Y correction onto a grip rotation.</summary>
        internal static Quaternion ApplyGlobalWeaponYaw(Quaternion rot)
            => rot * Quaternion.Euler(0f, WeaponGlobalYawDeg, 0f);

        // Cached base rotation for the current grip root (rig-derived + _swordGripEuler),
        // expressed in the hand bone's local space. ApplyHoldPose offsets from this.
        private Quaternion _baseGripRot = Quaternion.identity;

        private void Awake()
        {
            CacheRig();
            _loadout = GetComponent<GearLoadout>();
            // NOTE: the "does HeroBowAttachment own the bow?" question is NOT answered here any
            // more — see DeferBowToBowAttachment. At Awake the answer is always "no" because the
            // attachment component is added later in the same body build.
        }

        private void OnEnable()
        {
            // ORDER-INDEPENDENT SUBSCRIBE (BUG 1 fix): on the COMPANION body, BuildPlaceholder
            // adds the EquipmentController BEFORE the GearLoadout (the hero swapper does the
            // reverse). If we only ever subscribed here, a controller added first would resolve
            // _loadout == null, NEVER subscribe, and silently miss the BindOwnerClass->Refresh->
            // OnGearChanged that carries the companion's bow — so its weapon never attached.
            // EnsureLoadoutSubscribed re-resolves + (re)subscribes idempotently, and Update()
            // below keeps retrying until the loadout (and the Humanoid rig) come online.
            EnsureLoadoutSubscribed();
            // Always pull the latest local user settings before seating props (persistentDataPath
            // attachment-offsets.json wins over shipped defaults per id).
            AttachmentOffsetRegistry.Reload();
            // WO-994 probe 1 (candidate A): registry state on the FIRST-equip path, to diff
            // against the SCENELOAD probe in CoReapplyGearAfterSceneLoad. If SCENELOAD shows
            // shipped values where START showed user values, the Reload() asymmetry fired.
            FlowTrace.Step("Offset",
                $"WO-994 registryProbe path=START rows={AttachmentOffsetRegistry.Count} " +
                $"shield_A[{DescribeOffsetRow("shield_A")}] " +
                $"shield_A@sheathed[{DescribeOffsetRow("shield_A@sheathed")}] " +
                // WO-1215: shield_A is the TRIPO shield. The shield the player actually starts
                // with is knight_shield_starter -> key 'ShieldWithItemLogic', and its row was
                // re-dialled on 2026-08-30 (74d9e6546). The probe never printed it, so no capture
                // has ever proved WHICH values of that row shipped in the build under test.
                $"ShieldWithItemLogic[{DescribeOffsetRow("ShieldWithItemLogic")}] " +
                $"ShieldWithItemLogic@sheathed[{DescribeOffsetRow("ShieldWithItemLogic@sheathed")}]");
            EquipBestForHero();
            // WO-994: dungeon→town port breaks shield seat — height cache + hold pose must re-run
            // after the hub body/height settles (owner: seat is perfect until that transition only).
            SceneManager.sceneLoaded -= OnSceneLoadedReapplyGear;
            SceneManager.sceneLoaded += OnSceneLoadedReapplyGear;
        }

        private void OnSceneLoadedReapplyGear(Scene scene, LoadSceneMode mode)
        {
            if (!isActiveAndEnabled) return;
            // Hub/castle after dungeon is the failure mode; still safe to reapply on any load.
            StartCoroutine(CoReapplyGearAfterSceneLoad(scene.name));
        }

        private IEnumerator CoReapplyGearAfterSceneLoad(string sceneName)
        {
            // Wait for HeroBody swap / height retarget (dungeon keeper vs town body).
            yield return null;
            yield return null;
            InvalidateHeroHeightCache();
            CacheRig();
            // WO-994 tripwire checkpoint: BEFORE the re-equip touches anything, assert the
            // surviving shield still sits exactly where the last logged write put it. Drift
            // here = the PORT itself (or an unlogged writer during the load) moved the seat.
            VerifyOffHandSeat("scene-load-pre-reapply");
            // WO-994 probes 1+2 (candidates A+B): registry state + bake-marker/body identity
            // at the exact frame the re-seat runs. The frame number orders the race against
            // HeroBodySwapper's marker-add line (which logs its own frame).
            FlowTrace.Step("Offset",
                $"WO-994 registryProbe path=SCENELOAD rows={AttachmentOffsetRegistry.Count} " +
                $"shield_A[{DescribeOffsetRow("shield_A")}] " +
                $"shield_A@sheathed[{DescribeOffsetRow("shield_A@sheathed")}] " +
                // WO-1215: see the START probe — the live starter shield's key is
                // 'ShieldWithItemLogic', not 'shield_A'.
                $"ShieldWithItemLogic[{DescribeOffsetRow("ShieldWithItemLogic")}] " +
                $"ShieldWithItemLogic@sheathed[{DescribeOffsetRow("ShieldWithItemLogic@sheathed")}]");
            Transform bodyChild = transform.Find("HeroBody");
            FlowTrace.Step("Equip",
                $"WO-994 reapplyCtx scene='{sceneName}' frame={Time.frameCount} baked={PackageBakedGear} " +
                $"body='{(bodyChild != null ? bodyChild.name : "<none>")}' " +
                $"animator={(_animator != null ? (_animator.isHuman ? "human" : "generic") : "<null>")}");
            // Full re-equip so NormalizeInto + fullOverride seat against the NEW height/scale.
            // WO-994 (trace-proven 2026-08-16): the idempotent early-outs no-op EquipBestForHero
            // for props that SURVIVED the load (skip lines captured at frames 3354/5659), so a
            // surviving prop kept its old seat against the new body. Clear the shown-ids so this
            // re-equip is a REAL re-attach (fresh NormalizeInto + registry seat at the new
            // height) - the same path the trace proved healthy on every fresh boot.
            _currentWeaponId = null;
            _currentOffHandId = null;
            _offHandSheatheShownOn = null;
            EquipBestForHero();
            ApplyHoldPose();
            FlowTrace.Step("Equip",
                $"WO-994 post-scene gear reapply scene='{sceneName}' height={_cachedHeroHeightM:0.###}m " +
                $"offHand={(_currentOffHandProp != null)} weapon={(_gripRoot != null)}");
            // WO-994 probe 4: measured shield pose on the SCENELOAD path after ApplyHoldPose —
            // the ground truth to diff against the attach-time "AttachOffHandProp MEASURED"
            // line (which does NOT fire when the idempotent early-out skipped the re-attach).
            if (_currentOffHandProp != null)
            {
                var offT = _currentOffHandProp.transform;
                var offRend = _currentOffHandProp.GetComponentInChildren<Renderer>();
                Bounds owb = offRend != null ? offRend.bounds : new Bounds(offT.position, Vector3.zero);
                Transform offChild = offT.childCount > 0 ? offT.GetChild(0) : null;
                bool drawnNow = _combatActive && !(_seatingEditActive && _seatEditSheathed);
                FlowTrace.Step("Equip",
                    $"WO-994 shieldPose path=SCENELOAD parent='{(offT.parent != null ? offT.parent.name : "<null>")}' " +
                    $"state={(drawnNow ? "DRAWN" : "SHEATHED")} comp={_offHandParentCompensate} " +
                    $"gripLocalEuler={offT.localEulerAngles} " +
                    $"propLocalEuler={(offChild != null ? offChild.localEulerAngles.ToString() : "n/a")} " +
                    $"worldEuler={offT.eulerAngles} worldBounds=c{owb.center} s{owb.size} " +
                    $"boneLossy={(offT.parent != null ? offT.parent.lossyScale.ToString() : "n/a")} " +
                    $"height={_cachedHeroHeightM:0.###}");
            }
        }

        /// <summary>WO-994: clear proportional height so the next seat uses live body bounds.</summary>
        public void InvalidateHeroHeightCache()
        {
            _cachedHeroHeightM = 0f;
        }

        // WO-994 registry probe: render one offset row for the trace (or MISSING).
        private static string DescribeOffsetRow(string id)
        {
            return AttachmentOffsetRegistry.TryGetOffset(id, out var o)
                ? $"pos={o.pos} rot={o.eulerRot} scale={o.scale:0.###} full={o.fullOverride}"
                : "MISSING";
        }

        // Idempotent: resolve the GearLoadout (it may be added AFTER this controller on the
        // companion) and subscribe exactly once. Returns true when subscribed to a live loadout.
        private bool _subscribed;
        private bool EnsureLoadoutSubscribed()
        {
            if (_loadout == null) _loadout = GetComponent<GearLoadout>();
            if (_loadout != null && !_subscribed)
            {
                _loadout.OnGearChanged += HandleGearChanged;
                _subscribed = true;
                FlowTrace.Step("Equip", $"subscribed to GearLoadout.OnGearChanged on '{name}'");
            }
            return _subscribed;
        }

        // RIG-READINESS RETRY (BUG 1 fix): the companion's Animator finishes Humanoid Rebind a
        // few frames AFTER BuildPlaceholder fires BindOwnerClass, so the first EquipBestForHero
        // sees !isHuman / null hand bones and skips (exactly why the archer's bow never showed).
        // Mirror HeroBowAttachment's short retry: poll until the loadout is subscribed AND the
        // weapon prop is up, then stop. Cheap, self-terminating; off once equipped.
        private int _attachRetries;
        private void LateAttachRetry()
        {
            // Paladin bake skips sword attach; a staff on that marker is the Thrain bounce
            // (inventory has it, world does not) and MUST still retry until the rig is Humanoid.
            if (PackageBakedGear)
            {
                string pendingId = _loadout != null && _loadout.EquippedWeapon != null
                    ? _loadout.EquippedWeapon.id : _currentWeaponId;
                bool pendingStaff = !string.IsNullOrEmpty(pendingId) &&
                    (pendingId.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0
                     || pendingId.IndexOf("wand", System.StringComparison.OrdinalIgnoreCase) >= 0
                     || pendingId.StartsWith("mage", System.StringComparison.OrdinalIgnoreCase));
                if (!pendingStaff) return;
            }
            if (_attachRetries > 180) return;            // ~3s @60fps then give up quietly
            bool nowSubscribed = EnsureLoadoutSubscribed();
            CacheRig();
            bool rigReady = _animator != null && _animator.isHuman;
            bool needWeapon = _loadout != null && _loadout.EquippedWeapon != null && _currentWeaponProp == null;
            bool needOffHand = _loadout != null && _loadout.EquippedOffHand != null && _currentOffHandProp == null;
            if (nowSubscribed && rigReady && (needWeapon || needOffHand))
            {
                FlowTrace.Step("Equip", $"LateAttachRetry firing on '{name}' " +
                    $"(rigReady={rigReady} needWeapon={needWeapon} needOffHand={needOffHand} retry={_attachRetries})");
                EquipBestForHero();
            }
            _attachRetries++;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoadedReapplyGear;
            if (_loadout != null && _subscribed) _loadout.OnGearChanged -= HandleGearChanged;
            _subscribed = false;
            // Release any open Addressables weapon handle so a Blink prefab never leaks
            // when the hero is disabled/destroyed (the load may even still be in flight).
            ReleaseWeaponHandle();
            _offHandGeneration++;          // reject any in-flight off-hand completion
            DestroyCurrentOffHand();
            ReleaseOffHandHandle();
        }

        private void HandleGearChanged() => EquipBestForHero();

        /// <summary>
        /// Re-seat the equipped main-hand + off-hand onto a NEW body's bones. Called by HeroArmorVisual
        /// when an armored Blink body swaps in: the props were seated on the now-HIDDEN base hand, and a
        /// Blink full-body set has slightly different rig proportions, so the VISIBLE armor hand sits
        /// elsewhere — the "shield hangs off the arm" symptom. Re-point the animator at the new body and
        /// re-equip so the props follow the visible hands. No magic offsets — the equip path resolves the
        /// hand by humanoid bone id on the new rig. No-op until the new rig is a ready Humanoid.
        /// </summary>
        public void ReseatForBody(GameObject body)
        {
            if (body == null) return;
            var anim = body.GetComponentInChildren<Animator>();
            if (anim == null || !anim.isHuman) return;   // need a humanoid rig to seat on bones
            _animator = anim;
            _cachedHeroHeightM = 0f;   // new rig proportions → re-measure for heldLength scale
            // New body → both sheathe sockets are stale; re-create under the new rig's bones. The
            // arm flag goes with them: the next rig may map a forearm where this one did not, and a
            // carried-over `true` would pair ARM numbers with a HIP anchor.
            _sheatheSocketMain = null;
            _sheatheSocketOff  = null;
            _sheatheSocketOffIsArm = false;
            FlowTrace.Step("Equip", $"ReseatForBody: re-seating equipped props onto '{body.name}' bones (animator='{anim.name}').");
            EquipBestForHero();
        }

        /// <summary>
        /// Re-reads the hero's currently equipped weapon from GearLoadout and shows the
        /// matching mesh. This is the hook into the EXISTING equip-change event — no new
        /// gear model. Safe to call repeatedly (idempotent on an unchanged id).
        /// </summary>
        public void EquipBestForHero()
        {
            AttachmentOffsetRegistry.Reload();
            if (_loadout == null) _loadout = GetComponent<GearLoadout>();
            // Pass the WeaponDef (not just the id) so the attach path can read prefabPath /
            // loadVia and resolve a Blink Addressable weapon — the data-driven equip.
            Equip(_loadout != null ? _loadout.EquippedWeapon : null);
            // OFF-HAND: attach (or detach) the shield/off-hand to the OFF hand on the SAME event.
            EquipOffHand(_loadout != null ? _loadout.EquippedOffHand : null);
        }

        /// <summary>
        /// Show the weapon mesh for <paramref name="weaponId"/> (an id from weapons.json),
        /// attaching it to the Humanoid hand bone with the mapped grip offset. Passing null
        /// or an empty id unequips. Destroys the previous prop first (no stacking).
        /// Resolves the WeaponDef from the catalog so the data-driven (Addressable) path is
        /// used when the def carries an Addressable prefabPath.
        /// </summary>
        public void Equip(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) { Equip((WeaponDef)null, null); return; }
            Equip(GearCatalog.FindWeapon(weaponId), weaponId);
        }

        /// <summary>
        /// Data-driven equip: attaches the weapon described by <paramref name="def"/>. When the
        /// def's prefab loads via Addressables (Blink gear) the prefab is loaded async and
        /// attached a frame later; otherwise the existing hardcoded Resources map is used. A
        /// null def unequips. Safe to call repeatedly (idempotent on an unchanged id).
        /// </summary>
        public void Equip(WeaponDef def)
        {
            Equip(def, def != null ? def.id : null);
        }

        // Core equip. <paramref name="def"/> may be null (e.g. an id with no catalog row, or a
        // bare-id call) — then we fall back to the keyword/Resources Resolve path on the id.
        private void Equip(WeaponDef def, string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId) && def != null) weaponId = def.id;

            string ownerName = name;
            using var _ = FlowTrace.Enter("Equip", $"attach '{weaponId ?? "<null>"}' to '{ownerName}'");

            // PACKAGE de-dupe: the Paladin body bakes its own sword — do NOT attach a second KayKit mesh
            // (owner F8 "holding two swords"). Loadout still tracks the equipped weapon; only the visible
            // prop is suppressed. Legacy Tripo Knight (no marker) is unaffected.
            if (PackageBakedGear)
            {
                // Paladin-package bake is a SWORD. Skipping a staff here is how Thrain (Mage)
                // can list the item in inventory while the world hand is empty — the baked
                // Paladin sword is not a staff, and a Mage body should never carry this marker.
                // WO-1226 bounce: do NOT skip staff/wand/mage ids; attach them. Sword/shield
                // still skip so the Paladin does not grow a second blade.
                bool looksLikeStaff = !string.IsNullOrEmpty(weaponId) &&
                    (weaponId.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0
                     || weaponId.IndexOf("wand", System.StringComparison.OrdinalIgnoreCase) >= 0
                     || weaponId.StartsWith("mage", System.StringComparison.OrdinalIgnoreCase));
                if (looksLikeStaff)
                {
                    FlowTrace.Warn("Equip",
                        $"PACKAGE baked-gear hero '{ownerName}' would have SKIPPED staff '{weaponId}' " +
                        "(baked Paladin sword is not a staff). ATTACHING anyway so Thrain is not " +
                        "left with inventory-has-staff / world-empty-hand.");
                }
                else
                {
                    FlowTrace.Step("Equip",
                        $"PACKAGE baked-gear hero '{ownerName}' — SKIP weapon-mesh attach for '{weaponId ?? "<null>"}' " +
                        "(baked Paladin sword wins; de-dupes the second sword).");
                    return;
                }
            }

            // Idempotent: same weapon already shown -> nothing to do.
            if (string.Equals(_currentWeaponId, weaponId, System.StringComparison.OrdinalIgnoreCase)
                && _currentWeaponProp != null)
            {
                FlowTrace.Step("Equip", $"idempotent — '{weaponId}' already shown; no-op");
                return;
            }

            // New equip request — invalidate any in-flight async load + drop the old prop/handle.
            _equipGeneration++;
            DestroyCurrentWeapon();
            ReleaseWeaponHandle();
            _currentWeaponId = weaponId;

            if (string.IsNullOrEmpty(weaponId)) { FlowTrace.Step("Equip", "empty id -> unequip"); return; } // unequip

            WeaponVisual vis = Resolve(weaponId);
            if (vis == null) { FlowTrace.Fail("Equip", $"Resolve('{weaponId}') returned null — nothing to attach"); return; }
            FlowTrace.Step("Equip", $"resolved vis: mesh='{vis.mesh}' kind={vis.kind} leftHand={vis.leftHand} native={vis.native}");

            // The ranger's held bow is HeroBowAttachment's job — skip here to avoid two bows.
            // NOTE: this only applies to the HERO (which carries HeroBowAttachment). A COMPANION
            // archer has NO HeroBowAttachment, so DeferBowToBowAttachment is false and its bow is
            // attached HERE — that is the path BUG 1 needed working (its bow now seats like the hero's).
            if (DeferBowToBowAttachment && vis.mesh != null && vis.mesh.StartsWith("bow"))
            {
                FlowTrace.Step("Equip", "bow deferred to HeroBowAttachment (hero owns the held bow) -> skip");
                return;
            }

            CacheRig();
            if (_animator == null || !_animator.isHuman)
            {
                // Generic/invalid avatar OR the Humanoid rig hasn't finished Rebind yet (companion
                // attach-before-rebind). Skip now; Update()'s LateAttachRetry re-fires once the rig
                // reports Humanoid (mirrors HeroBowAttachment's retry). NOT a hard fail.
                FlowTrace.Warn("Equip", $"rig not Humanoid yet on '{ownerName}' " +
                    $"(animator={(_animator != null ? "present,!isHuman" : "null")}) — deferring to LateAttachRetry");
                return;
            }

            HumanBodyBones boneId = vis.leftHand ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            WeaponArchetype arch = GearSeat.Classify(vis.kind.ToString(),
                def != null ? def.category : null, vis.mesh ?? weaponId);

            // ONE door: GearSeat names the mount per family (staff RightHand, bow LeftHand,
            // shield Socket_Shield, sword RightHand). A rig-profiles.json override still
            // wins for non-shields; a strapped heater never follows LeftHand.
            GameObject heroRoot = _animator.gameObject;
            string rigId = heroRoot.name;
            Transform hand = null;
            string how = null;
            Transform overrideAnchor;
            if (arch != WeaponArchetype.Shield &&
                RigAttachmentRegistry.TryResolve(heroRoot, rigId, vis.leftHand, out overrideAnchor, out how))
            {
                hand = overrideAnchor;
                FlowTrace.Step("Offset", $"attach rig={rigId} hand={(vis.leftHand ? "L" : "R")} -> '{hand.name}' (via json-override)");
            }
            else
            {
                if (how != null && how.StartsWith("missing"))
                    FlowTrace.Fail("Offset", $"attach rig={rigId} hand={(vis.leftHand ? "L" : "R")} override path absent in model ({how}); falling back to GearSeat");

                var plan = GearSeat.ResolveMount(_animator, transform, arch);
                hand = plan.Mount;
            }

            if (hand == null)
            {
                FlowTrace.Fail("Equip", $"Humanoid rig on '{ownerName}' has NO mount for {arch} ({boneId}) — " +
                    $"weapon '{weaponId}' NOT attached (this is the null-bone BUG 1 cause if it fires).");
                return;
            }
            FlowTrace.Step("Equip", $"hand bone resolved: {arch} {boneId} -> '{hand.name}'");

            // ── DATA-DRIVEN PREFAB RESOLUTION (WO-Item, Blink Addressables) ───────────
            // If the equipped def loads via Addressables (Blink gear: prefabPath is an
            // address like "gear/weapon/Sword1h_01"), load it async and attach on completion.
            // Otherwise the EXISTING hardcoded Tripo/Resources map runs unchanged.
            if (LoadsViaAddressable(def))
            {
                FlowTrace.Step("Equip", $"branch: ADDRESSABLE ('{def.prefabPath}')");
                BeginAddressableEquip(def, vis, hand, weaponId, _equipGeneration);
                return;
            }

            FlowTrace.Step("Equip", $"branch: RESOURCES map (mesh='{vis.mesh}')");
            GameObject prop = LoadWeaponMesh(vis.mesh, weaponId) ?? BuildFallbackPrimitive(vis);
            if (prop == null) { FlowTrace.Fail("Equip", $"prop load+fallback both null for mesh '{vis.mesh}'"); return; }

            AttachLoadedProp(prop, vis, hand, weaponId);
        }

        // ── Addressable weapon load (Blink gear) ─────────────────────────────────────
        // True when the def's prefab must be loaded via Addressables: an explicit
        // loadVia=="addressable" OR a prefabPath that uses the shared "gear/" address scheme
        // (BlinkAddressableMarker / BlinkGearSource). Legacy/Tripo rows (null/empty) return false
        // → the existing Resources map runs (no behaviour change for current ids).
        private static bool LoadsViaAddressable(WeaponDef def)
        {
            if (def == null) return false;
            if (!string.IsNullOrEmpty(def.loadVia) &&
                def.loadVia.Equals("addressable", System.StringComparison.OrdinalIgnoreCase))
                return true;
            return !string.IsNullOrEmpty(def.prefabPath) &&
                   def.prefabPath.StartsWith("gear/", System.StringComparison.OrdinalIgnoreCase);
        }

        // Kick off the async Addressables load of the Blink weapon prefab. Attaches on
        // completion (the weapon appears a frame later — fine). The handle is stored +
        // released on the next swap/unequip/OnDisable. A failed/invalid handle is GUARDED:
        // FlowTrace.Warn + fall back to the hardcoded Resources map (or its primitive) so the
        // hero is NEVER left unarmed because a Blink prefab didn't resolve (WO-425 invariant).
        private void BeginAddressableEquip(
            WeaponDef def, WeaponVisual vis, Transform hand, string weaponId, int generation)
        {
            string address = def.prefabPath;
            FlowTrace.Step("Gear", $"Addressable equip begin: id='{weaponId}' address='{address}'");

            AsyncOperationHandle<GameObject> handle;
            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(address);
            }
            catch (System.Exception ex)
            {
                FlowTrace.Fail("Gear", $"Addressable load threw for '{address}': {ex.Message} — " +
                                       "falling back to Resources map (hero stays armed).");
                FallbackResourcesAttach(vis, hand, weaponId);
                return;
            }

            _weaponHandle = handle;
            _weaponHandleOpen = true;

            handle.Completed += op =>
            {
                // Stale: the player swapped weapons (or unequipped/disabled) while this load was
                // in flight — a newer equip already owns the slot. Release THIS handle and bail so
                // we never attach a ghost prop from an out-of-date request.
                if (generation != _equipGeneration)
                {
                    // BUG 2 FIX: do NOT call Addressables.Release(op) synchronously here — we are
                    // INSIDE the SDK's OnHandleCompleted dispatch over this same handle. A re-entrant
                    // Release invalidates the handle the SDK is still reading (handle.Status), which
                    // throws "Attempting to use an invalid operation handle". Defer to the next frame.
                    if (_weaponHandle.Equals(op)) { _weaponHandle = default; _weaponHandleOpen = false; }
                    DeferRelease(op);
                    return;
                }

                if (!op.IsValid() || op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    FlowTrace.Fail("Gear", $"Addressable load FAILED for '{address}' " +
                        $"(status={op.Status}) — falling back to Resources map (hero stays armed).");
                    ReleaseWeaponHandle();
                    FallbackResourcesAttach(vis, hand, weaponId);
                    return;
                }

                // Blink prefab is authored grip-at-origin + oriented → seat as NATIVE. Copy the
                // preset so we never flip `native` on the shared cached IdMap instance.
                var nativeVis = CopyOf(vis);
                nativeVis.native = true;
                // GUARDED Instantiate (TGVRU): a bad/destroyed-mid-frame prefab must not throw or
                // silently attach nothing — Guard.Try, null-check, Fail-loud, then fall back to
                // the Resources map so the hero is never left unarmed by an Addressable hiccup.
                GameObject prop = null;
                Guard.Try("Gear", $"instantiate addressable weapon '{address}'",
                    () => prop = Instantiate(op.Result));
                if (prop == null)
                {
                    FlowTrace.Fail("Gear", $"Addressable Instantiate returned null for '{address}' " +
                        $"(id='{weaponId}') — falling back to Resources map (hero stays armed).");
                    ReleaseWeaponHandle();
                    FallbackResourcesAttach(vis, hand, weaponId);
                    return;
                }
                AttachLoadedProp(prop, nativeVis, hand, weaponId);
                FlowTrace.Step("Gear", $"Addressable equip attached: id='{weaponId}' address='{address}'");
            };
        }

        // Armed-hero fallback: a Blink Addressable resolve failed — attach via the hardcoded
        // Resources map (or the tinted primitive) so the hero is never unarmed. Re-checks the
        // generation guard caller-side; here we just attach with the already-resolved vis.
        private void FallbackResourcesAttach(WeaponVisual vis, Transform hand, string weaponId)
        {
            // The Resources map vis is NON-native (legacy normalize/hilt path). Copy + clear the
            // native flag so we never mutate the shared cached IdMap preset.
            var fb = CopyOf(vis);
            fb.native = false;
            GameObject prop = LoadWeaponMesh(fb.mesh, weaponId) ?? BuildFallbackPrimitive(fb);
            if (prop == null) return;
            AttachLoadedProp(prop, fb, hand, weaponId);
        }

        // Shared attach + grip/orient (§4) for an ALREADY-LOADED prop — used by both the
        // synchronous Resources path and the async Addressables completion. Strips physics,
        // seats via the grip root (NATIVE = trust pivot; else bounds-normalize + hilt-infer),
        // parents to the hand, and computes the rig-aware base grip rotation + hold pose.
        private void AttachLoadedProp(GameObject prop, WeaponVisual vis, Transform hand, string weaponId)
        {
            using var _ = FlowTrace.Enter("Equip", $"AttachLoadedProp '{weaponId}' -> '{hand.name}' (native={vis.native} kind={vis.kind})");
            prop.name = PropName;
            // Cosmetic only — strip physics/colliders a prefab might carry.
            foreach (var c in prop.GetComponentsInChildren<Collider>(true)) if (c != null) Destroy(c);
            foreach (var rb in prop.GetComponentsInChildren<Rigidbody>(true)) if (rb != null) Destroy(rb);

            // ── WEAPON MATERIAL RECOVERY (deal-breaker fix 2026-07-16 "knight starts with no weapon") ──
            // The knight_starter prop (sword_A / Sword1h_01) carries material 'LowPolyWeaponMegaPack' on the
            // BUILT-IN STANDARD shader (that pack is gitignored; Standard is a Built-in-RP shader). Under URP a
            // Standard-shader material renders MAGENTA in-editor and is STRIPPED from the built player (its
            // shader resolves to Hidden/InternalErrorShader -> invisible/pink), so the weapon attaches + passes
            // VerifyWeaponRendersNow (enabled renderer + mesh + parented) yet is NOT VISIBLE in the hand — the
            // owner's "no weapon". MagentaGuard recovers exactly this class, but it only sweeps on scene-LOAD and
            // this prop is instantiated at RUNTIME (equip) AFTER that sweep, so the guard never reaches it. Recover
            // the prop's broken materials HERE, at attach time (mirrors MagentaGuard's fresh-URP/Lit-assigned-to-
            // renderer pattern — a fresh instance assigned into sharedMaterials STICKS in the build; an in-place
            // shader mutation of the shared asset does not). Idempotent: an already-URP prop is left untouched.
            RecoverWeaponMaterialsToUrp(prop, weaponId);
            // WO-1226 bounce (owner 2026-08-27, Thrain): inventory lists the staff, world shows
            // nothing. Activate + layer-match BEFORE NormalizeInto. KayKit staff_A is a ~2 cm Bits
            // mesh; if its renderer is inactive at instantiate, Renderer.bounds is empty,
            // NormalizeInto no-ops the scale, and a 2 cm shaft is what "nothing is displayed"
            // looks like. MagentaGuard's deferred sweep can also hide a fallback cube 1s later.
            EnsureWeaponRenderersVisible(prop, hand, weaponId);

            // Seat via a grip root.
            //  • WO-478 (default): NATIVE props (Blink grip-at-origin, e.g. knight_starter/sword_A)
            //    trust SeatNative — scale to heldLength, preserve authored pivot/orientation.
            //    Geometry inference is reserved for non-native Tripo/KayKit FBX (sword_D/F/G, staff_*).
            //  • DEPRECATED (ff.weapongripinfer): when ON, native melee ALSO runs the legacy
            //    NormalizeInto + SeatHiltLowerHalf + ComputeMeleeGripRotation path (superseded
            //    WO-435 "BUG 2 FIX" — see WORK_ORDER_478_weapon_grip_trust_native_pivot.md).
            // ── OFFSET RESOLUTION (WO-551: geometry-first, offset = nudge-on-top) ─────
            // GEOMETRY IS THE DEFAULT for all conforming melee. We ALWAYS true the prop
            // (NormalizeInto: longest axis -> +Y, narrowest -> +X) and, for melee, seat the grip
            // by geometry (SeatByHandle: hilt-forward). The Offset Forge offset is then a small
            // CALIBRATION NUDGE applied ON TOP of that trued+seated frame — NOT a replacement
            // that skips geometry. (The b773176d `seatNativeAuthored` bypass made the manual
            // offset the AUTHORITY — backwards vs the owner principle "trust the geometry" — and
            // dialed against the RAW pivot, so it never reproduced in-game = "handle still wrong".)
            // An all-zero entry == pure geometry. A weapon that genuinely breaks the
            // wide-Y/narrow-XZ pattern can opt OUT of geometry per-entry with "fullOverride":true
            // (the EXCEPTION, native-only). Key = mesh name (Forge save-id, e.g. "sword_A") then id.
            string offsetKey = !string.IsNullOrEmpty(vis.mesh) ? vis.mesh : weaponId;
            bool hasOffset = AttachmentOffsetRegistry.TryGetOffset(offsetKey, out var fo) ||
                             (offsetKey != weaponId && AttachmentOffsetRegistry.TryGetOffset(weaponId, out fo));
            bool meleeSeat = IsMelee(vis.kind);
            // EXCEPTION (opt-in, native-only): a non-conforming prop bypasses geometry and
            // reproduces the Forge raw-pivot frame exactly (legacy replacement). Default false,
            // so a normal authored entry NEVER skips geometry — it only adds a nudge on top.
            // OWNER CONVENTION (WO-577, 2026-06-28): fullOverride now means "author from the
            // 100%-VERTICAL baseline (geometry) + a saved DELTA", written by the in-game Seating
            // Editor — NOT the old raw-pivot/SeatNative bypass (which the WO-551 notes flagged as
            // the backwards approach that never reproduced in-game). Dropped the `&& vis.native`
            // gate so a vertical-delta can be authored for ANY weapon (Tripo/KayKit included).
            // SAFE: only entries with fo.fullOverride==true take this path, and none existed
            // before the editor — the DEFAULT geometry+nudge path (WO-551) is byte-for-byte intact.
            bool fullOverride = hasOffset && fo.fullOverride;

            var gripRoot = new GameObject(PropName);
            // WO-478: native melee trusts authored pivot unless ff.weapongripinfer restores inference.
            bool trustNativePivot = vis.native && !fullOverride &&
                (!meleeSeat || !FeatureFlags.WeaponGripInfer);
            // WO-1431: reset the main-hand seat-dispatch state so a PREVIOUS weapon's archetype can
            // never leak into this attach or into the Seating Editor preview. The melee branch
            // below overwrites both; a native/non-melee prop deliberately leaves them cleared,
            // which reads as "no archetype derivation was involved in this seat".
            _currentWeaponArchetype = WeaponArchetype.Unknown;
            _currentWeaponDerivable = false;
            float heldLen = ProportionalHeldLength(vis.heldLength);
            FlowTrace.Step("Equip",
                $"heldLength '{weaponId}' kind={vis.kind}: archetype={vis.heldLength:0.###}m " +
                $"proportional={heldLen:0.###}m hero={ResolveHeroHeightM():0.###}m");
            if (trustNativePivot)
            {
                FlowTrace.Step("Equip", meleeSeat
                    ? "seat: NATIVE melee (WO-478 trust grip-at-origin + scale)"
                    : "seat: NATIVE (trust authored grip-at-origin, scale-only)");
                SeatNative(prop, gripRoot.transform, heldLen);
            }
            else
            {
                // DEPRECATED geometry inference — ff.weapongripinfer for native melee, default for
                // non-native Tripo/KayKit FBX. Ref: WORK_ORDER_478_weapon_grip_trust_native_pivot.md
                FlowTrace.Step("Equip", meleeSeat
                    ? (vis.native
                        ? "seat: DEPRECATED GEOMETRY (ff.weapongripinfer) — NormalizeInto + SeatMeleeGripPoint"
                        : "seat: GEOMETRY — NormalizeInto (longest->+Y) + SeatMeleeGripPoint (WO-1431: staff -> 0.75 up the shaft, bladed -> hilt lower half, blade +Y)")
                    : "seat: GEOMETRY — NormalizeInto (bounds-true)");
                NormalizeInto(prop, gripRoot.transform, heldLen, ResolveHiltFromKind(vis.kind),
                              ResolveGripAnchorFromKind(vis.kind));   // WO-1105 R4: bow -> stave-surface grip
                if (meleeSeat)
                {
                    // ── WO-1431: ONE archetype, feeding BOTH the applied rule and the prediction ──
                    // Resolved ONCE, here, and passed to the seat AND to TraceMeasuredSeat. Before
                    // this change the trace classified by mesh NAME while the seat had no archetype
                    // input at all, which is precisely how the prediction could say 0.75 for eleven
                    // days while the seat did 0.18 and nobody could see the two were different
                    // programs. `vis.kind.ToString()` is fed as the CATEGORY so a staff whose mesh
                    // is not literally named "staff" still classifies as one (WeaponOrientHelper
                    // .Classify deliberately leaves wand/axe/hammer/mace/crossbow Unknown — the
                    // owner's 2026-08-19 spec covers bow/sword/staff/shield only, and Unknown means
                    // "keep today's behaviour and say so", not "guess").
                    WeaponArchetype seatArch = WeaponOrientHelper.Classify(
                        vis.kind.ToString(), !string.IsNullOrEmpty(vis.mesh) ? vis.mesh : weaponId);
                    // PRECEDENCE (WeaponOrientHelper.ResolveSource, WO-1123; `manual` value per
                    // WO-1215 ManualSeatIsSubstantiated). Same computation the off-hand already
                    // does — an authored Offset Forge row, or a `manual: true` that names a
                    // correction which actually exists, keeps the pre-WO-1431 seat untouched.
                    bool rawWeaponManual = IsManualOrientRow(weaponId);
                    bool weaponRowGenerated = IsGeneratedCatalogRow(weaponId);
                    bool weaponManual = WeaponOrientHelper.ManualSeatIsSubstantiated(
                        rawWeaponManual, weaponRowGenerated, hasOffset);
                    _currentWeaponArchetype = seatArch;
                    _currentWeaponDerivable = WeaponOrientHelper.MayDerive(hasOffset, weaponManual);
                    FlowTrace.Step("Equip",
                        $"melee seat source '{weaponId}' key='{offsetKey}': " +
                        $"{WeaponOrientHelper.ResolveSource(hasOffset, weaponManual, canDerive: true)} " +
                        $"(archetype={seatArch} authoredRow={hasOffset} manual={weaponManual} " +
                        $"rawManual={rawWeaponManual} generated={weaponRowGenerated} " +
                        $"derivable={_currentWeaponDerivable})");
                    FlowTrace.Try("Equip", "SeatMeleeGripPoint", () =>
                        SeatMeleeGripPoint(prop, gripRoot.transform, seatArch,
                                           _currentWeaponDerivable, weaponId));
                    // ── WO-1123 §4 STEP 1: MEASUREMENT, KEPT AS A CROSS-CHECK ───────────────────
                    // Still read-only, and NEVER stripped (CLAUDE.md §12). What changed on
                    // 2026-09-09 is that the staff half of this prediction is no longer the only
                    // place the rule exists: SeatMeleeGripPoint above APPLIES it, and this line now
                    // serves as the independent second opinion on the same mesh. If the
                    // "MELEE GRIP APPLIED" line and this PREDICTION line ever disagree for a staff,
                    // the two paths have split again — that divergence is the bug, read both.
                    FlowTrace.Try("Equip", "OrientMeasure(melee)", () =>
                        WeaponOrientHelper.TraceMeasuredSeat(prop, gripRoot.transform, hand,
                            seatArch, weaponId));
                    FlowTrace.Step("Equip", $"trued+seated: grip-shift localY={prop.transform.localPosition.y:0.###} (archetype={seatArch} grip dispatched by SeatMeleeGripPoint{(fullOverride ? ", vertical-delta" : "")} infer={FeatureFlags.WeaponGripInfer})");
                }
            }

            gripRoot.transform.SetParent(hand, false);
            EnsureWeaponRenderersVisible(gripRoot, hand, weaponId);
            _weaponParentCompensate = true;
            _weaponAuthoredScale = 1f;   // no offset yet — reset; the offset branches below record fo.scale
            CompensateParentScale(gripRoot.transform, 1f,
                SeatSubject("main-hand", weaponId, offsetKey), ref _weaponCompState);
            gripRoot.transform.localPosition = vis.gripPos;

            // Hold state: store the base grip euler so the idle<->combat pose can offset
            // from it; apply the current pose immediately.
            _gripRoot = gripRoot.transform;
            _baseGripEuler = vis.gripEuler;
            _currentWeaponProp = gripRoot;

            // Capture the attach inputs the in-game Seating Editor (WO-577) needs to live-preview
            // + reproduce this seat. Cheap struct copies; consumed only when an editor opens.
            _currentWeaponMeshKey    = offsetKey;
            _currentWeaponKind       = vis.kind;
            _currentWeaponMelee      = meleeSeat;
            _currentWeaponHeldLength = heldLen;
            _currentWeaponGripPos    = vis.gripPos;
            _currentWeaponGripEuler  = vis.gripEuler;
            _currentWeaponNative     = vis.native;

            // WHICH END HANGS DOWN, measured off THIS mesh (owner F8 2026-08-21). Resolved here,
            // once, because the answer belongs to the prop and not to the game: see the ⛔ note on
            // _sheatheLongAxisSign for the two contradictory captures that proved a global sign
            // cannot serve both a normalized prop and a native one. Runs AFTER NormalizeInto /
            // SeatHiltLowerHalf above, so it measures the frame the sheathe pose will actually use.
            ResolveSheathedTipSign(prop, gripRoot.transform, offsetKey, vis.kind);

            // Base orientation:
            //  • ALL melee (sword/dagger/axe/hammer/staff/wand — WO-435): build the grip-root
            //    rotation FROM the hand bone's own axes so the primary axis extends forward from
            //    the fist (not across the torso). The prop's primary/grip line is local +Y
            //    (NormalizeInto + SeatByHandle put the longest axis there); we point that down the
            //    hand's blade axis and keep the flat aligned to the hand's grip-up axis, then add
            //    the per-archetype calibration nudge (sword/dagger -> _swordGripEuler, default -25;
            //    others default 0 so the path generalizes WITHOUT regressing the existing look).
            //    Previously staff/wand/axe/hammer used identity gripEuler + NO rig rotation, so they
            //    inherited the bone's local axes and read sideways across the body — this is the fix.
            //  • BOW (companions + any non-ranger body with no HeroBowAttachment): DERIVED, exactly
            //    like the hero's — see the ComputeBowHeldRotation branch below.
            //  • Shield: keeps its proven preset gripEuler. NOTE: a shield does NOT reach this
            //    method at all — AttachOffHandProp owns every off-hand prop and has its own seat
            //    and its own ApplyGlobalWeaponYaw call. Nothing below touches it.
            // WO-478: native melee keeps the prefab frame + per-archetype calibration nudge only.
            // DEPRECATED (ff.weapongripinfer): native melee uses ComputeMeleeGripRotation like Tripo FBX.
            //
            // `bowDerivedSeat` records that _baseGripRot came out of ComputeBowHeldRotation, so the
            // global yaw below can be skipped for THAT ROTATION ONLY (see the guard at the yaw line).
            bool bowDerivedSeat = false;
            if (fullOverride)
            {
                _baseGripEuler = fo.eulerRot;
                _baseGripRot = Quaternion.Euler(fo.eulerRot);
            }
            else if (trustNativePivot && meleeSeat)
            {
                _baseGripRot = Quaternion.Euler(vis.gripEuler) * Quaternion.Euler(MeleeGripNudge(vis.kind));
                _baseGripEuler = _baseGripRot.eulerAngles;
            }
            else if (IsMelee(vis.kind))
                _baseGripRot = ComputeMeleeGripRotation(vis.kind);
            // ── COMPANION / NON-RANGER BOW: DERIVED, NEVER DIALED (owner ruling 2026-08-16) ──────
            // The hero's bow never reaches here (DeferBowToBowAttachment, :750) — a COMPANION archer
            // has no HeroBowAttachment, so THIS is the branch its bow takes, and it used to fall to
            // the raw `Quaternion.Euler(_baseGripEuler)` below with gripEuler == (0,0,0). That is an
            // IDENTITY hand-local seat: it maps the bow's prop-local +Y (the limb span, put there by
            // NormalizeInto + GripAnchor.BowGrip above) straight onto the hand BONE's own +Y, which
            // on this rig is the "points out of the fist" axis. Right for a sword; ~90 degrees wrong
            // for a bow, whose hand closes AROUND the riser. That is the horizontal companion bow.
            //
            // Fixed by DERIVATION, not a constant, and the reason is TWO AXES, not one. The owner's
            // archer reference has the STRING as the straight edge NEAREST the body and the limbs
            // curving AWAY toward the target. A single Z-roll constant (the tempting (0,0,-90)) can
            // stand the bow upright while leaving the BELLY facing BACKWARD — string downrange,
            // curve at the archer — which photographs as nearly right and is wrong. LookRotation on
            // (belly=body.forward, limb=body.up) sets BOTH axes and cannot make that mistake.
            //
            // NOT applied when trustNativePivot: SeatNative preserves the prefab's authored frame
            // instead of running NormalizeInto, so prop-local +Y is NOT guaranteed to be the limb
            // span and the derivation's premise does not hold. No bow preset is Native() today
            // (see the IdMap rows) — this guard is here so adding one cannot silently mis-seat.
            else if (vis.kind == WeaponClass.Bow && !trustNativePivot)
            {
                bowDerivedSeat = true;
                Transform bowBody = _animator != null ? _animator.transform : transform;
                // Guard.Try (Section 12): a bad rig transform must log and fall back to the old raw
                // seat, never throw out of the attach and leave a half-parented prop on the body.
                Quaternion derived = Guard.Try("Equip",
                    $"ComputeBowHeldRotation for '{weaponId}' on '{name}'",
                    () => WeaponBoundsOrient.ComputeBowHeldRotation(hand, bowBody),
                    Quaternion.identity);
                // gripEuler stays available as a felt-tune NUDGE composed ON TOP of the derived
                // seat (identical to HeroBowAttachment's GripLocalEuler) — never as the source.
                _baseGripRot = derived * Quaternion.Euler(_baseGripEuler);
            }
            else
                _baseGripRot = Quaternion.Euler(_baseGripEuler);

            FlowTrace.Step("Equip", $"attached '{weaponId}' on '{name}': gripPos={vis.gripPos} " +
                $"baseEuler={_baseGripRot.eulerAngles} kind={vis.kind} native={vis.native} " +
                $"trustNative={trustNativePivot} infer={FeatureFlags.WeaponGripInfer}");

            // ── OFFSET FORGE NUDGE (WO-551: applied ON TOP of geometry) ──────────────
            // The authored offset is a CALIBRATION NUDGE relative to the trued+seated runtime
            // frame, NOT a replacement: COMPOSE the rotation onto the geometric grip, ADD the
            // position, MULTIPLY the scale. So the pipeline is true -> seat-by-handle -> nudge.
            // An all-zero entry is a no-op == pure geometry (the conforming-sword case). The
            // fullOverride EXCEPTION (resolved above) instead REPLACED the frame; here it applies
            // only its residual pos/scale on the raw-pivot seat. The key is the weapon's mesh name
            // (e.g. "sword_A", what the Forge defaults its save-id to) with a fallback to the id.
            if (fullOverride)
            {
                // OVERRIDE: reproduce the Seating-Editor preview EXACTLY (raw pivot pose). Scale
                // composes through the ONE shared seam (CompensateParentScale = comp * authored):
                // WYSIWYG break proven 2026-07-07: preview lacked compensate (hand lossy 1.666) —
                // owner-dialed 0.46 rendered 0.276 at boot. Preview + attach + hold-pose now all
                // render ParentScaleCompensation(parent) * fo.scale, so the approved size persists.
                gripRoot.transform.localPosition = vis.gripPos + fo.pos;
                _weaponAuthoredScale = fo.scale > 0f ? fo.scale : 1f;
                CompensateParentScale(gripRoot.transform, _weaponAuthoredScale,
                    SeatSubject("main-hand", weaponId, offsetKey), ref _weaponCompState);
                FlowTrace.Step("Offset", $"OVERRIDE '{offsetKey}': raw-pivot pos={fo.pos} rot={fo.eulerRot} scale={fo.scale:0.###}");
            }
            else if (hasOffset)
            {
                // NUDGE on top of geometry: +pos, *rot (in the seated local frame), *scale.
                bool nudged = fo.pos != Vector3.zero || fo.eulerRot != Vector3.zero ||
                              (fo.scale > 0f && Mathf.Abs(fo.scale - 1f) > 1e-4f);
                gripRoot.transform.localPosition = vis.gripPos + fo.pos;
                _baseGripRot = _baseGripRot * Quaternion.Euler(fo.eulerRot);
                _baseGripEuler = _baseGripRot.eulerAngles;
                if (fo.scale > 0f && Mathf.Abs(fo.scale - 1f) > 1e-4f)
                {
                    // Record the authored multiplier so ApplyHoldPose's re-parent compensate
                    // (CompensateParentScale) re-composes comp * authored instead of wiping it.
                    _weaponAuthoredScale = fo.scale;
                    gripRoot.transform.localScale = gripRoot.transform.localScale * fo.scale;
                }
                FlowTrace.Step("Offset", nudged
                    ? $"NUDGE '{offsetKey}' on geometry: +pos={fo.pos} *rot={fo.eulerRot} *scale={fo.scale:0.###}"
                    : $"offset '{offsetKey}' is all-zero — pure geometry (no nudge).");
            }
            else
            {
                FlowTrace.Step("Offset", trustNativePivot
                    ? $"no offset stored for '{offsetKey}' — native pivot kept (WO-478)."
                    : $"no offset stored for '{offsetKey}' — pure geometry grip kept.");
            }

            // ── GLOBAL YAW: APPLIED TO EVERY RAW-EULER SEAT, WITHHELD FROM THE DERIVED BOW ───────
            // WeaponGlobalYawDeg (180) exists to correct grips that INHERITED the raw bone axes —
            // every branch above except the bow one ends in an authored/nudge euler expressed in the
            // bone's frame, so they still take it and their felt-approved look is byte-identical.
            // The derived bow seat is already a WORLD-built target (belly on body.forward); yawing
            // it 180 would swing the belly to face BACKWARD — string toward the target, curve toward
            // the archer — the exact half-right failure the derivation was chosen to prevent.
            // Precedent, not invention: in ApplyHoldPose the DERIVED ComputeSheathRotation result is
            // likewise consumed without the yaw, while the raw-euler off-hand sheathe seat beside it
            // still takes it — derived/no-yaw and authored/yaw, consistently. HeroBowAttachment drops
            // it for the same reason (see its bow-orientation block, HeroBowAttachment.cs:242-284).
            // Deliberately named by FUNCTION, not by line number: line refs in this file have gone
            // stale before, and a stale pointer is how the wrong conclusion got preserved last time.
            if (!bowDerivedSeat)
            {
                _baseGripRot = ApplyGlobalWeaponYaw(_baseGripRot);
                _baseGripEuler = _baseGripRot.eulerAngles;
            }
            else
            {
                FlowTrace.Step("Equip",
                    $"bow '{weaponId}': ApplyGlobalWeaponYaw WITHHELD (derived world seat). " +
                    $"baseEuler={_baseGripRot.eulerAngles:0.#}. If the belly ever reads ~180deg off " +
                    "the aim, a yaw got composed back on - do not compensate with a nudge.");
            }

            LogGripSeatDiagnostics(prop, gripRoot.transform, hand, weaponId,
                trustNativePivot ? "WO-478-native" : "geometry-infer");

            // RENDER-VERIFY + ROLLBACK (TGVRU, owner directive 2026-06-19: "anything that renders
            // can be broken — check render==true and roll back the error"). The prop can load +
            // attach but be INVISIBLE (no enabled Renderer / no mesh) or SEATED WRONG (the grip
            // root never landed under the hand bone). PROVE it renders + is parented under the
            // resolved hand BEFORE we leave it on the hero; on fail, destroy the half-attached prop
            // and clear the slot so no stray/invisible weapon is left behind (the never-left-broken
            // contract — the failure self-reports to the break-log instead of reaching the player).
            // MUST run while the prop is still parented to the hand (verify asserts that seat) — only
            // AFTER it passes do we record the draw target + apply the carry state (which may reparent
            // the prop onto the back sheathe socket when out of combat).
            if (!VerifyWeaponRendersNow(gripRoot, hand, weaponId))
            {
                RollbackWeaponProp(gripRoot,
                    $"render-verify failed for weapon '{weaponId}' (no visible renderer or not parented to '{hand.name}')");
                return;
            }

            // Record the DRAWN target (hand + final local pos); _baseGripRot already holds the drawn
            // rotation. ApplyHoldPose now PLACES the prop by combat state: on the hand in combat, on
            // the back socket (sheathed) out of combat — so the hand grip only shows where it seats right.
            _weaponHand = hand;
            _weaponDrawnLocalPos = gripRoot.transform.localPosition;
            // Set BEFORE ApplyHoldPose: the hold pose is what puts the prop on its FINAL transform,
            // and it is the caller that emits TraceBowSeatMeasured for whichever state it lands in.
            _bowSeatDerived = bowDerivedSeat;
            _lastBowSeatValid = false;   // a fresh attach always re-reports, never dedupes away
            ApplyHoldPose();
            // Re-assert AFTER the carry-state reparent (drawn hand vs sheathed hip). SetParent
            // does not copy layer, and MagentaGuard can hide a fallback cube between attach and
            // the first deferred sweep. Drawn and sheathed are different parents — both must show.
            Transform shownOn = _gripRoot != null ? _gripRoot.parent : hand;
            int shown = EnsureWeaponRenderersVisible(gripRoot, shownOn, weaponId);
            FlowTrace.Step("Equip",
                $"attached-and-shown '{weaponId}' on '{name}': activeMeshRenderers={shown} " +
                $"parent='{(shownOn != null ? shownOn.name : "<null>")}' " +
                $"kind={vis.kind} (0 = inventory can list this staff while the world hand is empty)");

        }

        // ── §12 PROVING LINE, COMPANION TWIN (owner defect 2026-08-16) ───────────────────────────
        // The instrumentation twin of HeroBowAttachment.cs:345-352, with the axis the hero's line
        // does NOT print: the BELLY. One capture must be able to separate the three outcomes without
        // a second run or a screenshot round-trip:
        //   limbTiltFromVertical ~0  + bellyOffAim ~0   -> correct (upright, curve downrange)
        //   limbTiltFromVertical ~90                    -> the raw-euler seat came back (horizontal)
        //   limbTiltFromVertical ~0  + bellyOffAim ~180 -> a yaw got composed onto the derived seat:
        //                                                  upright but the STRING faces the target
        //                                                  and the curve faces the archer. This is
        //                                                  the failure a dialed (0,0,-90) cannot even
        //                                                  detect, which is why it is printed.
        // Measured on the FINAL transform AFTER parenting + hold pose, so nothing downstream of the
        // solve can quietly re-tip it. `derived` and the live parent are printed so a sheathed
        // (back-socket) reading is never mistaken for a bad hand seat.
        //
        // ⚠ IT PRINTS BOTH CARRY STATES, AND THAT IS THE POINT (owner ruling 2026-08-16: "both
        // sheathed and drawn bow stay in this same pose"). On 2026-08-16 a capture showed
        // limbTiltFromVertical=0 for the HELD bow and the diagonal BACK carry was then reported to
        // the owner as correct - by generalising a measurement of one state to a transform it never
        // covered. So this is driven from ApplyHoldPose as well as from attach, and it names the
        // state in every line. Both states must now read the same angles; if they ever diverge, the
        // divergence is in the log rather than in a screenshot two days later.
        // DEDUPE STATE, and it is NOT optional: ApplyHoldPose re-runs EVERY FRAME through
        // SetCombatActive's no-change path (see the WO-959 note at the end of that method), so an
        // unguarded trace here would be a per-frame firehose — the same shape as the equip-log spam
        // that has buried real evidence before. These are ints/bools compared BEFORE any string is
        // built, so a steady state costs three comparisons and allocates nothing at all.
        private int  _lastBowSeatLimbDeg  = int.MinValue;
        private int  _lastBowSeatBellyDeg = int.MinValue;
        private bool _lastBowSeatOnHand;
        private bool _lastBowSeatValid;
        /// <summary>True when the CURRENT main-hand prop's seat came out of ComputeBowHeldRotation
        /// (so the trace can tell a derived bow from a native-pivot one it deliberately skipped).</summary>
        private bool _bowSeatDerived;

        private void TraceBowSeatMeasured(Transform seated, Transform hand, string weaponId, bool derived)
        {
            if (seated == null) return;
            Transform body = _animator != null ? _animator.transform : transform;
            if (body == null) return;

            Vector3 limbWorld  = seated.rotation * Vector3.up;        // prop +Y = limb-to-limb span
            Vector3 bellyWorld = seated.rotation * Vector3.forward;   // prop +Z = riser belly / aim
            float limbTilt = Vector3.Angle(limbWorld, body.up);
            float bellyOff = Vector3.Angle(bellyWorld, body.forward);
            bool onHand = hand != null && seated.parent == hand;

            // Cheap first: same carry state + same whole-degree angles = nothing new to report.
            // Compared before any string exists, because this runs every frame (see the fields).
            int limbDeg = Mathf.RoundToInt(limbTilt);
            int bellyDeg = Mathf.RoundToInt(bellyOff);
            if (_lastBowSeatValid && onHand == _lastBowSeatOnHand &&
                limbDeg == _lastBowSeatLimbDeg && bellyDeg == _lastBowSeatBellyDeg) return;
            _lastBowSeatValid    = true;
            _lastBowSeatOnHand   = onHand;
            _lastBowSeatLimbDeg  = limbDeg;
            _lastBowSeatBellyDeg = bellyDeg;

            string parentName = seated.parent != null ? seated.parent.name : "<none>";
            string state = onHand ? "DRAWN(hand)" : "SHEATHED(back socket)";

            FlowTrace.Step("Equip",
                $"BowSeat FINAL '{weaponId}' on '{name}': state={state} derived={derived} " +
                $"parent='{parentName}' localEuler={seated.localRotation.eulerAngles:0.#} " +
                $"bodyUp={body.up:0.##} bodyFwd={body.forward:0.##} limbAxisWorld={limbWorld:0.##} " +
                $"bellyAxisWorld={bellyWorld:0.##} limbTiltFromVertical={limbTilt:0.#}deg " +
                $"bellyOffAim={bellyOff:0.#}deg (BOTH STATES must read ~0/~0 per the owner ruling; " +
                "~90 tilt = raw-euler seat returned or the diagonal baldric carry; ~180 belly = a " +
                "global yaw was composed onto the derived seat)");

            if (derived && (limbTilt > 5f || bellyOff > 5f))
                FlowTrace.Warn("Equip",
                    $"BowSeat '{weaponId}' {state}: derived seat measured OFF SPEC - " +
                    $"limbTilt={limbTilt:0.#}deg bellyOff={bellyOff:0.#}deg (both should be ~0 in " +
                    "EITHER state). Something composed on top of ComputeBowHeldRotation - an " +
                    "attachment-offsets nudge, a re-applied global yaw, or the shared diagonal " +
                    "sheathe. Fix the composer; do NOT dial a constant to cancel it.");
        }

        // RENDER-VERIFY (synchronous, no camera/scene dependency): the attached weapon prop MUST
        // have >=1 ENABLED Renderer carrying a sharedMesh AND its grip root MUST be parented under
        // the resolved hand bone. Traces the exact counts so a capture splits "no visible mesh" vs
        // "wrong/unparented seat" with zero guessing. Returns false => caller rolls back + unequips.
        //
        // ⚠ WO-1226 bounce: `r.enabled` on an INACTIVE GameObject is typically still true, so the
        // old check passed while the player saw an empty hand. Require activeInHierarchy too.
        private bool VerifyWeaponRendersNow(GameObject prop, Transform handBone, string weaponId)
        {
            if (prop == null)
            {
                FlowTrace.Fail("Equip", $"VerifyWeaponRenders: weapon '{weaponId}' prop is null.");
                return false;
            }

            int total = 0, enabledRen = 0, withMesh = 0, inactiveGo = 0;
            foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                total++;
                if (!r.gameObject.activeInHierarchy) { inactiveGo++; continue; }
                if (r.enabled) enabledRen++;
                // A SkinnedMeshRenderer exposes sharedMesh; MeshRenderer's mesh lives on a sibling
                // MeshFilter. Treat either source as "has a mesh".
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh != null) withMesh++;
            }

            bool renders = enabledRen > 0 && withMesh > 0;
            bool seated = prop.transform.parent == handBone;

            FlowTrace.Step("Equip",
                $"VerifyWeaponRenders weapon='{weaponId}' on '{name}': renderers total={total} enabled={enabledRen} " +
                $"withMesh={withMesh} inactiveGo={inactiveGo}; gripParent='{(prop.transform.parent != null ? prop.transform.parent.name : "<null>")}' " +
                $"expectedHand='{handBone.name}' => renders={renders} seated={seated}");

            if (!renders || !seated)
            {
                FlowTrace.Fail("Equip",
                    $"VerifyWeaponRenders FAILED weapon='{weaponId}' on '{name}': renders={renders} " +
                    $"(enabled={enabledRen}, withMesh={withMesh}, inactiveGo={inactiveGo}) seated={seated} " +
                    $"(gripParent='{(prop.transform.parent != null ? prop.transform.parent.name : "<null>")}', expected='{handBone.name}').");
                return false;
            }
            return true;
        }

        /// <summary>
        /// WO-1226 bounce: count mesh-owning renderers that are actually ON SCREEN — enabled AND
        /// activeInHierarchy, carrying a non-null mesh. Public so the headless regression can ask
        /// the same question the player does ("is there a staff") instead of a derived tilt.
        /// TrailRenderer / LineRenderer / ParticleSystemRenderer do not count: they are effects.
        /// </summary>
        public static int CountActiveMeshRenderers(GameObject root)
        {
            int n = 0;
            if (root == null) return 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (MeshOf(r) != null) n++;
            }
            return n;
        }

        private static Mesh MeshOf(Renderer r)
        {
            if (r is SkinnedMeshRenderer smr) return smr.sharedMesh;
            var mf = r.GetComponent<MeshFilter>();
            return mf != null ? mf.sharedMesh : null;
        }

        /// <summary>
        /// WO-1226 bounce — the SEAT's visibility pass, not a deriver tweak. Closes three ways a
        /// staff can be in the loadout while the world (or the inventory preview camera) shows an
        /// empty hand:
        ///   1. inactive GameObject (FBX importVisibility / a hidden mesh node) — Renderer.bounds
        ///      then measure empty, NormalizeInto leaves KayKit staff_A at ~2 cm.
        ///   2. renderer.enabled=false — MagentaGuard's deferred scene sweep hides a fallback cube.
        ///   3. layer != seat layer — HeroPreviewViewer's camera culls HeroPreview only;
        ///      Instantiate leaves the prop on Default, so the paper-doll is empty-handed.
        /// TrailRenderer / LineRenderer are NOT re-enabled (preview NeutralizeEffectRenderers).
        /// Returns the post-pass <see cref="CountActiveMeshRenderers"/> so a capture can split
        /// "attached but invisible" from "never attached".
        /// </summary>
        public static int EnsureWeaponRenderersVisible(GameObject root, Transform seat, string weaponId)
        {
            if (root == null) return 0;
            int activated = 0, reenabled = 0, layered = 0;
            int seatLayer = seat != null ? seat.gameObject.layer : root.layer;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                    activated++;
                }
                if (t.gameObject.layer != seatLayer)
                {
                    t.gameObject.layer = seatLayer;
                    layered++;
                }
            }
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (MeshOf(r) == null) continue;   // leave neutralized trails/lines off
                if (!r.enabled)
                {
                    r.enabled = true;
                    reenabled++;
                }
            }
            MagentaGuard.ProtectPrimitiveArt(root, "EquipmentController.EnsureWeaponRenderersVisible");
            int shown = CountActiveMeshRenderers(root);
            FlowTrace.Step("Equip",
                $"SHOW weapon='{(string.IsNullOrEmpty(weaponId) ? "<null>" : weaponId)}' on '{root.name}': " +
                $"activatedGos={activated} reenabledMeshRenderers={reenabled} layerCopied={layered} " +
                $"seatLayer={seatLayer} activeMeshRenderers={shown} " +
                "(0 = inventory can list this item while the world/preview shows an empty hand)");
            if (shown == 0)
                FlowTrace.Fail("Equip",
                    $"SHOW FAILED weapon='{(string.IsNullOrEmpty(weaponId) ? "<null>" : weaponId)}': " +
                    $"zero active mesh renderers under '{root.name}'. The loadout/inventory can still " +
                    "name this staff; the player sees nothing.");
            return shown;
        }

        // ROLL BACK a half-attached weapon: destroy the grip root (and its loaded prop child) and
        // clear the current-weapon slot so no stray/invisible weapon is left on the hand. Never
        // behind the FlowTrace toggle — control-flow safety always runs. The hero ends UNARMED but
        // CLEAN (no ghost prop) — and the Fail above self-reports why so it can be fixed at root.
        private void RollbackWeaponProp(GameObject gripRoot, string reason)
        {
            FlowTrace.Fail("Equip", $"RollbackWeaponProp on '{name}': {reason} — destroying half-attached prop.");
            if (_currentWeaponProp == gripRoot) _currentWeaponProp = null;
            if (_gripRoot != null && gripRoot != null && _gripRoot == gripRoot.transform) _gripRoot = null;
            // The per-mesh sheathe sign was measured off THIS prop; it must not outlive it.
            _sheatheTipSign = 0f; _sheatheTipWhy = null;
            if (gripRoot != null) Destroy(gripRoot);
            ReleaseWeaponHandle();
        }

        // Release the held Addressables weapon handle (no-op if none open). ONE owner of the
        // handle — called on every swap / unequip / OnDisable so a Blink prefab never leaks.
        private void ReleaseWeaponHandle()
        {
            if (!_weaponHandleOpen) return;
            _weaponHandleOpen = false;
            if (_weaponHandle.IsValid())
                Addressables.Release(_weaponHandle);
            _weaponHandle = default;
        }

        // BUG 2: release a handle ONE FRAME LATER, off the SDK's OnHandleCompleted dispatch. Calling
        // Addressables.Release(op) synchronously inside the Completed delegate is re-entrant — the SDK
        // is still mid-dispatch over that handle and then reads handle.Status on an already-released
        // (invalid) handle. Deferring a frame lets the dispatch finish before we free it.
        private void DeferRelease(AsyncOperationHandle<GameObject> op)
        {
            if (!isActiveAndEnabled)
            {
                if (op.IsValid()) Addressables.Release(op);
                return;
            }
            StartCoroutine(ReleaseNextFrame(op));
        }

        private static IEnumerator ReleaseNextFrame(AsyncOperationHandle<GameObject> op)
        {
            yield return null;
            if (op.IsValid()) Addressables.Release(op);
        }

        // ── SWORD GRIP-POINT INFERENCE — ⚠ RETIRED 2026-07-04 (RC2) ──────────────────
        // NO LONGER CALLED. Kept only as reference for why the single-path seat exists.
        // This branched on a crossguard width-spike vs a bottom-16% fallback and could FLIP
        // the weapon 180° based on which end read wider — non-deterministic per weapon, and it
        // degraded to a blind fallback in a build (unreadable FBX verts) = the editor≠build bug.
        // ALL melee now seat via the deterministic, never-flipping SeatHiltLowerHalf (below).
        //
        // Owner's rule (asset-attachment-inference-engine, task #36):
        //   1. Longest axis -> Y (NormalizeInto already did this; we work in prop-local Y).
        //   2. Find the HILT = a WIDTH SPIKE: bin vertices along Y, measure the X/Z cross-
        //      section extent per bin. Profile: thin blade -> WIDE crossguard flare -> thin
        //      grip. The flare locates the blade/handle boundary.
        //   3. Grip = centre of the HANDLE segment (between the hilt spike and the pommel
        //      end, on the SHORT side — the blade is the long side). Re-seat so that grip
        //      point sits at the origin, blade pointing +Y (outward).
        //   4. Fallback: no clear spike (glaive / flat profile) -> grip the bottom ~16% of
        //      the length near the pommel.
        // Works on prop-LOCAL coordinates (relative to `parent`, the gripRoot) so it is
        // independent of the mesh's own pivot/scale.
        // DEPRECATED (WO-478): superseded by SeatHiltLowerHalf for geometry inference; retained
        // only for reference — not called on the default attach path. Enable ff.weapongripinfer
        // to use the legacy inference stack (SeatHiltLowerHalf, not this method).
        private static void SeatByHandle(GameObject prop, Transform parent)
        {
            if (!TryLocalBounds(prop, parent, out Bounds b)) return;
            float yMin = b.center.y - b.extents.y;
            float yMax = b.center.y + b.extents.y;
            float length = yMax - yMin;
            if (length < 1e-4f) return;

            // Sample mesh vertices into Y-bins; per bin track max |x| and |z| (half-width).
            const int Bins = 48;
            var widthHi = new float[Bins];       // max cross half-extent per bin
            var hit = new bool[Bins];
            CollectWidthProfile(prop, parent, yMin, length, Bins, widthHi, hit);

            // Locate the hilt: the bin with the largest width spike. Compare against the
            // median width to decide whether the spike is "real" (a crossguard) or the
            // profile is basically flat (no clear hilt -> fallback).
            float median = MedianOfHit(widthHi, hit);
            int spikeBin = -1; float spikeW = 0f;
            for (int i = 0; i < Bins; i++)
            {
                if (!hit[i]) continue;
                if (widthHi[i] > spikeW) { spikeW = widthHi[i]; spikeBin = i; }
            }

            // grip point in prop-local Y (relative to parent origin).
            float gripY;
            bool bladePointsPositiveY;
            float binH = length / Bins;
            bool clearSpike = spikeBin >= 0 && median > 1e-5f && spikeW >= median * 1.6f;
            FlowTrace.Step("Equip", $"SeatByHandle DEPRECATED: clearSpike={clearSpike} spikeBin={spikeBin} median={median:0.###} spikeW={spikeW:0.###}");

            if (clearSpike)
            {
                // The handle is the SHORTER segment on one side of the spike; the blade is
                // the longer side. Distances from the spike to each end:
                float spikeY = yMin + (spikeBin + 0.5f) * binH;
                float toMin = spikeY - yMin;     // length of the segment below the spike
                float toMax = yMax - spikeY;     // length of the segment above the spike
                if (toMin <= toMax)
                {
                    // handle is below the spike -> blade is above (+Y). Grip = centre of
                    // [yMin .. spikeY].
                    gripY = (yMin + spikeY) * 0.5f;
                    bladePointsPositiveY = true;
                }
                else
                {
                    // handle is above the spike -> blade is below; we'll flip so blade -> +Y.
                    gripY = (spikeY + yMax) * 0.5f;
                    bladePointsPositiveY = false;
                }
            }
            else
            {
                // FALLBACK: no clear crossguard. Grip the bottom ~16% near one end (treat the
                // narrower-tipped end as the pommel). Pick the end whose extreme bin is
                // narrower as the BLADE TIP, so the pommel/grip is the opposite (wider) end.
                const float HandleFrac = 0.16f;
                float wLow = FirstHitWidth(widthHi, hit, false);   // width near yMin
                float wHigh = FirstHitWidth(widthHi, hit, true);   // width near yMax
                bool pommelAtMin = wLow >= wHigh; // wider end = pommel/grip side
                if (pommelAtMin)
                {
                    gripY = yMin + length * HandleFrac * 0.5f;
                    bladePointsPositiveY = true;
                }
                else
                {
                    gripY = yMax - length * HandleFrac * 0.5f;
                    bladePointsPositiveY = false;
                }
            }

            // 1) Shift so the grip point sits at the parent origin (hand bone).
            Vector3 lp = prop.transform.localPosition;
            lp.y -= gripY;
            prop.transform.localPosition = lp;

            // 2) Ensure the BLADE points +Y (outward from the hand). If the blade is on the
            //    -Y side, flip 180° about local X — this rotates about the grip point because
            //    the grip is now at the origin.
            if (!bladePointsPositiveY)
            {
                prop.transform.localRotation =
                    Quaternion.AngleAxis(180f, Vector3.right) * prop.transform.localRotation;
                prop.transform.localPosition =
                    Quaternion.AngleAxis(180f, Vector3.right) * prop.transform.localPosition;
            }
        }

        // ── HILT-LOWER-HALF SEAT (WO-577 owner convention, 2026-06-28) ───────────────
        // From the 100%-vertical baseline (NormalizeInto: longest axis -> +Y, bounds-centre at
        // origin), the owner's FIXED rule is: the HILT is the LOWER HALF (-Y), the blade points
        // UP (+Y), and the hand grips the hilt on that lower half. This removes SeatByHandle's
        // which-end-is-the-hilt ambiguity (no flip): the grip is always in the bottom portion.
        // Default grip = ~18% up from the bottom (hilt centre near the pommel); a clear width
        // spike (crossguard) WITHIN the lower half refines the exact grip Y. Prop-LOCAL coords
        // relative to <paramref name="parent"/> (the grip root).
        //
        // ⚠ CORRECTED 2026-09-09 (WO-1431). This header used to close with "Used by the
        // vertical-authoring / fullOverride path + the in-game Seating Editor preview; the default
        // path keeps SeatByHandle." That was FALSE of the shipped tree: SeatByHandle is deprecated
        // (see its own header) and this method was the DEFAULT melee seat for EVERY non-native
        // melee prop — swords AND staves. It is now reached through
        // <see cref="SeatMeleeGripPoint"/>, which keeps this rule for the BLADED families and
        // hands a STAFF to the owner-ruled 0.75 derivation instead. Nothing about the rule below
        // changed; only who is routed into it.
        //
        // Reports the grip it used so the dispatcher can trace it and a regression can assert the
        // SHIPPED composition instead of re-typing the constants (the WO-1226 discipline).
        private static bool SeatHiltLowerHalf(GameObject prop, Transform parent,
                                              out float gripY, out float gripFraction)
        {
            gripY = 0f;
            gripFraction = 0f;
            if (!TryLocalBounds(prop, parent, out Bounds b)) return false;
            float yMin = b.center.y - b.extents.y;
            float yMax = b.center.y + b.extents.y;
            float length = yMax - yMin;
            if (length < 1e-4f) return false;
            float mid = yMin + length * 0.5f;

            // Default: hilt centre ~18% up from the bottom.
            gripY = yMin + length * 0.18f;

            // Refine within the LOWER half only: a crossguard width spike marks the blade/handle
            // boundary; grip the centre of the handle segment below it.
            const int Bins = 48;
            var widthHi = new float[Bins];
            var hit = new bool[Bins];
            CollectWidthProfile(prop, parent, yMin, length, Bins, widthHi, hit);
            float median = MedianOfHit(widthHi, hit);
            float binH = length / Bins;
            int spikeBin = -1; float spikeW = 0f;
            for (int i = 0; i < Bins; i++)
            {
                if (!hit[i]) continue;
                float by = yMin + (i + 0.5f) * binH;
                if (by > mid) break;                       // lower half only
                if (widthHi[i] > spikeW) { spikeW = widthHi[i]; spikeBin = i; }
            }
            if (spikeBin >= 0 && median > 1e-5f && spikeW >= median * 1.6f)
            {
                float spikeY = yMin + (spikeBin + 0.5f) * binH;
                gripY = (yMin + spikeY) * 0.5f;            // centre of the handle below the crossguard
            }

            // Shift so the grip point sits at the parent origin (hand bone). NEVER flips — blade
            // stays +Y by the owner rule.
            Vector3 lp = prop.transform.localPosition;
            lp.y -= gripY;
            prop.transform.localPosition = lp;
            gripFraction = (gripY - yMin) / length;
            FlowTrace.Step("Equip", $"SeatHiltLowerHalf: gripY={gripY:0.###} spikeBin={spikeBin} spikeW={spikeW:0.###} median={median:0.###} shiftedY={prop.transform.localPosition.y:0.###} fraction={gripFraction:0.###}");
            return true;
        }

        // ═══════════════════════════════════════════════════════════════════════════════════════
        //  WO-1431 — THE MELEE GRIP DISPATCHER: the derivation that was MEASURED now APPLIES
        // ═══════════════════════════════════════════════════════════════════════════════════════
        //
        // ⛔ READ THIS BEFORE CHANGING EITHER BRANCH.
        //
        // THE DEFECT (owner, 2026-09-06, verbatim): *"the staff needs reversed, right now they
        // grasp it 75% on the lower half instead of on the upper half of the staff"*.
        //
        // THE PROVEN CAUSE (READY_RCA_2026-09-09, device trace on Hero (Blaise) / tripo_staff_a):
        // the helper ALREADY computed the owner-ruled answer — a 1.2639 m staff, fraction 0.75 ->
        // gripY 0.7204 — and then THREW IT AWAY. `WeaponOrientHelper.TryDeriveStaffGripY` was
        // reachable only from `TraceMeasuredSeat`, a read-only prediction, while the live melee
        // attach path called `SeatHiltLowerHalf` for EVERY non-native melee prop. That method
        // defaults to 18% up from the foot and its crossguard refinement `break`s at the midpoint
        // (`if (by > mid) break;`), so it is STRUCTURALLY INCAPABLE of returning anything above
        // 0.50 — the measurement path and the live path were two different programs and only one
        // of them moved the prop. This dispatcher is the join: one function, one archetype input,
        // so a future reader cannot re-open the split by editing "the other" path.
        //
        // ⛔ IT IS NOT A JSON OFFSET. The old WO-1431 text proposed authoring a staff row in
        // offsets.json; the ledger rules that out as a workaround over a live/measure split — and
        // an authored row would have to be re-dialled per staff mesh, which is the per-asset
        // hand-tuning docs/WEAPON_ARMOR_ORIENT_LOGIC.md exists to abolish. Verified 2026-09-09:
        // offsets.json holds 26 rows and NONE of them is a staff.
        //
        // PRECEDENCE IS UNCHANGED (WeaponOrientHelper.ResolveSource, WO-1123 + WO-1215):
        // authored offset row -> substantiated manual -> derived -> archetype default. This method
        // only ever runs the derived branch when <paramref name="mayDerive"/> is true, which the
        // caller computes from that ladder. An owner-dialled seat therefore takes the byte-for-byte
        // pre-WO-1431 path (SeatHiltLowerHalf + the authored nudge on top) and is untouched.
        //
        // ONLY the Staff archetype changes. Sword/dagger keep the hilt-lower-half rule — the
        // bladed families are felt-verified and docs/WEAPON_ARMOR_ORIENT_LOGIC.md warns in its own
        // words that "a staff repair must not rotate every melee family to fix one" (that guard is
        // asserted in AttachmentOffsetRegression Case7 and again in StaffGripSeatRegression).
        // axe/hammer/mace/wand/crossbow classify Unknown and DERIVE NOTHING by the owner's 2026-08-19
        // ruling, so they land in the same untouched sword branch, exactly as they do today.
        //
        // ⚠ THIS TOUCHES THE GRIP POINT ONLY — the position of the shaft in the fist. It does not
        // touch a single rotation. The drawn staff's verticality is StaffDrawnGripNudgeDefault
        // (90,0,0), owner-ruled 2026-08-26 and pinned twice; do not fold the two together.
        public struct MeleeGripSeat
        {
            /// <summary>Which rule moved the prop — printed in the trace, asserted by the suite.</summary>
            public string Rule;
            /// <summary>Parent-local Y that was brought onto the hand bone.</summary>
            public float GripY;
            /// <summary>Where that grip sits along the measured long axis, 0 = foot, 1 = head.</summary>
            public float GripFraction;
            /// <summary>False = bounds unmeasurable; NOTHING was moved and the caller keeps its frame.</summary>
            public bool Measured;
        }

        /// <summary>
        /// WO-1431. Seat a melee prop's grip point on the hand, per ARCHETYPE. Staff (and only
        /// staff) takes the owner-ruled 0.75-up-the-long-axis derivation; every other melee family
        /// keeps the hilt-lower-half rule. Public static so the regression asserts the SHIPPED
        /// dispatch rather than re-typing its constants.
        /// </summary>
        public static MeleeGripSeat SeatMeleeGripPoint(GameObject prop, Transform parent,
                                                       WeaponArchetype archetype, bool mayDerive,
                                                       string subject)
        {
            var seat = new MeleeGripSeat { Rule = "none", Measured = false };
            if (prop == null || parent == null) return seat;

            if (archetype == WeaponArchetype.Staff && mayDerive &&
                WeaponOrientHelper.TryDeriveStaffGripY(prop, parent, out float staffGripY,
                                                       out float staffYMin, out float staffLen, out string why))
            {
                Vector3 lp = prop.transform.localPosition;
                lp.y -= staffGripY;
                prop.transform.localPosition = lp;

                seat.Rule = "STAFF-DERIVED-UPPER-SHAFT";
                seat.GripY = staffGripY;
                seat.GripFraction = (staffGripY - staffYMin) / staffLen;
                seat.Measured = true;
                FlowTrace.Step("Equip",
                    $"MELEE GRIP APPLIED '{subject}': rule={seat.Rule} archetype={archetype} " +
                    $"gripY={seat.GripY:0.####} fraction={seat.GripFraction:0.###} up the long axis " +
                    $"(ySpan={staffLen:0.####}m) shiftedY={prop.transform.localPosition.y:0.####} " +
                    $"| WHY: {why} | WO-1431: this value used to be computed and DISCARDED by " +
                    "TraceMeasuredSeat while SeatHiltLowerHalf seated the shaft at ~0.18. If this " +
                    "line reads at or below 0.50 for a staff, the lower-hilt rule has crept back.");
                return seat;
            }

            bool hilted = SeatHiltLowerHalf(prop, parent, out float hiltGripY, out float hiltFraction);
            seat.Rule = "HILT-LOWER-HALF";
            seat.GripY = hiltGripY;
            seat.GripFraction = hiltFraction;
            seat.Measured = hilted;
            FlowTrace.Step("Equip",
                $"MELEE GRIP APPLIED '{subject}': rule={seat.Rule} archetype={archetype} " +
                $"mayDerive={mayDerive} gripY={seat.GripY:0.####} fraction={seat.GripFraction:0.###} " +
                $"measured={seat.Measured}. " +
                (archetype == WeaponArchetype.Staff
                    ? "⚠ A STAFF took the bladed rule — either an authored/substantiated-manual seat " +
                      "owns this row (precedence, correct) or its bounds were unmeasurable (Warn above)."
                    : "Bladed/unknown archetype keeps the WO-577 hilt rule, unchanged by WO-1431."));
            return seat;
        }

        // =====================================================================================
        //  WO-1616 — THE ONE SHIELD-SEATING AUTHORITY (hero AND raid NPC read it)
        // =====================================================================================
        //
        // WHY THIS EXISTS. `TroopGearApplier.ApplyDefaultGrip` attached EVERY off-hand to LeftHand
        // with one hard-coded triple — `localPosition (0.05, 0.05, 0.02)`, `Euler(0, 90, 0)`,
        // `scale 1` — under a header that concedes its own ceiling ("Coarse grips for ~1.8 m
        // Supercyan / Tripo humanoids"). It carried no per-rig, per-mesh or per-shield term of any
        // kind, so every deployed raid NPC wore a visibly wrong shield for the whole raid
        // (`troop-echo-legionnaire`, five actors at once). The hero has never had that bug because
        // the hero path MEASURES: WeaponOrientHelper.TryResolveShieldFrame + GearSeat.GetShieldAxes
        // + TryComputeShieldMountRotation + EnsureShieldOuterFaces + GearSeat.ShieldPlateOffBone.
        //
        // ⛔ THE FIX IS TO DELETE THE COPY, NOT TO AUTHOR A BETTER ONE. A per-troop offsets file or
        // a second hand-dialled triple is the duplicated-state failure CLAUDE.md §2 / §5 / §16 each
        // describe in their own words — two authorities that drift, and the next rig breaks both.
        // These two methods ARE the hero's own steps, LIFTED (moved, not re-authored) so both
        // callers execute the same instructions. docs/ARCHITECTURE_PRINCIPLES.md §2b.1: one owner
        // per concern; shield seating is one concern.
        //
        // ⚠ WHY IT LIVES HERE AND NOT IN GearSeat. WO-1616 §3 suggested placing the authority beside
        // WeaponOrientHelper/GearSeat. It is here instead, as `public static`, on the orchestrator's
        // lane instruction and following today's `SeatMeleeGripPoint` precedent (WO-1431): this lane
        // may not edit WeaponOrientHelper.cs, and a shared entry point that lives where the hero's
        // steps already live is a move rather than a rewrite. The rotation/centre math it calls is
        // already in DeNelle.Core; lifting these two wrappers down to GearSeat later is a pure
        // relocation and should be its own ticket.
        //
        // SPLIT IN TWO, DELIBERATELY. The hero interleaves the Offset-Forge nudge and the
        // global-yaw-or-withhold decision BETWEEN the rotation and the plate seat, so one monolithic
        // "seat the shield" call could not be dropped into the hero without REORDERING the hero —
        // which this ticket explicitly must not do. The split follows the vocabulary WO-1123 already
        // uses: one measured half (per attach) and one cheap posing half.
        public struct ShieldSeat
        {
            /// <summary>The measured frame — hand it to the sheathed pose and to the plate seat.</summary>
            public WeaponOrientHelper.ShieldFrame Frame;
            /// <summary>True when the bounds answered (plate-shaped, measurable renderer).</summary>
            public bool FrameValid;
            /// <summary>True when the derived rotation was actually WRITTEN onto the grip root.</summary>
            public bool Derived;
            /// <summary>Which rule moved it — printed in the trace, asserted by the suite.</summary>
            public string Rule;
            /// <summary>The rotation written (mount-local), or identity when nothing was derived.</summary>
            public Quaternion MountLocal;
        }

        public const string ShieldRuleDerived    = "SHIELD-DERIVED-THICKNESS-OUTWARD";
        public const string ShieldRuleNotDerived = "SHIELD-NOT-DERIVED";
        public const string ShieldRulePrecedence = "SHIELD-SEAT-OWNED-BY-PRECEDENCE";

        /// <summary>
        /// WO-1616. THE shield seat: measure the plate's own frame, then (when precedence allows)
        /// point its measured thickness away from the body and its longest extent along the forearm,
        /// and WRITE that rotation onto <paramref name="gripRoot"/>. Public static so the raid NPC
        /// path and the regression both execute the SHIPPED instructions rather than a copy.
        /// <para>
        /// MEASURE FIRST, DECIDE AFTER (owner ruling 2026-08-20): the vertex walk used to sit inside
        /// the drawn-pose precedence gate, so a shield with an authored DRAWN row was never measured
        /// at all — and the SHEATHED pose, which has its own channel and its own precedence, was left
        /// with no frame to pose from. Proving line, logs/device/2026-08-20-equip.log: NOT ONE
        /// `ShieldFrame` line appears in the whole capture. Measurement is not a decision: one walk,
        /// at attach, and every pose afterwards reads the same numbers.
        /// </para>
        /// <para>
        /// NATIVE IS DELIBERATELY *NOT* EXCLUDED. "Native" means "trust the authored pivot", and the
        /// LIVE default shield (knight_shield_starter -> ShieldWithItemLogic) is exactly a native prop
        /// whose authored orientation is the broken one. Excluding native would have fixed every
        /// shield except the one that is wrong. Only the ROTATION is derived; pivot and scale are
        /// untouched.
        /// </para>
        /// </summary>
        /// <param name="mayDerive">The caller's precedence verdict (WeaponOrientHelper.ResolveSource:
        /// authored row -> substantiated manual -> derived -> archetype default). False means an
        /// owner-dialled seat owns this row and NOTHING here may move it.</param>
        public static ShieldSeat SeatShieldMountRotation(GameObject prop, Transform gripRoot, Transform hand,
                                                        Animator animator, Transform body,
                                                        bool mayDerive, string subject)
        {
            var seat = new ShieldSeat { Rule = ShieldRuleNotDerived, MountLocal = Quaternion.identity };
            if (prop == null || gripRoot == null || hand == null)
            {
                FlowTrace.Warn("Equip",
                    $"SeatShieldMountRotation '{subject}': null prop/gripRoot/hand — nothing seated. " +
                    "The caller keeps whatever frame the prop arrived with.");
                return seat;
            }

            var measured = default(WeaponOrientHelper.ShieldFrame);
            if (Guard.Try("Equip", $"shield frame measure for '{subject}' (owner ruling 2026-08-20)",
                    () => WeaponOrientHelper.TryResolveShieldFrame(prop, gripRoot, out measured),
                    false))
            {
                seat.Frame = measured;
                seat.FrameValid = measured.Valid;
            }

            if (!mayDerive)
            {
                seat.Rule = ShieldRulePrecedence;
                return seat;
            }
            if (!seat.FrameValid)
            {
                // §12 / WO-1123: ambiguity FALLS BACK, it never guesses. The Warn naming which clause
                // failed was already emitted inside TryResolveShieldFrame; this line names the
                // consequence so "the shield is wrong" can be split into "the derivation ran and is
                // wrong" vs "the derivation never ran".
                FlowTrace.Warn("Equip",
                    $"SeatShieldMountRotation '{subject}': rule={ShieldRuleNotDerived} — the frame is " +
                    "unmeasurable (see the ShieldFrame Warn immediately above for WHICH clause: null, " +
                    "no measurable renderer bounds, or not plate-shaped). The prop is left exactly as " +
                    "it arrived. WO-1616: this used to be a hand-typed triple on the troop path; that " +
                    "copy is deleted, so an unmeasurable NPC shield now sits at its parent's frame " +
                    "rather than at a constant nobody could tune.");
                return seat;
            }

            // Mount axes are the FOREARM SOCKET: +Z away from the arm (inner face flush), +Y along
            // the forearm toward the wrist. Body-forward here was "face the camera" and is why a
            // strapped heater spun off the limb.
            var frame = seat.Frame;
            Vector3 socketOut, socketUp;
            GearSeat.GetShieldAxes(animator, body, out socketOut, out socketUp);
            Quaternion derivedShield = Quaternion.identity;
            // Reads the frame measured above — STILL exactly one vertex walk per attach, and still
            // the frame overload (the GameObject one would walk the mesh a second time).
            bool ok = Guard.Try("Equip", $"derived shield seat for '{subject}' (WO-1123)",
                () => WeaponOrientHelper.TryComputeShieldMountRotation(
                          frame, subject, hand, socketOut, socketUp,
                          out derivedShield, out string shieldWhyUnused),
                false);
            if (!ok) return seat;

            derivedShield = GearSeat.EnsureShieldOuterFaces(
                derivedShield, hand, socketOut, socketUp, frame, body);
            gripRoot.localRotation = derivedShield;
            seat.Derived = true;
            seat.Rule = ShieldRuleDerived;
            seat.MountLocal = derivedShield;
            return seat;
        }

        /// <summary>
        /// WO-1616. The second half: put the measured plate ON the arm. Handle loop first (a prefab
        /// that ships a DUMMY/Handle node snaps that node onto the socket); otherwise centre the
        /// rendered AABB on the bone and push it OUT along the opening by half the measured
        /// thickness, so the only volume intersecting the bone is the handle — not the plate, not
        /// the torso. Lifted verbatim from the hero's snap/off-bone branch.
        /// </summary>
        public static void SeatShieldPlateOnSocket(Transform gripRoot, Transform hand, Animator animator,
                                                   Transform body, WeaponOrientHelper.ShieldFrame frame,
                                                   string subject, string extraTraceToken = null)
        {
            if (gripRoot == null || hand == null) return;
            if (GearSeat.FindHandleDummy(gripRoot) != null)
            {
                GearSeat.SnapHandleToSocket(gripRoot, hand);
                return;
            }
            // extraTraceToken exists so the HERO's centring line keeps the `(arm=…)` token it has
            // always printed — the F8 captures grep for that line, and a shared core that silently
            // dropped a token would make an old capture and a new one incomparable.
            CentreGripOnSocket(gripRoot, hand, Vector3.zero, subject, extraTraceToken);
            Vector3 outward, heaterUp;
            GearSeat.GetShieldAxes(animator, body, out outward, out heaterUp);
            gripRoot.localPosition += GearSeat.ShieldPlateOffBone(hand, outward, frame, gripRoot);
        }

        // WO-478 §12: dump seated transforms so headless equip captures prove native vs infer path.
        private static void LogGripSeatDiagnostics(GameObject prop, Transform gripRoot, Transform hand,
            string weaponId, string path)
        {
            if (prop == null || gripRoot == null) return;
            FlowTrace.Step("Equip",
                $"WO-478 seat dump [{path}] '{weaponId}': prop.localPos={prop.transform.localPosition} " +
                $"prop.localEuler={prop.transform.localRotation.eulerAngles} " +
                $"gripRoot.localPos={gripRoot.localPosition} gripRoot.localEuler={gripRoot.localRotation.eulerAngles} " +
                $"hand='{(hand != null ? hand.name : "<null>")}'");
        }

        // Bin mesh vertices along prop-local Y; record max |z| per bin (Z = wide axis, thickest
        // at hilt). Vertices are transformed mesh-local -> parent-local so the profile is
        // measured in the same frame the grip math uses.
        private static void CollectWidthProfile(
            GameObject prop, Transform parent, float yMin, float length,
            int bins, float[] widthHi, bool[] hit)
        {
            float inv = bins / length;
            foreach (var mf in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                // BUILD-SAFE: sharedMesh.vertices THROWS ("Not allowed to access vertices")
                // when the mesh isn't Read/Write-enabled — the default for imported FBX in a
                // player build. Skip non-readable meshes; SeatByHandle then degrades to the
                // bounds-based grip (no vertex access). This was the in-build sword crash.
                if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable) continue;
                var verts = mf.sharedMesh.vertices;
                Transform mt = mf.transform;
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 world = mt.TransformPoint(verts[v]);
                    Vector3 local = parent.InverseTransformPoint(world);
                    int bin = Mathf.Clamp((int)((local.y - yMin) * inv), 0, bins - 1);
                    float w = Mathf.Abs(local.z);
                    if (!hit[bin] || w > widthHi[bin]) widthHi[bin] = w;
                    hit[bin] = true;
                }
            }
            // Skinned meshes (rare for a held prop, but be safe): use renderer bounds slabs.
            foreach (var smr in prop.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null || !smr.sharedMesh.isReadable) continue;
                var verts = smr.sharedMesh.vertices;
                Transform mt = smr.transform;
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 world = mt.TransformPoint(verts[v]);
                    Vector3 local = parent.InverseTransformPoint(world);
                    int bin = Mathf.Clamp((int)((local.y - yMin) * inv), 0, bins - 1);
                    float w = Mathf.Abs(local.z);
                    if (!hit[bin] || w > widthHi[bin]) widthHi[bin] = w;
                    hit[bin] = true;
                }
            }
        }

        private static float MedianOfHit(float[] vals, bool[] hit)
        {
            var list = new List<float>();
            for (int i = 0; i < vals.Length; i++) if (hit[i]) list.Add(vals[i]);
            if (list.Count == 0) return 0f;
            list.Sort();
            return list[list.Count / 2];
        }

        // Width of the first hit bin scanning from one end (fromTop=true -> highest Y).
        private static float FirstHitWidth(float[] widthHi, bool[] hit, bool fromTop)
        {
            if (fromTop)
            {
                for (int i = widthHi.Length - 1; i >= 0; i--) if (hit[i]) return widthHi[i];
            }
            else
            {
                for (int i = 0; i < widthHi.Length; i++) if (hit[i]) return widthHi[i];
            }
            return 0f;
        }

        /// <summary>Removes the currently-shown weapon prop (no-op if none).</summary>
        public void Unequip()
        {
            _equipGeneration++;          // invalidate any in-flight async load
            DestroyCurrentWeapon();
            ReleaseWeaponHandle();
            _currentWeaponId = null;
            _gripRoot = null;
            // Per-mesh sheathe sign belongs to the prop that has just been destroyed. Clearing it
            // makes the NEXT prop's attach the only thing that can set it — a stale sign surviving
            // an unequip would hang a normalized sword by whatever the previous native one measured.
            _sheatheTipSign = 0f; _sheatheTipWhy = null;
        }

        // ── OFF-HAND (shield) attach ─────────────────────────────────────────────────
        /// <summary>
        /// Attach the off-hand/shield described by <paramref name="def"/> to the hero's OFF hand
        /// (LeftHand bone), mirroring the main-hand attach path. A null def DETACHES the off-hand.
        /// Reuses the same Resources/primitive resolve + grip/seat the main path uses (the off-hand
        /// goes through the Shield preset, which already seats centre-grip on the LeftHand). Safe to
        /// call repeatedly (idempotent on an unchanged id).
        ///
        /// BLINK FIX (2026-06-19, "shield hangs off the body, not on the arm"): a Blink shield row
        /// (category=="shield", loadVia=="addressable", prefabPath "gear/weapon/Shield1h_XX") must
        /// load its REAL addressable prefab — the old path ignored Addressables entirely and loaded
        /// the legacy Tripo "shield_A" Resources mesh, whose foreign pivot + the non-zero gripPos
        /// (-0.05) left the shield floating beside the forearm instead of strapped to the hand. We
        /// now branch: Blink shields go through the SAME async Addressables path the main weapon uses
        /// (and seat NATIVE — trust the authored grip), Tripo/Resources shields keep the sync path.
        /// </summary>
        /// <summary>String-id convenience: resolve the off-hand (shield) WeaponDef from the catalog
        /// and equip it. Used by the Gear Preview to mirror the equipped shield. Null/empty detaches.</summary>
        public void EquipOffHand(string offHandId)
        {
            EquipOffHand(string.IsNullOrEmpty(offHandId) ? null : GearCatalog.FindWeapon(offHandId));
        }

        public void EquipOffHand(WeaponDef def)
        {
            string id = def != null ? def.id : null;

            // PACKAGE de-dupe: the Paladin body bakes its own shield — do NOT attach a KayKit/Blink shield
            // (owner F8 "shield is 180 degrees wrong" — that was an ATTACHED shield; skipping it leaves the
            // correctly-baked one). Loadout still tracks the off-hand; only the visible prop is suppressed.
            if (PackageBakedGear)
            {
                FlowTrace.Step("Equip",
                    $"PACKAGE baked-gear hero '{name}' — SKIP off-hand/shield attach for '{id ?? "<null>"}' " +
                    "(baked Paladin shield wins; no wrong-oriented attached shield).");
                return;
            }

            // Idempotent: same off-hand already shown, OR the Addressable load for that
            // same id is still in flight. Captured 2026-08-30: GearLoadout.Refresh always
            // invokes OnGearChanged, WireHeroBody calls Refresh AFTER OnEnable already
            // started BeginAddressableOffHand. The old check required _currentOffHandProp
            // != null, so the in-flight load looked like "not equipped", EquipOffHand
            // bumped _offHandGeneration (killing the first callback), DestroyCurrentOffHand,
            // and rebuilt the prop from JSON — wiping a live Inspector pose. Same-id +
            // in-flight must skip, or every body-wire Refresh re-seats the heater.
            bool sameId = string.Equals(_currentOffHandId, id, System.StringComparison.OrdinalIgnoreCase);
            if (sameId && _currentOffHandProp != null)
            {
                FlowTrace.Step("Equip",
                    $"off-hand idempotent skip id='{id}' prop='{_currentOffHandProp.name}' " +
                    $"parent='{(_currentOffHandProp.transform.parent != null ? _currentOffHandProp.transform.parent.name : "<null>")}' " +
                    $"frame={Time.frameCount}");
                return;
            }
            if (sameId && _offHandHandleOpen)
            {
                FlowTrace.Step("Equip",
                    $"off-hand idempotent skip id='{id}' IN-FLIGHT Addressable " +
                    $"(generation={_offHandGeneration}) — GearLoadout.Refresh/HandleGearChanged " +
                    "will NOT bump generation or rebuild. Live Inspector pose is kept.");
                return;
            }

            // New off-hand request — invalidate any in-flight async off-hand load + drop the old prop/handle.
            _offHandGeneration++;
            DestroyCurrentOffHand();
            ReleaseOffHandHandle();
            _offHandAddressableFailed = false;
            _currentOffHandId = id;
            if (string.IsNullOrEmpty(id)) return;   // detach

            // Resolve the off-hand visual. An off-hand item is a shield; Resolve maps shield ids
            // to the Shield preset (LeftHand, centre-grip). Force the Shield seat so a non-shield
            // off-hand id (future 1h offhand) still seats sanely on the off hand.
            WeaponVisual vis = Resolve(id);
            if (vis == null || vis.kind != WeaponClass.Shield)
                vis = Shield(vis != null && !string.IsNullOrEmpty(vis.mesh) ? vis.mesh : "shield_A");

            using var _ = FlowTrace.Enter("Equip", $"attach off-hand '{id}' to '{name}'");

            CacheRig();
            if (_animator == null || !_animator.isHuman)
            {
                FlowTrace.Warn("Equip", $"off-hand: rig not Humanoid yet on '{name}' — deferring to LateAttachRetry");
                return;
            }

            // Strapped heater/kite/round: parent to Socket_Shield on LeftLowerArm, NEVER
            // LeftHand — a real heater sits on the forearm; the hand bone waves every
            // wrist/finger clip. Socket is a dedicated empty so shield swaps do not touch
            // the Avatar. Created after the Humanoid Avatar is ready (gate above).
            Transform hand = null;
            if (vis.kind == WeaponClass.Shield)
            {
                var shieldPlan = GearSeat.ResolveMount(_animator, transform, WeaponArchetype.Shield);
                hand = shieldPlan.Mount;
                if (hand != null)
                {
                    _sheatheSocketOffIsArm = true;
                    FlowTrace.Step("Equip",
                        $"off-hand '{id}' attach parent='{hand.name}' under '{(hand.parent != null ? hand.parent.name : "<null>")}' " +
                        "(GearSeat Shield — same parent sheathed and drawn).");
                }
            }
            if (hand == null)
            {
                // Buckler / missing-forearm fallback only. A heater should not land here.
                GameObject heroRoot = _animator.gameObject;
                string rigId = heroRoot.name;
                if (RigAttachmentRegistry.TryResolve(heroRoot, rigId, true, out var overrideAnchor, out var how))
                {
                    hand = overrideAnchor;
                    FlowTrace.Step("Offset", $"attach rig={rigId} hand=L -> '{hand.name}' (via json-override)");
                }
                else
                {
                    if (how != null && how.StartsWith("missing"))
                        FlowTrace.Fail("Offset", $"attach rig={rigId} hand=L override path absent in model ({how}); falling back to avatar");

                    hand = FlowTrace.Try("Equip", "GetBoneTransform(LeftHand)",
                        () => _animator.GetBoneTransform(HumanBodyBones.LeftHand), null);
                    FlowTrace.Step("Offset", $"attach rig={rigId} hand=L -> '{(hand != null ? hand.name : "<null>")}' (via avatar)");
                }
                if (vis.kind == WeaponClass.Shield)
                    FlowTrace.Warn("Equip",
                        $"off-hand '{id}': Socket_Shield unavailable — falling back to LeftHand. " +
                        "A strapped heater will follow wrist animation; map LeftLowerArm on the Avatar.");
            }
            if (hand == null)
            {
                FlowTrace.Fail("Equip", $"Humanoid rig on '{name}' has NO forearm socket and NO LeftHand bone — " +
                    $"off-hand '{id}' NOT attached.");
                return;
            }

            // BLINK SHIELD (Addressable): load the real prefab async + seat NATIVE (trust the
            // authored grip-at-origin). Mirrors the main-hand Addressable branch + its stale-load
            // guard. A failed/throwing handle falls back to the sync Resources path (never unequipped).
            if (LoadsViaAddressable(def))
            {
                FlowTrace.Step("Equip", $"off-hand branch: ADDRESSABLE ('{def.prefabPath}')");
                BeginAddressableOffHand(def, vis, hand, id, _offHandGeneration);
                return;
            }

            FlowTrace.Step("Equip", $"off-hand branch: RESOURCES map (mesh='{vis.mesh}')");
            GameObject prop = LoadWeaponMesh(vis.mesh, id) ?? BuildFallbackPrimitive(vis);
            if (prop == null) { FlowTrace.Fail("Equip", $"off-hand prop null for mesh '{vis.mesh}'"); return; }

            FlowTrace.Step("Equip", $"off-hand seated: id='{id}' mesh='{vis.mesh}' hand='{hand.name}'");
            AttachOffHandProp(prop, vis, hand, id);
        }

        // Kick off the async Addressables load of a Blink off-hand (shield) prefab + attach on
        // completion. Mirrors BeginAddressableEquip (main hand): the off-hand handle has ONE owner,
        // the generation guard rejects a stale completion, and any failure GUARDS back to the sync
        // Resources path so the off-hand is never silently dropped because a Blink prefab hiccupped.
        private void BeginAddressableOffHand(
            WeaponDef def, WeaponVisual vis, Transform hand, string id, int generation)
        {
            string address = def.prefabPath;
            FlowTrace.Step("Gear", $"Addressable off-hand equip begin: id='{id}' address='{address}'");

            AsyncOperationHandle<GameObject> handle;
            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(address);
            }
            catch (System.Exception ex)
            {
                _offHandAddressableFailed = true;
                FlowTrace.Fail("Gear", $"Addressable off-hand load threw for '{address}': {ex.Message} — " +
                                       "falling back to Resources shield (hero keeps a shield).");
                FallbackResourcesOffHand(vis, hand, id);
                return;
            }

            _offHandHandle = handle;
            _offHandHandleOpen = true;

            handle.Completed += op =>
            {
                // Stale: the player swapped/unequipped the off-hand, or a scene/body swap
                // destroyed the captured rig bone while this load was in flight. The latter can
                // happen before this component's OnDisable callback runs, so generation alone is
                // not a sufficient lifetime proof.
                if (generation != _offHandGeneration || hand == null || !isActiveAndEnabled)
                {
                    if (_offHandHandle.Equals(op)) { _offHandHandle = default; _offHandHandleOpen = false; }
                    DeferRelease(op);
                    return;
                }

                if (!op.IsValid() || op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    _offHandAddressableFailed = true;
                    FlowTrace.Fail("Gear", $"Addressable off-hand load FAILED for '{address}' " +
                        $"(status={op.Status}) — falling back to Resources shield (hero keeps a shield).");
                    ReleaseOffHandHandle();
                    FallbackResourcesOffHand(vis, hand, id);
                    return;
                }

                // Blink shield prefab is authored grip-at-origin + oriented → seat NATIVE.
                var nativeVis = CopyOf(vis);
                nativeVis.native = true;
                GameObject prop = null;
                Guard.Try("Gear", $"instantiate addressable off-hand '{address}'",
                    () => prop = Instantiate(op.Result));
                if (prop == null)
                {
                    _offHandAddressableFailed = true;
                    FlowTrace.Fail("Gear", $"Addressable off-hand Instantiate returned null for '{address}' " +
                        $"(id='{id}') — falling back to Resources shield (hero keeps a shield).");
                    ReleaseOffHandHandle();
                    FallbackResourcesOffHand(vis, hand, id);
                    return;
                }
                PreserveOffHandRenderAssets(prop, id);
                AttachOffHandProp(prop, nativeVis, hand, id);
                FlowTrace.Step("Gear", $"Addressable off-hand attached: id='{id}' address='{address}'");
            };
        }

        // Off-hand fallback: a Blink Addressable resolve failed — seat via the sync Resources map
        // (or the tinted primitive) so the hero is never left with the off slot blank. Non-native.
        private void FallbackResourcesOffHand(WeaponVisual vis, Transform hand, string id)
        {
            var fb = CopyOf(vis);
            fb.native = false;
            GameObject prop = LoadWeaponMesh(fb.mesh, id) ?? BuildFallbackPrimitive(fb);
            if (prop == null) return;
            AttachOffHandProp(prop, fb, hand, id);
        }

        // Release the held Addressables off-hand handle (no-op if none open). ONE owner — called on
        // every off-hand swap / detach / OnDisable so a Blink shield prefab never leaks.
        private void ReleaseOffHandHandle()
        {
            if (!_offHandHandleOpen) return;
            _offHandHandleOpen = false;
            if (_offHandHandle.IsValid())
                Addressables.Release(_offHandHandle);
            _offHandHandle = default;
        }

        // Compact TRS line for the off-hand attach step-in / step-out. Names GRIP (the Offset
        // Forge target, EquipmentProp_OffHand) separately from MESH (the inner renderer GO the
        // Inspector often has selected — that is NOT the offset row).
        private static string FormatTrs(Transform t)
        {
            if (t == null) return "<null>";
            string parent = t.parent != null ? t.parent.name : "<null>";
            return $"'{t.name}' parent='{parent}' " +
                   $"lPos={t.localPosition:0.###} lEuler={t.localEulerAngles:0.#} lScale={t.localScale:0.###} " +
                   $"wPos={t.position:0.###} wEuler={t.eulerAngles:0.#} lossy={t.lossyScale:0.###}";
        }

        private void TraceOffHandTrs(string step, Transform gripOrProp)
        {
            using var scope = FlowTrace.Enter("Equip", $"TRS {step}");
            if (gripOrProp == null)
            {
                FlowTrace.Warn("Equip", $"TRS {step}: transform NULL");
                return;
            }
            FlowTrace.Step("Equip", $"TRS {step} ROOT {FormatTrs(gripOrProp)}");
            int n = Mathf.Min(gripOrProp.childCount, 6);
            for (int i = 0; i < n; i++)
            {
                Transform c = gripOrProp.GetChild(i);
                FlowTrace.Step("Equip", $"TRS {step} CHILD[{i}] {FormatTrs(c)}");
                int m = Mathf.Min(c.childCount, 4);
                for (int j = 0; j < m; j++)
                    FlowTrace.Step("Equip", $"TRS {step} CHILD[{i}].[{j}] {FormatTrs(c.GetChild(j))}");
            }
            var rend = gripOrProp.GetComponentInChildren<Renderer>();
            if (rend != null)
            {
                Bounds wb = rend.bounds;
                FlowTrace.Step("Equip",
                    $"TRS {step} MESH '{rend.gameObject.name}' {FormatTrs(rend.transform)} " +
                    $"worldBounds c={wb.center:0.###} s={wb.size:0.###}");
            }
        }

        // Seat an off-hand prop on the off hand. Shields are centre-gripped (their own NormalizeInto
        // + preset euler, like the bow) — they do NOT run the melee handle-inference / rig-axis
        // rotation. Kept separate from the main-hand AttachLoadedProp so the off-hand prop has its
        // own lifecycle reference (no clobbering the main weapon's grip-root / hold-pose state).
        private void AttachOffHandProp(GameObject prop, WeaponVisual vis, Transform hand, string id)
        {
            // Async Addressables completion may race a scene/body replacement. Never inspect or
            // parent through Unity's destroyed-object sentinel; discard the unattached instance.
            if (prop == null) return;
            if (hand == null)
            {
                FlowTrace.Warn("Equip", $"AttachOffHandProp '{id}' discarded: target hand was destroyed.");
                Destroy(prop);
                return;
            }
            using var _ = FlowTrace.Enter("Equip", $"AttachOffHandProp '{id}' -> '{hand.name}' (kind={vis.kind})");
            prop.name = OffHandMeshName;
            foreach (var c in prop.GetComponentsInChildren<Collider>(true)) if (c != null) Destroy(c);
            foreach (var rb in prop.GetComponentsInChildren<Rigidbody>(true)) if (rb != null) Destroy(rb);
            EnsureWeaponRenderersVisible(prop, hand, id);

            // OFFSET RESOLUTION (WO-577): an off-hand can carry an authored VERTICAL-baseline
            // offset (fullOverride) from the in-game Seating Editor — keyed by mesh name then id.
            // fullOverride replaces the seat; a plain nudge composes on top of the preset grip
            // (same as main-hand) before ApplyGlobalWeaponYaw.
            string offsetKey = !string.IsNullOrEmpty(vis.mesh) ? vis.mesh : id;
            bool hasOffset = AttachmentOffsetRegistry.TryGetOffset(offsetKey, out var fo) ||
                             (offsetKey != id && AttachmentOffsetRegistry.TryGetOffset(id, out fo));
            if (!hasOffset)
            {
                // §12 / §1.4b: ONE bool out of TWO lookups used to have no else-branch, so an
                // unseated weapon and a deliberately-unauthored one were indistinguishable in the
                // trace. Name both keys that were tried and what the seat falls back to.
                FlowTrace.Warn("Equip",
                    $"off-hand seat: NO OFFSET for key='{offsetKey}' or id='{id}' — " +
                    "AttachmentOffsetRegistry has no authored row for either. CONSEQUENCE: the prop " +
                    "takes the un-dialed preset grip (no Seating Editor pose); it may sit wrong in hand.");
            }
            bool fullOverride = hasOffset && fo.fullOverride;

            var gripRoot = new GameObject(OffHandPropName);
            float heldLen = ProportionalHeldLength(vis.heldLength);
            FlowTrace.Step("Equip",
                $"off-hand heldLength '{id}' kind={vis.kind}: archetype={vis.heldLength:0.###}m " +
                $"proportional={heldLen:0.###}m hero={ResolveHeroHeightM():0.###}m");
            if (fullOverride)
            {
                // fullOverride MEANS the Offset Forge row IS the seat. Captured 2026-08-30
                // (Editor.log AttachOffHandProp MEASURED + WO-994 seatWrite): the previous
                // "GEOMETRY-VERTICAL + saved DELTA" path still ran three extra writers, so the
                // Inspector never showed the authored numbers:
                //   1. NormalizeInto rewrote the MESH child — scale 0.45/0.63=0.714, euler
                //      (0,270,0) which is Inspector (0,-90,0). That is the object the owner had
                //      selected (Fantasy_shield_Runtime Off Hand), not the grip root.
                //   2. vis.gripPos (-0.05,0,0) was ADDED → grip lPos=(-0.05, 0.33, 0.33) not
                //      the authored (0, 0.33, 0.33).
                //   3. ApplyGlobalWeaponYaw (+180 Y) still ran because derivation is skipped
                //      on fullOverride — grip euler (0,120,250) became (0,300,110).
                // Authored numbers live on the GRIP ROOT (EquipmentProp_OffHand under
                // Socket_Shield). The inner mesh stays at identity so it does not double-scale
                // or double-rotate under that row.
                FlowTrace.Step("Equip",
                    $"off-hand seat: FULL OVERRIDE exact pos={fo.pos} rot={fo.eulerRot} scale={fo.scale:0.###} " +
                    $"vis.gripPos={vis.gripPos} heldLen={heldLen:0.###} — NormalizeInto WITHHELD, " +
                    "vis.gripPos NOT added, ApplyGlobalWeaponYaw WITHHELD. Select EquipmentProp_OffHand " +
                    "(grip root) in the Inspector to see these numbers; the inner mesh is identity.");
                using (FlowTrace.Enter("Equip", "fullOverride parent prop identity under grip"))
                {
                    TraceOffHandTrs("fullOverride IN (prop before parent)", prop.transform);
                    prop.transform.SetParent(gripRoot.transform, false);
                    prop.transform.localPosition = Vector3.zero;
                    prop.transform.localRotation = Quaternion.identity;
                    prop.transform.localScale = Vector3.one;
                    TraceOffHandTrs("fullOverride after prop identity", gripRoot.transform);
                }
                using (FlowTrace.Enter("Equip", "fullOverride write authored TRS on grip"))
                {
                    gripRoot.transform.SetParent(hand, false);
                    gripRoot.transform.localPosition = fo.pos;
                    gripRoot.transform.localRotation = Quaternion.Euler(fo.eulerRot);
                    gripRoot.transform.localScale    = Vector3.one * (fo.scale > 0f ? fo.scale : 1f);
                    TraceOffHandTrs("fullOverride after authored write", gripRoot.transform);
                }
                _offHandAuthoredScale = fo.scale > 0f ? fo.scale : 1f;
                _offHandParentCompensate = false;   // owner dialed fo.scale by eye under this bone — don't re-solve it
            }
            // NATIVE Blink shield: trust the authored grip-at-origin + orientation (scale-only), and
            // seat dead-centre in the hand (zero gripPos/euler) like the bow's proven off-hand seat —
            // the foreign-pivot + (-0.05) offset of the legacy path is exactly what made the shield
            // dangle beside the forearm. Tripo/Resources shields keep the bounds-normalize + preset
            // grip (their pivot is unknown, so normalize centres them deterministically).
            else if (vis.native)
            {
                using (FlowTrace.Enter("Equip", "native seat (scale-only)"))
                {
                    FlowTrace.Step("Equip", "off-hand seat: NATIVE (trust authored grip-at-origin, scale-only)");
                    TraceOffHandTrs("native IN", prop.transform);
                    SeatNative(prop, gripRoot.transform, heldLen);
                    gripRoot.transform.SetParent(hand, false);
                    _offHandParentCompensate = true;
                    _offHandAuthoredScale = 1f;   // the nudge block below records fo.scale if present
                    CompensateParentScale(gripRoot.transform, 1f,
                        SeatSubject("off-hand", id, offsetKey), ref _offHandCompState);
                    gripRoot.transform.localPosition = Vector3.zero;
                    gripRoot.transform.localRotation = Quaternion.identity;
                    TraceOffHandTrs("native OUT", gripRoot.transform);
                }
            }
            else
            {
                using (FlowTrace.Enter("Equip", "NormalizeInto + preset grip"))
                {
                    FlowTrace.Step("Equip", "off-hand seat: NormalizeInto + preset grip (Tripo/Resources shield)");
                    TraceOffHandTrs("geometry IN", prop.transform);
                    NormalizeInto(prop, gripRoot.transform, heldLen, resolveHilt: false);
                    gripRoot.transform.SetParent(hand, false);
                    _offHandParentCompensate = true;
                    _offHandAuthoredScale = 1f;   // the nudge block below records fo.scale if present
                    CompensateParentScale(gripRoot.transform, 1f,
                        SeatSubject("off-hand", id, offsetKey), ref _offHandCompState);
                    gripRoot.transform.localPosition = vis.gripPos;
                    gripRoot.transform.localRotation = Quaternion.Euler(vis.gripEuler);
                    TraceOffHandTrs("geometry OUT", gripRoot.transform);
                }
            }
            EnsureWeaponRenderersVisible(gripRoot, hand, id);
            TraceOffHandTrs("after initial seat", gripRoot.transform);

            // ── WO-1123: DERIVED SHIELD SEAT (owner spec 2026-08-19) ─────────────────────────
            // WHAT THIS REPLACES: the drawn shield above is the preset euler on a hand bone —
            // for the LIVE default shield (knight_shield_starter -> ShieldWithItemLogic, which has
            // no authored row in either pose) that resolves to IDENTITY ∘ the 180° global yaw. No
            // derivation of any kind, which is the exact construct ARCHITECTURE_PRINCIPLES §4 bans.
            //
            // The owner's rule, verbatim: "the thinness/thickness of the shield is facing away from
            // the player ... with the handle where the hand mounts on the off-player's hand."
            // WeaponOrientHelper measures which extent IS the thickness and which face carries the
            // handle, and builds the pose in WORLD off the body's own axes — the same construction
            // ComputeSheathRotation and ComputeBowHeldRotation already use.
            //
            // PRECEDENCE (WeaponOrientHelper.ResolveSource): an authored Offset Forge row outranks
            // this, then `manual: true` on the catalog row, then this derivation, then the preset
            // constant — which is KEPT above, not deleted, and still runs whenever the geometry
            // cannot answer (§12: fallbacks are never stripped).
            //
            // GLOBAL YAW IS WITHHELD from a derived seat, exactly as the bow's derived path
            // withholds it (:1181-1188): the 180° flip exists to correct grips that INHERITED the
            // raw bone axes, and composing it onto a fully-derived world target spins the shield's
            // face to point back at the player — the very defect this fixes.
            bool offHandDerivedSeat = false;
            // ── WO-1215: SUBSTANTIATE `manual` BEFORE IT IS ALLOWED TO VETO ────────────────────
            // The precedence ladder is unchanged and correct; what was wrong is the value handed to
            // its `manual` input. 18 of the 19 shield rows in weapons.json are `generated:true` +
            // `manual:true` with NO row in offsets.json — a claim of a hand-dialled seat with no
            // hand-dialled seat behind it (stamped wholesale by the WO-500 balance pass, commit
            // af96fe788). Honouring it vetoed the derivation and left the prop on the NATIVE
            // addressable path's identity rotation: the flat slab through the hero's chest in
            // tmp/shield-seat-101829.png. See WeaponOrientHelper.ManualSeatIsSubstantiated for the
            // full measurement. `tripo_shield_a` is generated+manual AND has the authored `shield_A`
            // row, so it stays substantiated — and it is fullOverride besides, so it never reaches
            // this block at all. Nothing an owner dialled changes.
            bool rawOffHandManual = IsManualOrientRow(id);
            bool offHandRowGenerated = IsGeneratedCatalogRow(id);
            _currentOffHandManual = WeaponOrientHelper.ManualSeatIsSubstantiated(
                rawOffHandManual, offHandRowGenerated, hasOffset);
            if (rawOffHandManual && !_currentOffHandManual)
                FlowTrace.Step("Equip",
                    $"off-hand '{id}' key='{offsetKey}': catalog manual:true DEMOTED (WO-1215) — the " +
                    $"row is generated:true and AttachmentOffsetRegistry has no authored seat for " +
                    $"'{offsetKey}' or '{id}', so the flag names a correction that does not exist. " +
                    "It no longer vetoes the derived seat. If this shield SHOULD be hand-dialled, " +
                    "dial it in the Seating Editor — that writes the offsets.json row this test " +
                    "looks for, and the flag becomes true protection again.");
            else if (rawOffHandManual)
                FlowTrace.Step("Equip",
                    $"off-hand '{id}' key='{offsetKey}': catalog manual:true SUBSTANTIATED " +
                    $"(generated={offHandRowGenerated} authoredSeat={hasOffset}) — CANON, the derived " +
                    "pass leaves this row exactly as loaded.");
            _currentOffHandShieldFrame = default;
            // ── WO-1616: THE SEAT ITSELF NOW LIVES IN ONE PLACE ──────────────────────────────────
            // The measure-then-decide steps that used to be typed out here are unchanged; they were
            // MOVED into `SeatShieldMountRotation` (see its header) so the raid NPC path executes the
            // same instructions instead of its own hard-coded triple. Everything hero-specific —
            // the precedence inputs, the trace strings that name `vis`/`offsetKey`, the nudge and the
            // yaw decision below — deliberately stays here.
            if (vis.kind == WeaponClass.Shield)
            {
                bool shieldMayDerive = !fullOverride &&
                                       WeaponOrientHelper.MayDerive(hasOffset, _currentOffHandManual);
                ShieldSeat shieldSeat = SeatShieldMountRotation(
                    prop, gripRoot.transform, hand, _animator, transform, shieldMayDerive, id);
                _currentOffHandShieldFrame = shieldSeat.Frame;
                offHandDerivedSeat = shieldSeat.Derived;
                if (offHandDerivedSeat)
                {
                    FlowTrace.Step("Equip",
                        $"off-hand seat DERIVED (WO-1123) for '{id}' key='{offsetKey}': thickness -> " +
                        $"outboard left/forward-left, handle -> inward. presetEuler={vis.gripEuler} was " +
                        $"SUPERSEDED by derivedEuler={shieldSeat.MountLocal.eulerAngles:0.#}; global yaw WITHHELD.");
                }
                else
                {
                    // §12 / §1.4b: the un-derived shield must be distinguishable from the derived one
                    // in a capture — otherwise "the shield is wrong" cannot be split into "the
                    // derivation ran and is wrong" vs "the derivation never ran".
                    FlowTrace.Step("Equip",
                        $"off-hand seat NOT derived for '{id}' key='{offsetKey}': source=" +
                        $"{WeaponOrientHelper.ResolveSource(hasOffset, _currentOffHandManual, canDerive: true)} " +
                        $"(authoredRow={hasOffset} manual={_currentOffHandManual} native={vis.native} " +
                        $"fullOverride={fullOverride} rule={shieldSeat.Rule}) — keeping the preset " +
                        $"euler {vis.gripEuler}.");
                }
            }

            // OFFSET FORGE NUDGE (mirror main-hand): compose onto the seated frame, then global Y.
            if (!fullOverride && hasOffset)
            {
                bool nudged = fo.pos != Vector3.zero || fo.eulerRot != Vector3.zero ||
                              (fo.scale > 0f && Mathf.Abs(fo.scale - 1f) > 1e-4f);
                gripRoot.transform.localPosition += fo.pos;
                gripRoot.transform.localRotation =
                    gripRoot.transform.localRotation * Quaternion.Euler(fo.eulerRot);
                if (fo.scale > 0f && Mathf.Abs(fo.scale - 1f) > 1e-4f)
                {
                    // Record the authored multiplier so ApplyHoldPose's re-parent compensate
                    // re-composes comp * authored instead of wiping it (scale-parity 2026-07-07).
                    _offHandAuthoredScale = fo.scale;
                    gripRoot.transform.localScale = gripRoot.transform.localScale * fo.scale;
                }
                FlowTrace.Step("Offset", nudged
                    ? $"off-hand NUDGE '{offsetKey}' on geometry: +pos={fo.pos} *rot={fo.eulerRot} *scale={fo.scale:0.###}"
                    : $"off-hand offset '{offsetKey}' is all-zero — pure geometry (no nudge).");
            }

            // WO-1123: WITHHELD on a derived seat (bow precedent, :1181-1188) — the flip corrects
            // bone-inherited grips, and a derived world target has not inherited anything.
            // fullOverride: WITHHELD as well — captured 2026-08-30, the +180 Y turned authored
            // (0,120,250) into gripLocalEuler (0,300,110). The row already IS the seat.
            if (fullOverride)
                FlowTrace.Step("Equip",
                    $"off-hand '{id}': ApplyGlobalWeaponYaw WITHHELD (fullOverride). Authored euler " +
                    $"{fo.eulerRot} stays on the grip root.");
            else if (!offHandDerivedSeat)
                gripRoot.transform.localRotation = ApplyGlobalWeaponYaw(gripRoot.transform.localRotation);
            else
                FlowTrace.Step("Equip",
                    $"off-hand '{id}': ApplyGlobalWeaponYaw WITHHELD (derived world seat). Composing " +
                    "the 180 deg flip onto a derived target would face the shield's smooth side at the player.");
            TraceOffHandTrs("after yaw-or-withhold", gripRoot.transform);

            // Socket is the bone midpoint. Centre the heater, then shift it OUT along
            // the opening perpendicular so the plate sits ON the arm. The handle/opening
            // is the only volume allowed to intersect the bone — not the plate, not the torso.

            if (vis.kind == WeaponClass.Shield && !fullOverride && !_currentOffHandManual)
            {
                using (FlowTrace.Enter("Equip", "snap/off-bone (not fullOverride)"))
                {
                    TraceOffHandTrs("snap IN", gripRoot.transform);
                    // WO-1616: these four steps moved into SeatShieldPlateOnSocket so the raid NPC
                    // plate is pushed off the bone by the same instructions, not a second copy.
                    SeatShieldPlateOnSocket(gripRoot.transform, hand, _animator, transform,
                                            _currentOffHandShieldFrame, _currentOffHandMeshKey,
                                            $"(arm={_sheatheSocketOffIsArm})");
                    TraceOffHandTrs("snap OUT", gripRoot.transform);
                }
            }
            else if (fullOverride)
            {
                FlowTrace.Step("Equip",
                    $"off-hand '{id}': snap/off-bone WITHHELD (fullOverride) — grip stays at authored " +
                    $"lPos={gripRoot.transform.localPosition:0.###} lEuler={gripRoot.transform.localEulerAngles:0.#} " +
                    $"lScale={gripRoot.transform.localScale:0.###}");
            }

            _currentOffHandProp = gripRoot;
            // Capture off-hand attach inputs for the in-game Seating Editor (WO-577).
            _currentOffHandMeshKey    = offsetKey;
            _currentOffHandHeldLength = heldLen;
            _currentOffHandGripPos    = vis.gripPos;
            _currentOffHandGripEuler  = vis.gripEuler;
            _currentOffHandNative     = vis.native;
            // WO-1123: the sheathed pose needs the same three facts the drawn seat just used.
            _currentOffHandKind       = vis.kind;
            _currentOffHandDerivable  = !fullOverride && vis.kind == WeaponClass.Shield &&
                                        WeaponOrientHelper.MayDerive(hasOffset, _currentOffHandManual);
            // ⚠ The SHEATHED flag does NOT read `hasOffset` (owner ruling 2026-08-20). `hasOffset`
            // is the DRAWN row, and letting it speak here is what left the hip-hung shield posed by
            // a retired back-carry constant. A fullOverride row still wins (it owns the transform
            // outright) and `manual: true` is still canon; the sheathed row itself is checked per
            // pose in ComputeSheathedOffHandRotation, where it belongs.
            _currentOffHandSheathDerivable = !fullOverride && vis.kind == WeaponClass.Shield &&
                                             !_currentOffHandManual;
            FlowTrace.Step("Equip",
                $"off-hand SHEATHE derivability for '{id}' key='{offsetKey}': " +
                $"sheathDerivable={_currentOffHandSheathDerivable} (drawnDerivable=" +
                $"{_currentOffHandDerivable} authoredDrawnRow={hasOffset} manual={_currentOffHandManual} " +
                $"fullOverride={fullOverride}) frameValid={_currentOffHandShieldFrame.Valid} — the two " +
                "flags differing is EXPECTED and is the 2026-08-20 fix, not a bug.");

            // RENDER-VERIFY + DETACH-ON-FAIL (TGVRU): the shield can attach but be invisible (no
            // enabled renderer / no mesh) or seated on the wrong bone. PROVE it renders + is parented
            // under the resolved off hand (LeftHand) BEFORE leaving it on the hero; on fail destroy
            // it + clear the slot so no stray/invisible shield is left behind. Self-reports the why.
            if (!VerifyWeaponRendersNow(gripRoot, hand, id))
            {
                FlowTrace.Fail("Equip",
                    $"AttachOffHandProp: render-verify failed for off-hand '{id}' (no visible renderer or not parented to '{hand.name}') — detaching.");
                if (_currentOffHandProp == gripRoot) _currentOffHandProp = null;
                if (gripRoot != null) Destroy(gripRoot);
                return;
            }
            // Record the attach target. A strapped shield keeps this parent AND this local
            // pose in town and in combat (owner: sheathed and active stay in the same position).
            _offHandHand = hand;
            _offHandDrawnLocalPos = gripRoot.transform.localPosition;
            _offHandDrawnLocalRot = gripRoot.transform.localRotation;
            FlowTrace.Step("Equip",
                $"off-hand captured drawn locals pos={_offHandDrawnLocalPos:0.###} " +
                $"euler={_offHandDrawnLocalRot.eulerAngles:0.#} scale={gripRoot.transform.localScale:0.###} " +
                $"(ApplyHoldPose restamps these every frame onto the grip root, not the mesh child)");
            using (FlowTrace.Enter("Equip", "ApplyHoldPose after attach"))
            {
                TraceOffHandTrs("hold IN", gripRoot.transform);
                ApplyHoldPose();
                TraceOffHandTrs("hold OUT", gripRoot.transform);
            }

            // WO-994 C: measured world pose AFTER ApplyHoldPose (the hollow pre-pose line only
            // echoed offsets.json and ran before the carry-state re-parent).
            Transform propChild = gripRoot.transform.childCount > 0 ? gripRoot.transform.GetChild(0) : null;
            var rend = gripRoot.GetComponentInChildren<Renderer>();
            Bounds wb = rend != null ? rend.bounds : new Bounds(gripRoot.transform.position, Vector3.zero);
            string parentName = gripRoot.transform.parent != null ? gripRoot.transform.parent.name : "<null>";
            bool drawnNow = _combatActive && !(_seatingEditActive && _seatEditSheathed);
            // WO-1215 (reopened 2026-09-03): the owner's bounce carried no shield id and no hero.
            // Every capture searched (logs/device/freeze-20260904-095249.log, full-buffer-094110.log,
            // pull-20260830/boot-logcat.txt) either has ZERO AttachOffHandProp lines or names only
            // knight_shield_starter — so "which hero was wearing which shield" has never once been
            // recoverable from a log. This line already carried the POSE; it did not carry the
            // WEARER or the seat KEY, which is exactly the pair the RCA needed and could not get.
            // §12: instrument the missing facet rather than re-theorise the seat math.
            string wearerClassForTrace = _loadout != null ? _loadout.WearerClass : "<no-loadout>";
            Transform bodyForTrace = transform.Find("HeroBody");
            FlowTrace.Step("Equip",
                $"AttachOffHandProp MEASURED after hold: id='{id}' key='{offsetKey}' " +
                $"class='{wearerClassForTrace}' " +
                $"body='{(bodyForTrace != null ? bodyForTrace.name : "<none>")}' " +
                $"hand='{hand.name}' authoredRow={hasOffset} " +
                $"parent='{parentName}' " +
                $"state={(drawnNow ? "DRAWN" : "SHEATHED")} fullOverride={fullOverride} " +
                $"derived={offHandDerivedSeat} comp={_offHandParentCompensate} " +
                $"GRIP {FormatTrs(gripRoot.transform)} " +
                $"CHILD {(propChild != null ? FormatTrs(propChild) : "n/a")} " +
                $"worldBounds=c{wb.center} s{wb.size}");
        }

        private void DestroyCurrentOffHand()
        {
            if (_currentOffHandProp != null)
            {
                Destroy(_currentOffHandProp);
                _currentOffHandProp = null;
            }
            _offHandHand = null;   // drop the resolved draw target so a stale (old-body) hand is never reused
            // WO-1123: the measured shield frame belongs to the prop that just died. Clearing it is
            // not tidiness — a stale frame would pose the NEXT shield off the PREVIOUS shield's
            // handle side, which is a wrong pose that looks deliberate.
            _currentOffHandShieldFrame = default;
            _currentOffHandDerivable = false;
            _currentOffHandSheathDerivable = false;
            _currentOffHandManual = false;
            for (int i = 0; i < _offHandRuntimeMeshes.Count; i++)
                if (_offHandRuntimeMeshes[i] != null) Destroy(_offHandRuntimeMeshes[i]);
            for (int i = 0; i < _offHandRuntimeMaterials.Count; i++)
                if (_offHandRuntimeMaterials[i] != null) Destroy(_offHandRuntimeMaterials[i]);
            _offHandRuntimeMeshes.Clear();
            _offHandRuntimeMaterials.Clear();
        }

        /// <summary>Detach an instantiated Addressable shield from bundle-owned render assets.</summary>
        private int PreserveOffHandRenderAssets(GameObject prop, string id)
        {
            if (prop == null) return 0;
            int meshes = 0, materials = 0;
            foreach (var mf in prop.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                Mesh owned = Instantiate(mf.sharedMesh);
                owned.name = mf.sharedMesh.name + "_RuntimeOffHand";
                mf.sharedMesh = owned;
                _offHandRuntimeMeshes.Add(owned);
                meshes++;
            }
            foreach (var smr in prop.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null || smr.sharedMesh == null) continue;
                Mesh owned = Instantiate(smr.sharedMesh);
                owned.name = smr.sharedMesh.name + "_RuntimeOffHand";
                smr.sharedMesh = owned;
                _offHandRuntimeMeshes.Add(owned);
                meshes++;
            }
            foreach (var renderer in prop.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                Material[] source = renderer.sharedMaterials;
                if (source == null || source.Length == 0) continue;
                var owned = new Material[source.Length];
                bool changed = false;
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] == null) continue; // MagentaGuard owns null-slot recovery.
                    owned[i] = Instantiate(source[i]);
                    owned[i].name = source[i].name + "_RuntimeOffHand";
                    _offHandRuntimeMaterials.Add(owned[i]);
                    materials++;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = owned;
            }
            FlowTrace.Step("Equip", $"off-hand render assets PRESERVED for '{id}': " +
                $"runtimeMeshes={meshes} runtimeMaterials={materials}. Addressables release/bundle " +
                "eviction can no longer leave an enabled renderer with sharedMesh=null.");
            return meshes;
        }

        // ── Hold state: idle (lowered) ↔ combat (drawn/raised) ───────────────────────
        /// <summary>
        /// Authoritative hold-state driver. Call from HeroLocomotion / a combat-state
        /// registry with the SAME `engaged` flag that feeds ActorAnimator.SetCombatStance,
        /// e.g. <c>GetComponent&lt;EquipmentController&gt;()?.SetCombatActive(engaged);</c>.
        /// Once called, the auto WaveManager-mirror fallback turns off (the caller owns it).
        /// false = sword lowered at the side; true = sword drawn/ready.
        /// </summary>
        public void SetCombatActive(bool active)
        {
            _combatExplicit = true;
            if (_combatActive == active && _gripRoot != null) { ApplyHoldPose(); return; }
            _combatActive = active;
            ApplyHoldPose();
        }

        /// <summary>Current hold state (false = idle/lowered, true = combat/ready).</summary>
        public bool CombatActive => _combatActive;

        /// <summary>
        /// True while the weapon prop is DRAWN to the hand - the SAME predicate ApplyHoldPose
        /// seats the props by (a live SHEATHED Seating-Editor edit pins the props to the hips,
        /// so it reads as not-drawn even mid-combat). WO-959 (owner ruling 2026-08-10): element
        /// weapon auras (GearAura) render ONLY while this is true.
        /// </summary>
        public bool IsWeaponDrawn => _combatActive && !(_seatingEditActive && _seatEditSheathed);

        /// <summary>
        /// Raised ONCE per carry-state FLIP (true = drawn to the hand, false = sheathed on the
        /// back socket), AFTER ApplyHoldPose has re-seated the props - so a subscriber that
        /// measures the weapon prop (GearAura's blade solve, WO-959) sees it already parented at
        /// its new seat. Never raised on the per-frame no-change re-assert (HeroLocomotion calls
        /// SetCombatActive every frame; see CompensateParentScale's throttle note).
        /// </summary>
        public event System.Action<bool> OnCarryStateChanged;

        // Last drawn value notified. Default false matches _combatActive's default - a fresh
        // hero is sheathed, so the first ApplyHoldPose notifies only if it actually draws.
        private bool _lastNotifiedDrawn;

        // Auto-mirror fallback: if no caller drives SetCombatActive, derive the combat
        // hold the same way HeroLocomotion does — the blade rides ready ONLY while a wave is
        // genuinely live (Countdown/Active), not merely because a WaveManager exists in the
        // scene. The hub/town keeps an idle WaveManager, so presence alone must NOT draw the
        // weapon, or the hero holds it combat-ready in town. Cheap poll; the pose only
        // re-applies on a state change.
        private void Update()
        {
            // BUG 1: keep retrying the equip until the loadout subscribed + the Humanoid rig is
            // ready and the weapon/off-hand prop is actually up (companion attach-after-rebind).
            LateAttachRetry();

            // ARMOR TINT (WO-567): re-apply once the body renderers come online if an early
            // SetArmorTier landed before the HeroBody existed (cheap; clears the flag on success).
            if (_armorTintDirty) ApplyArmorTint();

            // While the in-game Seating Editor drives the grip root, the auto idle/combat hold
            // must not stomp the previewed pose (WO-577).
            if (_seatingEditActive) return;
            if (_combatExplicit || _gripRoot == null) return;
            if (_waveManager == null) _waveManager = Object.FindAnyObjectByType<WaveManager>();
            // CANONICAL in-combat signal — MUST match HeroLocomotion.IsWaveInCombat (BattleLock +
            // wave Active + imminent Countdown only). The old mirror treated ANY Countdown as drawn,
            // leaving the sword out for minutes while the animator read calm town idle.
            bool active = IsHeroCombatEngaged(_waveManager);
            if (active != _combatActive)
            {
                _combatActive = active;
                ApplyHoldPose();
            }
        }

        /// <summary>Mirror of <c>HeroLocomotion.IsWaveInCombat</c> — BattleLock (arena / in-scene
        /// duel) OR wave Active OR Countdown in its final imminent window only.</summary>
        private static bool IsHeroCombatEngaged(WaveManager wm)
        {
            if (DeNelle.Core.Combat.BattleLock.IsInBattle()) return true;
            if (wm == null) return false;
            if (wm.Phase == WavePhase.Active) return true;
            if (wm.Phase == WavePhase.Countdown && wm.CountdownRemaining <= 5f) return true;
            return false;
        }

        // CARRY STATE (owner design 2026-07-04): place each prop by combat state — DRAWN to the
        // hand in combat (the SAME seat battle uses, pure _baseGripRot), SHEATHED on the back socket
        // out of combat. This is the fix for the ~60° overworld float: the in-hand grip only ever
        // renders in combat (where it seats right); out of combat there is no hand grip to look wrong,
        // and the retired IdleHoldOffsetEuler tilt (the single context-dependent rotation) is gone.
        // Runs on every combat-state change (Update auto-mirror / SetCombatActive) and once per attach.
        // ── WO-994 SEAT DRIFT TRIPWIRE (owner 2026-08-16: "the offset sets it correctly —
        // look at the data at each point and compare"). Every WRITE of the off-hand seat
        // records a snapshot; a write whose numbers differ beyond tolerance from the LAST
        // recorded write logs BOTH old and new (the port bug = the same path writing
        // different numbers after the port, or the parent/bone scale changing under
        // unchanged numbers). A checkpoint (scene load) that finds the live transform
        // differing from the last recorded write names an UNLOGGED writer — that is a
        // Fail (break-log + screenshot). Change-only: silent while numbers repeat, so the
        // per-frame ApplyHoldPose no-change path costs two compares and no allocation.
        private bool       _offSeatHas;
        private string     _offSeatWriter;
        private Vector3    _offSeatPos;
        private Quaternion _offSeatRot;
        private Vector3    _offSeatScale;
        private Transform  _offSeatParentT;
        private Vector3    _offSeatParentLossy;

        private const float SeatPosTolM    = 0.01f;  // 1 cm
        private const float SeatAngTolDeg  = 0.5f;
        private const float SeatScaleTolFr = 0.01f;  // 1%

        /// <summary>
        /// ApplyHoldPose restamps captured attach locals EVERY FRAME via SetCombatActive
        /// (HeroLocomotion.Update). Owner-locked 2026-08-30: the ShieldWithItemLogic row
        /// IS the seat. Restamp pos/rot (and fullOverride scale) so a live gizmo or a
        /// Refresh cannot drift it. STOMP logs when LIVE disagreed before the write.
        /// </summary>
        private void WriteOffHandDrawnOrKeepLive(string writer, Transform offT)
        {
            bool wouldStomp =
                Vector3.Distance(offT.localPosition, _offHandDrawnLocalPos) > SeatPosTolM
                || Quaternion.Angle(offT.localRotation, _offHandDrawnLocalRot) > SeatAngTolDeg;
            if (wouldStomp)
            {
                string line =
                    $"STOMP {writer} key='{_currentOffHandMeshKey}' " +
                    $"LIVE lPos={offT.localPosition:0.###} lEuler={offT.localEulerAngles:0.#} lScale={offT.localScale:0.###} " +
                    $"WRITE lPos={_offHandDrawnLocalPos:0.###} lEuler={_offHandDrawnLocalRot.eulerAngles:0.#} " +
                    $"parent='{(offT.parent != null ? offT.parent.name : "<null>")}' " +
                    "LOCKED restamp of attach-captured Offset Forge row.";
                FlowTrace.Once("Equip", "stomp-first-" + writer, line);
                FlowTrace.Throttle("Equip", "stomp-" + writer + "-" + (_currentOffHandMeshKey ?? "?"), 1f, line);
            }

            offT.localPosition = _offHandDrawnLocalPos;
            offT.localRotation = _offHandDrawnLocalRot;
            if (!_offHandParentCompensate && _offHandAuthoredScale > 0f)
                offT.localScale = Vector3.one * _offHandAuthoredScale;
        }

        private void RecordOffHandSeatWrite(string writer)
        {
            if (_currentOffHandProp == null) return;
            var t = _currentOffHandProp.transform;
            Vector3 pLossy = t.parent != null ? t.parent.lossyScale : Vector3.one;
            bool moved = !_offSeatHas
                || Vector3.Distance(t.localPosition, _offSeatPos) > SeatPosTolM
                || Quaternion.Angle(t.localRotation, _offSeatRot) > SeatAngTolDeg
                || (t.localScale - _offSeatScale).magnitude
                   > SeatScaleTolFr * Mathf.Max(_offSeatScale.magnitude, 1e-4f)
                || !ReferenceEquals(t.parent, _offSeatParentT)
                || (pLossy - _offSeatParentLossy).magnitude
                   > SeatScaleTolFr * Mathf.Max(_offSeatParentLossy.magnitude, 1e-4f);
            if (moved)
            {
                bool sameWriter = _offSeatHas && _offSeatWriter == writer;
                string parentNow  = t.parent != null ? t.parent.name : "<null>";
                string parentPrev = _offSeatParentT != null ? _offSeatParentT.name : "<null>";
                Transform child = t.childCount > 0 ? t.GetChild(0) : null;
                string line =
                    $"WO-994 seatWrite by={writer} key='{_currentOffHandMeshKey}' " +
                    $"GRIP pos={t.localPosition} rot={t.localEulerAngles} scale={t.localScale} " +
                    $"parent='{parentNow}' parentLossy={pLossy}" +
                    (child != null
                        ? $" CHILD '{child.name}' pos={child.localPosition} rot={child.localEulerAngles} scale={child.localScale}"
                        : " CHILD=<none>") +
                    (sameWriter
                        ? $" PREV pos={_offSeatPos} rot={_offSeatRot.eulerAngles} scale={_offSeatScale} " +
                          $"parent='{parentPrev}' parentLossy={_offSeatParentLossy}"
                        : $" (prev writer={(_offSeatHas ? _offSeatWriter : "<none>")})");

                // ⚠ "CHANGE-ONLY" IS NOT THE SAME AS "RARE", AND THIS LINE PROVED IT ON A SHIPPED
                // DEVICE BUILD (owner capture 2026-08-21). The comment above this method reasons that
                // the tripwire is "silent while numbers repeat" — true, and the numbers DO NOT repeat:
                // ApplyOffHandCentreOnSocket derives its shift from Renderer.bounds, a WORLD AABB,
                // which changes shape as the hero turns and animates. So the seat legitimately
                // re-solves a few millimetres different EVERY FRAME, `moved` is true every frame, and
                // the capture is dozens of identical-shaped lines with the position walking
                // 0.03 -> 0.06. The F8 harness harvests only the last ~60 signal lines, so this one
                // diagnostic evicted the evidence window for every other system — the exact failure
                // ApplySheathedOffset and CompensateParentScale in this same file were both already
                // fixed for, and the known repo trap where a post-hoc `adb logcat -d` reads as "the
                // feature never ran".
                //
                // ⛔ IT IS THROTTLED, NOT REMOVED (§12 forbids stripping instrumentation) — AND THE
                // STRUCTURAL HALF IS NOT THROTTLED AT ALL. A change of PARENT or of WRITER is the
                // signal this tripwire exists for (it is how "the shield is on the hip" was proven);
                // that is rare by nature and goes out immediately. Only the numeric drift under an
                // unchanged parent+writer — the part that is per-frame by construction — is rate
                // limited, keyed by mesh+writer so a different prop or a different writer still
                // prints promptly instead of sharing one bucket (the mistake that hid the shield's
                // compensate line behind the sword's for a whole capture).
                bool structural = !sameWriter || !ReferenceEquals(t.parent, _offSeatParentT);
                if (structural)
                    FlowTrace.Step("Equip", line);
                else
                    FlowTrace.Throttle("Equip",
                        $"seatwrite-{writer}-{_currentOffHandMeshKey ?? "?"}", 5f, line +
                        " [THROTTLED 1/5s: same writer, same parent — only the numbers moved. Sub-cm " +
                        "drift here is ApplyOffHandCentreOnSocket re-solving off a rotation-sensitive " +
                        "world AABB, not a second writer.]");
            }
            _offSeatHas = true; _offSeatWriter = writer;
            _offSeatPos = t.localPosition; _offSeatRot = t.localRotation; _offSeatScale = t.localScale;
            _offSeatParentT = t.parent; _offSeatParentLossy = pLossy;
        }

        /// <summary>WO-994: assert the off-hand still sits where the LAST logged write put it.
        /// Drift here = an UNLOGGED writer (or a context change under it) moved the seat.</summary>
        private void VerifyOffHandSeat(string checkpoint)
        {
            if (!_offSeatHas || _currentOffHandProp == null) return;
            var t = _currentOffHandProp.transform;
            Vector3 pLossy = t.parent != null ? t.parent.lossyScale : Vector3.one;
            bool drifted =
                   Vector3.Distance(t.localPosition, _offSeatPos) > SeatPosTolM
                || Quaternion.Angle(t.localRotation, _offSeatRot) > SeatAngTolDeg
                || !ReferenceEquals(t.parent, _offSeatParentT)
                || (pLossy - _offSeatParentLossy).magnitude
                   > SeatScaleTolFr * Mathf.Max(_offSeatParentLossy.magnitude, 1e-4f);
            string parentNow  = t.parent != null ? t.parent.name : "<null>";
            string parentPrev = _offSeatParentT != null ? _offSeatParentT.name : "<null>";
            if (drifted)
                FlowTrace.Fail("Equip",
                    $"WO-994 SEAT DRIFT at {checkpoint}: off-hand moved since last write by={_offSeatWriter}. " +
                    $"NOW pos={t.localPosition} rot={t.localEulerAngles} parent='{parentNow}' parentLossy={pLossy} " +
                    $"EXPECTED pos={_offSeatPos} rot={_offSeatRot.eulerAngles} parent='{parentPrev}' " +
                    $"parentLossy={_offSeatParentLossy}");
            else
                FlowTrace.Step("Equip",
                    $"WO-994 seatVerify {checkpoint} ok (last write by={_offSeatWriter} parent='{parentNow}')");
        }

        private void ApplyHoldPose()
        {
            // A live SHEATHED seating edit pins the props to the sheathe sockets even if combat
            // starts — the owner is dialing the sheathed pose (Update's auto-mirror is already
            // suspended while _seatingEditActive; this covers an explicit SetCombatActive caller
            // mid-edit).
            bool drawn = _combatActive && !(_seatingEditActive && _seatEditSheathed);
            // TWO sockets, opposite hips (owner ruling 2026-08-20). The single shared `back` local
            // this replaced is the reason the shield was buried: one transform, one origin, two
            // props. Resolving them separately means no future edit can accidentally re-share them.
            Transform sheatheMain = drawn ? null : ResolveSheatheSocket(offHand: false);
            Transform sheatheOff  = drawn ? null : ResolveSheatheSocket(offHand: true);

            // ── Main weapon ──
            if (_gripRoot != null)
            {
                if (!drawn && sheatheMain != null)
                {
                    _gripRoot.SetParent(sheatheMain, false);
                    // Sheathe-socket bones carry a different lossyScale than the hand — always
                    // compensate so the attach-authored multiplier (fo.scale) survives the
                    // carry-state re-parent.
                    CompensateParentScale(_gripRoot, _weaponAuthoredScale,
                        SeatSubject("main-hand", _currentWeaponId, _currentWeaponMeshKey),
                        ref _weaponCompState);
                    // Body-space → socket-local (owner ruling 2026-08-20). A raw bone-local vector
                    // here was authored against a chest bone, inherited by a spine one, and scaled
                    // by whatever lossyScale that bone happened to carry.
                    _gripRoot.localPosition = ComputeSheathLocalPosition(
                        sheatheMain, _sheatheWeaponLocalPos, SheatheSideMain);
                    // DERIVED sheathe rotation (the fix): build the base orientation from the body's
                    // own axes via the SAME LookRotation(flat, blade) construction the correct battle
                    // draw uses (ComputeMeleeGripRotation), then compose the persisted authored nudge —
                    // instead of the old hand-guessed magic euler that ignored the chest-bone axes.
                    // ── BOW EXCEPTION: SHEATHED AND DRAWN ARE THE SAME POSE ──────────────────
                    // Owner ruling 2026-08-16, verbatim: "both sheathed and drawn bow stay in this
                    // same pose". The branch below is entered ONLY for WeaponClass.Bow and is
                    // UNTOUCHED by the 2026-08-20 hip ruling — a bow keeps its own derived carry.
                    // ⚠ The sentence that stood here — "ComputeSheathRotation lays the prop
                    // DIAGONALLY up the spine with its flat against the back ... and is
                    // felt-approved" — is now FALSE for every melee family: that diagonal back
                    // carry is exactly what the owner rejected on 2026-08-20. Melee now hangs
                    // vertical and inverted at the hip. Only the ANCHOR moved for the bow (it is
                    // anchor-independent by construction, see below), so its pose is unchanged.
                    //
                    // A slung bow must instead answer the SAME four clauses the held bow does, so
                    // the same solver produces it: ComputeBowHeldRotation builds its target in
                    // WORLD from the body's axes and expresses it in the ANCHOR's local frame, and
                    // it reads nothing off the anchor but its rotation. Feeding it the sheathe
                    // socket instead of the hand therefore yields the IDENTICAL WORLD ORIENTATION —
                    // which is the ruling stated exactly. Only the anchor changes; the pose does
                    // not, and that is precisely why moving the anchor from spine to HIP on
                    // 2026-08-20 did not disturb the bow's felt-verified orientation. (Position
                    // still comes from _sheatheWeaponLocalPos above: the ruling is about the POSE.
                    // A slung bow now hangs at the hip like everything else.)
                    //
                    // MEASURED, so the change is not asserted: ComputeSheathRotation put the bow at
                    // limbTiltFromVertical = _sheatheBladeDiagonalDeg (28 deg, the diagonal in the
                    // owner's photo) and bellyOffAim = 180 deg EXACTLY, because worldFlat was
                    // -body.forward - the flat lay against the back, which for a bow means the
                    // STRING faces downrange and the curve faces the archer. So the old sheathed
                    // bow was wrong on BOTH axes, not just the tilt. After this line both read 0.
                    //
                    // _sheatheWeaponLocalEuler (the melee sheathe felt-nudge, currently zero) is
                    // deliberately NOT composed here - it lives inside ComputeSheathRotation and
                    // belongs to the melee hip carry. The bow's nudge seam is ApplySheathedOffset,
                    // one line below, exactly as before.
                    //
                    // WHY THIS WAS MISSED: a capture proved limbTiltFromVertical=0 for the HELD
                    // seat and the diagonal back-carry was then called correct by generalising that
                    // one measurement to both states. The sheathed transform was never in that
                    // trace. TraceBowSeatMeasured is now driven from HERE as well as from attach,
                    // so a capture prints both states and that generalisation cannot be made again.
                    _gripRoot.localRotation = _currentWeaponKind == WeaponClass.Bow
                        ? Guard.Try("Equip", "sheathed bow ComputeBowHeldRotation",
                            () => WeaponBoundsOrient.ComputeBowHeldRotation(
                                      sheatheMain, _animator != null ? _animator.transform : transform),
                            Quaternion.identity)
                        : ComputeSheathRotation(sheatheMain, SheatheSideMain);
                    // Sheathed pose: explicit "<meshKey>@sheathed" wins; else fall back to the drawn
                    // offset ("<meshKey>") as a nudge on this built-in sheathe pose (town carry fix).
                    ApplySheathedOffset(_gripRoot, _currentWeaponMeshKey);
                    // WO-1226: AFTER ApplySheathedOffset, never before. ComputeSheathRotation's own
                    // tiltFromVertical line measures the quaternion it is about to RETURN, and the
                    // line above can still compose more rotation onto the transform. This measures
                    // the prop that is actually on screen.
                    TraceSeatChain("SHEATHED", sheatheMain);
                    if (_currentWeaponKind == WeaponClass.Bow)
                        TraceBowSeatMeasured(_gripRoot, _weaponHand, _currentWeaponId, _bowSeatDerived);
                }
                else if (_weaponHand != null)
                {
                    // Drawn (or sheathed with no sheathe bone on this rig — never leave it floating unparented).
                    _gripRoot.SetParent(_weaponHand, false);
                    if (_weaponParentCompensate)
                        CompensateParentScale(_gripRoot, _weaponAuthoredScale,
                            SeatSubject("main-hand", _currentWeaponId, _currentWeaponMeshKey),
                            ref _weaponCompState);
                    _gripRoot.localPosition = _weaponDrawnLocalPos;
                    _gripRoot.localRotation = _baseGripRot;
                    // WO-1226: THE DRAWN BRANCH HAD NO SEAT MEASUREMENT AT ALL. Every "is it
                    // standing up?" number this file printed came from ComputeSheathRotation, which
                    // only ever runs on the SHEATHED branch - so the owner's report ("weapon combat
                    // still horizontal") was about the one carry state no trace covered, and the
                    // sheathed line's honest 0deg was read as though it answered it.
                    TraceSeatChain("DRAWN", _gripRoot.parent);
                    if (_currentWeaponKind == WeaponClass.Bow)
                        TraceBowSeatMeasured(_gripRoot, _weaponHand, _currentWeaponId, _bowSeatDerived);
                }
            }

            // ── Off-hand / shield ──
            if (_currentOffHandProp != null)
            {
                var offT = _currentOffHandProp.transform;
                // Strapped heater: ONE parent, ONE local pose, town and combat.
                // Re-parenting to LeftHand on draw is what waved the plate with the wrist.
                Transform forearmSocket = _currentOffHandKind == WeaponClass.Shield
                    ? GearSeat.ResolveMount(_animator, transform, WeaponArchetype.Shield).Mount
                    : null;
                if (forearmSocket != null)
                {
                    if (offT.parent != forearmSocket)
                        offT.SetParent(forearmSocket, false);
                    if (_offHandParentCompensate)
                        CompensateParentScale(offT, _offHandAuthoredScale,
                            SeatSubject("off-hand", _currentOffHandId, _currentOffHandMeshKey),
                            ref _offHandCompState);
                    WriteOffHandDrawnOrKeepLive("ApplyHoldPose.forearm-socket", offT);
                    if (_currentOffHandProp != null &&
                        !ReferenceEquals(_offHandSheatheShownOn, forearmSocket))
                    {
                        _offHandSheatheShownOn = forearmSocket;
                        EnsureWeaponRenderersVisible(_currentOffHandProp, forearmSocket,
                            _currentOffHandId ?? _currentOffHandMeshKey ?? "off-hand");
                    }
                    RecordOffHandSeatWrite("ApplyHoldPose.forearm-socket");
                }
                else if (!drawn && sheatheOff != null)
                {
                    // ⛔ NOTE THE PARENT: `sheatheOff`, NOT the weapon's socket. This line used to
                    // read `offT.SetParent(back, false)` with `back` being the ONE shared socket the
                    // sword had just taken — same transform, same origin — which is why the capture
                    // shows a shield with a real 0.72 x 0.92 x 0.72 m volume that the player never
                    // sees. It was inside the hero.
                    offT.SetParent(sheatheOff, false);
                    // WO-994 B: sheathed path used to compensate UNCONDITIONALLY while the drawn
                    // path respected _offHandParentCompensate (false for fullOverride shields).
                    // That made one prop render at two sizes (hand vs sheathed). Same guard both ways.
                    if (_offHandParentCompensate)
                        CompensateParentScale(offT, _offHandAuthoredScale,
                            SeatSubject("off-hand", _currentOffHandId, _currentOffHandMeshKey),
                            ref _offHandCompState);
                    // THE OFFSET FOLLOWS THE ANCHOR THAT WAS ACTUALLY RESOLVED (owner F8 2026-08-21).
                    // Arm mount → the small arm-hugging offset on the LEFT (off-hand) side; hip
                    // fallback → the original hip offset on the hip side. Pairing an arm anchor with
                    // the 0.26 m hip figure floats the shield a quarter-metre off the elbow, and
                    // pairing it with the +1 hip side puts it on the wrong side of the body — two
                    // ways to be "posed perfectly about a lie", which is this file's oldest lesson.
                    Vector3 offBaseLocal = ComputeSheathLocalPosition(
                        sheatheOff,
                        _sheatheSocketOffIsArm ? _armOffHandLocalPos : _sheatheOffHandLocalPos,
                        OffHandSheatheSide());
                    offT.localPosition = offBaseLocal;
                    // DE-BAND-AID NOTE (2026-07-07): _sheatheOffHandLocalEuler (the hand-tuned magic
                    // euler, owner Z+=180 correction 2026-07-04) is now only the DEFAULT under the
                    // @sheathed offset seam — an owner-authored "<meshKey>@sheathed" registry entry
                    // (Seating Editor, Sheathed mode) supersedes it below. Kept, not removed: with no
                    // entry this line is the exact shipped pose (zero regression).
                    // WO-1123: DERIVED when the geometry answers, else the shipped constant — one
                    // method so this pose and the Seating Editor preview can never disagree.
                    offT.localRotation = ComputeSheathedOffHandRotation(sheatheOff);
                    // ── CENTRE THE PLATE ON THE HIP, don't hang it BY ITS HANDLE ─────────────
                    // MEASURED, 2026-08-20 (Builds/KnightGearProof/): every angle above read
                    // perfect — "faceOffOutward=0deg longTiltFromVertical=0deg" — and the shield
                    // still floated at CHEST height, clear of the body, with grey backdrop visible
                    // between it and the torso. The angles were never the problem. The POSITION was,
                    // for a reason no euler can express:
                    //
                    //   fantasy_shield localBounds c=(0, 0.315, 0.035) s=(0.512, 0.63, 0.161)
                    //
                    // It is a NATIVE prop, so its origin is its GRIP — and its grip is at the plate's
                    // BOTTOM EDGE. Seating the origin at the hip therefore hangs the whole 0.63 m
                    // plate UPWARD from the hip, putting its centre ~0.39 m up: the chest. The
                    // capture's own numbers said it plainly, worldPos=(0.24, 0.85, -0.11) against
                    // worldBounds c=(0.29, 1.24, -0.12).
                    //
                    // Grip-at-origin is exactly RIGHT for the sword — hilt at the belt, blade
                    // hanging down the thigh IS the owner's "inverted" — which is why this shift is
                    // deliberately applied to the OFF HAND ONLY. A shield has no hilt to hang from;
                    // what belongs at the hip is the plate's middle. So: shift by the prop's own
                    // origin-to-rendered-centre vector, expressed in the socket's frame. Derived per
                    // rig from the live mesh every time, never a typed constant — a differently
                    // pivoted shield self-corrects instead of needing a new magic number.
                    ApplyOffHandCentreOnSocket(offT, sheatheOff, offBaseLocal);
                    // A centre seat is necessary for a grip-at-the-rim prefab, but it is not
                    // sufficient on an ARM socket: the socket lies inside the skinned arm.  Push
                    // LATERAL (away from the torso) by half-thickness + arm skin so the plate
                    // straps to the forearm.  Do NOT clear the whole hero world-AABB — that is
                    // axis-aligned and facing-dependent, and it floated the heater a metre off
                    // Grom's side (owner capture, town rear).  Hip fallback keeps the centre seat.
                    if (_sheatheSocketOffIsArm)
                        ApplyOffHandArmSurfaceSeat(offT, sheatheOff);
                    ApplySheathedOffset(offT, _currentOffHandMeshKey);
                    // PROD-019: ensure renderers once per sheathe parent — NOT every ApplyHoldPose
                    // frame (that spammed SHOW lines and blinded F8 harvests on Seeker).
                    if (_currentOffHandProp != null &&
                        !ReferenceEquals(_offHandSheatheShownOn, sheatheOff))
                    {
                        _offHandSheatheShownOn = sheatheOff;
                        EnsureWeaponRenderersVisible(_currentOffHandProp, sheatheOff,
                            _currentOffHandId ?? _currentOffHandMeshKey ?? "off-hand");
                    }
                    RecordOffHandSeatWrite("ApplyHoldPose.sheathed");   // WO-994 tripwire
                }
                else if (_offHandHand != null)
                {
                    _offHandSheatheShownOn = null;   // next sheathe must re-run SHOW
                    offT.SetParent(_offHandHand, false);
                    if (_offHandParentCompensate)
                        CompensateParentScale(offT, _offHandAuthoredScale,
                            SeatSubject("off-hand", _currentOffHandId, _currentOffHandMeshKey),
                            ref _offHandCompState);
                    WriteOffHandDrawnOrKeepLive("ApplyHoldPose.drawn", offT);
                    RecordOffHandSeatWrite("ApplyHoldPose.drawn");      // WO-994 tripwire
                }
            }

            // ── WO-959: notify carry-state subscribers the state FLIPPED (change-only - this
            // method re-runs every frame via SetCombatActive's no-change path, so an unguarded
            // invoke here would fire at frame rate). Raised AFTER the re-seat above so a
            // subscriber measuring the prop (GearAura) sees it at its new parent. Guarded: a
            // throwing subscriber must never break the equip/pose path it piggybacks on.
            if (drawn != _lastNotifiedDrawn)
            {
                _lastNotifiedDrawn = drawn;
                FlowTrace.Step("Equip", "carry state -> " + (drawn ? "DRAWN (hand)" : "SHEATHED (hip)") +
                    " on '" + name + "' - notifying carry-state subscribers (WO-959).");
                var handler = OnCarryStateChanged;
                if (handler != null)
                    Guard.Try("Equip", "OnCarryStateChanged(" + (drawn ? "drawn" : "sheathed") + ")",
                        () => handler(drawn));
            }

            // WO-1254 D-SEAT / PROD-019: classify the FINAL off-hand state only
            // after every ApplyHoldPose writer has run. This is an oracle, not a
            // seat: it writes no transform, material, renderer, scene, or camera.
            // Use the sanctioned target already resolved by this pose pass. In
            // legacy/fallback rigs SHEATHED may use sheatheOff while DRAWN uses
            // _offHandHand; comparing both states only to the hand would report
            // a healthy town carry as attach-fail.
            TraceOffHandSeatProof(drawn, drawn ? _offHandHand : (sheatheOff ?? _offHandHand));
        }

        /// <summary>Pure D-SEAT cause classifier shared by runtime and regression.
        /// `ok` proves structural renderability, not gameplay-camera readability.</summary>
        public static string ClassifyOffHandSeatProof(bool equipped, bool packageBaked,
            bool addressableFailed, bool propPresent, bool parentMatches,
            int activeMeshRenderers, bool nonZeroBounds)
        {
            if (!equipped) return "loadout-null";
            if (packageBaked) return "baked-skip";
            if (!propPresent || !parentMatches) return "attach-fail";
            if (activeMeshRenderers <= 0 || !nonZeroBounds) return "attached-invisible";
            if (addressableFailed) return "addr-fail";
            return "ok";
        }

        private void TraceOffHandSeatProof(bool drawn, Transform expectedParent)
        {
            string equippedId = _loadout != null && _loadout.EquippedOffHand != null
                ? _loadout.EquippedOffHand.id : _currentOffHandId;
            bool equipped = !string.IsNullOrEmpty(equippedId);
            int active = CountActiveMeshRenderers(_currentOffHandProp);
            bool nonZeroBounds = false;
            Bounds combined = default;
            bool haveBounds = false;
            if (_currentOffHandProp != null)
            {
                foreach (var r in _currentOffHandProp.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || MeshOf(r) == null)
                        continue;
                    if (!haveBounds) { combined = r.bounds; haveBounds = true; }
                    else combined.Encapsulate(r.bounds);
                }
                nonZeroBounds = haveBounds && combined.size.sqrMagnitude > 1e-8f;
            }
            bool parentMatches = _currentOffHandProp != null && expectedParent != null &&
                ReferenceEquals(_currentOffHandProp.transform.parent, expectedParent);
            string cause = ClassifyOffHandSeatProof(equipped, PackageBakedGear,
                _offHandAddressableFailed, _currentOffHandProp != null, parentMatches,
                active, nonZeroBounds);
            string scene = SceneManager.GetActiveScene().name;
            string state = drawn ? "DRAWN" : "SHEATHED";
            string parent = _currentOffHandProp != null && _currentOffHandProp.transform.parent != null
                ? _currentOffHandProp.transform.parent.name : "<null>";
            string signature = scene + "|" + state + "|" + equippedId + "|" + cause + "|" +
                parent + "|" + active + "|" + nonZeroBounds;
            if (signature == _lastOffHandSeatProofSignature) return;
            _lastOffHandSeatProofSignature = signature;
            string bounds = haveBounds ? combined.size.ToString("0.###") : "(0, 0, 0)";
            FlowTrace.Step("Equip",
                $"seat-proof scene='{scene}' state={state} offHand='{(equipped ? equippedId : "<null>")}' " +
                $"prop={(_currentOffHandProp != null)} parent='{parent}' expected='" +
                $"{(expectedParent != null ? expectedParent.name : "<null>")}' activeMeshRenderers={active} " +
                $"bounds={bounds} baked={PackageBakedGear} addressableFailed={_offHandAddressableFailed} " +
                $"CAUSE={cause}");
        }

        // How a sheathed registry entry was resolved (explicit @sheathed vs drawn-key fallback).
        private enum SheathedOffsetSource { None, Explicit, DrawnFallback }

        // Resolve the offset that should refine a sheathed (back-socket) pose:
        //   1) "<meshKey>@sheathed" — owner-authored back pose (Seating Editor, Sheathed mode).
        //   2) "<meshKey>" — FALLBACK: reuse the drawn offset as a nudge on the built-in back pose
        //      so offsets dialed/saved without the @sheathed suffix still affect town carry.
        // Drawn fullOverride entries are NEVER applied as absolute on the back (hand frame ≠ socket).
        private static bool TryResolveSheathedOffset(string meshKey, out AttachmentOffset fo,
                                                     out SheathedOffsetSource source)
        {
            fo = default;
            source = SheathedOffsetSource.None;
            if (string.IsNullOrEmpty(meshKey)) return false;
            if (AttachmentOffsetRegistry.TryGetOffset(meshKey + SheathedKeySuffix, out fo))
            {
                source = SheathedOffsetSource.Explicit;
                return true;
            }
            if (AttachmentOffsetRegistry.TryGetOffset(meshKey, out fo))
            {
                source = SheathedOffsetSource.DrawnFallback;
                return true;
            }
            return false;
        }

        // OWNER-AUTHORABLE SHEATHED POSE consumption (root fix 2026-07-07): after the built-in
        // sheathe pose is applied, refine it from the registry in the BACK-SOCKET frame:
        //   • explicit @sheathed + fullOverride → absolute pos/rot in the socket frame.
        //   • explicit @sheathed + nudge → +pos, built-in rot ∘ Euler(rot).
        //   • drawn-key fallback → +pos ONLY by default: the drawn euler was authored in the HAND
        //     frame (e.g. sword_A (117,-61,-111)); composing it onto the chest-socket sheathe
        //     rotation is a frame mismatch. ff.sheathdrawnrot=1 restores the full pos+rot compose
        //     (the 0492d7dc behavior) as the owner's A/B backup.
        // Scale is deliberately untouched — scale is owned by the attach path (comp * authored).
        private static void ApplySheathedOffset(Transform t, string meshKey)
        {
            if (t == null || string.IsNullOrEmpty(meshKey)) return;
            if (!TryResolveSheathedOffset(meshKey, out var fo, out var source)) return;
            bool absolute = source == SheathedOffsetSource.Explicit && fo.fullOverride;
            if (absolute)
            {
                t.localPosition = fo.pos;
                t.localRotation = Quaternion.Euler(fo.eulerRot);
            }
            else
            {
                t.localPosition += fo.pos;
                bool composeRot = source == SheathedOffsetSource.Explicit
                                  || DeNelle.Core.FeatureFlags.SheathedDrawnRotFallback;
                if (composeRot)
                    t.localRotation = t.localRotation * Quaternion.Euler(fo.eulerRot);
            }
            // THROTTLED (owner F8 seq=2153, 2026-08-06). These two were FlowTrace.Step and this
            // method runs EVERY FRAME via the ApplyHoldPose re-assert, so they emitted ~120 lines
            // per second. The F8 harness harvests only the LAST 60 signal lines - so her capture of
            // "BUILDING IS ON fire but there is no option to repair" contained SIXTY lines of
            // sheathed-offset spam and not one line about the fire, the building, or the repair
            // path. Three captures in a row were blinded the same way.
            //
            // This is the exact failure the parent file already documents at :1959-1971 for the
            // parent-scale-compensate line ("a diagnostic that blinds the diagnostics is worse than
            // no diagnostic") - the same fix was applied there and these two were missed.
            //
            // Throttle key includes meshKey + source, so a CHANGE (different weapon, or explicit
            // flipping to fallback) still prints promptly instead of being swallowed for a second.
            if (source == SheathedOffsetSource.Explicit && fo.fullOverride)
                // ⚠ WARN, NOT STEP — AN ABSOLUTE SHEATHED ROW DISCARDS THE DERIVED HIP POSE.
                // Proven 2026-08-20 by the KnightGearProof capture: ComputeSheathRotation had just
                // logged the ruled pose for the starter sword ("tiltFromVertical=0deg
                // longAxisDotUp=-1"), and the very next line was this one applying the SHIPPED
                // 'sword_A@sheathed' row (pos=(0.23,-0.14,0.12) rot=(180,-28,-51) full=True) — an
                // absolute pose authored in the RETIRED spine/back socket's frame, whose -28 is
                // literally the retired baldric diagonal. The result on screen was a sword hanging
                // diagonally, off the body, on the WRONG hip, with nothing in the source looking
                // wrong. Those two shipped rows were deleted; this line is why the next one will be
                // found in one read instead of a night. Absolute means absolute: it replaces
                // position AND rotation, so it is only ever valid in the frame it was authored in.
                // ONCE, not Warn-every-frame: ApplyHoldPose re-asserts this method at frame rate, and
                // the block above this one exists because a per-frame diagnostic here already blinded
                // three of the owner's F8 captures. First hit per mesh key is all a reader needs.
                FlowTrace.Once("Offset", $"sheathed-absolute-{meshKey}",
                    $"⚠ sheathed ABSOLUTE override '{meshKey}{SheathedKeySuffix}' applied: " +
                    $"pos={fo.pos} rot={fo.eulerRot} full=True — this REPLACES the derived hip pose. " +
                    "If it was authored before the 2026-08-20 hip ruling it is expressed in the " +
                    "retired back-socket frame and the carry will read wrong; re-author it in the " +
                    "Seating Editor (Sheathed mode) or delete the row to let the derivation stand.");
            else if (source == SheathedOffsetSource.Explicit)
                FlowTrace.Throttle("Offset", $"sheathed-explicit-{meshKey}", 1f,
                    $"sheathed offset '{meshKey}{SheathedKeySuffix}' applied: " +
                    $"pos={fo.pos} rot={fo.eulerRot} full={fo.fullOverride}");
            else
                FlowTrace.Throttle("Offset", $"sheathed-fallback-{meshKey}", 1f,
                    $"sheathed FALLBACK (drawn '{meshKey}' on back pose): " +
                    $"pos={fo.pos} rot={(DeNelle.Core.FeatureFlags.SheathedDrawnRotFallback ? fo.eulerRot.ToString() : "SKIPPED (pos-only, ff.sheathdrawnrot=0)")}");
        }

        // Lazily create the shared BACK sheathe socket under the Chest bone (fallback Spine, then
        // UpperChest) — the exact torso-anchor pattern GearVisualApplier uses for chest armor
        // (GearVisualApplier.cs:217-218). Returns null on a non-Humanoid / torso-less rig, in which
        // case ApplyHoldPose keeps the prop on the hand (no back socket = never unparented/floating).
        // Cleared on a body swap (ReseatForBody) so it re-creates under the new visible body's chest.
        // Owner F8 2026-07-06 "Shield larger than hero": props are bounds-normalized to their
        // proportional heldLength at the WORLD ORIGIN (unit scale), then SetParent(bone, false)
        // preserves LOCAL scale — so the rendered size gets multiplied by the bone's lossyScale,
        // which carries the VisualFactory.Fit body-normalization factor (≠1 on CC/AccuRig rigs).
        // This divides it back out so the world-size solve survives parenting. Re-applied on every
        // re-parent (hand <-> back socket) since different bones can carry different lossy scales.
        // Skipped for owner-dialed fullOverride scales (those were tuned by eye under the bone).
        private bool _weaponParentCompensate, _offHandParentCompensate;

        // ONE SOURCE OF TRUTH for the parent-scale factor (2026-07-07): used by the runtime
        // CompensateParentScale below, by ApplySeatingPreview (so the Seating Editor renders the
        // exact scale composition every subsequent boot renders), and — since 2026-08-03 — by
        // WeaponTrailController.EnsureTrail, but there ONLY when its anchor is NOT a grip root (a
        // raw bone still carries the Fit factor; a grip root is already compensated by the code
        // below, so re-compensating would divide out the owner-dialled authored scale).
        // `internal`, not public, for that third caller: it lives in the same DeNelle.Village
        // asmdef and namespace, so the surface stays closed to other assemblies.
        // WYSIWYG break proven 2026-07-07: preview lacked compensate (hand lossy 1.666) —
        // owner-dialed 0.46 rendered 0.276 at boot.
        internal static Vector3 ParentScaleCompensation(Transform parent)
        {
            if (parent == null) return Vector3.one;
            Vector3 ls = parent.lossyScale;
            if (ls.x <= 1e-4f || ls.y <= 1e-4f || ls.z <= 1e-4f) return Vector3.one;
            return new Vector3(1f / ls.x, 1f / ls.y, 1f / ls.z);
        }

        // ── WHAT THE SOLVE LAST RAN AGAINST, PER SLOT (seat-trace fix 2026-08-18) ────────────────────────────
        // The compensation is a PURE FUNCTION of (gripRoot, parent, parent.lossyScale, authored).
        // Re-running it on a frame where all four are unchanged writes the identical localScale and
        // measures the identical bounds — pure waste, and (worse) it emitted the trace line at frame
        // rate. Recording the inputs turns the solve EVENT-DRIVEN: it fires on attach, on every
        // hand<->back re-parent, on a body/height swap that moves the bone's lossyScale, and on an
        // authored-scale change — and on nothing else. The instrumentation is NOT removed (CLAUDE.md
        // §12 forbids stripping FlowTrace); it now fires when something actually changed, which is
        // when a diagnostic is worth reading.
        private struct ParentCompensationState
        {
            public int gripRootId;      // 0 = never applied; a new grip root invalidates
            public int parentId;
            public Vector3 parentLossy;
            public float authored;

            public bool Matches(int grip, int parent, Vector3 lossy, float auth) =>
                gripRootId == grip && parentId == parent && Mathf.Approximately(authored, auth) &&
                (parentLossy - lossy).sqrMagnitude <= 1e-10f;
        }

        private ParentCompensationState _weaponCompState, _offHandCompState;
        private ParentCompensationState _previewCompState;   // seating-editor preview slot

        /// <summary>
        /// Subject label for the seat traces — §1.4b: a measurement you cannot attribute is
        /// decoration. Every compensate line now names the SLOT, the catalog id and the registry
        /// mesh key, because the back socket carries the main weapon AND the off-hand at once and
        /// the line used to name only the shared parent.
        /// </summary>
        private static string SeatSubject(string slot, string id, string meshKey) =>
            $"{slot} id='{(string.IsNullOrEmpty(id) ? "<none>" : id)}' mesh='{(string.IsNullOrEmpty(meshKey) ? "<none>" : meshKey)}'";

        private void CompensateParentScale(Transform gripRoot, float authoredScale,
                                           string subject, ref ParentCompensationState state)
        {
            var p = gripRoot != null ? gripRoot.parent : null;
            if (p == null) return;
            Vector3 ls = p.lossyScale;
            if (ls.x <= 1e-4f || ls.y <= 1e-4f || ls.z <= 1e-4f) return;
            if (authoredScale <= 0f) authoredScale = 1f;
            // ── CHANGE GATE (seat-trace fix 2026-08-18). Same grip root, same parent, same bone lossyScale, same
            // authored multiplier => the write below is a no-op and the measurement below is a
            // duplicate. Bail BEFORE the GetComponentsInChildren<Renderer> walk, which is the
            // per-frame allocation this method was paying ~30x/second per character for.
            if (state.Matches(gripRoot.GetInstanceID(), p.GetInstanceID(), ls, authoredScale)) return;
            state = new ParentCompensationState
            {
                gripRootId  = gripRoot.GetInstanceID(),
                parentId    = p.GetInstanceID(),
                parentLossy = ls,
                authored    = authoredScale,
            };
            // comp * authored — the owner-dialed offsets.json scale (fo.scale) survives every
            // re-parent instead of being wiped back to pure 1/lossy (scale-parity fix 2026-07-07).
            gripRoot.localScale = ParentScaleCompensation(p) * authoredScale;
            // §12: log the RESULTING world size, not just the math — capture 9403 showed the
            // sheathed shield still rendering oversized while these compensate lines fired,
            // so the proof must be the rendered bounds, not the applied scale.
            // §1.4b: count what was WALKED as well as what was FOUND. "<no renderer>" (nothing to
            // measure) and "(0,0,0)" (a renderer present but measuring nothing) are DIFFERENT
            // defects with different fixes, and the old line could print the second while the
            // reader assumed the first. Inactive renderers are counted separately because
            // GetComponentsInChildren skips them here while SeatNative's TryLocalBounds includes
            // them — so a prop can be SIZED off a renderer this measurement cannot see.
            // ── WO-1226: MEASURE THE PROP, NOT THE VFX BOLTED TO IT ─────────────────────────────
            // ⭐ THIS LINE REPORTED A ROD AS A CUBE, ON TWO BUILDS, AND THE CUBE BECAME THE
            // EVIDENCE. The owner's two device captures both read `renderers=2(inactive=0)` on a
            // staff and `worldBounds=(1.519, 1.401, 1.624)` / `(1.318, 1.509, 1.169)` — near-
            // isotropic, no long axis, on a mesh this repo has measured at (0.079, 0.097, 1.265).
            //
            // THE SECOND RENDERER IS NOT A SECOND MESH. `staff_A.fbx` contains exactly one Geometry
            // and one Model. It is the code-built **TrailRenderer** that
            // `WeaponTrailController.EnsureTrail` parents onto `EquipmentController.GripRoot` (a
            // GameObject named "WeaponTrail", created on the first swing and re-created on every
            // weapon swap). A TrailRenderer's `bounds` is the WORLD AABB of the emitted ribbon —
            // the arc the hero just swung through — so encapsulating it with the prop's own bounds
            // inflates a 1.3 m shaft into roughly a 1.5 m box centred on the hand.
            //
            // ⚠ AND THE NUMBER WAS ONLY EVER IN THIS LOG LINE. No deriver reads it: every
            // orient path measures `prop` (the CHILD), not `gripRoot`, and WeaponOrientHelper
            // .TryLocalBounds reads mesh-local bounds off a MeshFilter/SkinnedMeshRenderer, which a
            // TrailRenderer has neither of. So the cube never reached the seat — it corrupted the
            // DIAGNOSTIC, which is worse in its own way: it sent readers hunting a measurement bug
            // that did not exist. Split the walk so the reported volume is the PROP's, and say what
            // else was hanging there rather than silently dropping it (§12: nothing goes unlogged).
            Bounds wb = default; bool hasB = false;
            int rendTotal = 0, rendInactive = 0, rendMesh = 0, rendNonMesh = 0;
            string nonMeshNames = null;
            foreach (var r in gripRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                rendTotal++;
                if (!r.gameObject.activeInHierarchy) { rendInactive++; continue; }
                // A renderer that owns geometry (MeshFilter or SkinnedMeshRenderer) is the PROP.
                // Anything else — TrailRenderer, LineRenderer, ParticleSystemRenderer — is an
                // effect riding the same anchor and its world AABB describes the effect, not the item.
                var smr0 = r as SkinnedMeshRenderer;
                var mf0  = smr0 == null ? r.GetComponent<MeshFilter>() : null;
                bool ownsGeometry = smr0 != null
                    ? smr0.sharedMesh != null
                    : (mf0 != null && mf0.sharedMesh != null);
                if (!ownsGeometry)
                {
                    rendNonMesh++;
                    nonMeshNames = nonMeshNames == null
                        ? r.GetType().Name + " '" + r.gameObject.name + "'"
                        : nonMeshNames + ", " + r.GetType().Name + " '" + r.gameObject.name + "'";
                    continue;
                }
                rendMesh++;
                if (!hasB) { wb = r.bounds; hasB = true; } else wb.Encapsulate(r.bounds);
            }
            bool measuresNothing = !hasB || wb.size.sqrMagnitude <= 1e-8f;
            // THROTTLED (owner F8 seq 637, "cannot move" in the Healer's Cottage): this line was
            // FlowTrace.Step, and it fires EVERY FRAME. Chain: HeroLocomotion.Update:1076 calls
            // EquipmentController.SetCombatActive unconditionally each frame; SetCombatActive's
            // no-change path (:1725) still calls ApplyHoldPose(), which reaches this solve. The
            // result flooded break-log.jsonl and Player.log at frame rate, and the F8 harness
            // harvests only the LAST 60 signal lines - so EVERY line of that capture was this one
            // message, evicting the movement/lock traces needed to diagnose the actual report.
            // A diagnostic that blinds the diagnostics is worse than no diagnostic. The throttle
            // below is kept as the backstop, but it is no longer the only defence:
            //
            // ✔ THE DEEPER DEFECT THE OLD COMMENT DEFERRED IS NOW FIXED (2026-08-18). That comment
            //   read "the per-frame WORK (GetComponentsInChildren<Renderer> above + the pose
            //   re-solve) is a separate, deeper defect ... ticketed, not silently changed here",
            //   because touching the re-assert risked the "sword stays drawn" fixes. The change
            //   gate at the top of this method resolves it WITHOUT touching the re-assert at all:
            //   ApplyHoldPose still runs every frame and still re-asserts the seat exactly as it
            //   did, but this solve now returns immediately when its four inputs are unchanged. So
            //   the renderer walk, the localScale write and this log line all become event-driven
            //   while the pose path keeps its shipped behaviour byte for byte.
            //
            // ⛔ THE KEY NOW CARRIES THE SUBJECT, NOT JUST THE PARENT (seat-trace fix 2026-08-18).
            // It used to be "parent-scale-compensate-<parentInstanceId>" — and the BACK SOCKET is
            // ONE transform that carries BOTH the main weapon and the off-hand. ApplyHoldPose
            // compensates the weapon first, so the weapon's line took the bucket every second and
            // the SHIELD'S LINE WAS NEVER PRINTED. That is why the owner's capture is ~30
            // sword lines (authored=1.1, the sword_A offsets row) and one unattributable
            // authored=1 line: two props were sharing one throttle slot on every character.
            string key = "parent-scale-compensate-" + p.GetInstanceID() + "-" + subject;
            // WO-1209 Phase A: this change-gated solve is the one seam reached both when a
            // weapon is first attached and whenever ApplyHoldPose re-parents/re-solves it. Keep
            // the complete identity + scale chain in one event-driven line so a device capture
            // can distinguish an oversized authored body from parent-scale multiplication.
            Transform instantiatedBody = gripRoot.childCount > 0 ? gripRoot.GetChild(0) : null;
            string line =
                $"parent-scale compensate: {subject} on '{name}' " +
                $"gripRoot='{gripRoot.name}' body='{(instantiatedBody != null ? instantiatedBody.name : "<no child>")}' " +
                $"parentBone='{p.name}' " +
                $"lossy=({ls.x:0.###},{ls.y:0.###},{ls.z:0.###}) authored={authoredScale:0.###} " +
                $"localScale={gripRoot.localScale} " +
                $"renderers={rendTotal}(inactive={rendInactive} mesh={rendMesh} nonMesh={rendNonMesh}" +
                (nonMeshNames != null
                    ? " [" + nonMeshNames + " -> EXCLUDED from worldBounds (WO-1226): an effect's " +
                      "world AABB describes the effect, not the item; folding it in reported this rod as a cube]"
                    : "") + ") " +
                $"-> worldBounds={(hasB ? wb.size.ToString("0.###") : "<no renderer>")} " +
                "(the proportional solve should read here as heldLength * authored on the longest axis)";
            // ESCALATION: a prop that measures NOTHING is invisible to the player. That is the
            // report ("sword is wrong", "shield is missing"), not a diagnostic curiosity — so it
            // leaves the Step channel and goes out as a Warn the F8 harvest cannot miss. It needs no
            // throttle of its own: the change gate above already means this method only reaches here
            // when a seat input actually changed, and it re-asserts on the next re-parent.
            if (measuresNothing)
                FlowTrace.Warn("Equip", line +
                    " ⛔ MEASURES NOTHING: this prop renders no world volume, so the player sees " +
                    "NO ITEM in this slot. Cause is one of: no renderer under the grip root, every " +
                    "renderer inactive, or a MeshRenderer whose mesh is null/empty.");
            else
                FlowTrace.Throttle("Equip", key, 1f, line);
        }

        // ── SHEATHE ANCHORS: ONE PER SLOT, ON THE HIPS (owner ruling 2026-08-20) ─────────────────
        // ⛔ WHAT THIS REPLACES — `ResolveBackSocket()`, which created ONE GameObject named
        // 'SheatheSocket_Back' under Chest→Spine→UpperChest and handed the SAME transform to both
        // the sword and the shield. Two proven defects in four lines, from the device capture:
        //   1. "sheathe anchor under bone 'CC_Base_Spine01'" — on this CC rig GetBoneTransform(Chest)
        //      lands on the LOW spine, so the "back" pose was already at waist height. The owner's
        //      screenshot of a sword lying across the waist is that line rendered.
        //   2. Both props got the identical anchor at the identical origin, so the shield (measured
        //      0.72 x 0.92 x 0.72 m — it draws, it is not null) sat inside the torso and the body
        //      mesh ate it. "Shield isn't showing" was never a missing prop.
        // The owner's instruction is the hip bone, so Hips is now FIRST, not a fallback — and the
        // spine/chest chain is KEPT BELOW it, never stripped (§12), for a rig with no mapped Hips.
        // A rig that falls back logs a Warn, because on that rig the pose is NOT the ruled one and
        // the next reader must be able to see that from the capture instead of re-deriving it.
        // ── AND THE OFF-HAND ANCHOR MOVED AGAIN: HIP → FOREARM (owner F8 2026-08-21) ─────────────
        // Owner, verbatim: "shield is attaching to hip not wrist or arm" ... "the shield on arm or
        // arm bone". The capture named the parent on every frame — parent='SheatheSocket_HipOff' —
        // so this was never a pose question; the shield was on the anchor she is telling us is wrong.
        //
        // ⚠ ONLY THE OFF-HAND MOVES. The 2026-08-20 hip ruling was about the SHEATHED SWORD ("the
        // longest mesh (y) up and down attached to hip bone") and it still stands, unaltered, for
        // the main hand. Generalising one prop's ruling to the other prop is the mistake this file
        // has already recorded twice (the shield taking the sword's "inverted"; the shield sharing
        // the sword's socket). Two slots, two anchors, two rulings.
        //
        // ⛔ AND ASKING FOR A BONE IS NOT GETTING IT. GetBoneTransform(Chest) resolved to
        // CC_Base_Spine01 on this rig — that is the whole reason the "back" carry was at the waist.
        // So the resolved bone's REAL NAME is logged every time, and the trace states which of the
        // three tiers actually answered. Read the capture, never the enum in this source.
        private Transform ResolveSheatheSocket(bool offHand)
        {
            Transform cached = offHand ? _sheatheSocketOff : _sheatheSocketMain;
            if (cached != null) return cached;            // a destroyed Unity object compares == null → re-created
            if (_animator == null || !_animator.isHuman) return null;

            Transform anchor = null;
            bool onArm = false, onHips = false;
            string tier;
            if (offHand)
            {
                // The off-hand prop seats on the LEFT hand in every drawn pose (AttachOffHandProp),
                // so the strap goes on the LEFT forearm. LowerArm first — that is the forearm the
                // owner asked for; UpperArm is a poorer but still ARM answer; the hand itself is the
                // last arm-tier option (a wrist mount) before we fall out of the limb entirely.
                anchor = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
                onArm = anchor != null;
            }
            if (anchor == null)
            {
                anchor = _animator.GetBoneTransform(HumanBodyBones.Hips);
                onHips = anchor != null;
                if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.Spine);
                if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.Chest);
                if (anchor == null) anchor = _animator.GetBoneTransform(HumanBodyBones.UpperChest);
            }
            if (anchor == null) return null;
            tier = onArm ? "arm" : onHips ? "hips" : "spine/chest";

            // Names carry the slot AND the anchor's intent, so a capture line names which prop it is
            // talking about. The old shared name said "Back" while sitting on the spine — a name that
            // lies is how a reader concludes the pose is right when the transform says otherwise. The
            // off-hand name now says Arm because that is where it goes; if a rig falls back to the
            // hips the name still says Arm, which is why the WARN below exists and the trace prints
            // the real bone.
            var go = new GameObject(offHand ? "SheatheSocket_ArmOff" : "SheatheSocket_HipMain");
            go.transform.SetParent(anchor, false);
            go.layer = anchor.gameObject.layer;   // SetParent does not copy layer (WO-1226 preview/world show)
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            if (offHand) { _sheatheSocketOff = go.transform; _sheatheSocketOffIsArm = onArm; }
            else _sheatheSocketMain = go.transform;
            float side = offHand ? (onArm ? SheatheSideArmOff : SheatheSideOff) : SheatheSideMain;
            string msg = $"ResolveSheatheSocket({(offHand ? "off-hand" : "main-hand")}) on '{name}': " +
                         $"anchor '{go.name}' under bone '{anchor.name}' " +
                         $"(tier={tier}; side={side:+0;-0} * body.right).";
            if (offHand)
            {
                if (onArm) FlowTrace.Step("Equip", msg + " Shield rides the OFF-HAND ARM (owner F8 " +
                    "2026-08-21). If this line ever names a hip bone again, that is the reported defect.");
                else FlowTrace.Warn("Equip", msg + " ⛔ NOT AN ARM BONE — this rig maps no Left" +
                    "LowerArm/UpperArm/Hand, so the shield fell back to the retired HIP carry the owner " +
                    "rejected on 2026-08-21. The pose numbers follow the anchor that was really " +
                    "resolved (hip offset + hip side), so it is seated sanely — but it is the wrong " +
                    "mount. Fix the rig's humanoid arm mapping, not this code.");
            }
            else if (onHips) FlowTrace.Step("Equip", msg);
            else FlowTrace.Warn("Equip", msg + " ⛔ NOT THE HIPS BONE — this rig has no mapped Hips, so " +
                 "the sheathed prop is hanging off a spine/chest bone and will sit HIGHER than the " +
                 "owner-ruled hip carry. The pose math is unchanged (it is body-derived); only the " +
                 "attach height is wrong. Fix the rig's humanoid Hips mapping, not this code.");
            return go.transform;
        }

        /// <summary>
        /// The body.right multiplier for the off-hand's CURRENT anchor: the left-arm side when the
        /// forearm resolved, the right-hip side when it did not. One accessor so the position offset
        /// and the rotation's `outward` can never disagree about which side the prop is on — they
        /// disagreeing is a shield posed face-first into the hero with every angle reading 0.
        /// </summary>
        private float OffHandSheatheSide() =>
            _sheatheSocketOffIsArm ? SheatheSideArmOff : SheatheSideOff;

        /// <summary>
        /// Player-visible face direction for the town shield. A purely lateral normal
        /// (<c>body.right * side</c>) makes the whole plate edge-on to the normal
        /// third-person camera behind the hero: its world bounds remain nonzero while
        /// its projected shield area effectively disappears. Keep the arm-side term
        /// dominant, but cant the face rearward so the plate produces a recognizable
        /// silhouette during ordinary town play. Hip fallback retains its historical
        /// lateral pose because it is not the owner-approved forearm mount.
        /// </summary>
        private Vector3 OffHandShieldOutwardWorld()
        {
            Transform body = _animator != null ? _animator.transform : transform;
            Vector3 lateral = body.right * OffHandSheatheSide();
            // Arm strap stays LEFT of the body (owner: on the arm, not the hip/back). A
            // rear-follow camera still needs a cant or the heater is edge-on and reads as
            // "no shield" even when the renderer is on. 0.45/1.35 (back-heavy) parked the
            // plate IN the cape along -forward; left-dominant + a smaller rear cant puts
            // it beside the left arm with the orange face readable from town-rear.
            Vector3 outward = _sheatheSocketOffIsArm
                ? (lateral * 1.10f) - (body.forward * 0.55f)
                : lateral;
            return outward.sqrMagnitude > 1e-8f ? outward.normalized : lateral;
        }

        /// <summary>
        /// Slide the SHEATHED off-hand so its RENDERED CENTRE — not its grip origin — sits where
        /// <see cref="ComputeSheathLocalPosition"/> just put it. See the call site for why the
        /// off-hand needs this and the main hand must never get it.
        ///
        /// Uses <c>Renderer.bounds.CENTER</c>, which is a world POINT and therefore basis-safe.
        /// It deliberately does NOT touch <c>bounds.extents</c>: re-expressing a world AABB's
        /// extents in another frame smears a rotated box and reorders its axes — the defect fixed
        /// in WeaponOrientHelper.TryLocalBounds on 2026-08-20.
        ///
        /// ⛔ IT TAKES <paramref name="baseLocalPos"/> AND *ASSIGNS*. IT MUST NEVER SUBTRACT.
        /// The first cut did `grip.localPosition -= shiftLocal` and called itself idempotent in its
        /// own doc comment. It is not, and the reasoning was plainly wrong: the plate is RIGIDLY
        /// parented, so moving the grip moves the plate with it and `bounds.center - grip.position`
        /// comes back IDENTICAL on the next call — every re-assert subtracts the same shift again.
        /// SheathePoseRegression P1b caught it immediately (0 -> 1.303 m after two extra calls), and
        /// ApplyHoldPose re-asserts this pose EVERY FRAME, so shipped it would have walked the
        /// shield off the hero within a second of standing in town — a drift no screenshot taken on
        /// frame one could ever show. Assigning from the seat's own base position is idempotent by
        /// construction: the same inputs always produce the same result.
        /// </summary>
        private void ApplyOffHandCentreOnSocket(Transform grip, Transform socket, Vector3 baseLocalPos)
        {
            // WO-1616: the MATH moved to the static core so the raid NPC path centres its plate with
            // the same instructions instead of a second implementation. The instance wrapper keeps
            // the hero's own trace tokens (mesh key + which socket it is), which the static cannot
            // know — the line the F8 captures already grep for is therefore byte-identical.
            CentreGripOnSocket(grip, socket, baseLocalPos, _currentOffHandMeshKey,
                               $"(arm={_sheatheSocketOffIsArm})");
        }

        /// <summary>WO-1616. The shared centring core — see the contract on the wrapper above.</summary>
        public static void CentreGripOnSocket(Transform grip, Transform socket, Vector3 baseLocalPos,
                                              string subject, string extraTraceToken)
        {
            if (grip == null || socket == null) return;
            Renderer r = grip.GetComponentInChildren<Renderer>();
            if (r == null) { grip.localPosition = baseLocalPos; return; }
            Vector3 originToCentreWorld = r.bounds.center - grip.position;
            if (originToCentreWorld.sqrMagnitude < 1e-8f) { grip.localPosition = baseLocalPos; return; }
            Vector3 shiftLocal = socket.InverseTransformVector(originToCentreWorld);
            grip.localPosition = baseLocalPos - shiftLocal;
            FlowTrace.Throttle("Equip", "offhand-centre-" + (subject ?? "?"), 5f,
                $"sheathed off-hand centred on its mount: '{subject}' origin->renderedCentre " +
                $"was {originToCentreWorld} (world), shifted {shiftLocal} in socket '{socket.name}' " +
                $"{extraTraceToken}. A grip-at-origin shield otherwise hangs its whole " +
                "plate UPWARD from the anchor. NOTE: this shift is re-derived from a WORLD AABB, so " +
                "it moves a few mm as the hero turns — that drift is why the seat tripwire is " +
                "throttled, and it is the seam to fix if the plate ever visibly swims.");
        }

        /// <summary>
        /// Straps a centred shield to the outer skin of the off-hand forearm.
        /// POSITION is purely lateral (away from the torso). FACE cant lives in
        /// <see cref="OffHandShieldOutwardWorld"/> and must not drive this push —
        /// shoving along the canted vector into the character's world AABB is what
        /// floated the heater a body-width off Grom (owner town-rear capture).
        /// Distance = measured half-thickness + arm-bone-to-skin. Cap 0.28 m.
        /// Idempotent vs the centre seat: ApplyHoldPose re-assigns centre then adds this.
        /// </summary>
        private void ApplyOffHandArmSurfaceSeat(Transform grip, Transform socket)
        {
            if (grip == null || socket == null || !_currentOffHandShieldFrame.Valid) return;

            Transform body = _animator != null ? _animator.transform : transform;
            Vector3 pushWorld = body.right * OffHandSheatheSide();
            if (pushWorld.sqrMagnitude < 1e-8f) return;
            pushWorld.Normalize();

            Vector3 thicknessWorld = grip.TransformVector(_currentOffHandShieldFrame.ThicknessAxis);
            float halfThicknessWorld = 0.5f * _currentOffHandShieldFrame.Axes.NarrowestLen *
                                       thicknessWorld.magnitude;
            const float armSkinWorld = 0.10f;
            const float minWorld = 0.22f;
            const float capWorld = 0.28f;
            float outwardDistance = halfThicknessWorld + armSkinWorld;
            if (outwardDistance < minWorld) outwardDistance = minWorld;
            if (outwardDistance > capWorld) outwardDistance = capWorld;

            grip.localPosition += socket.InverseTransformVector(pushWorld * outwardDistance);

            FlowTrace.Throttle("Equip", "offhand-arm-surface-" + (_currentOffHandMeshKey ?? "?"), 5f,
                $"sheathed off-hand SURFACE-SEATED on arm: '{_currentOffHandMeshKey}' " +
                $"halfThickness={halfThicknessWorld:0.###}m armSkin={armSkinWorld:0.###}m " +
                $"push={pushWorld} total={outwardDistance:0.###}m socket='{socket.name}'. " +
                "Lateral strap only — face cant is rotation, not a body-AABB shove.");
        }

        /// <summary>
        /// Converts a BODY-space sheathe offset (x = outward on this slot's hip side, y = up,
        /// z = forward, metres) into the socket's local frame. Derived, never bone-local-typed:
        /// InverseTransformVector divides out both the bone's arbitrary axes and its lossyScale
        /// (1.67 on this rig, per the capture's boneLossy), so the authored metres are metres on
        /// the hero. The old code wrote a raw bone-local vector authored against a DIFFERENT bone.
        /// </summary>
        private Vector3 ComputeSheathLocalPosition(Transform socket, Vector3 bodyOffset, float sideSign)
        {
            if (socket == null) return bodyOffset;
            Transform body = _animator != null ? _animator.transform : transform;
            Vector3 world = body.right * (bodyOffset.x * sideSign)
                          + body.up * bodyOffset.y
                          + body.forward * bodyOffset.z;
            Vector3 ls = socket.lossyScale;
            if (Mathf.Abs(ls.x) < 1e-5f || Mathf.Abs(ls.y) < 1e-5f || Mathf.Abs(ls.z) < 1e-5f)
            {
                FlowTrace.Warn("Equip", $"ComputeSheathLocalPosition on '{name}': socket '{socket.name}' " +
                    $"has a degenerate lossyScale {ls} — cannot express the body-space offset in its " +
                    "frame. Falling back to the raw offset; the prop may sit off the hip.");
                return bodyOffset;
            }
            return socket.InverseTransformVector(world);
        }

        /// <summary>
        /// Measure, ONCE per attach, which way THIS mesh has to hang so its tip points at the
        /// ground. Writes <see cref="_sheatheTipSign"/> (0 = undecidable → the serialized fallback
        /// stands) and the reason, which <see cref="ComputeSheathRotation"/> prints in its throttled
        /// pose line so a capture answers "derived or guessed?" without a rebuild.
        ///
        /// WHY IT IS CALLED AT ATTACH AND NOT IN THE POSE: ApplyHoldPose re-asserts the sheathe pose
        /// EVERY FRAME. A bounds walk there is the per-frame allocation this file has already had to
        /// fix twice (CompensateParentScale's change gate, ApplySheathedOffset's throttle). The
        /// answer cannot change while the same prop is on the same grip root, so it is resolved with
        /// the prop and cleared with it.
        ///
        /// SLOT SCOPE: main hand only. A SHIELD has no tip and takes no inversion rule (see
        /// ComputeSheathedOffHandRotation's ⛔ note), and a BOW keeps its own derived carry
        /// (ComputeBowHeldRotation) in both states by the owner's 2026-08-16 ruling — neither reads
        /// this sign, so measuring one for them would be a number nothing consumes.
        /// </summary>
        private void ResolveSheathedTipSign(GameObject prop, Transform gripRoot,
                                            string meshKey, WeaponClass kind)
        {
            _sheatheTipSign = 0f;
            _sheatheTipWhy  = null;
            // Clear the scratch too: Guard.Try swallows a throw and returns the fallback, and a
            // stale struct from the PREVIOUS prop would then be read as this prop's answer.
            _sheathedTipScratch = default;
            if (prop == null || gripRoot == null) return;
            if (kind == WeaponClass.Shield || kind == WeaponClass.Bow)
            {
                _sheatheTipWhy = $"not applicable to {kind} (no tip to invert / own derived carry)";
                return;
            }

            bool ok = Guard.Try("Equip", $"TryResolveSheathedTipSign '{meshKey}'",
                () => WeaponOrientHelper.TryResolveSheathedTipSign(
                          prop, gripRoot, out _sheathedTipScratch),
                false);
            if (!ok || !_sheathedTipScratch.Valid)
            {
                _sheatheTipWhy = _sheathedTipScratch.Why;

                // ── SYMMETRICAL IS NOT BROKEN (owner ruling WO-1136, 2026-08-22) ────────────────
                // A `false` here used to mean exactly one thing — "this prop may hang upside down" —
                // and the Warn below says so in those words. For a mesh whose two ends are measurably
                // IDENTICAL that sentence is simply untrue: there is no upside down to hang, the
                // global fallback cannot be wrong about a symmetrical prop, and shouting about it
                // trains the reader to skim the line that DOES matter. What matters for this prop is
                // VERTICALITY, so that is what gets measured and logged instead.
                if (_sheathedTipScratch.Decision ==
                    WeaponOrientHelper.SheathedSignDecision.SignAgnostic)
                {
                    bool upright = WeaponOrientHelper.TrySheathesVertical(
                        _sheathedTipScratch, out float tiltDeg, out string vWhy);
                    string head = $"sheathe sign for '{meshKey}' on '{name}': SIGN-AGNOSTIC — the two " +
                                  "ends are measurably identical, so either way up is the same picture " +
                                  $"and the global fallback ({_sheatheLongAxisSign:+0;-0}) is harmless " +
                                  "here. The ruled requirement for this prop is VERTICALITY: ";
                    if (upright)
                        FlowTrace.Step("Equip", head + vWhy + " — ok.");
                    else
                        FlowTrace.Fail("Equip", head + vWhy + " ⛔ This prop is sheathing ACROSS THE " +
                            "BODY. Owner ruling WO-1136: a symmetrical prop still has to hang upright. " +
                            "Fix the SEAT (NormalizeInto / the authored native frame), not the sign — " +
                            "no sign can rotate a long axis onto the vertical.");
                    return;
                }

                FlowTrace.Warn("Equip",
                    $"sheathe sign for '{meshKey}' on '{name}': NOT DERIVABLE — " +
                    $"{(string.IsNullOrEmpty(_sheatheTipWhy) ? "the prop could not be measured" : _sheatheTipWhy)}. " +
                    $"Falling back to the GLOBAL _sheatheLongAxisSign ({_sheatheLongAxisSign:+0;-0}), " +
                    "which is a guess shared with every other weapon in the game. If this prop reads " +
                    "upside down at the hip, the fix is to make it measurable (a renderer with a " +
                    "non-degenerate long axis, or a grip that is not at the mesh's midpoint) — NOT to " +
                    "flip the field, which only moves the defect to the other heroes.");
                return;
            }

            _sheatheTipSign = _sheathedTipScratch.BodyUpSign;
            _sheatheTipWhy  = $"{_sheathedTipScratch.Source}/{_sheathedTipScratch.Why}";
            // The sheathe pose maps GRIP-ROOT-LOCAL +Y onto the vertical. If the measured long axis
            // is not Y, the pose's premise is broken in a way no SIGN can repair (it would hang the
            // prop's WIDTH vertically), so say it out loud rather than shipping a confident number
            // about the wrong direction — the flat-shield failure mode, restated for the main hand.
            if (_sheathedTipScratch.LongAxis != 1)
                FlowTrace.Warn("Equip",
                    $"sheathe sign for '{meshKey}' on '{name}': the measured long axis is " +
                    $"{(_sheathedTipScratch.LongAxis == 0 ? "X" : "Z")}, NOT Y, in the grip root's frame. " +
                    "ComputeSheathRotation hangs prop-local +Y on the vertical, so this prop will " +
                    "sheathe sideways no matter which sign is used. The sign below is correct about " +
                    "the wrong axis — fix the seat (NormalizeInto / the authored native frame), not " +
                    "the sign.");
            FlowTrace.Step("Equip",
                $"sheathe sign for '{meshKey}' on '{name}': {_sheatheTipSign:+0;-0} " +
                $"(tip at {(_sheathedTipScratch.TipAtPositiveEnd ? "+" : "-")}long-axis) via {_sheatheTipWhy}.");
        }

        // Scratch for the out-parameter above: a lambda cannot carry an `out`, and Guard.Try is the
        // §12-mandated wrapper. One field, written only inside ResolveSheathedTipSign.
        private WeaponOrientHelper.SheathedTipResolution _sheathedTipScratch;

        // ── DERIVED SHEATHE ROTATION (owner F8 fix 2026-07-04 — "the secret is on battle") ──────────
        // The DRAWN (battle) seat is correct because it is DERIVED from the rig, never guessed:
        // ComputeMeleeGripRotation builds Quaternion.LookRotation(up, blade) from the HAND bone's own
        // axes so the prop's blade line (prop-local +Y, put there by NormalizeInto + SeatHiltLowerHalf)
        // and its flat-plane normal (prop-local +Z) land forward-out-of-the-fist. The OLD sheathe used a
        // hand-typed magic euler (8,0,158) with no relationship to geometry OR the chest-bone axes, so it
        // sat wrong. This DERIVES the sheathed orientation the SAME way — from the BODY's own axes — with
        // the identical LookRotation(flat, blade) construction, so the sheathed sword sits right the way
        // the drawn one does. AT THE HIP (owner ruling 2026-08-20) we want the blade hanging VERTICAL
        // and INVERTED — hilt up at the belt, tip down the thigh — with the flat against the leg:
        //   • prop +Y (blade)  -> worldBlade = body up * _sheatheLongAxisSign (-1 = tip down), with an
        //                        optional _sheatheBladeDiagonalDeg lean off vertical (default 0)
        //   • prop +Z (flat)   -> worldFlat = this hip's OUTWARD side (blade lies flat on the leg)
        // Built in WORLD from the body's axes, then expressed in the socket's LOCAL frame so it follows
        // the hip bone through animation/turning exactly like the drawn seat follows the hand. Finally
        // the persisted authored nudge (_sheatheWeaponLocalEuler, owner felt-tune) composes on top —
        // the sheathe equivalent of _swordGripEuler nudging the drawn seat.
        private Quaternion ComputeSheathRotation(Transform socket) =>
            ComputeSheathRotation(socket, SheatheSideMain);

        // ⛔ THE BALDRIC DIAGONAL IS RETIRED (owner ruling 2026-08-20). The body of this method used
        // to lean the blade `_sheatheBladeDiagonalDeg` (28) off vertical toward the off shoulder and
        // lay its flat against the BACK (`worldFlat = -body.forward`) — a diagonal back carry. On a
        // socket that actually resolved to CC_Base_Spine01 that reads as a sword lying sideways
        // across the waist, which is precisely what the owner photographed. The CONSTRUCTION below
        // is unchanged and deliberately so — it is still LookRotation(flat, blade) built in WORLD
        // from the body's own axes and expressed in the socket's local frame, exactly as the correct
        // battle draw is, and there is still no hand-typed euler anywhere in it. Only the two INPUT
        // DIRECTIONS moved: the blade line goes VERTICAL (and inverted), and the flat turns to face
        // outward from the leg instead of backward, because a belt-hung sword lies against the thigh.
        private Quaternion ComputeSheathRotation(Transform socket, float sideSign)
        {
            Transform body = _animator != null ? _animator.transform : transform;
            // ── Long axis: VERTICAL, and the sign comes from THE MESH, not from a global field.
            //
            // ⛔ THE RETIRED LINE READ: `float sign = _sheatheLongAxisSign >= 0f ? 1f : -1f;` with
            // the comment "_sheatheLongAxisSign is the single number the owner flips if a particular
            // asset's authored tip axis reads the other way". The sentence names the defect while
            // prescribing the wrong cure: if the tip axis is a property of the ASSET, then ONE
            // number shared by every asset is wrong for some of them no matter what it is set to,
            // and flipping it just trades which hero reports the bug. That is precisely what the two
            // F8s recorded — Blaise at -1 on 08-20, the Flameblade at +1 on 08-21.
            // _sheatheTipSign is measured per mesh at attach (ResolveSheathedTipSign); the field is
            // the documented fallback for a prop that cannot answer, and is NOT deleted (§12).
            float sign = _sheatheTipSign != 0f
                ? (_sheatheTipSign >= 0f ? 1f : -1f)
                : (_sheatheLongAxisSign >= 0f ? 1f : -1f);
            // _sheatheBladeDiagonalDeg survives as an optional lean off vertical (default 0), and it
            // leans across the body toward the OTHER hip, so a leaning sword still hangs off its own.
            Vector3 vertical = body.up * sign;
            float rad = _sheatheBladeDiagonalDeg * Mathf.Deg2Rad;
            Vector3 worldBlade = (vertical * Mathf.Cos(rad) + body.right * (-sideSign) * Mathf.Sin(rad)).normalized;
            // Flat of the blade rests against the LEG, so its normal points outward on this hip's side.
            Vector3 worldFlat = body.right * sideSign;
            // Orthogonalize: with a non-zero lean the two are no longer perpendicular, and
            // LookRotation silently re-derives its own up in that case — which would quietly undo the
            // lean instead of applying it. Project the flat off the blade line so the frame we hand
            // over is the frame we asked for.
            worldFlat -= Vector3.Dot(worldFlat, worldBlade) * worldBlade;
            if (worldFlat.sqrMagnitude < 1e-6f)
                worldFlat = Vector3.ProjectOnPlane(body.forward, worldBlade);
            if (worldFlat.sqrMagnitude < 1e-6f) worldFlat = Vector3.forward;
            worldFlat.Normalize();
            // LookRotation(forward, upwards): +Z -> forward, +Y -> upwards. We want prop +Z -> flat and
            // prop +Y -> blade — the SAME axis mapping ComputeMeleeGripRotation uses (LookRotation(up, blade)).
            Quaternion worldTarget = Quaternion.LookRotation(worldFlat, worldBlade);
            // Express in the socket's local frame (the socket follows the hip bone), then the nudge.
            Quaternion localBase = Quaternion.Inverse(socket.rotation) * worldTarget;
            Quaternion result = localBase * Quaternion.Euler(_sheatheWeaponLocalEuler);
            // §12: the pose must PROVE itself in a capture, not be argued from source. tiltFromVertical
            // is the number that was 28 (by construction) and must now read ~0; longAxisDotUp names the
            // inversion sign so "it looks upside down" is answerable without a rebuild.
            //
            // ⛔ WO-1582: THE 5-SECOND THROTTLE IS RETIRED HERE. THE LINE IS NOT (§12 — never strip).
            // The retired call read `FlowTrace.Throttle("Equip", $"sheathe-rot-...", 5f, ...)`, and its
            // own comment named the defect while prescribing a cure that could not work: "Throttled:
            // ApplyHoldPose re-asserts this every frame." A time throttle on a per-frame site logs
            // FOREVER at its cadence whether or not anything moved — the owner's device log on
            // 2026-09-07 08:28-08:29 carried TWELVE identical `sheathed long axis on 'Hero (Blaise)':
            // tiltFromVertical=0deg longAxisDotUp=1` lines in one minute, with no value changes. At
            // 256 KiB the Android main ring cannot hold that plus a boot window (memory:
            // logcat-ring-buffer-destroys-evidence), so the instrument was evicting the evidence it
            // exists to preserve.
            //
            // THE CURE IS A KEYED LATCH, NOT A LONGER INTERVAL: emit once per (hero, prop, socket,
            // RESULT), and again the moment the result MOVES. A steady pose therefore costs exactly
            // one line for the whole session, and every transition still prints — which is strictly
            // MORE evidence than a 5s throttle gave (a change between two ticks used to be invisible).
            //
            // ⚠ NOT FlowTrace.Once WITH THE RESULT IN THE KEY. Once is a HashSet, so A -> B -> A never
            // re-emits A; a pose that goes wrong and comes back would go unrecorded, and a jittering
            // value would grow the set without bound. A LAST-VALUE dictionary re-fires on any
            // transition, including a return, and stays bounded at one entry per identity.
            //
            // ⚠ THE SIGNATURE IS QUANTIZED AND THE MESSAGE IS NOT, deliberately. If the latch key
            // carried the full-precision value, float noise on a rounding boundary would flip it every
            // frame and the latch would become a 60 Hz flood — worse than the throttle it replaces.
            // The key buckets tilt to WHOLE DEGREES and the dot to 0.1, which is far coarser than any
            // change this line is read for ("must read ~0; ~90 means it is lying across the body").
            // The logged text keeps its original precision.
            Vector3 bladeWorld = (socket.rotation * result) * Vector3.up;
            float tiltFromVertical = Vector3.Angle(bladeWorld, vertical);
            float longAxisDotUp = Vector3.Dot(bladeWorld, body.up);
            string slot = sideSign < 0f ? "main" : "off";
            string propKey = sideSign < 0f
                ? (!string.IsNullOrEmpty(_currentWeaponMeshKey) ? _currentWeaponMeshKey : (_currentWeaponId ?? "?"))
                : (!string.IsNullOrEmpty(_currentOffHandMeshKey) ? _currentOffHandMeshKey : (_currentOffHandId ?? "?"));
            string sheatheTraceIdentity = $"sheathe-rot-{slot}-{name}-{propKey}-{socket.name}";
            string sheatheTraceSignature = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "tilt={0}|dot={1:0.#}|sign={2:+0;-0}|src={3}|why={4}",
                Mathf.RoundToInt(tiltFromVertical),
                Mathf.Round(longAxisDotUp * 10f) / 10f,
                sign,
                _sheatheTipSign != 0f ? "PER-MESH derived" : "GLOBAL fallback field",
                string.IsNullOrEmpty(_sheatheTipWhy) ? "<none measured>" : _sheatheTipWhy);
            EmitSheatheTraceIfChanged(_sheatheTraceLatch, sheatheTraceIdentity, sheatheTraceSignature,
                $"sheathed long axis on '{name}': tiltFromVertical={tiltFromVertical:0.#}deg " +
                $"(must read ~{_sheatheBladeDiagonalDeg:0.#}; ~90 means it is lying across the body) " +
                $"longAxisDotUp={Vector3.Dot(bladeWorld, body.up):0.##} " +
                $"(sign={sign:+0;-0} maps prop+Y onto body.up*sign) " +
                $"src={(_sheatheTipSign != 0f ? "PER-MESH derived" : "GLOBAL fallback field")} " +
                $"why={(string.IsNullOrEmpty(_sheatheTipWhy) ? "<none measured>" : _sheatheTipWhy)} " +
                $"socket='{socket.name}'. " +
                "⚠ 'GLOBAL fallback field' here means this mesh could not be measured and is hanging " +
                "on a guess shared with every other prop — that is the 08-20/08-21 upside-down defect, " +
                "and the fix is to make the mesh measurable, NEVER to flip the field.");
            return result;
        }

        // ── WO-1582: THE SHEATHE-TRACE LATCH ─────────────────────────────────────────────────────
        // Per-INSTANCE, not static: the latch's lifetime is the hero's. A scene reload or a fresh
        // hero therefore re-emits its first line, which is the behaviour a reader wants (the same
        // reason FlowTrace.ResetSession exists) and it keeps no process-wide state that a later
        // session could inherit and go silent on. Bounded at one entry per
        // (hero, prop, socket) identity — a handful of rows for a hero's whole session.
        private readonly Dictionary<string, string> _sheatheTraceLatch = new Dictionary<string, string>();

        /// <summary>
        /// WO-1582 keyed latch. Emits <paramref name="message"/> at Step severity ONLY when
        /// <paramref name="signature"/> differs from the last one emitted for
        /// <paramref name="identity"/> — first hit, and every change thereafter INCLUDING a change
        /// back to a previous value. Returns true when it emitted.
        /// <para/>
        /// The latch is passed IN rather than read off a field so the headless fixture
        /// (<c>SheatheTraceLatchRegression</c>) can drive the REAL emit path with its own dictionary
        /// and count the lines that reach <see cref="FlowTrace.Sink"/>. A gate that only tested a
        /// bool would not prove the trace still reaches the log at all, which is the half of this
        /// ticket that matters most (§12: never strip instrumentation).
        /// </summary>
        public static bool EmitSheatheTraceIfChanged(
            Dictionary<string, string> latch, string identity, string signature, string message)
        {
            // No latch or no identity: emit rather than swallow. A diagnostic whose de-dupe state is
            // missing must fall back to LOUD, never to silent — a silenced trace is indistinguishable
            // from a code path that never ran, and that ambiguity is what §12 exists to remove.
            if (latch == null || string.IsNullOrEmpty(identity))
            {
                FlowTrace.Step("Equip", message);
                return true;
            }
            if (latch.TryGetValue(identity, out string previous) && previous == signature) return false;
            latch[identity] = signature;
            FlowTrace.Step("Equip", message);
            return true;
        }

        // ── DERIVED SHEATHED OFF-HAND (SHIELD) ROTATION — WO-1123 ────────────────────────────────
        // WHAT THIS REPLACES: `_sheatheOffHandLocalEuler = (0, 90, 192)` ∘ the global yaw. Its own
        // field comment concedes the euler has "no relationship to geometry OR the chest-bone axes"
        // — the §4 smell, hand-typed once for ONE mesh (shield_A) and inherited by every shield
        // since, including the live default (ShieldWithItemLogic) that has no authored row at all.
        //
        // The owner's rule is the SAME rule in both poses (2026-08-19): the shield's thickness axis
        // faces AWAY FROM THE PLAYER and the handled face is against the mount.
        //
        // ⚠ "AWAY FROM THE PLAYER" IS NOW THE HIP SIDE, NOT -body.forward (owner ruling 2026-08-20).
        // The retired line here said "On the back, 'away from the player' is -body.forward — the only
        // term that changes between hand and back", and it was true of a BACK mount. A shield slung
        // on the hip lies against the leg, so the face that must point away is the LATERAL one; and
        // the long axis is now handed `body.up * _sheatheLongAxisSign` so the shield obeys the same
        // vertical/inverted rule the sword does. Both terms are still body-derived — no euler.
        //
        // ⛔ THE SECOND HALF OF THE BUG, AND THE LESS OBVIOUS ONE: this derivation was gated on
        // `_currentOffHandDerivable`, which is computed at attach from the DRAWN pose's precedence
        // (WeaponOrientHelper.MayDerive(hasOffset, manual)). The capture's proving line is
        //   "off-hand seat NOT derived for 'knight_shield_starter' key='ShieldWithItemLogic':
        //    source=AuthoredOffset (authoredRow=True manual=False native=True fullOverride=False)"
        // — an authored DRAWN row therefore silently disabled the SHEATHED derivation too, and
        // (grep the capture) not one `ShieldFrame` line was ever emitted, so the frame was never
        // even measured. The sheathed pose has its OWN authored channel ("<meshKey>@sheathed",
        // checked below); a drawn-pose row must not speak for it. The gate is now
        // _currentOffHandSheathDerivable, which is about the sheathed row alone.
        //
        // PRECEDENCE, re-checked here and not just at attach, because a SHEATHED pose has its own
        // authored channel: an explicit "<meshKey>@sheathed" row outranks this derivation entirely
        // (ApplySheathedOffset would otherwise compose a nudge dialled against the constant on top
        // of a different base). The constant is KEPT as the return for every non-derivable case —
        // §12: fallbacks are never stripped, and with no shield attached this is byte-identical to
        // the shipped pose.
        //
        // CHEAP BY CONSTRUCTION: ApplyHoldPose calls this EVERY FRAME. The mesh-walking half was
        // resolved once at attach into _currentOffHandShieldFrame; this only builds a rotation.
        private Quaternion ComputeSheathedOffHandRotation(Transform socket)
        {
            Quaternion shipped = ApplyGlobalWeaponYaw(Quaternion.Euler(_sheatheOffHandLocalEuler));
            if (socket == null || !_currentOffHandSheathDerivable || !_currentOffHandShieldFrame.Valid ||
                _currentOffHandKind != WeaponClass.Shield)
            {
                // The shipped constant is a BACK-pose euler; on a hip socket it is a guess about a
                // mount it was never dialled against. Say so — this is the line that tells the next
                // reader "the shield is at the hip but its rotation is the old back constant", which
                // is a different defect from "the derivation ran and is wrong".
                if (socket != null && _currentOffHandKind == WeaponClass.Shield)
                    FlowTrace.Throttle("Equip", $"sheath-shield-fallback-{_currentOffHandMeshKey}", 5f,
                        $"sheathed shield '{_currentOffHandMeshKey}': NOT derived (sheathDerivable=" +
                        $"{_currentOffHandSheathDerivable} frameValid={_currentOffHandShieldFrame.Valid}) — " +
                        $"falling back to the retired BACK-carry constant on a {(_sheatheSocketOffIsArm ? "FOREARM" : "HIP")} " +
                        "socket, a mount it was never dialled against. Expect the " +
                        "face to read wrong; the cause is upstream (no measurable bounds, or an " +
                        "authored '@sheathed' row).");
                return shipped;
            }

            // An owner-authored @sheathed pose is tier 1 — it wins outright.
            if (TryResolveSheathedOffset(_currentOffHandMeshKey, out _, out var src) &&
                src == SheathedOffsetSource.Explicit)
            {
                FlowTrace.Throttle("Equip", $"sheath-derive-skip-{_currentOffHandMeshKey}", 5f,
                    $"sheathed shield '{_currentOffHandMeshKey}': derivation SKIPPED — an authored " +
                    "'@sheathed' row exists and outranks it (precedence: authored -> manual -> derived).");
                return shipped;
            }

            Transform body = _animator != null ? _animator.transform : transform;
            // Outward = the hip side the shield hangs on: it lies against the leg, face out.
            //
            // ⛔ THE SHIELD DOES *NOT* TAKE THE SWORD'S "INVERTED" RULE (2026-08-20, second pass).
            // The line here read `body.up * (_sheatheLongAxisSign >= 0f ? 1f : -1f)` with the comment
            // "the SAME vertical/inverted long axis the sword obeys, so one ruling drives both
            // props" — and generalising the owner's sentence from the prop it was about to a prop it
            // was not is the whole mistake. Her instruction, verbatim: "sheathed should sit inverted
            // with the longest mesh (y) up and down". That is a statement about a SWORD, where the
            // long axis IS the blade and "inverted" means tip-down in a scabbard. A shield has no
            // meaningful long axis to invert and no tip to point anywhere: turning a roughly
            // symmetric plate end-for-end is a no-op the player cannot see, so the constraint buys
            // nothing — while adding a second constraint to an over-determined solve is exactly how
            // a mis-measured axis gets to decide the pose. What matters for a shield is the rule
            // that was already felt-approved (WO-1123): the thickness faces AWAY FROM THE PLAYER and
            // the handled face is against the mount. That rule is now applied at the HIP anchor —
            // the anchor is all that the 08-20 instruction changes for this prop.
            //
            // `up` is still passed, because LookRotation needs a second axis to resolve the roll;
            // it is plain body.up, so the shield stands the way a shield stands.
            // _sheatheLongAxisSign is SWORD-ONLY and is not read here.
            // OUTWARD FOLLOWS THE ANCHOR, NOT A CONSTANT (owner F8 2026-08-21). On the ARM mount the
            // prop is on the hero's LEFT, so "away from the player" is -body.right; on the hip
            // fallback it is the right hip and +body.right. OffHandSheatheSide() is the same call the
            // POSITION above uses, so the plate cannot be pushed out one side while facing the other.
            Vector3 outward = OffHandShieldOutwardWorld();
            // longUp stays plain body.up even on the forearm. A standing hero's forearm hangs
            // roughly vertical, so this reads as a shield standing the way a shield stands, and it
            // keeps the felt-approved WO-1123 rule intact rather than opening a second creative
            // question in a defect fix. If the owner later wants the plate to track the forearm
            // through animation, THIS is the seam — swap in the arm bone's own long axis here, and
            // nowhere else.
            Vector3 longUp  = body.up;
            Quaternion derived = WeaponOrientHelper.ComputeShieldMountRotation(
                _currentOffHandShieldFrame, socket, outward, longUp);
            // ⛔ PROD-019 / Seeker 2026-08-29 live logcat: a permanent +180° LongAxis flip on
            // ShieldWithItemLogic produced faceOffOutward≈180 while SURFACE-SEATED — the pose
            // code believed it was healthy and the owner saw NO readable plate (sword only).
            // Thickness must face ALONG outward (away from the player / toward the camera rear
            // cant). Do NOT re-introduce an asset-specific 180 flip without a new seat-proof
            // line that shows faceOff≈0 AND a gameplay-camera silhouette.

            // THROTTLED (1/5s): this runs every frame and an unthrottled Step here is exactly the
            // spam that swallowed three F8 captures (see ApplySheathedOffset's note).
            //
            // ⚠ AND THE MEASURED EXTENTS ARE PRINTED WITH THEM. faceOff/longTilt are angles between
            // the pose and the FRAME's axes: they read 0/0 in the capture that showed a flat shield,
            // because the frame itself named the wrong axes. Without the extents beside them the
            // reader has to back-solve the world AABB by hand (which is how the 08-20 defect was
            // actually found) instead of seeing "narrowest=Y(0.78)" and knowing in one line.
            // MEASURE THE PROP'S OWN AXES, not +X/+Y. The frame's ThicknessAxis/LongAxis are whichever
            // extents MEASURED shortest/longest (a native prop keeps its authored axes — the live
            // default shield is one), so the old `worldRot * Vector3.right` was only the face by
            // luck. A trace that assumes the post-normalize permutation reports a healthy angle for
            // a prop that is sideways, which is the class of mistake this whole path exists to end.
            Quaternion worldRot = socket.rotation * derived;
            Vector3 faceWorld = worldRot * _currentOffHandShieldFrame.ThicknessAxis;
            Vector3 longWorld = worldRot * _currentOffHandShieldFrame.LongAxis;
            float faceOff = Vector3.Angle(faceWorld, outward);
            float longTilt = Vector3.Angle(longWorld, longUp);
            FlowTrace.Throttle("Equip", $"sheath-derived-{_currentOffHandMeshKey}", 5f,
                $"sheathed shield '{_currentOffHandMeshKey}' DERIVED: thickness faces away from the " +
                $"player at the HIP; localEuler={derived.eulerAngles:0.#} faceOffOutward={faceOff:0.#}deg " +
                $"longTiltFromVertical={longTilt:0.#}deg longAxisDotUp=" +
                $"{Vector3.Dot(longWorld, body.up):0.##} " +
                $"frame[{_currentOffHandShieldFrame.Axes.Describe()}] " +
                "(if 'narrowest' is not the plate's thinness the pose is right about a WRONG frame — " +
                "that reads as a flat shield however healthy these angles look) " +
                $"(shipped constant would have been {shipped.eulerAngles:0.#}).");
            return derived;
        }

        // Force the given slot's prop to its DRAWN (in-hand) seat regardless of combat state — used by
        // the in-game Seating Editor so a tune session always edits the in-hand grip, never the sheathed
        // back pose. No-op if that slot has no prop / no resolved hand yet.
        private void DrawForEditing(bool offHand)
        {
            if (offHand)
            {
                if (_currentOffHandProp != null)
                {
                    var t = _currentOffHandProp.transform;
                    Transform sock = _currentOffHandKind == WeaponClass.Shield
                        ? GearSeat.ResolveMount(_animator, transform, WeaponArchetype.Shield).Mount
                        : _offHandHand;
                    if (sock != null)
                    {
                        t.SetParent(sock, false);
                        t.localPosition = _offHandDrawnLocalPos;
                        t.localRotation = _offHandDrawnLocalRot;
                    }
                }
            }
            else if (_gripRoot != null && _weaponHand != null)
            {
                _gripRoot.SetParent(_weaponHand, false);
                _gripRoot.localPosition = _weaponDrawnLocalPos;
                _gripRoot.localRotation = _baseGripRot;
            }
        }

        // Build the sword grip-root's LOCAL rotation (in the hand bone's space) so the blade
        // extends forward from the fist. The prop's blade/grip line is local +Y; we rotate
        // the grip root so its +Y points along the hand bone's _handBladeAxis and its +Z
        // along the hand bone's _handGripUpAxis — i.e. the prop frame is rebuilt to match the
        // hand's natural "point" + grip axes rather than assuming a world-aligned bone. The
        // serialized _swordGripEuler is then applied in that corrected local frame as a final
        // calibration nudge (e.g. tip the blade forward-and-slightly-up). Rig-specific: the
        // axis choices are exposed so they can be re-picked in the Inspector without a recompile.
        // WO-435: generalized to ALL melee. <paramref name="kind"/> selects the per-archetype
        // calibration nudge (sword/dagger -> _swordGripEuler; staff/wand/axe/mace -> their own
        // field, defaulted 0). The rig-hand-axis basis is identical across families — every melee
        // weapon's primary axis is prop-local +Y (placed there by NormalizeInto + SeatByHandle),
        // so the same blade/grip-up axes seat it forward-from-the-fist; only the residual nudge differs.
        private Quaternion ComputeMeleeGripRotation(WeaponClass kind) =>
            ComposeMeleeGripRotation(_handBladeAxis, _handGripUpAxis, MeleeGripNudge(kind));

        // ── WO-1226: THE DRAWN MELEE COMPOSITION, EXTRACTED AS ONE PURE FUNCTION ────────────────
        // Byte-for-byte the body ComputeMeleeGripRotation used to carry — nothing about the math
        // moved. It is `public static` so a HEADLESS REGRESSION CAN DRIVE THE SHIPPED COMPOSITION
        // ITSELF instead of re-typing it, which is the mistake that let six prior fixes each assert
        // a rotation the game never applied ("derivation is not self-proving", 2026-08-16).
        //
        // ⭐ READ THE ALGEBRA BEFORE THEORISING ABOUT THIS METHOD. With the SHIPPED serialized
        // defaults — _handBladeAxis (0,1,0), _handGripUpAxis (0,0,1) — the line below is
        //     Quaternion.LookRotation(forward:(0,0,1), upwards:(0,1,0)) == Quaternion.identity
        // so for any archetype whose nudge is ZERO this whole "rig-aware derivation" is the
        // IDENTITY, and the prop's long axis (prop-local +Y, put there by NormalizeInto) lands on
        // the HAND BONE'S RAW LOCAL +Y. That is the sword rule, and it is doing precisely what it
        // is told — nothing here is "90 degrees wrong". What it is told is the ARCHETYPE NUDGE.
        // ⭐ UPDATED 2026-08-26: the staff no longer feeds this a zero. Owner ruling — *"staff drawn
        // is showing horizontal"* / *"should be up and down vertical"* — put the drawn staff's
        // correction at StaffDrawnGripNudgeDefault (90,0,0), which moves the shaft off the blade
        // axis and onto the grip-up axis, i.e. the body's vertical. The full arithmetic lives on
        // that field. See TraceSeatChain for the measurement that proves it on device.
        public static Quaternion ComposeMeleeGripRotation(Vector3 handBladeAxis, Vector3 handGripUpAxis,
                                                          Vector3 archetypeNudge)
        {
            Vector3 blade = handBladeAxis.sqrMagnitude > 1e-6f ? handBladeAxis.normalized : Vector3.up;
            Vector3 up    = handGripUpAxis.sqrMagnitude > 1e-6f ? handGripUpAxis.normalized : Vector3.forward;

            // Orthonormalize `up` against `blade` so the basis is valid even if the two
            // chosen axes aren't perfectly perpendicular on this rig.
            up = up - Vector3.Dot(up, blade) * blade;
            if (up.sqrMagnitude < 1e-6f)
            {
                // Degenerate (axes parallel) — pick any axis not collinear with the blade.
                up = Mathf.Abs(blade.y) < 0.9f ? Vector3.up : Vector3.forward;
                up = up - Vector3.Dot(up, blade) * blade;
            }
            up.Normalize();

            // Rotation mapping prop-local (+Y up, +Z forward) onto (blade, up): Quaternion
            // .LookRotation builds a frame whose +Z = forward, +Y = up. We want the prop's
            // +Y (its primary line) to land on `blade` and its +Z (its flat-plane normal) on
            // `up`, so feed forward=up, upwards=blade.
            Quaternion rigAligned = Quaternion.LookRotation(up, blade);
            return rigAligned * Quaternion.Euler(archetypeNudge);
        }

        // =====================================================================
        //  WO-1226 - THE SEAT CHAIN, MEASURED STEP BY STEP (owner F8 2026-08-26)
        // =====================================================================
        //
        // WHY THIS EXISTS, and why it is not another tilt number. Six commits fixed the
        // MEASUREMENT and the staff still lay across the body. The reason is structural, and this
        // repo already wrote it down on 2026-08-16: "derivation did NOT save the bow: its held
        // rotation was 90 degrees wrong at the ATTACH SEAT - a different failure from the grip
        // POSITION, which measured correct. Derivation is not self-proving."
        //
        // Concretely, the shipped `tiltFromVertical` line inside ComputeSheathRotation measures
        //     (socket.rotation * result) * Vector3.up
        // - the value that method is ABOUT TO RETURN, composed with the socket. It is a true
        // statement about the DERIVER'S OUTPUT and it proves nothing about the prop, because:
        //   1. it assumes grip-root-local +Y IS the prop's long axis (it asks Vector3.up, not the
        //      mesh), so a prop whose long axis landed on local X or Z reads a perfect 0 while
        //      hanging sideways - the flat-shield failure, restated for the main hand;
        //   2. ApplySheathedOffset composes MORE rotation onto the transform on the very next
        //      line, so the measured quaternion is not always the one that ends up on the prop;
        //   3. there is NO equivalent line at all on the DRAWN branch, which is the state the
        //      owner is reporting ("weapon combat still horizontal").
        // So the broken build prints tiltFromVertical=0deg and is telling the truth about the
        // wrong thing. THIS trace measures the prop's OWN measured long axis, transformed through
        // every step of the real chain, AFTER the final write.
        //
        // IT IS A MEASUREMENT AND ONLY A MEASUREMENT. It changes no transform (CLAUDE.md 12:
        // instrument first, and never strip the instrument afterwards).

        /// <summary>One rung of the seat chain, so a capture can name WHICH transform moved the
        /// long axis instead of only reporting where it ended up.</summary>
        public struct SeatedAxisMeasure
        {
            /// <summary>The prop's measured long axis as a unit vector in GRIP-ROOT-local space.
            /// If this is not ~(0,1,0) the seat's whole premise ("prop +Y is the long line") is
            /// already false before any rotation is applied.</summary>
            public Vector3 LocalUnit;
            /// <summary>The same axis after the grip root's own localRotation - i.e. expressed in
            /// the PARENT (hand bone / sheathe socket) frame.</summary>
            public Vector3 ParentUnit;
            /// <summary>The same axis in WORLD, after the parent's world rotation.</summary>
            public Vector3 WorldUnit;
            /// <summary>Angle between the seated long axis and the body's vertical, FOLDED to
            /// 0..90: a tip-down staff is upright, not 180 deg wrong. ~0 = standing, ~90 = lying
            /// across the body, which is the owner's report in one number.</summary>
            public float TiltFromVerticalDeg;
        }

        /// <summary>
        /// PURE. Pushes a prop-local long-axis unit vector through the two rotations that make up a
        /// seat and reports every intermediate. Public + static so the headless regression asserts
        /// the SEATED WORLD ROTATION through this exact function rather than re-deriving it.
        /// </summary>
        public static SeatedAxisMeasure MeasureSeatedLongAxis(Quaternion parentWorldRot,
                                                              Quaternion gripLocalRot,
                                                              Vector3 longAxisLocalUnit,
                                                              Vector3 bodyUp)
        {
            var m = new SeatedAxisMeasure();
            m.LocalUnit  = longAxisLocalUnit.sqrMagnitude > 1e-9f ? longAxisLocalUnit.normalized : Vector3.up;
            m.ParentUnit = (gripLocalRot * m.LocalUnit).normalized;
            m.WorldUnit  = (parentWorldRot * m.ParentUnit).normalized;
            Vector3 up = bodyUp.sqrMagnitude > 1e-9f ? bodyUp.normalized : Vector3.up;
            float a = Vector3.Angle(m.WorldUnit, up);
            m.TiltFromVerticalDeg = Mathf.Min(a, 180f - a);   // undirected: a tip-down staff is upright
            return m;
        }

        /// <summary>Unit vector along a measured axis role (0=X, 1=Y, 2=Z).</summary>
        private static Vector3 AxisUnit(int axis) =>
            axis == 0 ? Vector3.right : axis == 2 ? Vector3.forward : Vector3.up;

        /// <summary>
        /// THE PER-STEP SEAT TRACE. Called at the END of each ApplyHoldPose main-weapon branch -
        /// after the final localRotation write and after ApplySheathedOffset - so what it reads is
        /// what the player is looking at, never a deriver's return value. Throttled (ApplyHoldPose
        /// re-asserts at frame rate) and keyed on state so DRAWN and SHEATHED cannot share a slot
        /// the way the weapon and the shield once shared the compensate slot.
        /// </summary>
        private void TraceSeatChain(string state, Transform parent)
        {
            if (_gripRoot == null || parent == null) return;
            GameObject prop = _gripRoot.childCount > 0 ? _gripRoot.GetChild(0).gameObject : null;
            if (prop == null) return;
            Transform body = _animator != null ? _animator.transform : transform;

            // The long axis is MEASURED off the prop's mesh bounds in the grip root's frame -
            // mesh.bounds, never vertices: shipped props may import with Read/Write OFF, which
            // makes a vertex approach SILENTLY INERT ON DEVICE while looking right in the editor.
            if (!Guard.Try("Equip", "TraceSeatChain measure",
                    () => WeaponOrientHelper.TryMeasureAxes(prop, _gripRoot, out _seatChainAxes), false))
                return;

            Vector3 localUnit = AxisUnit(_seatChainAxes.LongestAxis);
            var m = MeasureSeatedLongAxis(parent.rotation, _gripRoot.localRotation, localUnit, body.up);

            // Step 1 is the clause the shipped tiltFromVertical line silently ASSUMES. Say it out
            // loud: a long axis that is not Y in the grip root's frame means every downstream angle
            // is a true statement about the wrong direction.
            bool longAxisIsY = _seatChainAxes.LongestAxis == 1;
            bool upright = m.TiltFromVerticalDeg <= 30f;

            string line =
                "SEAT CHAIN [" + state + "] main-hand id='" +
                (string.IsNullOrEmpty(_currentWeaponId) ? "<none>" : _currentWeaponId) + "' mesh='" +
                (string.IsNullOrEmpty(_currentWeaponMeshKey) ? "<none>" : _currentWeaponMeshKey) + "' " +
                $"kind={_currentWeaponKind} on '{name}'\n" +
                $"  step1 PROP->GRIPROOT : {_seatChainAxes.Describe()} " +
                $"longAxisLocal={m.LocalUnit.ToString("0.###")} " +
                $"propLocalEuler={prop.transform.localRotation.eulerAngles.ToString("0.#")} " +
                (longAxisIsY
                    ? "(long axis IS grip-local +Y - the seat's premise holds)"
                    : "LONG AXIS IS NOT GRIP-LOCAL +Y - every angle below is a true statement about " +
                      "the WRONG direction; fix the seat (NormalizeInto / the authored native frame), " +
                      "not the sign") + "\n" +
                $"  step2 GRIPROOT.LOCAL : localEuler={_gripRoot.localRotation.eulerAngles.ToString("0.#")} " +
                $"localPos={_gripRoot.localPosition.ToString("0.###")} " +
                $"localScale={_gripRoot.localScale.ToString("0.###")} " +
                $"-> longAxis in PARENT frame={m.ParentUnit.ToString("0.###")}\n" +
                $"  step3 PARENT (SEAT)  : '{parent.name}' " +
                $"worldEuler={parent.rotation.eulerAngles.ToString("0.#")} " +
                $"lossy={parent.lossyScale.ToString("0.###")}\n" +
                $"  step4 SEATED WORLD   : longAxisWorld={m.WorldUnit.ToString("0.###")} " +
                $"tiltFromVertical={m.TiltFromVerticalDeg:0.#}deg " +
                "(~0 = standing; ~90 = LYING ACROSS THE BODY) " +
                $"bodyUp={body.up.ToString("0.###")} " +
                $"dotBodyUp={Vector3.Dot(m.WorldUnit, body.up):0.##} " +
                $"dotBodyFwd={Vector3.Dot(m.WorldUnit, body.forward):0.##} " +
                $"dotBodyRight={Vector3.Dot(m.WorldUnit, body.right):0.##}";

            // WHICH STEP MOVED IT is the whole point, so name it rather than leaving the reader
            // to diff three vectors: step 2 is the seat rotation this controller composed, step 3
            // is the animated bone it inherited. A drawn melee prop with a ZERO archetype nudge has
            // an IDENTITY step 2 (see ComposeMeleeGripRotation) - every degree then comes from the
            // BONE, which is the sword rule being applied to a prop that is not a sword.
            float step2Turn = Vector3.Angle(m.LocalUnit, m.ParentUnit);
            float step3Turn = Vector3.Angle(m.ParentUnit, m.WorldUnit);
            line += "\n  ATTRIBUTION        : step2 (this controller's seat rotation) turned the long axis " +
                    $"{step2Turn:0.#}deg; step3 (the '{parent.name}' bone's world rotation) turned it a " +
                    $"further {step3Turn:0.#}deg. " +
                    (step2Turn <= 1f
                        ? "STEP 2 IS THE IDENTITY: this controller applied NO archetype correction, so " +
                          "the prop inherited the bone's raw axes verbatim. That is a seat, not a derivation."
                        : "step 2 is doing real work.");

            if (upright && longAxisIsY)
                FlowTrace.Throttle("Equip", "seat-chain-" + state + "-" + name, 5f, line + "  - ok.");
            else
                FlowTrace.Throttle("Equip", "seat-chain-" + state + "-" + name, 5f, line +
                    "\n  THIS PROP IS NOT STANDING. Report the four steps above verbatim; do NOT " +
                    "flip _sheatheLongAxisSign (WO-1136: that only moves the defect onto the other heroes).");
        }

        // Scratch for TryMeasureAxes' out-parameter: a lambda cannot carry an `out`, and Guard.Try
        // is the section-12 mandated wrapper. One field, written only inside TraceSeatChain.
        private MeasuredAxes _seatChainAxes;

        /// <summary>
        /// WO-1226: the COMPLETE drawn-melee grip-root local rotation for a prop with no authored
        /// offset row — the composition ApplyHoldPose actually writes, global yaw included. One
        /// function so a regression and the game can never disagree about what "the drawn seat" is.
        /// A prop WITH an offsets.json row composes `* Quaternion.Euler(fo.eulerRot)` on top of
        /// this (see the NUDGE branch in AttachLoadedProp); staff_A and tripo_staff_a have no row.
        /// </summary>
        public static Quaternion ComposeDrawnMeleeLocalRotation(Vector3 handBladeAxis,
                                                                Vector3 handGripUpAxis,
                                                                Vector3 archetypeNudge) =>
            ApplyGlobalWeaponYaw(ComposeMeleeGripRotation(handBladeAxis, handGripUpAxis, archetypeNudge));

        // The per-archetype additive calibration nudge (CANON; never auto-overwritten). Sword and
        // dagger share the bladed _swordGripEuler; the rest map to their own inspector field.
        private Vector3 MeleeGripNudge(WeaponClass kind)
        {
            switch (kind)
            {
                case WeaponClass.Sword:
                case WeaponClass.Dagger: return _swordGripEuler;
                case WeaponClass.Staff:  return _staffGripEuler;
                case WeaponClass.Wand:   return _wandGripEuler;
                case WeaponClass.Axe:    return _axeGripEuler;
                case WeaponClass.Hammer: return _maceGripEuler;
                default:                 return Vector3.zero;
            }
        }

        // ── ARMOR VISUAL (WO-567 — static-model TINT, NOT a mesh swap) ───────────────
        /// <summary>
        /// Drive the hero's armor look from the worn tier (0 = none … 5 = legendary). The
        /// combat-pivot north star keeps ONE static hero model — so this does NOT swap a mesh or
        /// revive Blink. Instead it tints the BODY with a tier accent (richer with tier) via a
        /// MaterialPropertyBlock, so equipping better armor is VISIBLE on the static model.
        /// Driven by GearLoadout.PushArmorTierToBody on every equip change, and by the Gear
        /// Preview (HeroPreviewViewer) for the showcase. Cheap + leak-free (MPB, no instancing).
        /// </summary>
        public void SetArmorTier(int tier)
        {
            _armorTier = Mathf.Max(0, tier);
            _armorTintDirty = true;
            ApplyArmorTint();   // body may not be ready yet — stays dirty + retried in Update
        }

        /// <summary>Current armor tier (0 = none).</summary>
        public int ArmorTier => _armorTier;

        // Owner-tunable BONES (OWNER-DECISION: felt-tune freely). Per-tier MULTIPLIER applied to
        // the body's authored base color, so tier 0 restores the original EXACTLY and higher tiers
        // add a metal sheen. Hues track ArmorVfxMap's rarity bands (cool steel → blue → violet →
        // gold) but as an albedo multiply (armor metal), NOT the additive rim GLOW the rim light
        // owns — the two reads compose. Kept gentle so the hero's skin/face never discolors hard.
        private static readonly Color[] ArmorTintByTier =
        {
            new Color(1.00f, 1.00f, 1.00f),  // 0 none — identity (no tint)
            new Color(0.97f, 0.98f, 1.00f),  // 1 common — faint cool steel
            new Color(0.90f, 0.94f, 1.00f),  // 2 uncommon — light steel-blue
            new Color(0.82f, 0.89f, 1.04f),  // 3 rare — cool blue sheen
            new Color(0.88f, 0.80f, 1.04f),  // 4 epic — violet sheen
            new Color(1.05f, 0.92f, 0.64f),  // 5 legendary — warm gold sheen
        };

        private static Color ArmorTintMultiplier(int tier)
        {
            if (tier <= 0) return Color.white;
            int i = Mathf.Clamp(tier, 0, ArmorTintByTier.Length - 1);
            return ArmorTintByTier[i];
        }

        // Apply (or clear) the tier tint on the hero BODY renderers via MPB. MULTIPLIES the captured
        // authored base color by the tier accent (tier 0 = identity restore). MERGE pattern
        // (GetPropertyBlock first) so HeroArmorRimLight's emission set is preserved. No-op (and stays
        // dirty) until the body renderers exist, so an early SetArmorTier re-applies once the body is up.
        private void ApplyArmorTint()
        {
            if (!ResolveBodyRenderers()) return;   // body not ready — stay dirty, retried in Update
            if (_armorMpb == null) _armorMpb = new MaterialPropertyBlock();

            Color mul = ArmorTintMultiplier(_armorTier);
            int applied = 0;
            for (int i = 0; i < _bodyRenderers.Count; i++)
            {
                var smr = _bodyRenderers[i];
                if (smr == null) continue;
                Color a = _bodyBaseColors[i];
                Color tinted = new Color(a.r * mul.r, a.g * mul.g, a.b * mul.b, a.a);
                smr.GetPropertyBlock(_armorMpb);          // merge — keep rim emission etc.
                _armorMpb.SetColor(BaseColorId, tinted);
                _armorMpb.SetColor(ColorId, tinted);      // Built-in/Standard fallback
                smr.SetPropertyBlock(_armorMpb);
                applied++;
            }
            _armorTintDirty = false;
            FlowTrace.Step("Equip",
                $"ApplyArmorTint on '{name}' tier={_armorTier} mul=({mul.r:0.00},{mul.g:0.00},{mul.b:0.00}) -> {applied} body renderer(s).");
        }

        // Resolve + cache the hero BODY SkinnedMeshRenderers (the static model) and snapshot each
        // one's authored base color, so the tint MULTIPLIES (never wipes a baked tint; tier 0 restores
        // exactly). SkinnedMeshRenderer only — the character body — so weapon/shield MeshRenderer props
        // are never tinted. Re-scans when the cache is empty/stale (body swap). False when no body yet.
        private bool ResolveBodyRenderers()
        {
            bool stale = _bodyRenderers.Count == 0;
            for (int i = 0; i < _bodyRenderers.Count && !stale; i++)
                if (_bodyRenderers[i] == null) stale = true;
            if (stale)
            {
                _bodyRenderers.Clear();
                _bodyBaseColors.Clear();
                GetComponentsInChildren(true, _bodyRenderers);
                foreach (var smr in _bodyRenderers)
                {
                    Color c = Color.white;
                    var mat = smr != null ? smr.sharedMaterial : null;
                    if (mat != null)
                    {
                        if (mat.HasProperty(BaseColorId)) c = mat.GetColor(BaseColorId);
                        else if (mat.HasProperty(ColorId)) c = mat.GetColor(ColorId);
                    }
                    _bodyBaseColors.Add(c);
                }
            }
            return _bodyRenderers.Count > 0;
        }

        // ═════════════════════════════════════════════════════════════════════════════
        //  IN-GAME SEATING EDITOR API (WO-577, Offset Forge slice 2)
        //  Drives the offset of the CURRENTLY equipped weapon/off-hand live, by eye, on the
        //  REAL hero — the runtime parallel of the editor-only Offset Forge window. The
        //  SeatingEditorOverlay (DeNelle.Village.UI) is the on-screen UI; this is the model.
        //  what-you-see-is-what-you-save: the preview mirrors the exact attach math so a Save
        //  (-> offsets.json via AttachmentOffsetRegistry) reproduces the previewed pose on the
        //  next equip / scene load. DEV-only — gated by the caller (AdminOverlay dev tools).
        // ═════════════════════════════════════════════════════════════════════════════

        /// <summary>Snapshot handed to the editor when an edit session begins.</summary>
        public struct SeatingEditInfo
        {
            public bool    valid;
            public bool    offHand;
            public bool    sheathed;      // true = editing the BACK (sheathed) pose (key gets "@sheathed")
            public string  offsetKey;     // what the offset is saved under (mesh name [+ "@sheathed"])
            public string  label;         // human label (weapon/off-hand id)
            public bool    melee;
            public Vector3 pos;           // seeded from any existing saved offset
            public Vector3 euler;
            public float   scale;
            public bool    fullOverride;
        }

        /// <summary>True while a seating edit session is live (auto hold suspended).</summary>
        public bool SeatingEditActive => _seatingEditActive;

        /// <summary>Does the requested slot currently have an equipped prop to edit?</summary>
        public bool HasSeatingTarget(bool offHand) =>
            (offHand ? _currentOffHandProp : _currentWeaponProp) != null;

        /// <summary>
        /// Begin editing the offset of the equipped <paramref name="offHand"/> slot. Seeds the
        /// returned info from any existing saved offset, suspends the auto idle/combat hold, and
        /// applies the (seeded) preview so the live model immediately reflects the editable pose.
        /// Returns false (info.valid=false) when that slot has no prop equipped.
        /// </summary>
        public bool BeginSeatingEdit(bool offHand, out SeatingEditInfo info)
            => BeginSeatingEdit(offHand, false, out info);

        /// <summary>
        /// Overload (2026-07-07): <paramref name="sheathed"/>=true edits the BACK (sheathed) pose —
        /// the prop is forced to the back socket (not drawn to the hand), the offset is keyed
        /// "&lt;meshKey&gt;@sheathed", and the preview runs in the back-socket frame per the
        /// ApplyHoldPose consumption contract. This makes the town carry pose owner-authorable via
        /// the SAME registry the drawn seat uses (the root fix for "my offsets are invisible in town").
        /// </summary>
        public bool BeginSeatingEdit(bool offHand, bool sheathed, out SeatingEditInfo info)
        {
            info = default;
            var grip = offHand ? _currentOffHandProp : _currentWeaponProp;
            if (grip == null)
            {
                FlowTrace.Warn("Offset", $"BeginSeatingEdit: no {(offHand ? "off-hand" : "weapon")} prop equipped on '{name}'.");
                return false;
            }

            string key = offHand ? _currentOffHandMeshKey : _currentWeaponMeshKey;
            string id  = offHand ? _currentOffHandId      : _currentWeaponId;
            string baseKey = !string.IsNullOrEmpty(key) ? key : id;
            AttachmentOffset fo = default;
            SheathedOffsetSource src = SheathedOffsetSource.None;
            bool has = false;
            if (!string.IsNullOrEmpty(baseKey))
            {
                if (sheathed)
                    has = TryResolveSheathedOffset(baseKey, out fo, out src);
                else
                    has = AttachmentOffsetRegistry.TryGetOffset(baseKey, out fo);
            }
            string offsetKey = sheathed && !string.IsNullOrEmpty(baseKey)
                ? baseKey + SheathedKeySuffix
                : baseKey;

            info.valid        = true;
            info.offHand      = offHand;
            info.sheathed     = sheathed;
            info.offsetKey    = offsetKey;
            info.label        = !string.IsNullOrEmpty(id) ? id : offsetKey;
            info.melee        = !offHand && _currentWeaponMelee;
            info.pos          = has ? fo.pos      : Vector3.zero;
            info.euler        = has ? fo.eulerRot : Vector3.zero;
            info.scale        = has && fo.scale > 0f ? fo.scale : 1f;
            if (sheathed)
                info.fullOverride = has && src == SheathedOffsetSource.Explicit && fo.fullOverride;
            else
                info.fullOverride = has ? fo.fullOverride : true;

            _seatingEditActive = true;
            _seatEditOffHand   = offHand;
            _seatEditSheathed  = sheathed;
            _seatEditMode      = -1;   // force a re-seat on the first preview (drawn mode)
            if (sheathed)
            {
                // Sheathed edit tunes the HIP pose — force the sheathed placement (ApplyHoldPose
                // treats combat as inactive while _seatEditSheathed) so the preview math runs in
                // the sheathe-socket frame the runtime consumption uses. The socket asked for is
                // THIS SLOT's (2026-08-20): the two slots no longer share one, so checking the
                // weapon's socket would report a healthy edit for a shield that has none.
                ApplyHoldPose();
                if (ResolveSheatheSocket(offHand) == null)
                    FlowTrace.Warn("Offset", $"BeginSeatingEdit(sheathed): no sheathe socket on '{name}' " +
                        $"for the {(offHand ? "off-hand" : "main-hand")} slot — sheathed preview would run " +
                        "in the hand frame; pose may not reproduce.");
            }
            else
            {
                // The drawn edit tunes the IN-HAND seat — make sure the edited prop is DRAWN to its
                // hand (not sheathed on the back), so the preview math runs in the correct frame.
                DrawForEditing(offHand);
            }
            ApplySeatingPreview(info.pos, info.euler, info.scale, info.fullOverride);
            FlowTrace.Step("Offset", $"BeginSeatingEdit '{info.offsetKey}' offHand={offHand} sheathed={sheathed} seed pos={info.pos} euler={info.euler} scale={info.scale:0.###} full={info.fullOverride}");
            return true;
        }

        /// <summary>
        /// Live-preview an offset on the equipped slot being edited. Mirrors the attach seat math
        /// so the preview == the saved runtime result. <paramref name="fullOverride"/> true =
        /// VERTICAL baseline (hilt-lower-half for melee) + this rotation as the absolute in-hand
        /// pose; false = NUDGE on top of the geometric rig-aware grip (legacy WO-551). Re-seats the
        /// prop child only when the baseline mode flips (cheap; no destroy / async reload).
        /// </summary>
        public void ApplySeatingPreview(Vector3 pos, Vector3 euler, float scale, bool fullOverride)
        {
            bool offHand = _seatEditOffHand;
            var grip = offHand ? _currentOffHandProp : _currentWeaponProp;
            if (grip == null) return;
            var grt = grip.transform;
            if (grt.childCount == 0) return;

            // SHEATHED edit (2026-07-07): pos/euler live in the BACK-SOCKET frame per the
            // ApplyHoldPose consumption contract — no hand-grip composition, no global yaw.
            if (_seatEditSheathed)
            {
                ApplySheathedSeatingPreview(grt, offHand, pos, euler, fullOverride);
                return;
            }

            var child = grt.GetChild(0).gameObject;

            bool    melee   = !offHand && _currentWeaponMelee;
            float   held    = offHand ? _currentOffHandHeldLength : _currentWeaponHeldLength;
            Vector3 gripPos = offHand ? _currentOffHandGripPos    : _currentWeaponGripPos;
            if (scale <= 0f) scale = 1f;

            int wantMode = fullOverride ? 1 : 0;
            bool nativeMeleePreview = melee && _currentWeaponNative && !FeatureFlags.WeaponGripInfer;
            if (_seatEditMode != wantMode)
            {
                // Reset the grip root so the child re-seat math (measured in parent-local) is clean.
                grt.localRotation = Quaternion.identity;
                grt.localScale    = Vector3.one;
                grt.localPosition = Vector3.zero;
                if (nativeMeleePreview && wantMode == 0)
                {
                    SeatNative(child, grt, held > 0f ? held : 1f);
                }
                else
                {
                    // WO-1105 R4: the Seating-Editor preview must seat a bow the SAME way the attach
                    // path does, or the preview would show a grip the game never uses.
                    WeaponClass previewKind = offHand ? WeaponClass.Shield : _currentWeaponKind;
                    NormalizeInto(child, grt, held > 0f ? held : 1f,
                        ResolveHiltFromKind(previewKind), ResolveGripAnchorFromKind(previewKind));
                    // WO-1431: the preview MUST dispatch the grip on the same archetype + the same
                    // derivability the attach path used, or the owner dials a nudge against an
                    // 0.18 baseline and the game ships against an 0.75 one — two baselines, the
                    // exact class of drift the shield's sheathed preview was fixed for
                    // (docs/WEAPON_ARMOR_ORIENT_LOGIC.md: "the Seating Editor preview shares the
                    // same method so the two can never disagree"). Both fields are captured at
                    // attach; an off-hand preview keeps the bladed rule as before.
                    if (melee)
                        SeatMeleeGripPoint(child, grt,
                            offHand ? WeaponArchetype.Unknown : _currentWeaponArchetype,
                            !offHand && _currentWeaponDerivable,
                            (_currentWeaponMeshKey ?? "<unkeyed>") + " [seating-preview]");
                }
                _seatEditMode = wantMode;
                // WO-1123: re-measure the shield frame AGAINST THE PREVIEW'S OWN SEAT. The cached
                // attach-time frame was measured on the runtime seat, which for a NATIVE shield is
                // SeatNative — while this preview re-seats through NormalizeInto. Reusing the attach
                // frame across that difference would pose the preview off the wrong axis. Measured
                // here, inside the mode-change branch, so it costs one vertex walk per mode flip and
                // not one per drag frame.
                _previewShieldFrame = default;
                if (offHand && _currentOffHandKind == WeaponClass.Shield)
                    Guard.Try("Offset", "seating-preview ShieldFrame",
                        () => WeaponOrientHelper.TryResolveShieldFrame(child, grt, out _previewShieldFrame));
            }

            // Compose the grip-root transform exactly as the attach path does for this mode.
            Quaternion baseRot;
            if (fullOverride)
                baseRot = Quaternion.identity;                                   // delta IS the pose
            else if (nativeMeleePreview)
            {
                baseRot = Quaternion.Euler(_currentWeaponGripEuler) *
                          Quaternion.Euler(MeleeGripNudge(_currentWeaponKind));
            }
            else if (melee)
                baseRot = ComputeMeleeGripRotation(_currentWeaponKind);          // rig-aware grip
            else
                baseRot = Quaternion.Euler(offHand ? _currentOffHandGripEuler : _currentWeaponGripEuler);

            // WYSIWYG for the DERIVED BOW seat (2026-08-16). The attach path now derives a bow's
            // grip and withholds the global yaw; if this preview kept the raw euler + yaw it would
            // render the horizontal bow the game no longer ships, and the owner would dial a nudge
            // to cancel a pose that does not exist at runtime. `offHand` is excluded on purpose —
            // the off-hand slot is the SHIELD's, previewed and attached unchanged.
            bool bowPreview = !offHand && !fullOverride && !nativeMeleePreview && !melee &&
                              _currentWeaponKind == WeaponClass.Bow && grt.parent != null;
            if (bowPreview)
            {
                Transform previewBody = _animator != null ? _animator.transform : transform;
                baseRot = Guard.Try("Equip", "seating-preview ComputeBowHeldRotation",
                    () => WeaponBoundsOrient.ComputeBowHeldRotation(grt.parent, previewBody),
                    Quaternion.identity) * baseRot;
            }

            // WYSIWYG for the DERIVED SHIELD seat (WO-1123) — the same reasoning as the bow above.
            // The bow's comment says "the off-hand slot is the SHIELD's, previewed and attached
            // unchanged"; that stopped being true today. Now that the drawn shield derives its pose
            // and withholds the global yaw, a preview still showing the preset euler + yaw would
            // render a shield the game does not ship, and the owner would dial a delta to cancel a
            // pose that does not exist at runtime — the exact WO-994 class of bug.
            bool shieldPreview = offHand && !fullOverride && _currentOffHandDerivable &&
                                 _previewShieldFrame.Valid && grt.parent != null;
            if (shieldPreview)
            {
                Transform shieldPreviewBody = _animator != null ? _animator.transform : transform;
                baseRot = WeaponOrientHelper.ComputeShieldMountRotation(
                    _previewShieldFrame, grt.parent, shieldPreviewBody.forward, shieldPreviewBody.up);
            }

            grt.localPosition = gripPos + pos;
            grt.localRotation = bowPreview || shieldPreview
                ? baseRot * Quaternion.Euler(euler)                    // derived: no global yaw
                : ApplyGlobalWeaponYaw(baseRot * Quaternion.Euler(euler));
            // WYSIWYG break proven 2026-07-07: preview lacked compensate (hand lossy 1.666) —
            // owner-dialed 0.46 rendered 0.276 at boot. Mirror the runtime scale composition
            // EXACTLY: a compensated slot renders ParentScaleCompensation(parent) * scale —
            // the SAME helper CompensateParentScale (attach + hold-pose) composes from.
            bool compensate = offHand ? _offHandParentCompensate : _weaponParentCompensate;
            grt.localScale = (compensate ? ParentScaleCompensation(grt.parent) : Vector3.one) * scale;
            // The line above is a SECOND WRITER to the same localScale the change-gated
            // CompensateParentScale owns (seat-trace fix 2026-08-18). Invalidate that slot's recorded state so the
            // next hold-pose re-solve actually runs instead of short-circuiting on stale inputs and
            // leaving the editor's preview scale live in the game. (Before the gate, the per-frame
            // re-solve made this self-healing by brute force; now it must be said out loud.)
            if (offHand) _offHandCompState = default; else _weaponCompState = default;

            // Keep the editor's mirror of base state coherent so a later EndSeatingEdit / hold
            // re-apply uses the previewed orientation as the base (no snap-back).
            if (!offHand)
            {
                _baseGripRot   = grt.localRotation;
                _baseGripEuler = _baseGripRot.eulerAngles;
            }
        }

        // SHEATHED-pose preview (2026-07-07): applies pos/euler in THIS SLOT'S SHEATHE-SOCKET frame
        // exactly per ApplySheathedOffset's consumption contract, so what the owner dials on the
        // sheathed pose is byte-identical to what ApplyHoldPose reproduces in town on every boot:
        //   • fullOverride=true  → localPosition = pos, localRotation = Euler(euler) — absolute in
        //     the socket frame, NO global yaw (the authored value IS the pose).
        //   • fullOverride=false → nudge composed on the built-in sheathe pose (derived
        //     ComputeSheathRotation for the weapon; _sheatheOffHandLocal* default for the shield).
        // Scale is deliberately untouched — the sheathe never owns scale (the attach path does).
        private void ApplySheathedSeatingPreview(Transform grt, bool offHand,
            Vector3 pos, Vector3 euler, bool fullOverride)
        {
            // THIS SLOT's socket (2026-08-20). Previewing the shield in the WEAPON's socket would
            // dial a nudge against a pose on the other hip — the WO-994 lesson, one hip over.
            Transform socket = ResolveSheatheSocket(offHand);
            // ⚠ RESOLVE THE SIDE **AFTER** THE SOCKET, NEVER BEFORE. ResolveSheatheSocket is what
            // decides (and records) whether the off-hand landed on the ARM or fell back to the HIP,
            // and the side + offset pair must follow the anchor that was really resolved. Reading
            // OffHandSheatheSide() first would sample a stale flag on the very first preview of a
            // session — a preview dialled in the wrong frame is the WO-994 lesson restated.
            float sideSign = offHand ? OffHandSheatheSide() : SheatheSideMain;
            if (socket == null)
            {
                FlowTrace.Warn("Offset", $"ApplySheathedSeatingPreview: no sheathe socket on '{name}' for the " +
                    $"{(offHand ? "off-hand" : "main-hand")} slot — cannot preview the sheathed pose.");
                return;
            }
            if (grt.parent != socket)
            {
                grt.SetParent(socket, false);
                bool comp = offHand ? _offHandParentCompensate : _weaponParentCompensate;
                if (comp)
                    CompensateParentScale(grt, offHand ? _offHandAuthoredScale : _weaponAuthoredScale,
                        SeatSubject(offHand ? "off-hand(preview)" : "main-hand(preview)",
                                    offHand ? _currentOffHandId : _currentWeaponId,
                                    offHand ? _currentOffHandMeshKey : _currentWeaponMeshKey),
                        ref _previewCompState);
            }

            Vector3    basePos;
            Quaternion baseRot;
            if (offHand)
            {
                // The SAME body-space → socket-local conversion the runtime uses, or the preview
                // sits somewhere the game never puts it.
                // The SAME anchor-dependent offset the runtime picks (arm vs hip fallback) — a
                // preview using the hip's 0.26 m against an ARM anchor would render a quarter-metre
                // from where the game seats it, which is exactly the WYSIWYG break this method exists
                // to prevent.
                basePos = ComputeSheathLocalPosition(socket,
                    _sheatheSocketOffIsArm ? _armOffHandLocalPos : _sheatheOffHandLocalPos, sideSign);
                // WO-1123: the SAME base the runtime sheathe uses (derived when the geometry answers,
                // the shipped constant otherwise). If these two ever diverge again the owner dials a
                // nudge against a sheathed pose the game never renders — the WO-994 lesson.
                baseRot = ComputeSheathedOffHandRotation(socket);
            }
            else
            {
                basePos = ComputeSheathLocalPosition(socket, _sheatheWeaponLocalPos, sideSign);
                // BOW: sheathed and drawn are the SAME pose (owner ruling 2026-08-16), so the
                // preview must show the derived upright carry, not a shared melee carry — otherwise
                // the owner dials a nudge against a sheathed pose the game never renders.
                // Bow-only; every melee family still previews ComputeSheathRotation unchanged, and
                // the shield is the offHand branch above, untouched.
                baseRot = _currentWeaponKind == WeaponClass.Bow
                    ? Guard.Try("Offset", "sheathed-preview ComputeBowHeldRotation",
                        () => WeaponBoundsOrient.ComputeBowHeldRotation(
                                  socket, _animator != null ? _animator.transform : transform),
                        Quaternion.identity)
                    : ComputeSheathRotation(socket, sideSign);
            }

            if (fullOverride)
            {
                grt.localPosition = pos;
                grt.localRotation = Quaternion.Euler(euler);
            }
            else
            {
                grt.localPosition = basePos + pos;
                grt.localRotation = baseRot * Quaternion.Euler(euler);
            }
        }

        /// <summary>
        /// Persist the edited offset to offsets.json (via AttachmentOffsetRegistry) under the slot's
        /// id, reload the registry, and keep the live preview. Returns the writable dev path + a
        /// copy-pasteable JSON snippet for the owner to bake into the repo offsets.json. In the
        /// editor it also writes the repo file directly. FlowTrace'd per §12.
        /// </summary>
        public bool SaveSeating(Vector3 pos, Vector3 euler, float scale, bool fullOverride,
                                out string devPath, out string snippet)
        {
            devPath = null; snippet = null;
            string key = _seatEditOffHand ? _currentOffHandMeshKey : _currentWeaponMeshKey;
            if (string.IsNullOrEmpty(key)) key = _seatEditOffHand ? _currentOffHandId : _currentWeaponId;
            if (string.IsNullOrEmpty(key))
            {
                FlowTrace.Fail("Offset", "SaveSeating: no offset key for the edited slot — nothing saved.");
                return false;
            }
            // Sheathed edits persist under "<key>@sheathed" — verified the registry does NO key
            // sanitization ('@' passes save/load/remove untouched), so the drawn entry is never clobbered.
            if (_seatEditSheathed) key += SheathedKeySuffix;
            if (scale <= 0f) scale = 1f;

            bool ok = AttachmentOffsetRegistry.SaveOffset(key, pos, euler, scale, fullOverride, out devPath, out snippet);
            if (!ok)
            {
                FlowTrace.Step("Offset", $"SaveSeating '{key}': WRITE FAILED (see warnings).");
                return false;
            }

            FlowTrace.Step("Offset",
                $"SaveSeating '{key}': pos={pos} euler={euler} scale={scale:0.###} full={fullOverride} -> {devPath}");

            // Re-seat from the persisted local config immediately — preview-only was the WYSIWYG gap.
            bool offHand    = _seatEditOffHand;
            bool sheathed   = _seatEditSheathed;
            _seatingEditActive = false;
            _seatEditSheathed  = false;
            _seatEditMode      = -1;
            EquipBestForHero();
            ApplyHoldPose();

            BeginSeatingEdit(offHand, sheathed, out _);
            ApplySeatingPreview(pos, euler, scale, fullOverride);
            return true;
        }

        /// <summary>Re-equip from the (reloaded) registry to PROVE the saved file reproduces the pose.</summary>
        public void ReapplySeatingFromRegistry()
        {
            AttachmentOffsetRegistry.Reload();
            bool wasEditing = _seatingEditActive;
            bool offHand    = _seatEditOffHand;
            bool sheathed   = _seatEditSheathed;
            _seatingEditActive = false;     // allow the re-attach to seat normally
            _seatEditSheathed  = false;
            _seatEditMode = -1;
            EquipBestForHero();
            if (wasEditing)
            {
                // Re-enter edit on the freshly attached prop so the panel stays live (same carry mode).
                BeginSeatingEdit(offHand, sheathed, out _);
            }
        }

        /// <summary>End the edit session: restore the auto sheathe/draw carry state on both props.</summary>
        public void EndSeatingEdit()
        {
            if (!_seatingEditActive) return;
            _seatingEditActive = false;
            _seatEditSheathed  = false;
            _seatEditMode = -1;
            // Re-apply the carry state so both props resume drawn (combat) / sheathed (town) cleanly.
            ApplyHoldPose();
            FlowTrace.Step("Offset", $"EndSeatingEdit on '{name}'.");
        }

        // ── Internals ──────────────────────────────────────────────────────────────
        // Scale archetype heldLength (authored for RefHeroHeightM) to this hero's measured height.
        private float ProportionalHeldLength(float archetypeMetersAtRefHero)
        {
            if (archetypeMetersAtRefHero <= 0f) return archetypeMetersAtRefHero;
            return archetypeMetersAtRefHero * (ResolveHeroHeightM() / RefHeroHeightM);
        }

        private float ResolveHeroHeightM()
        {
            if (_cachedHeroHeightM > 0.01f) return _cachedHeroHeightM;
            float measured = MeasureHeroBodyHeightM();
            _cachedHeroHeightM = measured > 0.5f ? measured : RefHeroHeightM;
            FlowTrace.Step("Equip",
                $"hero standing height={_cachedHeroHeightM:0.###}m (ref={RefHeroHeightM:0.###}m)");
            return _cachedHeroHeightM;
        }

        // =====================================================================
        //  WO-1209 - THE HERO MEASURED HER OWN WEAPON AS PART OF HER BODY
        // =====================================================================
        //
        // THE RETIRED LINE, verbatim, was:
        //     if (n.StartsWith("EquipmentProp") || n.StartsWith("GearVisual")) continue;
        // and `n` is the renderer's OWN GameObject name. Both of those prefixes name a grip
        // ROOT - a bare, renderer-less GameObject this controller creates (:1200, :2122). The
        // geometry hangs one level DOWN, on the FBX's mesh node, whose name is whatever the
        // artist called it ('staff_B', 'Object_2'), and the WeaponTrailController parents a
        // second child called "WeaponTrail" onto the same grip root (WeaponTrailController.cs
        // :183). NEITHER child starts with either prefix, so the test excluded the two empty
        // holders and measured every attached prop - and every trail ribbon - as if it were
        // the hero's own silhouette.
        //
        // ⭐ PROVEN BY CAPTURE, NOT INFERRED (owner felt-test 2026-08-25, build 2026.08.25.341262):
        //   tmp/felt2/logcat-auth.txt:276652  19:29:25.481  scene 'dg_starter_loop'
        //     [Flow:Equip] hero standing height=3.976m (ref=1.8m)
        //     [Flow:Equip] heldLength 'mage_arcane' kind=Staff: archetype=1.3m proportional=2.871m
        //   tmp/felt2/logcat-auth.txt:314023  19:31:31.409  scene 'Main_Castle_Overworld'
        //     [Flow:Equip] hero standing height=1.75m (ref=1.8m)
        //     [Flow:Equip] heldLength 'mage_arcane' kind=Staff: archetype=1.3m proportional=1.264m
        //   ...and the RENDERED result, from the two `renderers=1` compensate lines (the only two
        //   in the session with no trail riding the grip root, so the only two whose worldBounds
        //   describe the prop alone):
        //     :276798  dungeon  parent='SheatheSocket_HipMain' -> worldBounds=(1.488, 2.973, 0.613)
        //     :314243  town     parent='SheatheSocket_HipMain' -> worldBounds=(0.177, 1.291, 0.445)
        //   2.973 / 1.291 = 2.30x, against a height ratio of 3.976 / 1.75 = 2.27x and a heldLength
        //   ratio of 2.871 / 1.264 = 2.27x. The three agree: the staff is rendering ~2.3x too long
        //   in the dungeon and EVERY BIT of it comes from the measured height. That is the owner's
        //   "staff oversized in starter loop dugeon" (tmp/wo970/staff-dungeon-193002.png), and her
        //   "same thing we saw with the knight sword and shield" is the SAME LINE on the Knight a
        //   week earlier - Logs/device/2026-08-20-portal.log:5335737 reads
        //     hero standing height=3.848m -> heldLength 'knight_starter' Sword 0.65m -> 1.39m.
        //
        // ⛔ THE TICKET'S PRIME SUSPECT IS DISPROVEN BY ITS OWN LOG, and it is worth saying so
        // here so nobody re-opens it: WO-1209 nominated "the dungeon instantiates a different hero
        // body, so the bone lossyScale differs". Every parent-scale compensate line in that
        // session - town AND dungeon, hand AND hip - reads `lossy=(1.72,1.72,1.72)`. Same rig,
        // same Fit factor. And 'Hero (Blaise)' names the hero in the TOWN lines too (19:11, 19:20,
        // 19:26, all Main_Castle_Overworld), so the stale display name is not a dungeon tell.
        // The compensation maths is innocent (WO-970 sec 6 cleared it once already) and so is the
        // rotation - the dungeon screenshot shows a correctly VERTICAL staff at the correct hip,
        // simply 2.3x too big.
        //
        // WHY IT ONLY BIT IN A DUNGEON: the height is cached and re-measured exactly once per
        // body, from InvalidateHeroHeightCache in CoReapplyGearAfterSceneLoad (:735). On a FRESH
        // hero the measurement runs from HeroBodySwapper's Awake with nothing equipped yet - the
        // clean 1.75. On a SURVIVING hero carried through a scene load the props are still on the
        // rig when the re-measure fires (Equip's DestroyCurrentWeapon at :951 is a Unity Destroy,
        // which is deferred to end of frame, so even the OUTGOING prop is still walkable) - so the
        // measurement swallows the prop plus its trail. It is a feedback loop: a bigger measured
        // height makes a bigger prop, which makes a bigger measured height on the next port.
        //
        // THE FIX IS STRUCTURAL, NOT A CLAMP (the ticket forbids a per-scene constant, and rightly:
        // a clamp would hide a wrong measurement rather than take a right one). Two rules, both
        // already proven elsewhere in this repo:
        //   1. EXCLUDE THE WHOLE SUBTREE, not the node whose name matches. Attached gear hangs
        //      under a named holder; its geometry does not carry the holder's name.
        //   2. EXCLUDE NON-GEOMETRY RENDERERS. WO-1226 established this at CompensateParentScale
        //      (:3040 and its comment block): a TrailRenderer's `bounds` is the WORLD AABB of the
        //      ribbon the hero just swung through, which reported a 1.3 m rod as a 1.5 m cube.
        //      That is the same corruption, and this was the second site carrying it.

        /// <summary>
        /// Node-name prefixes that mark the root of an ATTACHED-GEAR subtree hanging off the body
        /// rig. Everything BELOW one of these belongs to a prop, an armour visual or an effect -
        /// never to the hero's own silhouette. Kept as data (not three inline StartsWith calls) so
        /// the regression asserts the same list the runtime walks.
        /// </summary>
        public static readonly string[] AttachedGearNodePrefixes =
        {
            "EquipmentProp",       // this controller's own grip roots (main hand + off hand)
            "GearVisual",          // GearVisualApplier's armour/helmet meshes
            "WeaponTrail",         // WeaponTrailController's ribbon AND its synthetic origin holder
            "SheatheSocket",       // the code-built carry sockets (:3183) and anything parked on them
        };

        /// <summary>True when <paramref name="name"/> names an attached-gear subtree root.</summary>
        public static bool IsAttachedGearNodeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < AttachedGearNodePrefixes.Length; i++)
                if (name.StartsWith(AttachedGearNodePrefixes[i], System.StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// True when <paramref name="r"/> is part of the hero's OWN body silhouette - i.e. it owns
        /// real mesh geometry AND no node between it and <paramref name="body"/> is an attached-gear
        /// holder. Pure apart from the transform walk, so AttachmentOffsetRegression can prove both
        /// the exclusion AND the inclusion on a synthetic hierarchy with no hero and no scene.
        /// </summary>
        /// <param name="excludedBy">On false, the rule that rejected it - so a capture can say WHY.</param>
        public static bool IsBodyOwnRenderer(Renderer r, Transform body, out string excludedBy)
        {
            excludedBy = null;
            if (r == null) { excludedBy = "null renderer"; return false; }

            // Rule 2 (WO-1226): only a MeshFilter/SkinnedMeshRenderer owns geometry. A
            // TrailRenderer / LineRenderer / ParticleSystemRenderer reports the world AABB of an
            // EFFECT, which describes the swing, not the body.
            var smr = r as SkinnedMeshRenderer;
            bool ownsGeometry;
            if (smr != null) ownsGeometry = smr.sharedMesh != null;
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                ownsGeometry = mf != null && mf.sharedMesh != null;
            }
            if (!ownsGeometry)
            {
                excludedBy = "non-geometry renderer (" + r.GetType().Name + " '" + r.gameObject.name +
                             "') - its bounds describe an effect, not the body (WO-1226)";
                return false;
            }

            // Rule 1: walk UP to the body root; any attached-gear holder on the way disqualifies
            // the whole subtree beneath it.
            for (Transform t = r.transform; t != null && t != body; t = t.parent)
            {
                if (IsAttachedGearNodeName(t.name))
                {
                    excludedBy = "attached-gear subtree rooted at '" + t.name + "'";
                    return false;
                }
            }
            return true;
        }

        // Renderer bounds on HeroBody, BODY GEOMETRY ONLY - same frame GearVisualApplier targets.
        private float MeasureHeroBodyHeightM()
        {
            var body = transform.Find("HeroBody");
            if (body == null) return 0f;
            bool any = false;
            Bounds b = default;
            int measured = 0, skipped = 0;
            string firstSkip = null;
            foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (!IsBodyOwnRenderer(r, body, out string why))
                {
                    skipped++;
                    if (firstSkip == null) firstSkip = why;
                    continue;
                }
                measured++;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            float h = any ? b.size.y : 0f;
            // Section 12: the number AND what produced it, on one line. The 2026-08-25 capture had
            // only "hero standing height=3.976m" to go on, which cannot be split into "the hero is
            // huge" and "the hero is holding something". These counts split it in one read.
            FlowTrace.Step("Equip",
                $"MeasureHeroBodyHeightM on '{name}': measured {measured} body renderer(s), " +
                $"skipped {skipped} attached-gear/effect renderer(s) -> height={h:0.###}m " +
                $"(first skip: {firstSkip ?? "<none>"})");
            return h;
        }

        private void CacheRig()
        {
            if (_animator != null && _animator.isHuman) return;
            _cachedHeroHeightM = 0f;
            using var _ = FlowTrace.Enter("Equip", $"CacheRig on '{name}'");
            // Body lives under "HeroBody" on the hero root (same convention as GearLoadout
            // / GearVisualApplier). Fall back to any child Animator.
            var body = transform.Find("HeroBody");
            _animator = body != null ? body.GetComponentInChildren<Animator>() : null;
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            FlowTrace.Step("Equip",
                $"CacheRig: animator={(_animator != null ? _animator.name : "<null>")} " +
                $"isHuman={(_animator != null && _animator.isHuman)} (body='{(body != null ? body.name : "<none>")}')");
        }

        private void DestroyCurrentWeapon()
        {
            if (_currentWeaponProp != null)
            {
                Destroy(_currentWeaponProp);
                _currentWeaponProp = null;
            }
            _weaponHand = null;   // drop the resolved draw target so a stale (old-body) hand is never reused
        }

        // ── ARMED-HERO INVARIANT (regression surface) ────────────────────────────────
        /// <summary>
        /// True when the def loads its prefab via Addressables (Blink "gear/" scheme or an
        /// explicit loadVia=="addressable"). Public so the headless DataRegression can pick the
        /// same load path this controller does when asserting the armed-hero invariant.
        /// </summary>
        public static bool IsAddressableWeapon(WeaponDef def) => LoadsViaAddressable(def);

        /// <summary>
        /// The build-safe Resources mesh path the controller would load for <paramref name="weaponId"/>
        /// on the NON-Addressable path (e.g. "Heroes/Props/Weapons/sword_A"). Resolve() never
        /// returns null for a non-empty id (it defaults to a sword family), so this always yields a
        /// path — the armed-hero guarantee: the hero attaches at worst a tinted primitive, never nothing.
        /// Public so DataRegression can assert the Resources prop exists for the auto-equipped starters.
        /// </summary>
        public static string ResolveWeaponMeshResourcePath(string weaponId)
        {
            var vis = Resolve(weaponId);
            return vis != null && !string.IsNullOrEmpty(vis.mesh)
                ? WeaponPropResourceDir + vis.mesh
                : null;
        }

        /// <summary>
        /// Map a weapons.json id -> a WeaponVisual: exact-id table first, then the catalog
        /// row's prefabPath + category (icon/title and held mesh stay in lockstep), then
        /// keyword classification on the id (future ids still resolve to a sensible family).
        /// </summary>
        private static WeaponVisual Resolve(string weaponId)
        {
            if (string.IsNullOrEmpty(weaponId)) return null;
            if (IdMap.TryGetValue(weaponId, out var hit)) return hit;

            var def = GearCatalog.FindWeapon(weaponId);
            if (def != null)
            {
                var fromCatalog = VisualFromCatalog(def);
                if (fromCatalog != null) return fromCatalog;
            }

            string id = weaponId.ToLowerInvariant();
            // Order matters: more specific keywords first.
            if (id.Contains("bow"))     return Bow("bow_A");
            if (id.Contains("dagger"))  return Dagger("dagger_A");
            if (id.Contains("axe"))     return Axe("axe_A");
            if (id.Contains("hammer") || id.Contains("mace")) return Hammer("hammer_A");
            if (id.Contains("staff"))   return Staff("staff_A");
            if (id.Contains("wand") || id.Contains("scepter") || id.Contains("scept"))
                                        return Wand("wand_A");
            if (id.Contains("shield"))  return Shield("shield_A");
            // Job-coded ids without a weapon keyword.
            if (id.StartsWith("mage"))  return Staff("staff_A");
            if (id.StartsWith("ranger"))return Bow("bow_A");

            // ⛔ BEHAVIOUR UNCHANGED — a visible weapon beats an invisible one, so this still
            // returns a sword. What changed 2026-08-14 is that it now REPORTS. Resolve() never
            // returns null, so a row with no IdMap entry AND no usable prefabPath/category
            // silently arms ANY class with sword_A (live: cleric_starter, knight_flameblade,
            // both category:null). That failure rendered as success to every gate and to F8.
            FlowTrace.Warn("Equip",
                $"Resolve: NO VISUAL AUTHORED for id='{weaponId}' — no IdMap entry, and the catalog row " +
                "gave no usable prefabPath/category (missing row, or prefabPath empty). FALLING BACK to " +
                "sword_A. CONSEQUENCE: the player is armed with a GENERIC SWORD regardless of class — " +
                "fix by adding a prefabPath+category to the weapons.json row (preferred) or an IdMap entry.");
            // Default: a sword (knight / generic melee).
            return Sword("sword_A");
        }

        // Derive the held mesh from the catalog row (ITEM_MODEL §3/§4): prefabPath names the
        // Resources prop; category picks the grip family. Blink Addressables rows keep `native`.
        private static WeaponVisual VisualFromCatalog(WeaponDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.prefabPath)) return null;

            string mesh;
            if (LoadsViaAddressable(def))
            {
                // Address is e.g. "gear/weapon/Sword1h_01" — last segment is the load key.
                int slash = def.prefabPath.LastIndexOf('/');
                mesh = slash >= 0 ? def.prefabPath.Substring(slash + 1) : def.prefabPath;
            }
            else
            {
                mesh = System.IO.Path.GetFileName(def.prefabPath);
            }
            if (string.IsNullOrEmpty(mesh)) return null;

            WeaponVisual vis = VisualForCategory(def.category, mesh);
            return LoadsViaAddressable(def) ? CopyOf(Native(vis)) : vis;
        }

        private static WeaponVisual VisualForCategory(string category, string mesh)
        {
            switch ((category ?? "").ToLowerInvariant())
            {
                case "bow":    return Bow(mesh);
                case "dagger": return Dagger(mesh);
                case "axe":    return Axe(mesh);
                case "hammer":
                case "mace":   return Hammer(mesh);
                case "staff":  return Staff(mesh);
                case "wand":   return Wand(mesh);
                case "shield": return Shield(mesh);
                default:       return Sword(mesh);
            }
        }

        /// <summary>
        /// Loads a KayKit weapon mesh from the build-safe Resources path (prefab first,
        /// then a model/fbx GameObject). Returns null when the prop hasn't been copied
        /// into Resources/Heroes/Props/Weapons yet (see file header gap note).
        /// </summary>
        private static GameObject LoadWeaponMesh(string meshName, string forId = null)
        {
            using var _ = FlowTrace.Enter("Equip", $"LoadWeaponMesh '{meshName ?? "<null>"}'");
            string who = string.IsNullOrEmpty(forId) ? "<unknown id>" : forId;
            if (string.IsNullOrEmpty(meshName))
            {
                // WO hollow-report fix (2026-08-14): an empty mesh key is a FAILURE, not a Step.
                // The caller will fall back to BuildFallbackPrimitive and the player is handed a
                // grey box with no other record that anything went wrong.
                FlowTrace.Warn("Equip",
                    $"LoadWeaponMesh: NO MESH KEY for id='{who}' — nothing to load from '{WeaponPropResourceDir}'. " +
                    "CONSEQUENCE: the player sees a tinted grey box (BuildFallbackPrimitive), not this weapon.");
                return null;
            }
            string path = WeaponPropResourceDir + meshName;
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                // ⚠ THIS WAS A FlowTrace.Step UNTIL 2026-08-14 — a hard art failure that reported as
                // routine, so it tripped no gate and raised no F8 flag while the hero visibly carried
                // the wrong thing. Promoted to Warn and made to name id + key + player-visible result.
                FlowTrace.Warn("Equip",
                    $"LoadWeaponMesh: MISSING mesh for id='{who}' — key='{meshName}' path='{path}' " +
                    "did not resolve under Resources. CONSEQUENCE: the player sees a tinted grey box " +
                    "(BuildFallbackPrimitive), or a generic sword if Resolve() already substituted one.");
                return null;
            }
            FlowTrace.Step("Equip", $"LoadWeaponMesh: id='{who}' path='{path}' prefab=found");
            return Instantiate(prefab);
        }

        /// <summary>
        /// Tinted-primitive stand-in for when the real KayKit mesh isn't in Resources yet
        /// (keeps the hero visibly armed). One thin box; NormalizeInto sizes it to heldLength.
        /// </summary>
        private static GameObject BuildFallbackPrimitive(WeaponVisual vis)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            // A thin tall box reads as a blade/haft; NormalizeInto puts the long axis up.
            go.transform.localScale = new Vector3(0.05f, 1f, 0.05f);

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (sh != null)
                {
                    var mat = new Material(sh);
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", vis.tint);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", vis.tint);
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.6f);
                    if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.5f);
                    mr.sharedMaterial = mat;
                }
            }
            return go;
        }

        // Bladed melee: Z thickest at hilt → handle at the short Y end. Bow/shield/staff/wand skip.
        private static bool ResolveHiltFromKind(WeaponClass kind) =>
            kind == WeaponClass.Sword || kind == WeaponClass.Dagger ||
            kind == WeaponClass.Axe || kind == WeaponClass.Hammer;

        /// <summary>
        /// WO-1105 R4 (owner rule: "that rule will apply to every bow"): a BOW seats its grip on the
        /// stave SURFACE — the perpendicular from the straight (string) edge's midpoint — because a
        /// bow's bounds centre is the hollow between string and belly, i.e. empty air. Every other
        /// kind keeps the bounds-centre seat it has today, so this is bow-only by construction.
        /// Owner-tuned manual offsets (attachment-offsets.json) are applied downstream as a nudge on
        /// top and are NEVER overwritten by this derived pass.
        /// CROSSBOWS (R4a) are excluded: none can reach the runtime catalog while the exclusion
        /// stands (pinned by RangedPrimaryRegression), so no name-token branch is authored here.
        /// </summary>
        private static WeaponBoundsOrient.GripAnchor ResolveGripAnchorFromKind(WeaponClass kind) =>
            kind == WeaponClass.Bow ? WeaponBoundsOrient.GripAnchor.BowGrip
                                    : WeaponBoundsOrient.GripAnchor.Centre;

        // ── Bounds-normalize (WeaponBoundsOrient: Y-long, X-narrow, Z-wide — BINDING canon) ──
        private static void NormalizeInto(GameObject prop, Transform parent, float targetLength,
                                        bool resolveHilt = true,
                                        WeaponBoundsOrient.GripAnchor anchor = WeaponBoundsOrient.GripAnchor.Centre)
            => WeaponBoundsOrient.NormalizeInto(prop, parent, targetLength, anchor, resolveHilt);

        // Seat a NATIVE prop (authored grip-at-origin + correct orientation, e.g. Blink): trust the
        // prefab — parent at identity, scale to the target held length by the LONGEST bound, and do
        // NOT re-centre (re-centring is what moved the grip to mid-blade on the normalize path). The
        // prefab origin (the grip) stays at the gripRoot origin → the hand holds the handle, not the blade.
        private static void SeatNative(GameObject prop, Transform parent, float targetLength)
        {
            prop.transform.SetParent(parent, false);
            prop.transform.localPosition = Vector3.zero;
            prop.transform.localRotation = Quaternion.identity;
            prop.transform.localScale = Vector3.one;
            if (TryLocalBounds(prop, parent, out Bounds b))
            {
                float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                if (longest > 1e-4f) prop.transform.localScale = Vector3.one * (targetLength / longest);
            }
        }

        private static bool TryLocalBounds(GameObject prop, Transform parent, out Bounds bounds)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                Bounds wb = r.bounds;
                Vector3 c = parent.InverseTransformPoint(wb.center);
                Vector3 e = parent.InverseTransformVector(wb.extents);
                var lb = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (!any) { bounds = lb; any = true; } else bounds.Encapsulate(lb);
            }
            return any;
        }

        // ── WEAPON MATERIAL RECOVERY HELPERS (deal-breaker fix 2026-07-16) ────────────
        // Cached URP/Lit shader for the runtime weapon-material recovery. Shader.Find is not free,
        // so resolve once (mirrors MagentaGuard._lit). Null on a broken build => recovery no-ops loudly.
        private static Shader _urpLit;

        // SINGLE AUTHORITY (2026-08-02): the local IsBrokenPropShader predicate was DELETED. Its
        // header used to read "kept local so this silo never edits MagentaGuard" - and that is exactly
        // how the copy was allowed to DRIFT: it never got MagentaGuard's `!sh.isSupported` branch. That
        // branch is the ANDROID/ON-DEVICE case - a shader that compiles in the editor and on desktop but
        // fails against the device's graphics API keeps its NAME, so every name-only test passes it as
        // "fine" while it renders MAGENTA. An equipped weapon prop on the Seeker was therefore
        // structurally undetectable AND unrecoverable. There is now exactly ONE definition of "would
        // this render magenta" in the runtime tree: DeNelle.Core.MagentaGuard.IsBrokenShader.
        //
        // WHY THE RECOVERY LOOP BELOW IS *NOT* REPLACED BY MagentaGuard.SweepGameObject: the shared
        // sweep emits FlowTrace.Fail (MagentaProbe.ProbeFail) per recovered material. This path was
        // DELIBERATELY demoted to Step (see the block comment inside RecoverWeaponMaterialsToUrp) because
        // a re-imported gitignored weapon pack is a KNOWN, FULLY-HANDLED condition and Fail-logging it
        // floods the errors-only F8 break-log with a false live-break on the owner's device tests. The
        // shared sweep also recovers NULL material slots, which this path intentionally skips. Swapping
        // the whole loop would change both behaviours; swapping only the predicate changes NOTHING except
        // adding the missing on-device case. Consolidating the recovery machinery too is a separate,
        // owner-visible decision.

        // Recover every broken-shader material on the just-attached weapon prop to a FRESH URP/Lit
        // (carrying colour + albedo + emission), assigned back into the renderer's sharedMaterials so
        // the recovery STICKS in the built player. Idempotent + null-guarded (a valid-URP prop is a no-op).
        private static void RecoverWeaponMaterialsToUrp(GameObject prop, string weaponId)
        {
            if (prop == null) return;
            if (_urpLit == null) _urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (_urpLit == null)
            {
                FlowTrace.Warn("HeroWeapon",
                    $"no URP/Lit shader found — cannot recover weapon '{weaponId ?? "<null>"}' material (may render magenta/invisible).");
                return;
            }

            var freshFor = new Dictionary<Material, Material>();
            int scanned = 0, recovered = 0;
            foreach (var r in prop.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var work = r.sharedMaterials;
                if (work == null) continue;
                bool changed = false;
                for (int i = 0; i < work.Length; i++)
                {
                    var m = work[i];
                    if (m == null) continue;
                    scanned++;
                    if (!MagentaGuard.IsBrokenShader(m.shader)) continue;   // valid URP/Unlit -> leave it alone
                    string dead = m.shader != null ? m.shader.name : "<null>";
                    if (!freshFor.TryGetValue(m, out var fresh))
                    {
                        fresh = BuildRecoveredWeaponMaterial(m);   // fresh URP/Lit, once per unique source
                        freshFor[m] = fresh;
                        recovered++;
                        // ROOT FIX 2026-07-19 (device: knight_starter magenta): the source .mat
                        // (Blink MegaWeaponPack1/LowPolyWeaponMegaPack) is now re-authored to ship URP/Lit,
                        // so on a correctly-imported build this recovery does NOT fire at all (the shader
                        // reads URP/Lit -> MagentaGuard.IsBrokenShader false -> skipped). It STILL fires as a backstop
                        // only when a gitignored pack is re-imported fresh and its .mat reverts to Built-in
                        // Standard. That is a KNOWN, FULLY-HANDLED condition — the fresh URP/Lit carries the
                        // authored albedo/colour, so the weapon renders correctly. It is therefore NOT a live
                        // break: log Step (Player.log only), NOT Fail (which would spam the F8 break-log as a
                        // false live-break on every equip). A genuinely unrecoverable case (URP/Lit shader not
                        // found) still Fails, above.
                        FlowTrace.Step("HeroWeapon",
                            $"weapon '{weaponId ?? "<null>"}' material '{m.name}' shipped on dead shader '{dead}' -> " +
                            "auto-healed to FRESH URP/Lit (assigned to renderer, sticks in build). Expected only when a " +
                            "gitignored weapon pack is re-imported to Built-in Standard; source .mat now ships URP/Lit.");
                    }
                    work[i] = fresh;
                    changed = true;
                }
                if (changed) r.sharedMaterials = work;   // the assignment is what makes the recovery stick
            }
            FlowTrace.Step("HeroWeapon",
                $"weapon '{weaponId ?? "<null>"}' material recovery: scanned {scanned} slot(s), recovered {recovered} " +
                "broken-shader material(s) to URP/Lit (0 recovered = prop already valid URP).");
        }

        // Build a FRESH URP/Lit carrying the authored colour + albedo + emission read robustly off the
        // dead/stripped SOURCE (mirrors MagentaGuard.BuildRecoveredMaterial). HasProperty-gated reads are
        // safe here: this runs on the owner's real GPU where the shader table resolves; the -nographics
        // fleet never equips a real weapon prop through this path. Defaults to white, never magenta.
        private static Material BuildRecoveredWeaponMaterial(Material src)
        {
            Color col = Color.white;
            if (src != null && src.HasProperty("_Color")) col = src.GetColor("_Color");
            else if (src != null && src.HasProperty("_BaseColor")) col = src.GetColor("_BaseColor");

            Texture tex = null;
            if (src != null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
            if (tex == null && src != null && src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");

            Color emis = (src != null && src.HasProperty("_EmissionColor")) ? src.GetColor("_EmissionColor") : Color.black;

            var fresh = new Material(_urpLit)
            { name = ((src != null && src.name != null) ? src.name : "Weapon") + "_UrpRecovered" };
            if (fresh.HasProperty("_BaseColor")) fresh.SetColor("_BaseColor", col);
            if (fresh.HasProperty("_Color")) fresh.SetColor("_Color", col);
            if (tex != null)
            {
                if (fresh.HasProperty("_BaseMap")) fresh.SetTexture("_BaseMap", tex);
                if (fresh.HasProperty("_MainTex")) fresh.SetTexture("_MainTex", tex);
            }
            if (emis != Color.black && fresh.HasProperty("_EmissionColor"))
            {
                fresh.SetColor("_EmissionColor", emis);
                fresh.EnableKeyword("_EMISSION");
            }
            if (fresh.HasProperty("_Surface")) fresh.SetFloat("_Surface", 0f);   // URP: 0 = Opaque
            if (fresh.HasProperty("_ZWrite"))  fresh.SetFloat("_ZWrite", 1f);
            fresh.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            return fresh;
        }
    }

    /// <summary>
    /// Marker on a hero ROOT whose body BAKES its own weapon/shield/helmet (the Paladin hero package,
    /// loaded via ff.heropackage in HeroBodySwapper.BuildPackageHeroBody). EquipmentController checks
    /// for it (PackageBakedGear) and SKIPS the KayKit weapon-mesh + shield-mesh prop attach so the baked
    /// gear is the only gear shown — no duplicate second sword, no wrongly-oriented attached shield
    /// (owner F8 2026-07-03). Added by the swapper's package wiring BEFORE the EquipmentController, so even
    /// the first synchronous equip on AddComponent already sees it. Loadout/stat/armor-tint are unaffected;
    /// only the visible prop attach is suppressed. The legacy Tripo Knight never carries this marker, so
    /// its attach path stays byte-for-byte intact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PackageBakedGearMarker : MonoBehaviour { }
}
