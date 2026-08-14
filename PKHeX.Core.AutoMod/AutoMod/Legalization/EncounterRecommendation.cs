using System;
using System.Collections.Generic;
using System.Linq;
using PKHeX.Core;

namespace PKHeX.Core.AutoMod
{
    /// <summary>
    /// Describes a single legal encounter method available for a species/form in a given game version.
    /// </summary>
    public sealed class EncounterRecommendation
    {
        /// <summary>
        /// The encounter that was found.
        /// </summary>
        public IEncounterable Encounter { get; init; } = null!;

        /// <summary>
        /// Human-readable name of the encounter method (e.g. "Wild Grass", "Static Encounter", "Trade", "Egg", "Raid").
        /// </summary>
        public string MethodName { get; init; } = string.Empty;

        /// <summary>
        /// The game version the encounter belongs to.
        /// </summary>
        public GameVersion Version { get; init; }

        /// <summary>
        /// Minimum level at which the encounter can appear.
        /// </summary>
        public int LevelMin { get; init; }

        /// <summary>
        /// Maximum level at which the encounter can appear.
        /// </summary>
        public int LevelMax { get; init; }

        /// <summary>
        /// Whether the encounter can be shiny.
        /// </summary>
        public Shiny ShinyState { get; init; }

        /// <summary>
        /// The ball that is forced for this encounter (if any).
        /// </summary>
        public Ball FixedBall { get; init; }

        /// <summary>
        /// Recommended priority (lower = higher priority, based on <see cref="EncounterTypeGroup"/> ordering).
        /// </summary>
        public int Priority { get; init; }

        public override string ToString()
        {
            var shiny = ShinyState switch
            {
                Shiny.Always => " (Shiny)",
                Shiny.AlwaysStar => " (Shiny Star)",
                Shiny.AlwaysSquare => " (Shiny Square)",
                Shiny.Never => " (Non-Shiny)",
                _ => string.Empty,
            };
            return $"{MethodName}: Lv.{LevelMin}-{LevelMax} ({Version}){shiny}";
        }
    }

    /// <summary>
    /// Provides utilities for recommending legal encounter methods for a species in a given game version.
    /// </summary>
    public static class EncounterRecommender
    {
        /// <summary>
        /// Returns all legal encounter methods for the specified species/form in the given game version,
        /// sorted by encounter priority (Egg > Static > Trade > Slot > Mystery).
        /// </summary>
        /// <param name="dest">Trainer info / destination context.</param>
        /// <param name="species">Species to find encounters for.</param>
        /// <param name="form">Form to find encounters for.</param>
        /// <param name="level">Target level (0 for any).</param>
        /// <param name="shiny">Whether a shiny encounter is desired.</param>
        /// <param name="nativeOnly">If true, only return encounters from the current version pair.</param>
        /// <returns>List of encounter recommendations sorted by priority.</returns>
        public static List<EncounterRecommendation> GetRecommendedEncounters(
            this ITrainerInfo dest,
            ushort species,
            byte form,
            int level = 0,
            bool shiny = false,
            bool nativeOnly = false
        )
        {
            var blank = EntityBlank.GetBlank(dest);
            blank.Species = species;
            blank.Form = form;
            blank.Gender = blank.GetSaneGender();
            blank.CurrentLevel = (byte)(level > 0 ? level : 1);

            GameVersion[] gamelist = nativeOnly
                ? SimpleEdits.GetIsland(dest.Version) is { } island
                    ? GameUtil.GetVersionsWithinRange(blank, blank.Context)
                        .Where(v => island.Contains(v))
                        .ToArray()
                    : [dest.Version]
                : GameUtil.GetVersionsWithinRange(blank, blank.Context).ToArray();

            // Generate encounters with empty moves (all moves valid)
            var encounters = EncounterMovesetGenerator.GenerateEncounters(
                blank,
                ReadOnlyMemory<ushort>.Empty,
                gamelist
            );

            var result = new List<EncounterRecommendation>();
            var seen = new HashSet<string>();

            foreach (var enc in encounters)
            {
                // Filter by shiny if requested
                if (shiny && enc.Shiny == Shiny.Never)
                    continue;
                if (!shiny && enc.Shiny.IsShiny())
                    continue;

                // Filter by level if specified
                if (level > 0 && (enc.LevelMin > level || enc.LevelMax < level))
                    continue;

                var method = GetEncounterMethodName(enc);
                var key = $"{enc.Name}-{enc.Version}-{enc.LevelMin}-{enc.LevelMax}-{enc.Shiny}";
                if (seen.Add(key))
                {
                    result.Add(new EncounterRecommendation
                    {
                        Encounter = enc,
                        MethodName = method,
                        Version = enc.Version,
                        LevelMin = enc.LevelMin,
                        LevelMax = enc.LevelMax,
                        ShinyState = enc.Shiny,
                        FixedBall = enc.FixedBall,
                        Priority = GetPriority(enc),
                    });
                }
            }

            // Sort by priority (Egg=0, Static=1, Trade=2, Slot=3, Mystery=4)
            return result.OrderBy(r => r.Priority).ThenByDescending(r => r.Version.Generation).ToList();
        }

