using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace HediffHUD
{
    [StaticConstructorOnStartup]
    public static class HediffHudMod
    {
        static HediffHudMod()
        {
            new Harmony("Hexi.HediffHud").PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    public class HediffCollection {
        public BodyPartDef part;
        public List<RecipeDef> recipes;

        public HediffCollection(BodyPartDef def) {
            part = def;
            recipes = new List<RecipeDef>();
        }
    }
    public class HediffGroupCollection {
        public BodyPartGroupDef part;
        public List<RecipeDef> recipes;

        public HediffGroupCollection(BodyPartGroupDef def) {
            part = def;
            recipes = new List<RecipeDef>();
        }
    }

    [HarmonyPatch(typeof(HealthCardUtility), "VisibleHediffs")]
    public static class HediffHudMod_VisibleHediffPostfix
    {
        [HarmonyPostfix]
        private static IEnumerable<Hediff> VisibleHediffPostfix(
            IEnumerable<Hediff> returned,
            Pawn pawn, bool showBloodLoss)
        {
            var missingOrAddedParts = new List<Hediff>();
            foreach (Hediff h in returned)
            {
                if (!missingOrAddedParts.Contains(h) && (h is Hediff_MissingPart || h is Hediff_AddedPart)) missingOrAddedParts.Add(h);
                yield return h; // things already shown - use this?
            }
            
            var theList = new List<HediffCollection>();
            var theListGroup = new List<HediffGroupCollection>();

            foreach (RecipeDef recipe in pawn.def.AllRecipes) {
                // note: a dog said isnt reporting its recipes as added part or implant
                if (recipe.addsHediff == null /*|| !recipe.addsHediff.countsAsAddedPartOrImplant*/) continue;

                if (recipe.appliedOnFixedBodyPartGroups != null) {
                    foreach (var bodyGroup in recipe.appliedOnFixedBodyPartGroups) {
                        var first = theListGroup.FirstOrDefault(item => item.part.Equals(bodyGroup));
                        if (first == null) {
                            first = new HediffGroupCollection(bodyGroup);
                            theListGroup.Add(first);
                        }
                        first.recipes.Add(recipe);
                    }
                }
                if (recipe.appliedOnFixedBodyParts != null) {
                    foreach (BodyPartDef bodyPartDef in recipe.appliedOnFixedBodyParts) {
                        var first = theList.FirstOrDefault(item => item.part.Equals(bodyPartDef));
                        if (first == null) {
                            first = new HediffCollection(bodyPartDef);
                            theList.Add(first);
                        }
                        first.recipes.Add(recipe);
                    }
                }
            }
            foreach (BodyPartRecord partRecord in pawn.def.race.body.AllParts)
            {
                // get relevant results
                var parts = theList.FindAll(item => item.part.Equals(partRecord.def));
                var groups = theListGroup.FindAll(item => partRecord.groups.Contains(item.part));
                var finalList = new List<RecipeDef>();
                parts.ForEach(item => finalList = finalList.Union(item.recipes).ToList());
                groups.ForEach(item => finalList = finalList.Union(item.recipes).ToList());
                // remove recipes that can't be used right now
                finalList.RemoveAll(recipe => {
                    // make sure the parent part exists and is not an added part
                    var parent = partRecord.parent;
                    while (parent != null) {
                        if (missingOrAddedParts.Any(p => p.Part == parent)) return true;
                        parent = parent.parent;
                    }

                    if (returned.Any(hediff => hediff.Part == partRecord && hediff.def == recipe.addsHediff)) return true; // exact part already on the pawn
                    if (returned.Any(hediff => !recipe.CompatibleWithHediff(hediff.def))) return true; // incompatible with existing hediff
                    if (!recipe.AvailableOnNow(pawn, partRecord)) return true; // unavailable for some other reason

                    return false;
                });
                // create new hediff if recipes exist
                if (finalList.Count > 0) yield return HediffDefCanUpgrade.MakeHediff(pawn, partRecord, finalList);
            }
        }
    }

    public class HediffDefCanUpgrade : HediffDef
    {
        public static Hediff MakeHediff(Pawn pawn, BodyPartRecord record, List<RecipeDef> recipes)
        {
            Hediff h = new Hediff();
            h.pawn = pawn;
            h.def = new HediffDefCanUpgrade(recipes);
            h.Part = record;
            return h;
        }
        
        public HediffDefCanUpgrade(List<RecipeDef> recipes)
        {
            isBad = false;
            makesAlert = false;
            everCurableByItem = false;
            label = recipes.Count + " part install" + (recipes.Count == 1 ? "" : "s");
            description = "There are " + recipes.Count + " available part install" + (recipes.Count == 1 ? "" : "s") + " that install a prosthetic or implant for this body part.";
            descriptionHyperlinks = recipes.Select(item => new DefHyperlink(item)).ToList();
        }
    }
}