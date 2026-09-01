using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using ActiveScanner.Models;

namespace ActiveScanner.Services
{
    /// <summary>
    /// In-memory cache service for AD query results
    /// </summary>
    public class CacheService
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _domainCache = new();
        private readonly ConcurrentDictionary<string, CacheEntry> _groupCache = new();

        /// <summary>
        /// Gets the cache key for a domain/OU path
        /// </summary>
        public static string GetDomainCacheKey(string distinguishedName)
        {
            return distinguishedName.ToLowerInvariant();
        }

        /// <summary>
        /// Gets the cache key for a group
        /// </summary>
        public static string GetGroupCacheKey(string groupId)
        {
            return $"group:{groupId}";
        }

        /// <summary>
        /// Tries to get a cached domain/OU result
        /// </summary>
        public CacheEntry? GetDomainCache(string distinguishedName, int cacheTtlMinutes)
        {
            var key = GetDomainCacheKey(distinguishedName);
            
            if (_domainCache.TryGetValue(key, out var entry) && entry.IsValid(cacheTtlMinutes))
            {
                return entry;
            }

            return null;
        }

        /// <summary>
        /// Tries to get a cached group result
        /// </summary>
        public CacheEntry? GetGroupCache(string groupId, int cacheTtlMinutes)
        {
            var key = GetGroupCacheKey(groupId);
            
            if (_groupCache.TryGetValue(key, out var entry) && entry.IsValid(cacheTtlMinutes))
            {
                return entry;
            }

            return null;
        }

        /// <summary>
        /// Caches domain/OU results
        /// </summary>
        public void SetDomainCache(string distinguishedName, string displayName, 
            List<AdObjectInfo> results, List<FolderItem> subfolders)
        {
            var key = GetDomainCacheKey(distinguishedName);
            var entry = new CacheEntry
            {
                Key = key,
                DisplayName = displayName,
                LastUpdated = DateTime.Now,
                Results = results.ToList(),
                Subfolders = subfolders.ToList(),
                IsGroupCache = false
            };

            _domainCache[key] = entry;
        }

        /// <summary>
        /// Caches group results
        /// </summary>
        public void SetGroupCache(string groupId, string groupName, List<AdObjectInfo> results)
        {
            var key = GetGroupCacheKey(groupId);
            var entry = new CacheEntry
            {
                Key = key,
                DisplayName = groupName,
                LastUpdated = DateTime.Now,
                Results = results.ToList(),
                Subfolders = new List<FolderItem>(),
                IsGroupCache = true
            };

            _groupCache[key] = entry;
        }

        /// <summary>
        /// Invalidates a specific domain/OU cache entry
        /// </summary>
        public void InvalidateDomainCache(string distinguishedName)
        {
            var key = GetDomainCacheKey(distinguishedName);
            _domainCache.TryRemove(key, out _);
        }

        /// <summary>
        /// Invalidates a specific group cache entry
        /// </summary>
        public void InvalidateGroupCache(string groupId)
        {
            var key = GetGroupCacheKey(groupId);
            _groupCache.TryRemove(key, out _);
        }

        /// <summary>
        /// Clears all domain cache entries
        /// </summary>
        public void ClearDomainCache()
        {
            _domainCache.Clear();
        }

        /// <summary>
        /// Clears all group cache entries
        /// </summary>
        public void ClearGroupCache()
        {
            _groupCache.Clear();
        }

        /// <summary>
        /// Clears all cache entries
        /// </summary>
        public void ClearAllCache()
        {
            _domainCache.Clear();
            _groupCache.Clear();
        }

        /// <summary>
        /// Gets statistics about the cache
        /// </summary>
        public (int DomainEntries, int GroupEntries) GetCacheStats()
        {
            return (_domainCache.Count, _groupCache.Count);
        }

        /// <summary>
        /// Gets the current cache entry for a domain path (without checking validity)
        /// </summary>
        public CacheEntry? GetCurrentDomainCacheEntry(string distinguishedName)
        {
            var key = GetDomainCacheKey(distinguishedName);
            _domainCache.TryGetValue(key, out var entry);
            return entry;
        }

        /// <summary>
        /// Gets the current cache entry for a group (without checking validity)
        /// </summary>
        public CacheEntry? GetCurrentGroupCacheEntry(string groupId)
        {
            var key = GetGroupCacheKey(groupId);
            _groupCache.TryGetValue(key, out var entry);
            return entry;
        }
    }
}
