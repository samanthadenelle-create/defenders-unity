using System;

namespace DeNelle.Core.State
{
    /// <summary>Stable timer identity independent of grid coordinates and story-castle placement keys.</summary>
    public static class OwnedTownJobKey
    {
        private const string Prefix = "owned-town@";
        public static string Compose(string baseId, string instanceId)
        {
            if (!OwnedBaseProgression.Token(baseId) || !OwnedBaseProgression.Token(instanceId))
                throw new ArgumentException("Owned-town jobs require valid property and structure identities.");
            return Prefix + Uri.EscapeDataString(baseId) + "|" + Uri.EscapeDataString(instanceId);
        }

        public static bool IsOwned(string key) => key != null && key.StartsWith(Prefix, StringComparison.Ordinal);

        public static bool TryParse(string key, out string baseId, out string instanceId)
        {
            baseId = instanceId = null;
            if (!IsOwned(key) || key.Length > 2048) return false;
            string[] parts = key.Substring(Prefix.Length).Split('|');
            if (parts.Length != 2) return false;
            try
            {
                string property = Uri.UnescapeDataString(parts[0]), structure = Uri.UnescapeDataString(parts[1]);
                if (!OwnedBaseProgression.Token(property) || !OwnedBaseProgression.Token(structure) ||
                    Compose(property, structure) != key) return false;
                baseId = property; instanceId = structure; return true;
            }
            catch (UriFormatException) { return false; }
        }
    }
}
