using System.Linq;
using System.Diagnostics.CodeAnalysis;
using Verse.AI.Group;

namespace PickUpAndHaul;
public static class PawnUnloadChecker
{
	public static float EmptyEncumbrancePercent = 0.9f;
	public static float EmptyTicksForRotables = 3000;

	public static void BeforeSleepPostfix(Pawn pawn, ref Job __result)
	{
		// Player orders and medical rest use other job givers. Exhausted pawns should go straight to sleep.
		if (__result?.def != JobDefOf.LayDown || __result.playerForced || pawn.Downed || pawn.InMentalState
		    || pawn.needs.rest.CurCategory == RestCategory.Exhausted || HealthAIUtility.ShouldSeekMedicalRestUrgent(pawn)
		    || !pawn.isAutoUnloadInventoryAllowed())
		{
			return;
		}

		var tracked = pawn.HauledToInventoryComp()?.GetHashSet();
		if (tracked == null || !tracked.Any(pawn.inventory.innerContainer.Contains))
		{
			return;
		}

		// Let the think tree choose again after unloading so urgent needs can take priority.
		__result = JobMaker.MakeJob(JobDefOfs.PUAH_UnloadBeforeSleep, pawn);
	}

	extension(Pawn pawn)
	{
		public bool isAutoUnloadInventoryAllowed()
		{
			return pawn.Faction == Faction.OfPlayerSilentFail
			       && pawn.GetLord() == null
			       && Settings.IsAllowedRace(pawn.RaceProps)
			       && pawn.inventory.innerContainer != null
			       && pawn.inventory.innerContainer.Count != 0;
		}

		public void CheckIfPawnShouldUnloadInventory(bool forced = false)
		{
			if (pawn == null || !pawn.isAutoUnloadInventoryAllowed())
			{
				return;
			}

			var itemsTakenToInventory = pawn.HauledToInventoryComp();
			if (itemsTakenToInventory == null || itemsTakenToInventory.IsEmpty())
			{
				return;
			}

			// Multiplayer: MakeJob consumes a unique job ID, so it must only run when the pawn really has hauled items to unload.
			if (forced || MassUtility.EncumbrancePercent(pawn) >= EmptyEncumbrancePercent)
			{
				var job = JobMaker.MakeJob(JobDefOfs.UnloadYourHauledInventory, pawn);
				if (job.TryMakePreToilReservations(pawn, false))
				{
					pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
					return;
				}
			}

			foreach (var thing in pawn.inventory.innerContainer)
			{
				var compRottable = thing.TryGetComp<CompRottable>();
				if (!(compRottable?.TicksUntilRotAtCurrentTemp < EmptyTicksForRotables))
				{
					continue;
				}

				var job = JobMaker.MakeJob(JobDefOfs.UnloadYourHauledInventory, pawn);
				pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
				return;
			}

			if (Find.TickManager.TicksGame % 50 == 0 && pawn.inventory.innerContainer.Count < itemsTakenToInventory.GetHashSet().Count)
			{
				Verse.Log.Warning($"[PickUpAndHaul] {pawn} inventory was found out of sync with haul index. Pawn will drop their inventory.");
				itemsTakenToInventory.Clear();
				pawn.inventory.UnloadEverything = true;
			}
		}
	}
}
