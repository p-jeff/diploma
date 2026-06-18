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

        static readonly int s_baseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int s_baseMapId   = Shader.PropertyToID("_BaseMap");

        void Awake()
        {
            Debug.Log("[EnvironmentCylinder] Awake on '" + gameObject.name + "'", this);
            CacheComponents();
            GenerateMesh();
            CreateMaterial();
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
            float h   = Mathf.Max(height, 0.1f);
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

            // Vertex alpha gradient: fade to transparent at the left/right edges.
            Color32[] colors = new Color32[vertCount];
            for (int i = 0; i <= seg; i++)
            {
                float t = (float)i / seg;
                float edgeFade = 1f - Mathf.Abs(t - 0.5f) * 2f; // 0 at edges, 1 at center
                edgeFade = Mathf.SmoothStep(0f, 1f, edgeFade);
                byte a = (byte)(edgeFade * 255);
                colors[i * 2]     = new Color32(255, 255, 255, a);
                colors[i * 2 + 1] = new Color32(255, 255, 255, a);
            }
            mesh.colors32 = colors;

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            if (m_meshFilter != null)
                m_meshFilter.sharedMesh = mesh;
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

        /// <summary>Position the cylinder with the bottom edge on the floor (y=0), facing a direction.
        /// The passed Y value is ignored; height determines the vertical placement.</summary>
        public void PositionAt(Vector3 centerXZ, Vector3 forwardDir)
        {
            transform.position = new Vector3(centerXZ.x, height * 0.5f, centerXZ.z);
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
