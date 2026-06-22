using UnityEngine;

namespace Plants
{
    /// <summary>
    /// Procedurally generated partial-cylinder mesh for displaying 180° environment
    /// paintings. Generates a half-cylinder arc with UVs mapped flat across the arc,
    /// and provides a MaterialPropertyBlock-driven alpha fade via _BaseColor.a.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    [ExecuteAlways]
    public class EnvironmentCylinder : MonoBehaviour
    {
        [Header("Geometry")]
        [Tooltip("Radius of the cylinder arc in metres.")]
        [SerializeField] private float radius = 3.5f;
        [Tooltip("Height of the cylinder in metres.")]
        [SerializeField] private float height = 4f;
        [Tooltip("Arc angle in degrees (180 = half cylinder).")]
        [SerializeField, Range(10f, 360f)] private float arcDeg = 180f;
        [Tooltip("Segments along the arc. Higher = smoother curve.")]
        [SerializeField] private int segments = 32;

        [Header("Texture")]
        [Tooltip("The environment painting texture to display.")]
        [SerializeField] private Texture2D texture;

        private MeshFilter m_meshFilter;
        private MeshRenderer m_meshRenderer;
        private Material m_material;
        private MaterialPropertyBlock m_propertyBlock;
        private bool m_initialized;
        // The mesh WE generated — tracked so a regenerate frees the previous one (no leak when
        // Configure is called repeatedly) without ever touching a mesh we didn't create.
        private Mesh m_generatedMesh;
        // Texture aspect ratio (width/height); 0 = unknown → fall back to the serialized `height`.
        private float m_aspect;
        // Per-column explicit height in metres; >0 overrides the aspect-derived height. 0 = follow aspect.
        private float m_heightOverride;
        // When true, keep hard edges (rely on the texture's own alpha) instead of fading the sides.
        private bool m_hardEdges;

        static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int s_baseMapId   = Shader.PropertyToID("_BaseMap");

        /// <summary>
        /// Cylinder height in metres. An explicit per-column <see cref="m_heightOverride"/> (>0) wins
        /// outright — set it to make a column taller/shorter than its neighbours (the painting stretches
        /// to fit). Otherwise the height is derived from the texture's aspect ratio so the image is never
        /// distorted: the arc's surface width (radius × arc) divided by the texture's width/height. Falls
        /// back to the serialized <see cref="height"/> when no override and no texture aspect are known.
        /// </summary>
        private float EffectiveHeight
        {
            get
            {
                if (m_heightOverride > 0.0001f)
                    return m_heightOverride;
                if (m_aspect > 0.0001f)
                {
                    float arcLen = Mathf.Max(radius, 0.1f) * Mathf.Max(arcDeg, 1f) * Mathf.Deg2Rad;
                    return Mathf.Max(arcLen / m_aspect, 0.1f);
                }
                return Mathf.Max(height, 0.1f);
            }
        }

        /// <summary>The column's current height in metres (override, aspect-derived, or fallback).
        /// Used by floor/ceiling caps to auto-size to the top of the tallest column.</summary>
        public float Height => EffectiveHeight;

        /// <summary>This column's radius in metres. Caps read it to auto-size their disc.</summary>
        public float Radius => radius;

        void Awake()
        {
            Debug.Log("[EnvironmentCylinder] Awake on '" + gameObject.name + "'", this);
            EnsureInitialized();
        }

        /// <summary>Idempotently build the mesh + material. Safe to call before Awake (e.g. right
        /// after AddComponent on an inactive GameObject) so Configure() can run at any time.</summary>
        private void EnsureInitialized()
        {
            if (m_initialized) return;
            CacheComponents();
            GenerateMesh();
            CreateMaterial();
            m_initialized = true;
        }

