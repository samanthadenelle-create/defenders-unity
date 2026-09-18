using System.Collections.Generic;
using DeNelle.Core.Diagnostics;
using DeNelle.Core.UI;
using DeNelle.Village;
using UnityEngine;

namespace DeNelle.Dungeons.RoomForge
{
    public enum CommonDoorPolicy { Proximity, Interaction, Locked }

    /// <summary>Shared runtime visual, blocker, animation and traversal for a Door socket.</summary>
    // =========================================================================
    // WO-1568 - the door used to read as a MOVING WALL, and the reason was geometry.
    // -------------------------------------------------------------------------
    // Before this change BuildDoor hung ONE PrimitiveType.Cube - the same primitive
    // family DefaultDungeonRoomsBuilder.BuildSolidWall makes the walls from - in a raw
    // full-height gap: no jamb, no lintel, no panel relief, no inset. The leaf was
    // 2.4 m tall inside a 4.0 m opening, so a CLOSED door had a 1.6 m see-through
    // letterbox over it, and it was exactly as wide as the gap, so there was no reveal.
    // The single cue separating it from a wall was its brown COLOUR - and the owner is
    // colourblind, so the door had, for her, no cue at all.
    //
    // The fix is PRESENTATION ONLY. Open/closed policy, keys, locks, the prompt, the
    // hinge angle and the hinge speed are untouched; section 1.2 of the WO proves the swing
    // was already correct. What changed is that the opening is now a DOORWAY - two
    // proud jambs and a lintel that closes the letterbox to RoomForgeCanon.WallHeight,
    // all of it static so the opening still reads as a doorway while the door is OPEN -
    // and the leaf is real KayKit door art, inset inside the jambs, with a
    // still-door-shaped primitive fallback (stile + raised panels + handle) for when
    // the art will not resolve. Every dimension is READ from RoomForgeCanon, never
    // re-typed (RoomForgeCanon.cs header: "a copied oracle constant is not an oracle").
    //
    // WHY THE VISUAL IS A STATIC SEAM (BuildDoorVisual):
    // DungeonSceneCapture opens the baked scenes in EDIT mode and never enters play,
    // so RoomSocket.Start / CommonDungeonDoor.Start never run and the existing capture
    // set physically cannot contain a door. Both the capture and the regression oracle
    // drive this one static method, so the thing photographed and the thing pinned are
    // the thing the game builds - the RoomForge "single source of truth" idiom.
    // =========================================================================
    [DisallowMultipleComponent]
    public sealed class CommonDungeonDoor : MonoBehaviour
    {
        private const float OpenDistance = 2.6f;
        private const float CloseDistance = 4.2f;
        /// <summary>Swing angle of the open leaf. Public for the oracle ONLY - value unchanged.</summary>
        public const float OpenAngle = 100f;
        private const float DegreesPerSecond = 240f;
        private const int PromptPriority = 60;

        // -- Presentation constants (frame trim; NOT canon - canon is read, never typed) --
        /// <summary>Jamb post width (m). Wide enough to read as trim at hero eye height.</summary>
        private const float JambWidth = 0.28f;
        /// <summary>How far the frame stands proud of EACH wall face (m) - this is the shadow line.</summary>
        private const float FrameProud = 0.18f;
        /// <summary>Clear air between the leaf edge and each jamb (m) - the reveal that says "inset".</summary>
        private const float LeafReveal = 0.1f;
        /// <summary>Fallback leaf height as a fraction of the wall - derived, so canon still rules.</summary>
        private const float FallbackLeafHeightFraction = 0.7f;
        /// <summary>Fallback leaf slab thickness (m). Deliberately thinner than WallThickness.</summary>
        private const float FallbackLeafThickness = 0.18f;

