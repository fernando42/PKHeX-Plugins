using System;
using System.Linq;
using static PKHeX.Core.AutoMod.TracebackType;

namespace PKHeX.Core.AutoMod
{
    /// <summary>
    /// Provides configurable legal PID generation based on <see cref="PIDGenerationSettings"/>.
    /// </summary>
    public static class LegalPIDGenerator
    {
        /// <summary>
        /// Current PID generation settings used by the legalization pipeline.
        /// </summary>
        public static PIDGenerationSettings Settings { get; set; } = PIDGenerationSettings.Default;

        /// <summary>
        /// Generates a legal PID for the given Pokémon based on the provided settings and encounter.
        /// Respects shiny type, nature, gender, and Hidden Power type constraints.
        /// </summary>
        /// <param name="pk">Pokémon to modify</param>
        /// <param name="enc">Encounter the Pokémon originated from</param>
        /// <param name="set">Battle template with requested properties</param>
        /// <param name="settings">PID generation settings (null for default)</param>
        /// <param name="tb">Traceback handler</param>
        public static void GenerateLegalPID(
            this PKM pk,
            IEncounterable enc,
            IBattleTemplate set,
            PIDGenerationSettings? settings = null,
            ITracebackHandler? tb = null
        )
        {
            settings ??= Settings;

            // For generations 1-2, no PID exists
            if (pk.Generation <= 2)
                return;

            // Skip encounters with fixed PID/IV (raids, mystery gifts, etc.)
            if (APILegality.IsPIDIVSet(pk, enc))
            {
                tb?.Handle(PID_IV, "Encounter has fixed PID/IV, skipping");
                return;
            }

            // For Gen 6+, PID is random and not correlated with IVs/nature (except for specific encounters)
            if (enc.Generation is not (3 or 4 or 5))
            {
                GenerateModernPID(pk, enc, set, settings, tb);
                return;
            }

            // For Gen 3-5, PID is correlated with IVs, nature, gender, and ability
            GenerateLegacyPID(pk, enc, set, settings, tb);
        }

        /// <summary>
        /// Generates a PID for Gen 6+ Pokémon where PID is generally random.
        /// </summary>
        private static void GenerateModernPID(
            PKM pk,
            IEncounterable enc,
            IBattleTemplate set,
            PIDGenerationSettings settings,
            ITracebackHandler? tb
        )
        {
            // For Gen 6+, PID is random unless shiny constraints apply
            if (!settings.RespectShinyType)
            {
                pk.PID = Util.Rand32();
                tb?.Handle(PID_IV, "Generated random PID (no shiny constraint)");
                return;
            }

            var shiny = settings.ShinyType;
            if (shiny == Shiny.Never)
            {
                // Generate non-shiny PID
                GenerateNonShinyPID(pk);
                tb?.Handle(PID_IV, "Generated non-shiny PID");
                return;
            }

            if (shiny is Shiny.Always or Shiny.AlwaysStar or Shiny.AlwaysSquare)
            {
                GenerateShinyPID(pk, shiny, settings.PreferSquareShiny);
                tb?.Handle(PID_IV, $"Generated {shiny} PID");
                return;
            }

            // Random shiny chance
            pk.PID = Util.Rand32();
            tb?.Handle(PID_IV, "Generated random PID (random shiny chance)");
        }

