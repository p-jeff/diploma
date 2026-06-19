using Gsplat.Animation;
using UnityEngine;

namespace Plants.Net
{
    /// <summary>
    /// Marks a live splat object as network-syncable and gives it a stable id + kind. On the host
    /// the hub SAMPLES this component each tick (pose + reveal/ripe state + active); on the
    /// spectator client it APPLIES received state.
    ///
    /// Three kinds (<see cref="NetKind"/>): static authored hero bodies (authored id, toggled
    /// active), runtime scatter clones, and runtime canopy-fruit orbs. Pose is read/written
    /// relative to a garden root transform so the spectator's framing is independent of the
    /// headset user's room anchor. The splat reveal is a single deterministic float
    /// (<see cref="GsplatRevealAnimator.progress"/>) reproduced on the client's own GPU; the fruit
    /// orb's state is a single ripe/dormant bool.
    /// </summary>
    [DisallowMultipleComponent]
    public class NetPlant : MonoBehaviour
    {
        [Tooltip("Stable id, unique within the scene. Hero bodies author 1..N; runtime instances get host-allocated ids.")]
        public ushort id;

        [Tooltip("What this instance is. Hero bodies are authored HeroBody; runtime clones/orbs are set at spawn.")]
        public NetKind kind = NetKind.HeroBody;

        [Tooltip("Owning hero-plant id, so the client knows which species to recreate for a clone/orb. " +
                 "0 = this is its own species (a hero body).")]
        public ushort speciesId;

        /// <summary>Owning hero-plant id; a hero body is its own species.</summary>
        public ushort SpeciesId => speciesId != 0 ? speciesId : id;

        GsplatRevealAnimator[] m_reveals;
        bool m_searchedReveal;
        Plants.ContextFruit m_fruit;
        bool m_searchedFruit;

        // A hero body or scatter clone can host several reveal animators (the experience always
        // plays them together — Plant.PlayAnimation / RevealLikedInstances iterate them as a group),
        // so we sample one representative progress and apply it to all of them in lockstep.
        GsplatRevealAnimator[] Reveals
        {
            get
            {
                if (m_reveals == null && !m_searchedReveal)
                {
                    m_reveals = GetComponentsInChildren<GsplatRevealAnimator>(true);
                    m_searchedReveal = true;
                }
                return m_reveals;
            }
        }

        float SampleProgress()
        {
            var revs = Reveals;
            if (revs != null)
                foreach (var r in revs)
                    if (r != null) return r.progress;
            return 1f;
        }

        void ApplyProgress(float p)
        {
            var revs = Reveals;
            if (revs == null) return;
            float c = Mathf.Clamp01(p);
            foreach (var r in revs)
                if (r != null) r.progress = c;
        }

        Plants.ContextFruit Fruit
        {
            get
            {
                if (m_fruit == null && !m_searchedFruit)
                {
                    m_fruit = GetComponentInChildren<Plants.ContextFruit>(true);
                    m_searchedFruit = true;
                }
                return m_fruit;
            }
        }

        // Hero bodies carry their authored id before OnEnable, so they self-register. Runtime
        // instances are added with id 0 and registered by Configure() once their id is set, so the
        // id-0 placeholder never lands in the table. We deliberately do NOT unregister on disable:
        // a hidden hero body must stay routable so a later active=true snapshot can revive it.
        void OnEnable()
        {
            if (id != 0) NetPlantRegistry.Register(this);
        }

        void OnDestroy() => NetPlantRegistry.Unregister(this);

        /// <summary>Host: assign id/kind/species to a freshly-spawned runtime instance and register it
        /// (immediately if active; otherwise OnEnable registers it when it is activated).</summary>
        public void Configure(ushort newId, NetKind k, ushort species)
        {
            id = newId;
            kind = k;
            speciesId = species;
            if (isActiveAndEnabled) NetPlantRegistry.Register(this);
        }

        /// <summary>Host: capture current authoritative state, relative to <paramref name="root"/>.</summary>
        public InstanceState Sample(Transform root)
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

            return new InstanceState
            {
                id         = id,
                kind       = (byte)kind,
                speciesId  = SpeciesId,
                localPos   = lp,
                localRot   = lr,
                localScale = transform.localScale,
                progress   = kind == NetKind.FruitOrb ? 0f : SampleProgress(),
                ripe       = kind == NetKind.FruitOrb && Fruit != null && Fruit.IsRipe,
                active     = gameObject.activeSelf,
            };
        }

        /// <summary>Client: apply received state, placing the instance relative to <paramref name="root"/>.</summary>
        public void Apply(in InstanceState s, Transform root)
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

            if (s.kind == (byte)NetKind.FruitOrb)
            {
                var f = Fruit;
                if (f != null) f.SetRipeImmediate(s.ripe);
            }
            else
            {
                ApplyProgress(s.progress);
            }
        }
    }
}
