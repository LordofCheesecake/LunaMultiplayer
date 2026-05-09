using System;
using System.Globalization;

namespace LmpClient.VesselUtilities
{
    /// <summary>
    /// Defensive rewriter for non-finite double tokens ("Infinity"/"+Infinity"/"-Infinity"/"NaN",
    /// any casing) in the <c>DISCOVERY</c> sub-node of a vessel <see cref="ConfigNode"/>.
    /// </summary>
    public static class DiscoveryInfoSanitizer
    {
        public const double FiniteSentinelSeconds = 1e20;

        private const string DefaultStateValue = "-1";

        private const string DefaultSizeValue = "0";

        public static int SanitizeVesselNode(ConfigNode vesselNode, Guid vesselId, string origin)
        {
            return SanitizeDiscoveryNode(vesselNode?.GetNode("DISCOVERY"), vesselId, origin);
        }

        public static int SanitizeDiscoveryNode(ConfigNode discoveryNode, Guid vesselId, string origin)
        {
            if (discoveryNode == null) return 0;

            var rewrites = 0;
            rewrites += RewriteNonFiniteDouble(discoveryNode, "lifetime", vesselId, origin);
            rewrites += RewriteNonFiniteDouble(discoveryNode, "refTime", vesselId, origin);
            rewrites += RewriteNonFiniteDouble(discoveryNode, "lastObservedTime", vesselId, origin);

            if (rewrites > 0)
            {
                LunaLog.Log($"[LMP]: Sanitised {rewrites} non-finite DiscoveryInfo value(s) " +
                            $"on vessel {vesselId} ({origin}) to keep ProtoVessel.Load from " +
                            $"throwing FormatException in DiscoveryInfo.Load.");
            }
            return rewrites;
        }

        public static bool EnsureSafeDiscoveryInfo(ProtoVessel vesselProto)
        {
            if (vesselProto == null) return false;

            var changed = false;
            if (vesselProto.discoveryInfo == null)
            {
                vesselProto.discoveryInfo = new ConfigNode("DISCOVERY");
                changed = true;
            }

            var node = vesselProto.discoveryInfo;
            var vesselId = vesselProto.vesselID;
            var sentinel = FiniteSentinelSeconds.ToString("R", CultureInfo.InvariantCulture);

            if (EnsureFiniteDouble(node, "lifetime", sentinel, vesselId)) changed = true;
            if (EnsureFiniteDouble(node, "refTime", sentinel, vesselId)) changed = true;
            if (EnsureFiniteDouble(node, "lastObservedTime", "0", vesselId)) changed = true;

            if (AddIfMissing(node, "state", DefaultStateValue)) changed = true;
            if (AddIfMissing(node, "size", DefaultSizeValue)) changed = true;

            if (changed)
            {
                LunaLog.Log($"[LMP]: Vessel {vesselId} pre-Load DiscoveryInfo normalised " +
                            $"(state={node.GetValue("state")}, " +
                            $"lifetime={node.GetValue("lifetime")}, " +
                            $"refTime={node.GetValue("refTime")}, " +
                            $"lastObservedTime={node.GetValue("lastObservedTime")}, " +
                            $"size={node.GetValue("size")}). " +
                            $"This blocks ProtoVessel.Load's synthesise-then-parse path that " +
                            $"throws FormatException on \"Infinity\"/\"\" inside DiscoveryInfo.Load.");
            }
            return changed;
        }

        private static bool EnsureFiniteDouble(ConfigNode node, string key, string replacement, Guid vesselId)
        {
            var raw = node.GetValue(key);
            string reason;
            if (raw == null)
            {
                reason = "missing";
            }
            else if (string.IsNullOrWhiteSpace(raw))
            {
                reason = $"empty('{raw}')";
            }
            else if (IsNonFiniteDoubleToken(raw.Trim(), out _))
            {
                reason = $"non-finite('{raw}')";
            }
            else
            {
                return false;
            }

            if (raw == null) node.AddValue(key, replacement);
            else node.SetValue(key, replacement);

            LunaLog.Log($"[LMP]: Vessel {vesselId} (pre-Load) DISCOVERY/{key} {reason}; " +
                        $"setting to '{replacement}' to survive DiscoveryInfo.Load.");
            return true;
        }

        private static bool AddIfMissing(ConfigNode node, string key, string value)
        {
            if (node.HasValue(key)) return false;
            node.AddValue(key, value);
            return true;
        }

        private static int RewriteNonFiniteDouble(ConfigNode node, string key, Guid vesselId, string origin)
        {
            var raw = node.GetValue(key);
            if (string.IsNullOrEmpty(raw)) return 0;

            var trimmed = raw.Trim();
            if (!IsNonFiniteDoubleToken(trimmed, out var negative)) return 0;

            var replacement = (negative ? -FiniteSentinelSeconds : FiniteSentinelSeconds)
                .ToString("R", CultureInfo.InvariantCulture);
            node.SetValue(key, replacement);
            LunaLog.Log($"[LMP]: Vessel {vesselId} ({origin}) DISCOVERY/{key}='{raw}' is " +
                        $"non-finite; rewriting to '{replacement}' to survive DiscoveryInfo.Load.");
            return 1;
        }

        private static bool IsNonFiniteDoubleToken(string value, out bool negative)
        {
            negative = false;
            if (string.Equals(value, "Infinity", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "+Infinity", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (string.Equals(value, "-Infinity", StringComparison.OrdinalIgnoreCase))
            {
                negative = true;
                return true;
            }
            return string.Equals(value, "NaN", StringComparison.OrdinalIgnoreCase);
        }
    }
}
