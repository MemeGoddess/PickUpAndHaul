using System.Linq;

namespace PickUpAndHaul;
public class JobDriver_HaulToInventory : JobDriver
{
	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		Log.Message($"{pawn} starting HaulToInventory job: {job.targetQueueA.ToStringSafeEnumerable()}:{job.countQueue.ToStringSafeEnumerable()}");
		pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
		pawn.ReserveAsManyAsPossible(job.targetQueueB, job);
		return true;
	}

	//get next, goto, take, check for more. Branches off to "all over the place"
	public override IEnumerable<Toil> MakeNewToils()
	{
		var wait = Toils_General.Wait(2);

		var pruneQueue = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(TargetIndex.A);
		yield return pruneQueue;
		yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A); //also does count

		yield return CheckForOverencumberedForCombatExtended();
		yield return GotoThing();
		yield return TakeThing();
		yield return Toils_Jump.JumpIf(pruneQueue, () => !job.targetQueueA.NullOrEmpty());

		// This causes vast amounts of over-fetching due to a lack of reservations, disable until it can be debugged/fixed/removed
		//Find more to haul, in case things spawned while this was in progess
		//yield return FindMoreToHaul();

		//maintain cell reservations on the trip back
		//TODO: do that when we carry things
		//I guess that means TODO: implement carrying the rest of the items in this job instead of falling back on HaulToStorageJob
		yield return TargetB.HasThing ? Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
			: Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.ClosestTouch);

		yield return NextJob();
		yield return wait;
	}

	protected Toil GotoThing()
	{
		var gotoThing = new Toil
		{
			debugName = nameof(GotoThing),
			initAction = () => pawn.pather.StartPath(TargetThingA, PathEndMode.ClosestTouch),
			defaultCompleteMode = ToilCompleteMode.PatherArrival
		};
		gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);
		return gotoThing;
	}

	protected Toil TakeThing()
	{
		return new Toil
		{
			debugName = nameof(TakeThing),
			initAction = () =>
			{
				var actor = pawn;
				var thing = actor.CurJob.GetTarget(TargetIndex.A).Thing;
				// Toils_Haul.ErrorCheckForCarry can terminate the job, so avoid using it, and just inline it's checks
				if (!thing.SpawnedOrAnyParentSpawned || !thing.Spawned || thing.stackCount == 0)
				{
					return;
				}
				// debug check
				if (pawn.jobs.curJob.count <= 0)
				{
					Log.Message($"{pawn} Invalid count: " + pawn.jobs.curJob.count + ", setting to 1. Job was " + pawn.jobs.curJob);
					job.count = 1;
				}

				if (pawn.TryAddItemToInventory(thing, job.count, out var canRetryPickup, false))
				{
					// entire thing was picked up
					return;
				}

				if (canRetryPickup <= 0)
				{
					return;
				}

				//thing still remains, so manipulate the queue to make it the next thing to try picking up
				job.GetTargetQueue(TargetIndex.A).Insert(0, thing);
				job.countQueue.Insert(0, thing.stackCount);
			}
		};
	}

	protected Toil FindMoreToHaul()
	{
		return new Toil
		{
			debugName = nameof(FindMoreToHaul),
			initAction = () =>
			{
				var haulables = WorkGiver_HaulToInventory.SortedHaulables(pawn);

				Job haulMoreJob = null;
				var haulMoreThing = WorkGiver_HaulToInventory.GetClosestAndRemove(pawn.Position, pawn.Map, haulables, PathEndMode.ClosestTouch,
					TraverseParms.For(pawn), 12, t => (haulMoreJob = WorkGiverDefs.HaulToInventoryWorker.JobOnThing(pawn, t)) != null);

				//WorkGiver_HaulToInventory found more work nearby
				if (haulMoreThing != null)
				{
					Log.Message($"{pawn} hauling again : {haulMoreThing}:{haulMoreThing.stackCount}");
					if (haulMoreJob == null)
					{
						Verse.Log.Error($"Returned an thing to haul but haul job is missing");
						return;
					}

					if (haulMoreJob.TryMakePreToilReservations(pawn, false))
					{
						pawn.jobs.jobQueue.EnqueueFirst(haulMoreJob, JobTag.Misc);
						EndJobWith(JobCondition.Succeeded);
					}
				}
			}
		};
	}

	protected Toil NextJob()
	{
		return new Toil //Queue next job
		{
			debugName = nameof(NextJob),
			initAction = () =>
			{
				var actor = pawn;
				var curJob = actor.jobs.curJob;
				var storeCell = curJob.targetB;

				var unloadJob = JobMaker.MakeJob(JobDefOfs.UnloadYourHauledInventory, storeCell);
				if (unloadJob.TryMakePreToilReservations(actor, false))
				{
					actor.jobs.jobQueue.EnqueueFirst(unloadJob, JobTag.Misc);
					EndJobWith(JobCondition.Succeeded);
					//This will technically release the cell reservations in the queue, but what can you do
				}
			}
		};
	}

	/// <summary>
	/// the workgiver checks for encumbered, this is purely extra for CE
	/// </summary>
	/// <returns></returns>
	public Toil CheckForOverencumberedForCombatExtended()
	{
		var toil = ToilMaker.MakeToil();

		if (!ModCompatibilityCheck.CombatExtendedIsActive)
		{
			return toil;
		}

		toil.initAction = () =>
		{
			var actor = toil.actor;
			var curJob = actor.jobs.curJob;
			var nextThing = curJob.targetA.Thing;

			var ceOverweight = CompatHelper.CeOverweight(pawn);

			if (!(MassUtility.EncumbrancePercent(actor) <= 0.9f && !ceOverweight))
			{
				var haul = HaulAIUtility.HaulToStorageJob(actor, nextThing, curJob.playerForced);
				if (haul?.TryMakePreToilReservations(actor, false) ?? false)
				{
					//note that HaulToStorageJob etc doesn't do opportunistic duplicate hauling for items in valid storage. REEEE
					actor.jobs.jobQueue.EnqueueFirst(haul, JobTag.Misc);
					EndJobWith(JobCondition.Succeeded);
				}
			}
		};

		return toil;
	}
}