        /// <summary>
        /// Configure this cylinder as one parallax layer: assign its painting, set its radius and
        /// physical width in metres (regenerating the mesh — height follows the texture aspect so it
        /// never distorts), choose hard vs faded side edges, and pin its transparent draw order via
        /// an explicit <paramref name="renderQueue"/>. Because the width is a real-world size wrapped
        /// onto the radius, a larger radius makes the same painting subtend a smaller angle — radius
        /// reads as true distance. Concentric cylinders share the same bounds centre (the head), so
        /// URP's per-object transparent sort can't order them — and would even treat them as the
        /// nearest object — hence the pinned queue; keep it below the gsplat plants' queue (3000) so
        /// the diorama renders behind them. Zero radius/width fall back to sane defaults (3.5 m, a
        /// full 180° wrap) so an uninitialised inspector list element can't collapse the layer.
        /// <paramref name="heightOverride"/> (>0) forces an exact column height in metres (the image
        /// stretches to fit) so individual columns can be taller/shorter than their neighbours; 0 keeps
        /// the aspect-correct height.
        /// </summary>
        public void Configure(Texture2D tex, float layerRadius, float layerWidth, float heightOverride, bool hardEdges, int renderQueue)
        {
            EnsureInitialized();
            radius = layerRadius > 0f ? layerRadius : 3.5f;
            // Physical width (m) → arc angle at this radius. 0 = wrap a full 180°.
            float w = layerWidth > 0f ? layerWidth : Mathf.PI * radius;
            arcDeg = Mathf.Clamp(w / radius * Mathf.Rad2Deg, 1f, 360f);
            m_heightOverride = heightOverride;
            m_hardEdges = hardEdges;
            m_aspect = (tex != null && tex.height > 0) ? (float)tex.width / tex.height : 0f;
            GenerateMesh();
            SetTexture(tex);

            if (m_material != null)
                m_material.renderQueue = renderQueue;
        }

        private void CacheComponents()
        {
            m_meshFilter = GetComponent<MeshFilter>();
            m_meshRenderer = GetComponent<MeshRenderer>();
        }

        /// <summary>
        /// Build a partial-cylinder mesh: a ribbon of quads forming an arc.
        /// UV.x goes 0→1 along the arc, UV.y goes 0→1 bottom→top.
        /// </summary>
        private void GenerateMesh()
        {
            int seg = Mathf.Max(segments, 3);
            float rad = Mathf.Max(radius, 0.1f);
            float h   = EffectiveHeight;
            float arcRad = arcDeg * Mathf.Deg2Rad;

            int vertRows = seg + 1;
            int vertCount = vertRows * 2;           // bottom + top ring
            int triCount = seg * 2 * 3;             // 2 triangles per quad segment

            Vector3[] verts = new Vector3[vertCount];
            Vector2[] uvs   = new Vector2[vertCount];
            int[]     tris  = new int[triCount];

            for (int i = 0; i <= seg; i++)
            {
                float t = (float)i / seg;
                float angle = (t - 0.5f) * arcRad; // centered at forward (Z)
                float x = Mathf.Sin(angle) * rad;
                float z = Mathf.Cos(angle) * rad;

                int bottom = i * 2;
                int top    = i * 2 + 1;

                verts[bottom] = new Vector3(x, -h * 0.5f, z);
                verts[top]    = new Vector3(x,  h * 0.5f, z);

                uvs[bottom] = new Vector2(t, 0f);
                uvs[top]    = new Vector2(t, 1f);

                if (i < seg)
                {
                    int ti = i * 6;
                    int b0 = i * 2;
                    int b1 = b0 + 1; // top of same column
                    int n0 = (i + 1) * 2;
                    int n1 = n0 + 1;

                    // Triangle 1: bottom-left, bottom-right, top-left
                    tris[ti]     = b0;
                    tris[ti + 1] = n0;
                    tris[ti + 2] = b1;

                    // Triangle 2: top-left, bottom-right, top-right
                    tris[ti + 3] = b1;
                    tris[ti + 4] = n0;
                    tris[ti + 5] = n1;
                }
            }

            var mesh = new Mesh();
            mesh.name = "EnvironmentCylinder_Procedural";
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;

            // Vertex alpha gradient: fade to transparent at the left/right edges (unless this layer
            // wants hard edges and relies on its own texture alpha — e.g. a cutout sprite).
            Color32[] colors = new Color32[vertCount];
            for (int i = 0; i <= seg; i++)
            {
                byte a = 255;
                if (!m_hardEdges)
                {
                    float t = (float)i / seg;
                    float edgeFade = 1f - Mathf.Abs(t - 0.5f) * 2f; // 0 at edges, 1 at center
                    edgeFade = Mathf.SmoothStep(0f, 1f, edgeFade);
                    a = (byte)(edgeFade * 255);
                }
                colors[i * 2]     = new Color32(255, 255, 255, a);
                colors[i * 2 + 1] = new Color32(255, 255, 255, a);
            }
            mesh.colors32 = colors;

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (m_meshFilter != null)
                m_meshFilter.sharedMesh = mesh;

            // Free the previously generated mesh (if any) now that the new one is assigned.
            if (m_generatedMesh != null)
            {
                if (Application.isPlaying) Destroy(m_generatedMesh);
                else DestroyImmediate(m_generatedMesh);
            }
            m_generatedMesh = mesh;
        }

