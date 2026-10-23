using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace RaceRidingRestrictions
{
    // A default list. All loaded defs of this type are merged, so another mod can add its own list or patch this one.
    public class RidingRestrictionDef : Def
    {
        // Race ThingDef defNames, not def references: an entry whose race is not loaded is skipped without an error,
        // and MayRequire works on each entry.
        public List<string> races = new List<string>();
    }

    public static class RidingRestrictions
    {
        // Set whenever the settings change; the per-race answers are recomputed on the next check.
        public static bool dirty = true;
        private static readonly Dictionary<ThingDef, bool> verdicts = new Dictionary<ThingDef, bool>();
        private static HashSet<string> defaults;

        // Big and Small builds a combined race for every mix of body changes (centaur with a tail, and so on) and
        // records the races it was built from in RaceExtension.isFusionOf. Read by reflection so Big and Small stays
        // optional; without it every race simply stands for itself.
        private static bool fusionLookupReady;
        private static Type raceExtensionType;
        private static FieldInfo isFusionOfField;

        // Big and Small's Sapient Animals / Sapient Mechanoids modules generate a humanlike race for every animal and
        // mech, registered in HumanlikeAnimalGenerator.humanlikeAnimals (keyed by the humanlike race). They are ordinary
        // races for blocking; the settings menu only hides them from its list unless the player chooses to see them.
        private static bool sapientLookupReady;
        private static IDictionary sapientAnimals;

        private static HashSet<string> Defaults
        {
            get
            {
                if (defaults == null)
                {
                    defaults = new HashSet<string>();
                    foreach (RidingRestrictionDef def in DefDatabase<RidingRestrictionDef>.AllDefsListForReading)
                    {
                        foreach (string race in def.races)
                        {
                            defaults.Add(race);
                        }
                    }
                }
                return defaults;
            }
        }

        public static bool InDefault(ThingDef race)
        {
            return Defaults.Contains(race.defName);
        }

        public static bool IsBlocked(ThingDef race)
        {
            return RidingRestrictionsMod.settings.Blocked(race.defName, InDefault(race));
        }

        public static bool IsSapientAnimal(ThingDef race)
        {
            if (!sapientLookupReady)
            {
                Type generator = GenTypes.GetTypeInAnyAssembly("BigAndSmall.HumanlikeAnimalGenerator");
                sapientAnimals = generator?.GetField("humanlikeAnimals", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IDictionary;
                sapientLookupReady = true;
            }
            return sapientAnimals != null && sapientAnimals.Contains(race);
        }

        // The races a Big and Small combined race was built from, or null for an ordinary race.
        public static List<ThingDef> FusionSources(ThingDef race)
        {
            if (!fusionLookupReady)
            {
                raceExtensionType = GenTypes.GetTypeInAnyAssembly("BigAndSmall.RaceExtension");
                isFusionOfField = raceExtensionType?.GetField("isFusionOf", BindingFlags.Public | BindingFlags.Instance);
                fusionLookupReady = true;
            }
            if (isFusionOfField == null || race.modExtensions == null)
            {
                return null;
            }
            foreach (DefModExtension extension in race.modExtensions)
            {
                if (raceExtensionType.IsInstanceOfType(extension) && isFusionOfField.GetValue(extension) is List<ThingDef> sources)
                {
                    return sources;
                }
            }
            return null;
        }

        // A combined race is blocked when it is listed itself or when any race it was built from is blocked.
        private static bool Compute(ThingDef race)
        {
            if (IsBlocked(race))
            {
                return true;
            }
            List<ThingDef> sources = FusionSources(race);
            if (sources != null)
            {
                foreach (ThingDef source in sources)
                {
                    if (source != null && IsBlocked(source))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // Only called when Giddy-Up or Mount Passengers asks whether a pawn may ride, never per tick. pawn.def is the
        // pawn's current race, so a body gene that swapped it to a centaur counts whatever the xenotype.
        public static bool CannotRide(Pawn pawn)
        {
            ThingDef race = pawn?.def;
            if (race == null)
            {
                return false;
            }
            if (dirty)
            {
                verdicts.Clear();
                dirty = false;
            }
            if (!verdicts.TryGetValue(race, out bool blocked))
            {
                blocked = Compute(race);
                verdicts[race] = blocked;
            }
            return blocked;
        }
    }
}
