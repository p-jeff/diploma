using Gsplat.Animation;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// Marks a plant as network-syncable and gives it a stable id. On the host the
    /// hub SAMPLES this component each tick (current pose + reveal progress); on the
    /// spectator client it APPLIES received state.
    ///
    /// Pose is read/written relative to a garden root transform so the spectator's
    /// framing is independent of the headset user's room anchor. The reveal is a
    /// single float (<see cref="GsplatRevealAnimator.progress"/>) and the morph it
    /// drives is deterministic, so replicating that float reproduces the exact
    /// visual on the client's own GPU — no animation playback is sent over the wire.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetPlant : MonoBehaviour
    {
        [Tooltip("Stable id, unique within the scene. Host and client must agree (same serialized value).")]
        public ushort id;

        GsplatRevealAnimator m_reveal;
        bool m_searched;

        GsplatRevealAnimator Reveal
        {
            get
            {
                if (m_reveal == null && !m_searched)
                {
                    m_reveal = GetComponentInChildren<GsplatRevealAnimator>(true);
                    m_searched = true;
                }
                return m_reveal;
            }
        }

        void OnEnable()  => NetPlantRegistry.Register(this);
        void OnDisable() => NetPlantRegistry.Unregister(this);

        /// <summary>Host: capture current authoritative state, relative to <paramref name="root"/>.</summary>
        public PlantState Sample(Transform root)
        {
            Vector3 lp;
            Quaternion lr;
            if (root != null)
            {
                lp = root.InverseTransformPoint(transform.position);
                lr = Quaternion.Inverse(root.rotation) * transform.rotation;
            }
            else
            {
                lp = transform.position;
                lr = transform.rotation;
            }

            return new PlantState
            {
                id         = id,
                localPos   = lp,
                localRot   = lr,
                localScale = transform.localScale,
                progress   = Reveal != null ? Reveal.progress : 1f,
                active     = gameObject.activeSelf,
            };
        }

        /// <summary>Client: apply received state, placing the plant relative to <paramref name="root"/>.</summary>
        public void Apply(in PlantState s, Transform root)
        {
            if (gameObject.activeSelf != s.active)
                gameObject.SetActive(s.active);
            if (!s.active)
                return;

            if (root != null)
                transform.SetPositionAndRotation(root.TransformPoint(s.localPos), root.rotation * s.localRot);
            else
                transform.SetPositionAndRotation(s.localPos, s.localRot);

            transform.localScale = s.localScale;

            var rev = Reveal;
            if (rev != null)
                rev.progress = Mathf.Clamp01(s.progress);
        }
    }
}