        private const string Sys = "DungeonDoor";
        private const string LeafStem = "wall_doorway_door";
        private const string LeafResourcePath = "Dungeon/Door/" + LeafStem;
        private const string LeafKitPath =
            "Assets/Models/KayKit/KayKit Dungeon Remastered 1.1/Assets/obj/" + LeafStem + ".obj";
        private const string KitTextureResourcePath = "Dungeon/Exit/dungeon_texture";

        // Child names. The oracle and the capture find the pieces by these - keep them stable.
        public const string HingeName = "CommonDoor_Hinge";
        public const string LeafName = "CommonDoor_Leaf";
        public const string JambLeftName = "CommonDoor_Jamb_L";
        public const string JambRightName = "CommonDoor_Jamb_R";
        public const string LintelName = "CommonDoor_Lintel";

        private static readonly HashSet<string> ClaimedConnections = new HashSet<string>();

        private RoomSocket _socket;
        private Transform _hero;
        private Transform _hinge;
        private Collider _blocker;
        private CommonDoorPolicy _policy;
        private bool _open;
        private float _angle;

        /// <summary>What the built door is made of, so a caller can inspect it without name-guessing.</summary>
        public readonly struct DoorVisual
        {
            public readonly Transform Hinge;
            public readonly GameObject Leaf;
            public readonly Collider Blocker;
            /// <summary>"resources" | "editor-kit" | "primitive-fallback".</summary>
            public readonly string LeafSource;
            /// <summary>Top of the leaf in socket-local metres - where the lintel starts.</summary>
            public readonly float LeafTop;
            /// <summary>Clear width of the leaf in metres (always &lt; RoomForgeCanon.DoorGap).</summary>
            public readonly float LeafWidth;

            public DoorVisual(Transform hinge, GameObject leaf, Collider blocker,
                              string leafSource, float leafTop, float leafWidth)
            {
                Hinge = hinge; Leaf = leaf; Blocker = blocker;
                LeafSource = leafSource; LeafTop = leafTop; LeafWidth = leafWidth;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetClaims() => ClaimedConnections.Clear();

        public void Configure(RoomSocket socket)
        {
            _socket = socket;
            _policy = socket != null ? socket.doorPolicy : CommonDoorPolicy.Proximity;
        }

        private void Start()
        {
            if (_socket == null) _socket = GetComponent<RoomSocket>();
            if (_socket == null || _socket.type != RoomSocketType.Door || !_socket.commonDoor) { enabled = false; return; }
            string key = string.IsNullOrEmpty(_socket.matedTo)
                ? $"{gameObject.scene.handle}:{transform.position.x:0.00}:{transform.position.y:0.00}:{transform.position.z:0.00}"
                : _socket.matedTo;
            if (!ClaimedConnections.Add(key)) { enabled = false; return; }
            BuildDoor();
            FlowTrace.Step("DungeonDoor", $"common door ready socket='{_socket.id}' connection='{key}' policy={_policy}.");
        }

        private void BuildDoor()
        {
            var visual = BuildDoorVisual(transform, _socket.halfWidth, false);
            _hinge = visual.Hinge;
            _blocker = visual.Blocker;
        }

        // -------------------------------------------------------------------------
        // THE SEAM. Builds the whole door presentation under `socket` and returns the
        // pieces the runtime component needs. Safe in edit mode (the capture and the
        // oracle call it there), which is why nothing here uses Object.Destroy directly.
        // `open` only seats the hinge angle for a static caller; at runtime Update owns it.
        // -------------------------------------------------------------------------
        public static DoorVisual BuildDoorVisual(Transform socket, float halfWidth, bool open)
        {
            if (socket == null)
            {
                FlowTrace.Fail(Sys, "BuildDoorVisual called with a null socket - no door built.");
                return default;
            }

            float half = Mathf.Max(0.75f, halfWidth);
            float wallHeight = RoomForgeCanon.WallHeight;
            float frameDepth = RoomForgeCanon.WallThickness + (FrameProud * 2f);
            float targetLeafWidth = Mathf.Max(0.4f, (half * 2f) - (LeafReveal * 2f));

            // -- Hinge: UNCHANGED pivot. It sits on the jamb line at -half, which is what
            // makes the leaf swing from the frame edge rather than from its own middle.
            var hingeGo = new GameObject(HingeName);
            Transform hinge = hingeGo.transform;
            hinge.SetParent(socket, false);
            hinge.localPosition = new Vector3(-half, 0f, 0f);

            GameObject leaf = BuildLeaf(hinge, targetLeafWidth, out Collider blocker,
                                        out string leafSource, out float leafTop);

            // -- Frame. Render-only: every collider is stripped. A new collider in an
            // already-baked opening can trap the hero and would demand a NavMesh re-bake,
            // which this WO is explicitly scoped to avoid (DungeonExitInteractable.AddProp
            // strips colliders for the same reason). The leaf's blocker is the ONE collider.
            Material frameMat = ResolveFrameMaterial();
            float jambCentre = half + (JambWidth * 0.5f);
            AddFramePiece(socket, JambLeftName, new Vector3(-jambCentre, wallHeight * 0.5f, 0f),
                          new Vector3(JambWidth, wallHeight, frameDepth), frameMat);
            AddFramePiece(socket, JambRightName, new Vector3(jambCentre, wallHeight * 0.5f, 0f),
                          new Vector3(JambWidth, wallHeight, frameDepth), frameMat);

            // The lintel closes the letterbox: it runs from the TOP OF THE LEAF up to the
            // canon wall height, so a closed door cannot be seen over. Measured from the
            // leaf that was actually built - never from an assumed leaf height.
            float lintelHeight = Mathf.Max(0.05f, wallHeight - leafTop);
            AddFramePiece(socket, LintelName, new Vector3(0f, leafTop + (lintelHeight * 0.5f), 0f),
                          new Vector3((half * 2f) + (JambWidth * 2f), lintelHeight, frameDepth), frameMat);

            hinge.localRotation = Quaternion.Euler(0f, open ? OpenAngle : 0f, 0f);

            FlowTrace.Step(Sys, $"door visual built leaf='{leafSource}' leafWidth={targetLeafWidth:0.##}m " +
                                $"leafTop={leafTop:0.##}m lintel={lintelHeight:0.##}m frame=jambs+lintel " +
                                $"gap={RoomForgeCanon.DoorGap:0.##}m wallHeight={wallHeight:0.##}m open={open}.");

            return new DoorVisual(hinge, leaf, blocker, leafSource, leafTop, targetLeafWidth);
        }

        // -- The leaf -------------------------------------------------------------
        private static GameObject BuildLeaf(Transform hinge, float targetWidth,
                                            out Collider blocker, out string source, out float leafTop)
        {
            GameObject model = ResolveDoorProp(out source);
            if (model != null)
            {
                GameObject art = BuildArtLeaf(hinge, model, targetWidth, out blocker, out leafTop);
                if (art != null) return art;
                FlowTrace.Warn(Sys, $"leaf model '{LeafStem}' resolved from {source} but carried no renderer - " +
                                    "falling back to the built door-shaped leaf.");
                source = "primitive-fallback";
            }

            return BuildFallbackLeaf(hinge, targetWidth, out blocker, out leafTop);
        }

        private static GameObject BuildArtLeaf(Transform hinge, GameObject model, float targetWidth,
                                               out Collider blocker, out float leafTop)
        {
            blocker = null;
            leafTop = 0f;

            // Instantiate DETACHED and measure there. A door socket on an E/W facing is
            // rotated 90 degrees (DefaultDungeonRoomsBuilder.AddSocket sets localRotation
            // per facing), so a world-space bounds read taken AFTER parenting would hand
            // back the leaf's DEPTH as its width on half the doors in the dungeon.
            var leaf = Instantiate(model);
            leaf.name = LeafName;
            leaf.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            leaf.transform.localScale = Vector3.one;

            var renderers = leaf.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
            {
                DestroyNow(leaf);
                return null;
            }
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            if (b.size.x <= 0.001f)
            {
                DestroyNow(leaf);
                return null;
            }

            float scale = targetWidth / b.size.x;

            foreach (var col in leaf.GetComponentsInChildren<Collider>(true)) DestroyNow(col);

            leaf.transform.SetParent(hinge, false);
            leaf.transform.localScale = Vector3.one * scale;
            leaf.transform.localRotation = Quaternion.identity;
            // Hinge edge sits one reveal inside the jamb; base on the floor; centred in the wall.
            leaf.transform.localPosition = new Vector3(
                LeafReveal + (targetWidth * 0.5f) - (b.center.x * scale),
                -b.min.y * scale,
                -b.center.z * scale);

            Material mat = ResolveKitMaterial();
            if (mat != null)
                foreach (var rend in renderers) if (rend != null) rend.sharedMaterial = mat;

            // Exactly ONE collider, and it is the blocker SetOpen already toggles. Its depth
            // is clamped to the wall so the door's hinge hardware cannot bulge a blocker into
            // the room (the art AABB is 0.77 m deep; the slab body itself is only ~0.2 m).
            var box = leaf.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = new Vector3(b.size.x, b.size.y,
                                   Mathf.Min(b.size.z, RoomForgeCanon.WallThickness / Mathf.Max(0.0001f, scale)));
            blocker = box;

            // WO-1829: assign the leaf to the Structure layer so the hero's LoS linecast
            // (HeroTargetIndicator.HasLoS, masked to "Structure" layer) will be blocked by
            // the closed door. When SetOpen(true) disables the blocker collider, the door
            // becomes transparent to LoS checks. Matches WallSegment.cs:646-647 pattern.
            int structureLayer = LayerMask.NameToLayer("Structure");
            if (structureLayer >= 0)
            {
                leaf.layer = structureLayer;
                FlowTrace.Step("DungeonDoor", $"door leaf '{leaf.name}' assigned to Structure layer (WO-1829 LoS gate).");
            }
            else
            {
                FlowTrace.Warn("DungeonDoor", $"door leaf '{leaf.name}' could not resolve Structure layer — door will not block LoS (WO-1829).");
            }

            // Height, not max.y: the leaf is SEATED at -b.min.y*scale, so its top in hinge space
            // is its own size. Reading max.y here happens to agree only because this asset's
            // min.y is 0 - swap in a centre-pivot export and the lintel would silently miss.
            leafTop = b.size.y * scale;
            return leaf;
        }

        // The fallback must still READ as a door: a leaf narrower than the gap, a raised
        // hinge-side stile, two raised panel reliefs and a handle. A fallback that
        // reproduces the old flat slab is a failed fallback - it is the exact silhouette
        // this work order exists to remove.
        private static GameObject BuildFallbackLeaf(Transform hinge, float targetWidth,
                                                    out Collider blocker, out float leafTop)
        {
            float height = RoomForgeCanon.WallHeight * FallbackLeafHeightFraction;
            float thickness = FallbackLeafThickness;

            var root = new GameObject(LeafName);
            root.transform.SetParent(hinge, false);
            root.transform.localPosition = new Vector3(LeafReveal, 0f, 0f);

            // Body carries the one collider.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "CommonDoor_Leaf_Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(targetWidth * 0.5f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(targetWidth, height, thickness);
            blocker = body.GetComponent<Collider>();

            Material planks = MakeLit(new Color(0.24f, 0.15f, 0.08f, 1f), "CommonDungeonDoor_Planks");
            Material iron = MakeLit(new Color(0.16f, 0.16f, 0.18f, 1f), "CommonDungeonDoor_Iron");
            Paint(body, planks);

            float stileWidth = Mathf.Min(0.22f, targetWidth * 0.18f);
            AddRelief(root.transform, "CommonDoor_Leaf_Stile",
                      new Vector3(stileWidth * 0.5f, height * 0.5f, 0f),
                      new Vector3(stileWidth, height, thickness + 0.09f), iron);

            // Two raised panels - the relief that reads as a door in greyscale.
            float panelWidth = Mathf.Max(0.2f, targetWidth - stileWidth - 0.34f);
            float panelHeight = height * 0.34f;
            float panelX = stileWidth + 0.12f + (panelWidth * 0.5f);
            AddRelief(root.transform, "CommonDoor_Leaf_Panel_Lower",
                      new Vector3(panelX, height * 0.28f, 0f),
                      new Vector3(panelWidth, panelHeight, thickness + 0.05f), planks);
            AddRelief(root.transform, "CommonDoor_Leaf_Panel_Upper",
                      new Vector3(panelX, height * 0.70f, 0f),
                      new Vector3(panelWidth, panelHeight, thickness + 0.05f), planks);

            AddRelief(root.transform, "CommonDoor_Leaf_Handle",
                      new Vector3(targetWidth - 0.16f, height * 0.48f, 0f),
                      new Vector3(0.09f, 0.09f, thickness + 0.16f), iron);

            // WO-1829: assign the leaf to the Structure layer so the hero's LoS linecast
            // (HeroTargetIndicator.HasLoS, masked to "Structure" layer) will be blocked by
            // the closed door. When SetOpen(true) disables the blocker collider, the door
            // becomes transparent to LoS checks. Matches WallSegment.cs:646-647 pattern.
            int structureLayer = LayerMask.NameToLayer("Structure");
            if (structureLayer >= 0)
            {
                root.layer = structureLayer;
                FlowTrace.Step("DungeonDoor", $"door fallback leaf '{root.name}' assigned to Structure layer (WO-1829 LoS gate).");
            }
            else
            {
                FlowTrace.Warn("DungeonDoor", $"door fallback leaf '{root.name}' could not resolve Structure layer — door will not block LoS (WO-1829).");
            }

            leafTop = height;
            return root;
        }

        private static void AddRelief(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyNow(col); // decoration never blocks - the body is the one blocker
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            Paint(go, mat);
        }

        private static void AddFramePiece(Transform parent, string name, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyNow(col); // render-only: never trap the hero, never need a re-bake
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            Paint(go, mat);
        }

        // -- Art + material resolution ladder (the DungeonExitInteractable.ResolveExitProp
        // shape, reused verbatim rather than invented a second time): tracked Resources
        // copy first so it resolves in a PLAYER build, editor AssetDatabase against the
        // gitignored kit second, and a warned door-shaped primitive last. Never silent.
        private static GameObject ResolveDoorProp(out string source)
        {
            var fromResources = Guard.Try(Sys, $"resolve door leaf '{LeafStem}' (Resources)",
                () => Resources.Load<GameObject>(LeafResourcePath), null);
            if (fromResources != null) { source = "resources"; return fromResources; }
#if UNITY_EDITOR
            var fromKit = Guard.Try(Sys, $"resolve door leaf '{LeafStem}' (editor kit)",
                () => UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(LeafKitPath), null);
            if (fromKit != null) { source = "editor-kit"; return fromKit; }
#endif
            FlowTrace.Warn(Sys, $"door leaf '{LeafStem}' unresolved (no Resources copy at " +
                                $"'{LeafResourcePath}'" +
#if UNITY_EDITOR
                                ", no kit asset at '" + LeafKitPath + "'" +
#endif
                                ") - building the door-shaped primitive leaf instead.");
            source = "primitive-fallback";
            return null;
        }

        private static Material ResolveKitMaterial()
        {
#if UNITY_EDITOR
            var kitMat = Guard.Try(Sys, "resolve kit material (editor)",
                () => UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(
                    "Assets/Models/KayKit/dungeon/dungeon_texture_URP.mat"), null);
            if (kitMat != null) return kitMat;
#endif
            Material mat = MakeLit(Color.white, "CommonDungeonDoor_Kit");
            if (mat == null) return null;
            var tex = Guard.Try(Sys, "resolve kit texture (Resources)",
                () => Resources.Load<Texture2D>(KitTextureResourcePath), null);
            if (tex != null && mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else FlowTrace.Warn(Sys, "kit texture unresolved - door leaf renders plain-lit (still visible, still door-shaped)");
            return mat;
        }

        // Frame reads by SHAPE (proud posts, a header, the shadow line they cast), never by
        // hue - the owner is colourblind, so a colour-only cue is no cue at all.
        private static Material ResolveFrameMaterial() =>
            MakeLit(new Color(0.31f, 0.30f, 0.28f, 1f), "CommonDungeonDoor_Frame");

        private static Material MakeLit(Color c, string name)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                FlowTrace.Warn(Sys, $"URP/Lit shader unresolved - '{name}' keeps the default material.");
                return null;
            }
            // WO-1588: set _BaseColor BY NAME as well as through Material.color. The owner's
            // locked-port frame showed an unpainted WHITE body with its emission intact, which is
            // what `Material.color` looks like when it does not reach URP/Lit's _BaseColor on the
            // resolved variant. That cause is UNPROVEN from this machine - the trace below is how
            // the device settles it; the SetColor is correct either way and costs nothing.
            var mat = new Material(shader) { name = name };
            mat.color = c;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            // supported= splits "the property never applied" from "URP/Lit resolved by name but
            // every variant was stripped from the Android build", which renders the fallback.
            FlowTrace.Once(Sys, "makelit-material",
                           $"MakeLit shader='{shader.name}' supported={shader.isSupported} " +
                           $"hasBaseColor={mat.HasProperty("_BaseColor")} " +
                           $"hasColor={mat.HasProperty("_Color")} want={c} readback={mat.color}");
            return mat;
        }

        private static void Paint(GameObject go, Material mat)
        {
            if (mat == null) return;
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.sharedMaterial = mat;
        }

        /// <summary>Destroy that works in BOTH play mode and edit mode - the seam runs in both.</summary>
        private static void DestroyNow(UnityEngine.Object o)
        {
            if (o == null) return;
#if UNITY_EDITOR
            if (!Application.isPlaying) { DestroyImmediate(o); return; }
#endif
            Destroy(o);
        }

        private void Update()
        {
            if (_hinge == null) return;
            if (_hero == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _hero = player.transform;
                if (_hero == null) return;
            }
            float distance = Vector3.Distance(_hero.position, transform.position);
            if (_policy == CommonDoorPolicy.Proximity && distance <= OpenDistance) SetOpen(true);
            else if (_policy == CommonDoorPolicy.Proximity && distance >= CloseDistance) SetOpen(false);
            if (distance <= OpenDistance && _policy != CommonDoorPolicy.Proximity)
            {
                // WO-1857: the interact prompt is player copy - two alternatives, two keys.
                string label = _policy == CommonDoorPolicy.Locked
                    ? new LocalizedText("dungeon.door.prompt_locked").Resolve()
                    : new LocalizedText("dungeon.door.prompt_open").Resolve();
                MobileInteractButton.Request(this, label,
                    _policy == CommonDoorPolicy.Locked ? (System.Action)(() => { }) : () => SetOpen(true),
                    PromptPriority);
            }
            float target = _open ? OpenAngle : 0f;
            _angle = Mathf.MoveTowards(_angle, target, DegreesPerSecond * Time.deltaTime);
            _hinge.localRotation = Quaternion.Euler(0f, _angle, 0f);
        }

        private void SetOpen(bool open)
        {
            if (_open == open) return;
            _open = open;
            if (_blocker != null) _blocker.enabled = !open;
            FlowTrace.Step("DungeonDoor", $"door '{name}' {(open ? "OPEN" : "CLOSED")} freeTraversal={open}.");
        }

        private void OnDisable() => MobileInteractButton.Release(this);
    }
}
