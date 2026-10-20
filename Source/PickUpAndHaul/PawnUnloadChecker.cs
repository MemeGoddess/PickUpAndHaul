using System.Diagnostics.CodeAnalysis;

namespace PickUpAndHaul;
public static class PawnUnloadChecker
{
	public static float EmptyEncumbrancePercent = 0.9f;
	public static float EmptyTicksForRotables = 3000;

	public static bool isAutoUnloadInventoryAllowed(this Pawn pawn)
	{
		return pawn.Faction == Faction.OfPlayerSilentFail
		       && Settings.IsAllowedRace(pawn.RaceProps)
		       && pawn.inventory.innerContainer != null
		       && pawn.inventory.innerContainer.Count != 0;
	}

	public static void CheckIfPawnShouldUnloadInventory(this Pawn pawn, bool forced = false)
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

		var job = JobMaker.MakeJob(JobDefOfs.UnloadYourHauledInventory, pawn);
		if ((forced || MassUtility.EncumbrancePercent(pawn) >= EmptyEncumbrancePercent) && job.TryMakePreToilReservations(pawn, false))
		{
			pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
			return;
		}

		foreach (var thing in pawn.inventory.innerContainer)
		{
			var compRottable = thing.TryGetComp<CompRottable>();
			if (!(compRottable?.TicksUntilRotAtCurrentTemp < EmptyTicksForRotables))
			{
				continue;
			}

			pawn.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
			return;
		}

		if (Find.TickManager.TicksGame % 50 == 0 && pawn.inventory.innerContainer.Count < itemsTakenToInventory.GetHashSet().Count)
		{
			Verse.Log.Warning("[PickUpAndHaul] " + pawn + " inventory was found out of sync with haul index. Pawn will drop their inventory.");
			itemsTakenToInventory.Clear();
			pawn.inventory.UnloadEverything = true;
		}
	}
}