        /// <summary>
        /// Generates a PID for Gen 3-5 Pokémon where PID is correlated with IVs/nature/gender.
        /// </summary>
        private static void GenerateLegacyPID(
            PKM pk,
            IEncounterable enc,
            IBattleTemplate set,
            PIDGenerationSettings settings,
            ITracebackHandler? tb
        )
        {
            // Determine the PID method for this encounter
            var method = APILegality.FindLikelyPIDType(pk);
            if (method == PIDType.None)
            {
                if (enc is EncounterGift3 g3)
                    method = g3.Method;
                else
                    method = PIDType.Method_1;
            }

            // Handle CXD special case
            if (pk.Version == GameVersion.CXD && method != PIDType.PokeSpot)
                method = PIDType.CXD;

            // Get the hidden power type if requested
            var hpType = settings.RespectHiddenPower ? settings.HiddenPowerType : set.HiddenPowerType;
            if (hpType < 0)
                hpType = -1;

            // Get the desired nature
            var nature = settings.RespectNature && settings.Nature != Nature.Random
                ? settings.Nature
                : pk.Nature;

            // Get the desired gender
            var gender = settings.RespectGender && settings.Gender.HasValue
                ? settings.Gender.Value
                : pk.Gender;

            // Get the desired shiny state
            var shiny = settings.RespectShinyType ? settings.ShinyType : (set.Shiny ? Shiny.Always : Shiny.Never);

            // Sync encryption constant for Gen 3-5
            if (settings.SyncEncryptionConstant)
            {
                var ec = pk.PID;
                pk.EncryptionConstant = ec;
                var pidxor = ((pk.TID16 ^ pk.SID16 ^ (int)(ec & 0xFFFF) ^ (int)(ec >> 16)) & ~0x7) == 8;
                pk.PID = pidxor ? ec ^ 0x80000000 : ec;
                tb?.Handle(PID_IV, $"Synced EC as PID for Generation {enc.Generation}");
            }

            // Use PKHeX's PID generation to find a valid seed
            FindLegacyPID(pk, method, hpType, shiny, enc, nature, gender, settings.MaxIterations);
            APILegality.ValidateGender(pk);
            tb?.Handle(PID_IV, $"Generated legacy PID (Method: {method}, Nature: {nature}, Shiny: {shiny})");
        }

        /// <summary>
        /// Searches for a valid PID that satisfies all constraints for Gen 3-5.
        /// </summary>
        private static void FindLegacyPID(
            PKM pk,
            PIDType method,
            int hpType,
            Shiny shiny,
            IEncounterable enc,
            Nature nature,
            byte gender,
            int maxIterations
        )
        {
            var iterPKM = pk.Clone();
            var gr = pk.PersonalInfo.Gender;
            var count = 0;
            var compromise = false;

            do
            {
                if (count >= 2_500_000)
                    compromise = true;

                uint seed = Util.Rand32();
                SetValuesFromSeed(pk, method, seed);

                // Check nature
                if (!compromise && pk.PID % 25 != (uint)nature)
                    continue;

                // Check gender
                if (pk.Gender != EntityGender.GetFromPIDAndRatio(pk.PID, gr))
                {
                    if (pk.Gender != gender)
                        continue;
                }

                // Check ability
                if (pk.AbilityNumber != iterPKM.AbilityNumber && !compromise && pk.Nature != iterPKM.Nature)
                    continue;
                if (pk.PIDAbility != iterPKM.PIDAbility && !compromise)
                    continue;

                // Check Hidden Power type
                if (hpType >= 0 && pk.HPType != hpType)
                    continue;

                // Check shiny state
                if (shiny == Shiny.Never && pk.ShinyXor < 16)
                    continue;
                if (shiny == Shiny.Always && pk.ShinyXor > 15)
                    continue;
                if (shiny == Shiny.AlwaysSquare && pk.ShinyXor != 0)
                    continue;
                if (shiny == Shiny.AlwaysStar && pk.ShinyXor != 1)
                    continue;

                // Check Unown form
                if (pk.Species == (int)Species.Unown)
                {
                    if (enc.Generation == 3 && pk.Form != EntityPID.GetUnownForm3(pk.PID))
                        continue;
                }

                // CXD verification
                if (pk.Version == GameVersion.CXD && method == PIDType.CXD)
                {
                    pk.EncryptionConstant = pk.PID;
                    var la = new LegalityAnalysis(pk);
                    if (!la.Info.PIDIVMatches)
                        continue;
                }

                break;
            } while (++count < maxIterations);
        }

        /// <summary>
        /// Generates a non-shiny PID for Gen 6+ Pokémon.
        /// </summary>
        private static void GenerateNonShinyPID(PKM pk)
        {
            while (true)
            {
                pk.PID = Util.Rand32();
                if (pk.ShinyXor >= 16)
                    break;
            }
        }