        private void CreateMaterial()
        {
            if (m_meshRenderer == null) return;

            var shader = Shader.Find("Custom/URP/EnvironmentCylinder");
            if (shader == null)
            {
                Debug.LogError("[EnvironmentCylinder] Custom/URP/EnvironmentCylinder shader not found!", this);
                return;
            }

            m_material = new Material(shader);
            m_material.name = "EnvironmentCylinder_Mat";

            if (texture != null)
                m_material.SetTexture(s_baseMapId, texture);

            // Start fully invisible via _BaseColor.a
            m_material.SetColor(s_baseColorId, new Color(1f, 1f, 1f, 0f));

            m_propertyBlock = new MaterialPropertyBlock();
            m_propertyBlock.SetColor(s_baseColorId, new Color(1f, 1f, 1f, 0f));

            m_meshRenderer.sharedMaterial = m_material;
            m_meshRenderer.SetPropertyBlock(m_propertyBlock);

            Debug.Log("[EnvironmentCylinder] Material created (transparent, alpha=0)", this);
        }

        /// <summary>
        /// Set the texture to display on the cylinder. Reassign at runtime for different paintings.
        /// </summary>
        public void SetTexture(Texture2D tex)
        {
            texture = tex;
            if (m_material != null)
                m_material.SetTexture(s_baseMapId, tex);
            else if (m_meshRenderer != null && m_meshRenderer.sharedMaterial != null)
                m_meshRenderer.sharedMaterial.SetTexture(s_baseMapId, tex);

            Debug.Log("[EnvironmentCylinder] SetTexture: " + (tex != null ? tex.name : "null"), this);
        }

        /// <summary>Set the opacity via MaterialPropertyBlock (0 = invisible, 1 = fully opaque).
        /// Uses _BaseColor.a multiplied with vertex color alpha from the custom shader.</summary>
        public void SetAlpha(float alpha)
        {
            float a = Mathf.Clamp01(alpha);
            if (m_propertyBlock == null && m_meshRenderer != null)
                m_propertyBlock = new MaterialPropertyBlock();
            if (m_propertyBlock != null)
            {
                m_propertyBlock.SetColor(s_baseColorId, new Color(1f, 1f, 1f, a));
                if (m_meshRenderer != null)
                    m_meshRenderer.SetPropertyBlock(m_propertyBlock);
            }
        }

        /// <summary>Position the cylinder with its bottom edge on the floor (y=0), facing a direction.
        /// The passed Y is ignored; height determines the vertical placement. <paramref name="verticalOffset"/>
        /// raises (+) or lowers (−) the whole layer in metres — use it to sit the visible art on the
        /// floor when the texture has empty space at the bottom.</summary>
        public void PositionAt(Vector3 centerXZ, Vector3 forwardDir, float verticalOffset = 0f)
        {
            transform.position = new Vector3(centerXZ.x, EffectiveHeight * 0.5f + verticalOffset, centerXZ.z);
            if (forwardDir.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(forwardDir, Vector3.up);
        }

        void OnDestroy()
        {
            if (m_material != null)
            {
                if (Application.isPlaying)
                    Destroy(m_material);
                else
                    DestroyImmediate(m_material);
            }
            if (m_generatedMesh != null)
            {
                if (Application.isPlaying)
                    Destroy(m_generatedMesh);
                else
                    DestroyImmediate(m_generatedMesh);
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!Application.isPlaying && m_meshFilter != null)
                GenerateMesh();
        }
#endif
    }
}
