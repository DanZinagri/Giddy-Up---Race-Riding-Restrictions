using System.Reflection;
using HarmonyLib;
using MountPassengers;
using Verse;

namespace RaceRidingRestrictions.MountPassengersCompat
{
    [StaticConstructorOnStartup]
    public static class MountPassengersCompatInit
    {
        static MountPassengersCompatInit()
        {
            new Harmony("DanZinagri.GiddyUp.RaceRidingRestrictions.MountPassengers").PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    // Mount Passengers never asks Giddy-Up whether a passenger can ride, only "humanlike, same faction". Every way of
    // boarding (gizmo, float menu, the boarding job, raid attach) goes through CanBoard, and its reason is free text,
    // so the player sees why.
    [HarmonyPatch(typeof(MountPassengerUtility), nameof(MountPassengerUtility.CanBoard))]
    public static class Patch_CanBoard
    {
        public static void Postfix(Pawn passenger, ref string reason, ref bool __result)
        {
            if (__result && RidingRestrictions.CannotRide(passenger))
            {
                reason = passenger.LabelShortCap + " (" + passenger.def.label + ") can't ride.";
                __result = false;
            }
        }
    }

    // Raid generation picks its passengers with its own check before CanBoard is ever involved.
    [HarmonyPatch(typeof(MountPassengerUtility), nameof(MountPassengerUtility.CanAttachGeneratedRaidPassenger))]
    public static class Patch_CanAttachGeneratedRaidPassenger
    {
        public static void Postfix(Pawn passenger, ref bool __result)
        {
            if (__result && RidingRestrictions.CannotRide(passenger))
            {
                __result = false;
            }
        }
    }
}
