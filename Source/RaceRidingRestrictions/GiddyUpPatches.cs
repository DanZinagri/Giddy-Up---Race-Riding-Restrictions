using System.Collections.Generic;
using System.Reflection;
using GiddyUp;
using HarmonyLib;
using Verse;

namespace RaceRidingRestrictions
{
    // Patched from RidingRestrictionsMod's constructor.

    // Float menu, auto-mount, caravan loading and mech riding all ask this first.
    // Giddy-Up's refusal messages are fixed per reason; IncompatibleEquipment is the one it uses itself for a rider
    // it refuses.
    [HarmonyPatch(typeof(IsMountableUtility), nameof(IsMountableUtility.IsCapableOfRiding))]
    public static class Patch_IsCapableOfRiding
    {
        public static void Postfix(Pawn pawn, ref IsMountableUtility.Reason reason, ref bool __result)
        {
            if (__result && RidingRestrictions.CannotRide(pawn))
            {
                reason = IsMountableUtility.Reason.IncompatibleEquipment;
                __result = false;
            }
        }
    }

    // Every mount check that knows the rider, including NPC remounts and RimJoust's knights.
    [HarmonyPatch(typeof(IsMountableUtility), nameof(IsMountableUtility.IsMountable))]
    public static class Patch_IsMountable
    {
        public static void Postfix(Pawn rider, ref IsMountableUtility.Reason reason, ref bool __result)
        {
            if (__result && rider != null && RidingRestrictions.CannotRide(rider))
            {
                reason = IsMountableUtility.Reason.IncompatibleEquipment;
                __result = false;
            }
        }
    }

    // Raid, visitor and trader mount generation never consults the rider checks above. Hide blocked pawns from the
    // generator, then put them back so the incident still spawns them, on foot.
    // MountUtility is internal in Giddy-Up's 1.6 build and public in its 1.7 one, so the two patches on it name the
    // class as a string and the same source compiles for both.
    [HarmonyPatch]
    public static class Patch_GenerateMounts
    {
        public static MethodBase TargetMethod() => AccessTools.Method("GiddyUp.MountUtility:GenerateMounts");

        public static void Prefix(ref List<Pawn> list, out List<Pawn> __state)
        {
            __state = null;
            if (list == null)
            {
                return;
            }
            List<Pawn> kept = null;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                if (p?.RaceProps != null && p.RaceProps.Humanlike && RidingRestrictions.CannotRide(p))
                {
                    if (__state == null)
                    {
                        __state = new List<Pawn>();
                        kept = new List<Pawn>(list.Count);
                        for (int j = 0; j < i; j++)
                        {
                            kept.Add(list[j]);
                        }
                    }
                    __state.Add(p);
                }
                else if (kept != null)
                {
                    kept.Add(p);
                }
            }
            if (kept != null)
            {
                list = kept;
            }
        }

        public static void Postfix(ref List<Pawn> list, List<Pawn> __state)
        {
            if (__state != null && list != null)
            {
                list.AddRange(__state);
            }
        }
    }

    // Backstop: everything that seats a rider ends in this private method, including paths that skip the checks
    // above (RimJoust mounts its visiting jousters straight through GoMount). A null animal means dismount and must
    // always go through.
    [HarmonyPatch]
    public static class Patch_Mount
    {
        public static MethodBase TargetMethod() => AccessTools.Method("GiddyUp.MountUtility:Mount");

        public static bool Prefix(Pawn rider, Pawn animal)
        {
            return animal == null || !RidingRestrictions.CannotRide(rider);
        }
    }

    // Giddy-Up's mech raids (What The Hack) seat humanlike raiders on hacked mechs with only a "humanlike, not already
    // mounted" check. Skipping the assignment leaves that mech riderless.
    [HarmonyPatch(typeof(GiddyUpMechanoids.Patch_Raid_PostProcessSpawned), "AssignRider")]
    public static class Patch_AssignRider
    {
        public static bool Prefix(Pawn rider)
        {
            return !RidingRestrictions.CannotRide(rider);
        }
    }
}
