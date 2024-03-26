using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace TX_TOOLBOX
{
    /// <summary>
    /// 淘汰最久未使用缓存
    /// </summary>
    public class LruCache<TKey, TCache>
    {
        private readonly int capacity;
        private readonly Dictionary<TKey, TCache> innerCache;
        private readonly LinkedList<TKey> linkedList;

        public LruCache(int capacity)
        {
            this.capacity = capacity;
            innerCache = new Dictionary<TKey, TCache>();
            linkedList = new LinkedList<TKey>();
        }

        public bool Contains(TKey key, bool updatePriority)
        {
            if (innerCache.TryGetValue(key, out TCache cache))
            {
                if (updatePriority)
                {
                    linkedList.Remove(key);
                    linkedList.AddLast(key);
                }
                return true;
            }
            return false;
        }

        public bool Get(TKey key, out TCache cache)
        {
            if (innerCache.TryGetValue(key, out cache))
            {
                linkedList.Remove(key);
                linkedList.AddLast(key);
                return true;
            }
            return false;
        }

        public void Put(TKey key, TCache value)
        {
            TCache cache;
            if (innerCache.TryGetValue(key, out cache))
            {
                innerCache[key] = value;
                linkedList.Remove(key);
                linkedList.AddLast(key);
            }
            else
            {
                var newNode = new LinkedListNode<TKey>(key);
                linkedList.AddLast(newNode);
                innerCache[key] = value;
                if (innerCache.Count > capacity)
                {
                    var first = linkedList.First.Value;
                    linkedList.RemoveFirst();
                    innerCache.Remove(first);
                }
            }
        }
    }
}
