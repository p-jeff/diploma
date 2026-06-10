using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gsplat
{
    public static class GsplatResourceManager
    {
        class Cache
        {
            public GsplatResource Resource;
            public int RefCount;
        }

        // Key includes the asset type so that reimporting a .ply with a different compression
        // (Spark → Uncompressed) does not collide with the previous entry. Unity reuses
        // GetInstanceID() across reimports of the same file (same GUID + localFileID), so
        // without the type in the key a Release() from an old renderer would decrement the
        // refcount of the newly created resource for the new type, disposing it prematurely.
        static readonly Dictionary<(int, Type), Cache> k_resourceCache = new();

        public static GsplatResource Get(GsplatAsset asset)
        {
            var key = (asset.GetInstanceID(), asset.GetType());
            if (k_resourceCache.TryGetValue(key, out var cache))
            {
                cache.RefCount++;
                return cache.Resource;
            }

            cache = new Cache
            {
                Resource = asset.CreateResource(),
                RefCount = 1
            };
            k_resourceCache[key] = cache;
            return cache.Resource;
        }

        public static void Release(GsplatAsset asset)
        {
            Release(asset.GetInstanceID(), asset.GetType());
        }

        public static void Release(int instanceID, Type assetType)
        {
            if (instanceID == 0)
                return;
            var key = (instanceID, assetType);
            if (!k_resourceCache.TryGetValue(key, out var cache))
            {
                Debug.LogWarning("Trying to release a GPU resource that is not cached.");
                return;
            }

            cache.RefCount--;
            if (cache.RefCount != 0) return;
            cache.Resource.Dispose();
            k_resourceCache.Remove(key);
        }
    }
}
