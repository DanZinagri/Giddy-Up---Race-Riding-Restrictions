using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace RaceRidingRestrictions
{
    public class RidingRestrictionsSettings : ModSettings
    {
        // Only the player's departures from the default lists are stored, so a later change to a default list
        // still reaches them. defNames, so an entry survives its mod being temporarily unloaded.
        public HashSet<string> forcedOn = new HashSet<string>();
        public HashSet<string> forcedOff = new HashSet<string>();
        // View preference only: keep Big and Small's generated sapient animal and mechanoid races out of the list.
        public bool hideSapientAnimals = true;

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref forcedOn, "forcedOn", LookMode.Value);
            Scribe_Collections.Look(ref forcedOff, "forcedOff", LookMode.Value);
            Scribe_Values.Look(ref hideSapientAnimals, "hideSapientAnimals", true);
            if (forcedOn == null)
            {
                forcedOn = new HashSet<string>();
            }
            if (forcedOff == null)
            {
                forcedOff = new HashSet<string>();
            }
        }

        public bool Blocked(string race, bool inDefault)
        {
            return forcedOn.Contains(race) || (inDefault && !forcedOff.Contains(race));
        }

        public void Set(string race, bool inDefault, bool blocked)
        {
            forcedOn.Remove(race);
            forcedOff.Remove(race);
            if (blocked && !inDefault)
            {
                forcedOn.Add(race);
            }
            else if (!blocked && inDefault)
            {
                forcedOff.Add(race);
            }
        }
    }

    public class RidingRestrictionsMod : Mod
    {
        public static RidingRestrictionsSettings settings;

        private const float RowHeight = 30f;

        private class Row
        {
            public ThingDef race;
            public string label;
            public string modName;
            public string searchText;
            public string tip;
            public bool sapient;
        }

        private enum Show
        {
            All,
            Blocked,
            Allowed
        }

        private List<Row> rows;
        private readonly List<Row> filtered = new List<Row>();
        private string search = "";
        private string appliedSearch;
        private Show show;
        private Show appliedShow;
        private Vector2 scroll;
        // Recounted only when a tick box changes.
        private bool countsDirty = true;
        private int blockedCount;
        private int listedCount;
        private int sapientCount;

        public RidingRestrictionsMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<RidingRestrictionsSettings>();
            new Harmony("DanZinagri.GiddyUp.RaceRidingRestrictions").PatchAll(typeof(RidingRestrictionsMod).Assembly);
        }

        public override string SettingsCategory()
        {
            return "Giddy-Up - Race Riding Restrictions";
        }

        private void SetBlocked(ThingDef race, bool blocked)
        {
            settings.Set(race.defName, RidingRestrictions.InDefault(race), blocked);
            Changed();
        }

        // Takes effect the next time anyone tries to mount.
        private void Changed()
        {
            RidingRestrictions.dirty = true;
            countsDirty = true;
        }

        // Every humanlike race except Big and Small's combined races, which follow the races they are built from. Its
        // sapient animals and mechanoids are included but hidden by default (the header tick box).
        private void BuildRows()
        {
            List<ThingDef> races = new List<ThingDef>();
            Dictionary<ThingDef, int> fusionsBuiltFrom = new Dictionary<ThingDef, int>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (def.race == null || !def.race.Humanlike)
                {
                    continue;
                }
                if (RidingRestrictions.IsSapientAnimal(def))
                {
                    sapientCount++;
                }
                List<ThingDef> sources = RidingRestrictions.FusionSources(def);
                if (sources == null)
                {
                    races.Add(def);
                    continue;
                }
                foreach (ThingDef source in sources)
                {
                    if (source != null)
                    {
                        fusionsBuiltFrom[source] = fusionsBuiltFrom.TryGetValue(source, out int n) ? n + 1 : 1;
                    }
                }
            }

            rows = new List<Row>();
            foreach (ThingDef race in races)
            {
                string label = race.LabelCap;
                string modName = race.modContentPack?.Name ?? "?";
                string tip = label + " (" + race.defName + ")";
                if (!race.description.NullOrEmpty())
                {
                    tip += "\n\n" + race.description;
                }
                if (fusionsBuiltFrom.TryGetValue(race, out int fusions))
                {
                    tip += "\n\nAlso covers the " + fusions + " combined Big and Small races built from it.";
                }
                bool sapient = RidingRestrictions.IsSapientAnimal(race);
                if (sapient)
                {
                    tip += "\n\nGenerated by Big and Small's sapient animals or mechanoids module.";
                }
                rows.Add(new Row
                {
                    race = race,
                    label = label,
                    modName = modName,
                    searchText = (label + " " + race.defName + " " + modName).ToLowerInvariant(),
                    tip = tip,
                    sapient = sapient
                });
            }
            rows.Sort((a, b) => string.CompareOrdinal(a.searchText, b.searchText));
        }

        // The list is rebuilt when the search text or the Show mode changes, not on every tick box,
        // so a row does not vanish from under the cursor the moment it is toggled.
        private void ApplyFilter()
        {
            appliedSearch = search;
            appliedShow = show;
            filtered.Clear();
            string needle = search.ToLowerInvariant();
            foreach (Row row in rows)
            {
                if (row.sapient && settings.hideSapientAnimals)
                {
                    continue;
                }
                if (needle.Length != 0 && !row.searchText.Contains(needle))
                {
                    continue;
                }
                if (show != Show.All && RidingRestrictions.IsBlocked(row.race) != (show == Show.Blocked))
                {
                    continue;
                }
                filtered.Add(row);
            }
        }

        private void Recount()
        {
            countsDirty = false;
            blockedCount = 0;
            listedCount = 0;
            foreach (Row row in rows)
            {
                if (row.sapient && settings.hideSapientAnimals)
                {
                    continue;
                }
                listedCount++;
                if (RidingRestrictions.IsBlocked(row.race))
                {
                    blockedCount++;
                }
            }
        }

        private static string Info(Row row)
        {
            string text = row.race.defName + "  -  " + row.modName;
            return RidingRestrictions.InDefault(row.race) ? "default  -  " + text : text;
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            if (rows == null)
            {
                BuildRows();
            }
            if (appliedSearch != search || appliedShow != show)
            {
                ApplyFilter();
            }
            if (countsDirty)
            {
                Recount();
            }

            Text.Font = GameFont.Small;
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, sapientCount > 0 ? 92f : 66f);
            string what = ModsConfig.IsActive("font.mountpassengers")
                ? "Ticked races cannot ride Giddy-Up mounts or board Mount Passengers mounts as passengers."
                : "Ticked races cannot ride Giddy-Up mounts.";
            Widgets.Label(new Rect(header.x, header.y, header.width, 22f), what + " Changes apply to the next mount attempt.");
            Widgets.Label(new Rect(header.x, header.y + 22f, header.width, 22f), blockedCount + " of " + listedCount + " listed races blocked.");
            GUI.color = Color.gray;
            Widgets.Label(new Rect(header.x, header.y + 44f, header.width, 22f), "A pawn counts as the race its body is now, whatever its xenotype. Big and Small's combined races follow the races they are built from.");
            GUI.color = Color.white;
            if (sapientCount > 0)
            {
                bool hideSapient = settings.hideSapientAnimals;
                Widgets.CheckboxLabeled(new Rect(header.x, header.y + 68f, 640f, 24f), "Hide Big and Small's " + sapientCount + " sapient animal and mechanoid races from this list", ref hideSapient);
                if (hideSapient != settings.hideSapientAnimals)
                {
                    settings.hideSapientAnimals = hideSapient;
                    appliedSearch = null;
                    countsDirty = true;
                }
            }

            Rect bar = new Rect(inRect.x, header.yMax + 4f, inRect.width, 28f);
            search = Widgets.TextField(new Rect(bar.x, bar.y, 220f, bar.height), search);
            if (Widgets.ButtonText(new Rect(bar.x + 226f, bar.y, 140f, bar.height), "Show: " + show.ToString().ToLowerInvariant()))
            {
                show = (Show)(((int)show + 1) % 3);
            }
            if (Widgets.ButtonText(new Rect(bar.xMax - 370f, bar.y, 110f, bar.height), "Block shown"))
            {
                foreach (Row row in filtered)
                {
                    SetBlocked(row.race, true);
                }
            }
            if (Widgets.ButtonText(new Rect(bar.xMax - 256f, bar.y, 110f, bar.height), "Allow shown"))
            {
                foreach (Row row in filtered)
                {
                    SetBlocked(row.race, false);
                }
            }
            if (Widgets.ButtonText(new Rect(bar.xMax - 142f, bar.y, 142f, bar.height), "Reset to defaults"))
            {
                settings.forcedOn.Clear();
                settings.forcedOff.Clear();
                Changed();
                appliedSearch = null;
            }

            Rect outRect = new Rect(inRect.x, bar.yMax + 6f, inRect.width, inRect.yMax - bar.yMax - 6f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, filtered.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            // Only the rows on screen are drawn.
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / RowHeight));
            int last = Mathf.Min(filtered.Count - 1, Mathf.CeilToInt((scroll.y + outRect.height) / RowHeight));
            for (int i = first; i <= last; i++)
            {
                Row row = filtered[i];
                Rect rect = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (i % 2 == 1)
                {
                    Widgets.DrawLightHighlight(rect);
                }
                Widgets.DrawHighlightIfMouseover(rect);

                bool blocked = RidingRestrictions.IsBlocked(row.race);
                Widgets.CheckboxDraw(rect.x + 4f, rect.y + 3f, blocked, false);

                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rect.x + 36f, rect.y, 250f, RowHeight), row.label);
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x + 292f, rect.y, rect.width - 296f, RowHeight), Info(row));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;

                if (Mouse.IsOver(rect))
                {
                    TooltipHandler.TipRegion(rect, row.tip);
                }
                // The whole row is the tick box.
                if (Widgets.ButtonInvisible(rect))
                {
                    SetBlocked(row.race, !blocked);
                }
            }
            Widgets.EndScrollView();
        }
    }
}