        /// <summary>
        /// Generates a shiny PID for Gen 6+ Pokémon.
        /// </summary>
        private static void GenerateShinyPID(PKM pk, Shiny shiny, bool preferSquare)
        {
            while (true)
            {
                pk.PID = Util.Rand32();
                var xor = pk.ShinyXor;

                if (preferSquare || shiny == Shiny.AlwaysSquare)
                {
                    if (xor != 0)
                        continue;
                    break;
                }

                if (shiny == Shiny.AlwaysStar && xor != 1)
                    continue;
                if (shiny == Shiny.Always && xor >= 16)
                    continue;

                break;
            }
        }

        /// <summary>
        /// Sets PID and IVs from a given seed using the specified PID method.
        /// Reimplements the forward RNG generation that was removed from PKHeX.Core.
        /// </summary>
        public static void SetValuesFromSeed(PKM pk, PIDType method, uint seed)
        {
            // For non-Gen3-4 encounters, just set a random PID from the seed
            if (pk.Generation < 3)
            {
                pk.PID = seed;
                return;
            }

            switch (method)
            {
                case PIDType.CXD or PIDType.CXD_ColoStarter:
                {
                    // XDRNG: IV, IV, ability, PID, PID
                    var iv1 = XDRand(ref seed);
                    var iv2 = XDRand(ref seed);
                    seed = XDRNG.Next(seed); // ability
                    var hid = XDRand(ref seed);
                    var lod = XDRand(ref seed);
                    pk.PID = (hid << 16) | lod;
                    SetIVs(pk, iv1, iv2);
                    return;
                }
                case PIDType.Channel when pk is PK3 pk3:
                {
                    EncounterGift3.SetValuesFromSeedChannel(pk3, seed);
                    return;
                }
                case PIDType.Method_2:
                {
                    var pid = ClassicEraRNG.GetSequentialPID(ref seed);
                    seed = LCRNG.Next(seed); // VBlank skip
                    var iv32 = ClassicEraRNG.GetSequentialIVs(ref seed);
                    SetValues(pk, pid, iv32);
                    return;
                }
                case PIDType.Method_3:
                {
                    var iv32 = ClassicEraRNG.GetSequentialIVs(ref seed);
                    var pid = ClassicEraRNG.GetSequentialPID(ref seed);
                    SetValues(pk, pid, iv32);
                    return;
                }
                case PIDType.Method_4:
                {
                    var pid = ClassicEraRNG.GetSequentialPID(ref seed);
                    seed = LCRNG.Next3(seed); // 3 skips
                    var iv32 = ClassicEraRNG.GetSequentialIVs(ref seed);
                    SetValues(pk, pid, iv32);
                    return;
                }
                default:
                {
                    // Method 1 (and BACD variants): PID first, then IVs
                    var pid = method.IsUnown
                        ? ClassicEraRNG.GetReversePID(seed)
                        : ClassicEraRNG.GetSequentialPID(ref seed);
                    if (method.IsUnown)
                        seed = LCRNG.Next2(seed);
                    var iv32 = ClassicEraRNG.GetSequentialIVs(ref seed);
                    SetValues(pk, pid, iv32);
                    return;
                }
            }
        }

        private static uint XDRand(ref uint seed)
        {
            seed = XDRNG.Next(seed);
            return seed >> 16;
        }

        private static void SetValues(PKM pk, uint pid, uint iv32)
        {
            pk.PID = pid;
            pk.IV_HP = (int)(iv32 & 0x1F);
            pk.IV_ATK = (int)((iv32 >> 5) & 0x1F);
            pk.IV_DEF = (int)((iv32 >> 10) & 0x1F);
            pk.IV_SPE = (int)((iv32 >> 15) & 0x1F);
            pk.IV_SPA = (int)((iv32 >> 20) & 0x1F);
            pk.IV_SPD = (int)((iv32 >> 25) & 0x1F);
        }

        private static void SetIVs(PKM pk, uint iv1, uint iv2)
        {
            pk.IV_HP = (int)((iv1 >> 10) & 0x1F);
            pk.IV_ATK = (int)((iv1 >> 5) & 0x1F);
            pk.IV_DEF = (int)(iv1 & 0x1F);
            pk.IV_SPE = (int)((iv2 >> 10) & 0x1F);
            pk.IV_SPA = (int)((iv2 >> 5) & 0x1F);
            pk.IV_SPD = (int)(iv2 & 0x1F);
        }
    }
}
