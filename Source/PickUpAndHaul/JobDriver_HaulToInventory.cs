using System.Linq;

namespace PickUpAndHaul;
public class JobDriver_HaulToInventory : JobDriver
{
	public override bool TryMakePreToilReservations(bool errorOnFailed)
	{
		Log.Message($"{pawn} starting HaulToInventory job: {job.targetQueueA.ToStringSafeEnumerable()}:{job.countQueue.ToStringSafeEnumerable()}");
		pawn.ReserveAsManyAsPossible(job.targetQueueA, job);
		pawn.ReserveAsManyAsPossible(job.targetQueueB, job);
		return pawn.Reserve(job.targetQueueA[0], job) && pawn.Reserve(job.targetB, job);
	}

	//get next, goto, take, check for more. Branches off to "all over the place"
	public override IEnumerable<Toil> MakeNewToils()
	{
		var takenToInventory = pawn.HauledToInventoryComp();

		var wait = Toils_General.Wait(2);

		var pruneQueue = Toils_JobTransforms.ClearDespawnedNullOrForbiddenQueuedTargets(TargetIndex.A);
		yield return pruneQueue;
		yield return Toils_JobTransforms.ExtractNextTargetFromQueue(TargetIndex.A); //also does count

		yield return CheckForOverencumberedForCombatExtended();

		var gotoThing = new Toil
		{
			initAction = () => pawn.pather.StartPath(TargetThingA, PathEndMode.ClosestTouch),
			defaultCompleteMode = ToilCompleteMode.PatherArrival
		};
		gotoThing.FailOnDespawnedNullOrForbidden(TargetIndex.A);
		yield return gotoThing;

		var takeThing = new Toil
		{
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

				// todo the pickup count should be based on free-space not the job count which is only vaguely related to inventory size
				//get max we can pick up
				var countToPickUp = Mathf.Min(job.count, MassUtility.CountToPickUpUntilOverEncumbered(actor, thing));
				if (ModCompatibilityCheck.CombatExtendedIsActive)
				{
					countToPickUp = CompatHelper.CanFitInInventory(pawn, thing);
				}
				if (countToPickUp <= 0)
				{
					return;
				}
				Log.Message($"{actor} is hauling to inventory {thing}:{countToPickUp}");

				var splitThing = thing.SplitOff(countToPickUp);
				var shouldMerge = takenToInventory.GetHashSet().Any(x => x.def == thing.def);
				actor.inventory.GetDirectlyHeldThings().TryAdd(splitThing, shouldMerge);
				takenToInventory.RegisterHauledItem(splitThing);

				if (ModCompatibilityCheck.CombatExtendedIsActive)
				{
					CompatHelper.UpdateInventory(pawn);
				}

				//thing still remains, so manipulate the queue to make it the next thing to try picking up
				if (thing.Spawned)
				{
					Log.Message($"{pawn} picked up {countToPickUp} but {thing.stackCount} remains");
					job.GetTargetQueue(TargetIndex.A).Insert(0, thing);
					job.countQueue.Insert(0, thing.stackCount);
				}
			}
		};
		yield return takeThing;
		yield return Toils_Jump.JumpIf(pruneQueue, () => !job.targetQueueA.NullOrEmpty());

		//Find more to haul, in case things spawned while this was in progess
		yield return new Toil
		{
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

		//maintain cell reservations on the trip back
		//TODO: do that when we carry things
		//I guess that means TODO: implement carrying the rest of the items in this job instead of falling back on HaulToStorageJob
		yield return TargetB.HasThing ? Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch)
			: Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.ClosestTouch);

		yield return new Toil //Queue next job
		{
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
		yield return wait;
	}

	/// <summary>
	/// the workgiver checks for encumbered, this is purely extra for CE
	/// </summary>
	/// <returns></returns>
	public Toil CheckForOverencumberedForCombatExtended()
	{
		var toil = new Toil();

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
