using System.Reflection;
using HarmonyLib;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Emit;

namespace PickUpAndHaul;
[StaticConstructorOnStartup]
[SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Harmony magic paramter names")]
static class HarmonyPatches
{
	public static readonly List<(Type, string)> knownMethodsWhichTriggerCarryDrops =
	[
		(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob)),
		// don't drop carried stuff on instructed to fill a biosculpter pod
		(typeof(JobDriver_CarryToBiosculpterPod), nameof(JobDriver_CarryToBiosculpterPod.DropCarryToGrabIngredients)), // delegate
		// don't drop carried stuff on entering a portal
		(typeof(JobDriver_EnterPortal), nameof(JobDriver_EnterPortal.MakeNewToils)), // yield
		// do not drop the book on the floor, hold onto it
		(typeof(JobDriver_Reading), nameof(JobDriver_Reading.MakeNewToils)), // yield
		// left-over reloading
		(typeof(JobDriver_Reload), nameof(JobDriver_Reload.MakeNewToils)), // yield
		(typeof(PawnFlyer), nameof(PawnFlyer.MakeFlyer)), // direct call
		// drafting drops whatever is being carried on the ground
		(typeof(Pawn_DraftController), "set_" + nameof(Pawn_DraftController.Drafted)), // direct call
		// carrying a thing which isn't for the job
		(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.MakeNewToils)), // yield + delegate
	];

	static HarmonyPatches()
	{
		var harmony = new Harmony("mehni.rimworld.pickupandhaul.main");
#if DEBUG
		Harmony.DEBUG = true;
#endif

		if (!ModCompatibilityCheck.CombatExtendedIsActive)
		{
			harmony.Patch(original: AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.GetMaxAllowedToPickUp), new[] { typeof(Pawn), typeof(ThingDef) }),
				prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(MaxAllowedToPickUpPrefix)));

			harmony.Patch(original: AccessTools.Method(typeof(PawnUtility), nameof(PawnUtility.CanPickUp)),
				prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(CanBeMadeToDropStuff)));
		}

		harmony.Patch(original: AccessTools.Method(typeof(JobGiver_DropUnusedInventory), nameof(JobGiver_DropUnusedInventory.TryGiveJob)),
			postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(DropUnusedInventory_PostFix)));

		harmony.Patch(original: AccessTools.Method(typeof(JobDriver_HaulToCell), nameof(JobDriver_HaulToCell.MakeNewToils)),
			postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(JobDriver_HaulToCell_PostFix)));

		harmony.Patch(original: AccessTools.Method(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.Notify_ItemRemoved)),
			postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Pawn_InventoryTracker_PostFix)));

		harmony.Patch(original: AccessTools.Method(typeof(JobGiver_DropUnusedInventory), nameof(JobGiver_DropUnusedInventory.Drop)),
			prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(Drop_Prefix)));

		harmony.Patch(original: AccessTools.Method(typeof(JobGiver_Idle), nameof(JobGiver_Idle.TryGiveJob)),
			postfix: new HarmonyMethod(typeof(HarmonyPatches), nameof(IdleJoy_Postfix)));

		harmony.Patch(original: AccessTools.Method(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.DrawThingRow)),
			transpiler: new HarmonyMethod(typeof(HarmonyPatches), nameof(GearTabHighlightTranspiler)));

		harmony.Patch(original: AccessTools.Method(typeof(WorkGiver_Haul), nameof(WorkGiver_Haul.ShouldSkip)),
			prefix: new HarmonyMethod(typeof(HarmonyPatches), nameof(SkipCorpses_Prefix)));

		harmony.Patch(AccessTools.Method(typeof(JobGiver_Haul), nameof(JobGiver_Haul.TryGiveJob)),
			transpiler: new(typeof(HarmonyPatches), nameof(JobGiver_Haul_TryGiveJob_Transpiler)));

		var stashInventoryOnDropTranspiler = new HarmonyMethod(typeof(HarmonyPatches), nameof(StashInventoryOnDropTranspiler));
		foreach (var (type, methodPrefix) in knownMethodsWhichTriggerCarryDrops)
		{
			ApplyTranspilerForEnumerationMethods(harmony, type, methodPrefix, stashInventoryOnDropTranspiler);
		}

		Verse.Log.Message("PickUpAndHaul v1.1.2¼ welcomes you to RimWorld with pointless logspam.");
	}


	public static void ApplyTranspilerForEnumerationMethods(Harmony harmony, Type type, string methodPrefix, HarmonyMethod Transpiler)
	{
		const BindingFlags bindingFlags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		var prefix = $"<{methodPrefix}>";
		foreach (var methodInfo in type.GetMethods(bindingFlags))
		{
			// delegate or direct method
			if (methodInfo.IsAbstract && (methodInfo.Name == methodPrefix || methodInfo.Name.StartsWith(prefix)))
			{
				harmony.Patch(methodInfo, transpiler: Transpiler);
			}
		}

		// IEnumerable support
		foreach (var nestedType in type.GetNestedTypes(BindingFlags.NonPublic))
		{
			// delegate+IEnumerable support
			foreach (var methodInfo in nestedType.GetMethods(bindingFlags))
			{
				if (!methodInfo.IsAbstract && methodInfo.Name.StartsWith(prefix))
				{
					harmony.Patch(methodInfo, transpiler: Transpiler);
				}
			}

			var moveNextInfo = nestedType.GetMethod(nameof(IEnumerator.MoveNext), bindingFlags | BindingFlags.FlattenHierarchy);
			if (moveNextInfo != null && !moveNextInfo.IsAbstract)
			{
				harmony.Patch(moveNextInfo, transpiler: Transpiler);
			}
		}
	}

	private static bool Drop_Prefix(Pawn pawn, Thing thing) => !pawn.HasHauledThingInInventory(thing);

	private static void Pawn_InventoryTracker_PostFix(Pawn_InventoryTracker __instance, Thing item) => __instance.pawn.HauledToInventoryComp()?.UnregisterHauledItem(item);

	private static void JobDriver_HaulToCell_PostFix(JobDriver_HaulToCell __instance)
	{
		var pawn = __instance.pawn;
		if (__instance.job.haulMode == HaulMode.ToCellStorage
			&& pawn.carryTracker.CarriedThing.IsNotCorpseOrAllowed())
		{
			pawn.CheckIfPawnShouldUnloadInventory(true);
		}
	}

	public static void IdleJoy_Postfix(Pawn pawn) => pawn.CheckIfPawnShouldUnloadInventory(true);

	public static void DropUnusedInventory_PostFix(Pawn pawn) => pawn.CheckIfPawnShouldUnloadInventory();

	public static bool MaxAllowedToPickUpPrefix(Pawn pawn, ref int __result)
	{
		if (!Settings.AlwaysAllowPickup)
		{
			return true;
		}

		__result = int.MaxValue;
		return pawn.IsQuestLodger();
	}

	public static bool CanBeMadeToDropStuff(Pawn pawn, ref bool __result)
	{
		if (!Settings.AlwaysAllowPickup)
		{
			return true;
		}

		__result = !pawn.IsQuestLodger();
		return false;
	}

	public static bool SkipCorpses_Prefix(WorkGiver_Haul __instance, ref bool __result, Pawn pawn)
	{
		if (__instance is not WorkGiver_HaulCorpses)
		{
			return true;
		}

		if (Settings.AllowCorpses //Don't use the vanilla HaulCorpses WorkGiver if PUAH is allowed to haul those
			|| pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).Count < 1) //...or if there are no corpses to begin with. Indeed Tynan did not foresee this situation
		{
			__result = true;
			return false;
		}

		return true;
	}

	/// <summary>
	/// For animal hauling
	/// </summary>
	public static IEnumerable<CodeInstruction> JobGiver_Haul_TryGiveJob_Transpiler(IEnumerable<CodeInstruction> instructions)
		=> instructions.MethodReplacer(HaulAIUtility.HaulToStorageJob, HaulToStorageJobByRace);

	public static Job HaulToStorageJobByRace(Pawn p, Thing t, bool forced) => Settings.IsAllowedRace(p.RaceProps) ? WorkGiverDefs.HaulToInventoryWorker.JobOnThing(p, t, forced) : HaulAIUtility.HaulToStorageJob(p, t, forced);

	//ITab_Pawn_Gear
	//private void DrawThingRow(ref float y, float width, Thing thing, bool inventory = false)
	public static IEnumerable<CodeInstruction> GearTabHighlightTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase method)
	{
		var ColorWhite = AccessTools.PropertyGetter(typeof(Color), nameof(Color.white));
		var state = 0;
		foreach (var i in instructions)
		{
			switch (state)
			{
				// TooltipHandler.TipRegion(rect2, (TipSignal) "DropThing".Translate());
				case 0 when i.LoadsConstant("DropThing"):
					state += 1;
					// GetTipForHauled(pawn,thing)
					yield return FishTranspiler.This;
					yield return FishTranspiler.CallPropertyGetter(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.SelPawnForGear));
					yield return FishTranspiler.Argument(method, "thing");
					yield return FishTranspiler.Call(GetTipForHauled);
					break;
				// Color color = flag ? Color.grey : Color.white;
				case 1 when i.Calls(ColorWhite):
					state += 1;
					// GetColorForHauled(pawn,thing)
					yield return FishTranspiler.This;
					yield return FishTranspiler.CallPropertyGetter(typeof(ITab_Pawn_Gear), nameof(ITab_Pawn_Gear.SelPawnForGear));
					yield return FishTranspiler.Argument(method, "thing");
					yield return FishTranspiler.Call(GetColorForHauled);
					break;
				default:
					yield return i;
					break;
			}
		}

		if (state != 2)
		{
			Verse.Log.Warning("Pick Up And Haul failed to patch ITab_Pawn_Gear.DrawThingRow. This is only used for coloring and totally harmless, but you might wanna know anyway");
		}
	}

	private static string GetTipForHauled(Pawn pawn, Thing thing)
		=> pawn.HasHauledThingInInventory(thing)
			? "PUAH.DropThing"
			: "DropThing";

	private static Color GetColorForHauled(Pawn pawn, Thing thing)
		=> pawn.HasHauledThingInInventory(thing)
		? Color.Lerp(Color.grey, Color.red, 0.5f)
		: Color.white;

	private static IEnumerable<CodeInstruction> multiMethodReplacer(IEnumerable<CodeInstruction> instructions, MethodBase original, Dictionary<MethodInfo, MethodInfo> map)
	{
		var foundMatch = false;
		foreach (var instruction in instructions)
		{
			if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt)
			    && instruction.operand is MethodInfo method
			    && map.TryGetValue(method, out var replacement))
			{
				foundMatch = true;
				instruction.opcode = OpCodes.Call;
				instruction.operand = replacement;
			}

			yield return instruction;
		}

		if (foundMatch)
		{
			Log.Message($"Patched {original.DeclaringType}.{original.Name} for calling TryDropCarriedThing");
		}
	}

	private static bool TryStashToInventoryOrDrop1(Pawn_CarryTracker carryTracker, IntVec3 dropLoc, ThingPlaceMode mode, out Thing resultingThing, Action<Thing, int> placedAction)
	{
		if (!carryTracker.pawn.TryAddItemToInventory(Settings.StashInsteadOfDropOnNewJob, carryTracker.CarriedThing, out _))
		{
			return carryTracker.TryDropCarriedThing(dropLoc, mode, out resultingThing, placedAction);
		}

		resultingThing = null;
		return false;
	}

	private static bool TryStashToInventoryOrDrop2(Pawn_CarryTracker carryTracker, IntVec3 dropLoc, int count, ThingPlaceMode mode, out Thing resultingThing, Action<Thing, int> placedAction)
	{
		if (!carryTracker.pawn.TryAddItemToInventory(Settings.StashInsteadOfDropOnNewJob, carryTracker.CarriedThing, out _))
		{
			return carryTracker.TryDropCarriedThing(dropLoc, count, mode, out resultingThing, placedAction);
		}

		resultingThing = null;
		return false;
	}

	private static readonly Dictionary<MethodInfo, MethodInfo> StashInventoryOnDrop = new()
	{
		{
			AccessTools.Method(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryDropCarriedThing), [
				typeof(IntVec3), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>),
			]),
			AccessTools.Method(typeof(HarmonyPatches), nameof(TryStashToInventoryOrDrop1))
		},
		{
			AccessTools.Method(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryDropCarriedThing), [
				typeof(IntVec3), typeof(int), typeof(ThingPlaceMode), typeof(Thing).MakeByRefType(), typeof(Action<Thing, int>),
			]),
			AccessTools.Method(typeof(HarmonyPatches), nameof(TryStashToInventoryOrDrop2))
		},
	};

	private static IEnumerable<CodeInstruction> StashInventoryOnDropTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
		=> multiMethodReplacer(instructions, original, StashInventoryOnDrop);
}