        /// <summary>
        /// Gets the best (highest priority) recommended encounter for the given species/form/version.
        /// </summary>
        /// <param name="dest">Trainer info / destination context.</param>
        /// <param name="species">Species to find encounters for.</param>
        /// <param name="form">Form to find encounters for.</param>
        /// <param name="level">Target level (0 for any).</param>
        /// <param name="shiny">Whether a shiny encounter is desired.</param>
        /// <param name="nativeOnly">If true, only return encounters from the current version pair.</param>
        /// <returns>The best encounter recommendation, or null if none found.</returns>
        public static EncounterRecommendation? GetBestEncounter(
            this ITrainerInfo dest,
            ushort species,
            byte form,
            int level = 0,
            bool shiny = false,
            bool nativeOnly = false
        )
        {
            return dest.GetRecommendedEncounters(species, form, level, shiny, nativeOnly).FirstOrDefault();
        }

        /// <summary>
        /// Returns a human-readable description of all legal encounter methods for a species in the current version.
        /// </summary>
        /// <param name="dest">Trainer info / destination context.</param>
        /// <param name="species">Species to find encounters for.</param>
        /// <param name="form">Form to find encounters for.</param>
        /// <param name="nativeOnly">If true, only return encounters from the current version pair.</param>
        /// <returns>Formatted string listing all recommended encounter methods.</returns>
        public static string GetEncounterRecommendationReport(
            this ITrainerInfo dest,
            ushort species,
            byte form,
            bool nativeOnly = false
        )
        {
            var recs = dest.GetRecommendedEncounters(species, form, 0, false, nativeOnly);
            if (recs.Count == 0)
                return $"No legal encounters found for {(Species)species} (Form {form}) in {dest.Version}.";

            var lines = recs
                .GroupBy(r => r.MethodName)
                .Select(g => $"  - {g.Key}: {g.Count()} encounter(s), Lv.{g.Min(r => r.LevelMin)}-{g.Max(r => r.LevelMax)}");

            return $"Legal encounter methods for {(Species)species} (Form {form}) in {dest.Version}:\n"
                + string.Join("\n", lines);
        }

        private static string GetEncounterMethodName(IEncounterable enc)
        {
            if (enc is IEncounterEgg)
                return "Egg";

            var typeName = enc.GetType().Name;
            if (typeName.StartsWith("EncounterStatic", StringComparison.Ordinal))
                return "Static Encounter";
            if (typeName.StartsWith("EncounterTrade", StringComparison.Ordinal))
                return "Trade";
            if (typeName.StartsWith("EncounterSlot", StringComparison.Ordinal))
                return "Wild Encounter";
            if (enc is MysteryGift)
                return "Mystery Gift";

            return enc.Name ?? "Unknown";
        }

        private static int GetPriority(IEncounterable enc)
        {
            if (enc is IEncounterEgg)
                return 0;

            var typeName = enc.GetType().Name;
            if (typeName.StartsWith("EncounterStatic", StringComparison.Ordinal))
                return 1;
            if (typeName.StartsWith("EncounterTrade", StringComparison.Ordinal))
                return 2;
            if (typeName.StartsWith("EncounterSlot", StringComparison.Ordinal))
                return 3;
            if (enc is MysteryGift)
                return 4;

            return 5;
        }
    }
}
