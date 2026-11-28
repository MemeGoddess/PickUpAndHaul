using System.Linq;

namespace PickUpAndHaul;

public class JobDriver_UnloadYourHauledInventory : JobDriver
{
#pragma warning disable CS0414 // Field is assigned but its value is never used
	[Obsolete]
	private int _countToDrop = -1;
#pragma warning restore CS0414 // Field is assigned but its value is never used
	private int _unloadDuration = 3;

	public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

	/// <summary>
	/// Find spot, reserve spot, pull thing out of inventory, go to spot, drop stuff, repeat.
	/// </summary>
	/// <returns></returns>
	public override IEnumerable<Toil> MakeNewToils()
	{
		if (ModCompatibilityCheck.ExtendedStorageIsActive)
		{
			_unloadDuration = 20;
		}

		var begin = Toils_General.Wait(_unloadDuration);
		yield return begin;

		var hauledToInventoryComp = pawn.HauledToInventoryComp();
		yield return FindTargetOrDrop(hauledToInventoryComp);
		yield return PullItemFromInventory(hauledToInventoryComp, begin);

		var releaseReservation = ReleaseReservation();
		var carryToCell = Toils_Haul.CarryHauledThingToCell(TargetIndex.B);

		// Equivalent to if (TargetB.HasThing)
		yield return Toils_Jump.JumpIf(carryToCell, TargetIsCell);

		var carryToContainer = Toils_Haul.CarryHauledThingToContainer();
		yield return carryToContainer;
		yield return Toils_Haul.DepositHauledThingInContainer(TargetIndex.B, TargetIndex.None);
		yield return Toils_Haul.JumpToCarryToNextContainerIfPossible(carryToContainer, TargetIndex.B);
		// Equivalent to jumping out of the else block
		yield return Toils_Jump.Jump(releaseReservation);

		// Equivalent to else
		yield return carryToCell;
		yield return Toils_Haul.PlaceHauledThingInCell(TargetIndex.B, carryToCell, true);

		//If the original cell is full, PlaceHauledThingInCell will set a different TargetIndex resulting in errors on yield return Toils_Reserve.Release.
		//We still gotta release though, mostly because of Extended Storage.
		yield return releaseReservation;
		yield return Toils_Jump.Jump(begin);
	}

	private bool TargetIsCell() => !TargetB.HasThing;

	private Toil ReleaseReservation()
	{
		return new()
		{
			debugName = nameof(ReleaseReservation),
			initAction = () =>
			{
				if (pawn.Map.reservationManager.ReservedBy(job.targetB, pawn, pawn.CurJob)
				    && !ModCompatibilityCheck.HCSKIsActive)
				{
					pawn.Map.reservationManager.Release(job.targetB, pawn, pawn.CurJob);
				}
			}
		};
	}

	private Toil PullItemFromInventory(CompHauledToInventory hauledToInventoryComp, Toil wait)
	{
		return new()
		{
			debugName = nameof(PullItemFromInventory),
			initAction = () =>
			{
				var thing = job.GetTarget(TargetIndex.A).Thing;
				if (thing == null || !pawn.inventory.innerContainer.Contains(thing))
				{
					hauledToInventoryComp.UnregisterHauledItem(thing);
					pawn.jobs.curDriver.JumpToToil(wait);
					return;
				}
				if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !thing.def.EverStorable(false))
				{
					Log.Message($"{pawn} incapable of hauling, dropping {thing}:{thing.stackCount}");
					pawn.TryDropThing(thing, job.count);
					EndJobWith(JobCondition.Incompletable);
					return;
				}

				pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer, job.count, out var carriedThing);
				if (carriedThing == null)
				{
					Log.Message($"{pawn} Failed to move {thing}:{job.count} into hands");
					EndJobWith(JobCondition.Incompletable);
					return;
				}

				Log.Message($"{pawn} moved {carriedThing}:{carriedThing.stackCount} into hands");
				job.SetTarget(TargetIndex.A, carriedThing);
				job.count = carriedThing.stackCount;

				CompatHelper.UpdateInventory(pawn);
			}
		};
	}

	private Toil FindTargetOrDrop(CompHauledToInventory hauledToInventoryComp)
	{
		return new()
		{
			debugName = nameof(FindTargetOrDrop),
			initAction = () =>
			{
				// reset to a known good-state
				job.SetTarget(TargetIndex.A, null);
				job.count = 0;

				var trackedItemsOnly = job.def == JobDefOfs.PUAH_UnloadBeforeSleep;
				var unloadableThing = FirstUnloadableThingWithDestination(pawn, hauledToInventoryComp,
					trackedItemsOnly, out var cell, out var destination);

				if (unloadableThing.count == 0)
				{
					if (trackedItemsOnly || hauledToInventoryComp.IsEmpty())
					{
						EndJobWith(JobCondition.Succeeded);
					}
					return;
				}

				if (destination != null || cell != IntVec3.Invalid)
				{
					job.SetTarget(TargetIndex.A, unloadableThing.Thing);
					if (cell == IntVec3.Invalid)
					{
						job.SetTarget(TargetIndex.B, destination as Thing);
					}
					else
					{
						job.SetTarget(TargetIndex.B, cell);
					}

					Log.Message($"{pawn} found destination {job.targetB} for thing {unloadableThing.Thing}:{unloadableThing.count}");
					if (!pawn.Map.reservationManager.Reserve(pawn, job, job.targetB, errorOnFailed: false))
					{
						Log.Message(
							$"{pawn} failed reserving destination {job.targetB}, dropping {unloadableThing.Thing}:{unloadableThing.count}");
						pawn.TryDropThing(unloadableThing.Thing, unloadableThing.count);
						return;
					}
					job.count = unloadableThing.count;
					return;
				}

				Log.Message($"{pawn} unable to find hauling destination, dropping {unloadableThing.Thing}:{unloadableThing.count}");
				pawn.TryDropThing(unloadableThing.Thing, unloadableThing.count);
			}
		};
	}

	private static ThingCount FirstUnloadableThingWithDestination(Pawn pawn, CompHauledToInventory hauledToInventoryComp, bool trackedItemsOnly, out IntVec3 foundCell, out IHaulDestination foundDestination)
	{
		foundCell = IntVec3.Invalid;
		foundDestination = null;
		if (hauledToInventoryComp.IsEmpty())
		{
			return default;
		}

		var pendingDrops = hauledToInventoryComp.GetHashSet();
		var innerPawnContainer = pawn.inventory.innerContainer;
		var inventory = new HashSet<Thing>(innerPawnContainer);
		var candidates = new List<(Thing,IntVec3,IHaulDestination)>(innerPawnContainer.Count);
		(Thing, IntVec3, IHaulDestination) instantDropCandidate = default;
		var pickFirstMatch = !Settings.ScanForClosestBestDestination;

		var noStorageFree = true;
		foreach (var thing in pendingDrops.OrderBy(t => t.def.FirstThingCategory?.index ?? -1).ThenBy(x => x.def.defName))
		{
			Thing candidate;
			//find the overlap.
			if (inventory.Contains(thing))
			{
				candidate = thing;
			}
			else
			{
				// Bedtime unloading must not substitute a personal item for a stale haul entry.
				if (trackedItemsOnly)
				{
					continue;
				}

				//merged partially picked up stacks get a different thingID in inventory
				//we have no method of grabbing the newly generated thingID. This is the solution to that.
				hauledToInventoryComp.UnregisterHauledItem(thing);
				candidate = null;
				var stragglerDef = thing.def;
				foreach (var dirtyStraggler in innerPawnContainer)
				{
					if (dirtyStraggler.def == stragglerDef)
					{
						candidate = dirtyStraggler;
						break;
					}
				}

				if (candidate == null)
				{
					continue;
				}
			}

			var isInvalid = true;
			if (StoreUtility.TryFindBestBetterStorageFor(
					candidate,
					pawn,
					pawn.Map,
					StoragePriority.Unstored,
					pawn.Faction,
					out var cell,
					out var haulDestination,
					true
				))
			{
				if (haulDestination is Zone haulZone)
				{
					// zones stockpiles return a random cell. so pick the closest cell for the pawn instead of what was provided
					var i = PickNearestInCellList(haulZone.cells, pawn, candidate);
					if (i != -1)
					{
						Log.Info($"{pawn} when unloading, found valid zone for {candidate}");
						isInvalid = false;
						noStorageFree = false;
						cell = haulZone.cells[i];
						candidates.Add((candidate, cell, haulDestination));
					}
				}
				else if (haulDestination is Thing || cell != IntVec3.Invalid)
				{
					Log.Info($"{pawn} on when unloading, found validate hauling destination for {candidate}");
					isInvalid = false;
					noStorageFree = false;
					candidates.Add((candidate, cell, haulDestination));
				}
			}

			if (isInvalid)
			{
				Log.Info($"{pawn} on when unloading, could not find a destination for {candidate}");
				// Nowhere to store it; PUAH drops it on the spot, which costs no travel.
				instantDropCandidate = (candidate, IntVec3.Invalid, null);
			}

			if (pickFirstMatch)
			{
				break;
			}
		}

		if (candidates.Count == 0)
		{
			if (instantDropCandidate.Item1 != null)
			{
				foundCell = instantDropCandidate.Item2;
				foundDestination = instantDropCandidate.Item3;

				return new ThingCount(instantDropCandidate.Item1, instantDropCandidate.Item1.stackCount);
			}

			// Nothing in the hauled to inventory list matches the pawn's inventory, just clear it as it is obviously badly out of sync
			if (!trackedItemsOnly)
			{
				hauledToInventoryComp.Clear();
			}
			return default;
		}

		var index = noStorageFree ? 0 : PickNearest(candidates, pawn.Position);
		var chosen = candidates[index];
		foundCell = chosen.Item2;
		foundDestination = chosen.Item3;

		return new ThingCount(chosen.Item1, chosen.Item1.stackCount);
	}

	private static int PickNearestInCellList(IReadOnlyList<IntVec3> destinations, Pawn pawn, Thing thingToPlace)
	{
		var best = -1;
		var bestDistance = int.MaxValue;
		var map = pawn.Map;
		var from = pawn.Position;
		for (var i = 0; i < destinations.Count; i++)
		{
			var cell = destinations[i];
			var distance = (cell - from).LengthHorizontalSquared;
			// Use RimWorld's storage checks for both empty cells and cells with room to stack.
			if (distance >= bestDistance || !StoreUtility.IsGoodStoreCell(cell, map, thingToPlace, pawn, pawn.Faction))
			{
				continue;
			}

			bestDistance = distance;
			best = i;
		}

		return best;
	}

	private static int PickNearest(IReadOnlyList<(Thing,IntVec3, IHaulDestination)> destinations, IntVec3 from)
	{
		var best = 0;
		var bestDistance = int.MaxValue;
		for (var i = 0; i < destinations.Count; i++)
		{
			var cell = destinations[i].Item2.IsValid ? destinations[i].Item2 : (destinations[i].Item3 as Thing)?.Position ?? throw new Exception("PickNearest precondition failed");
			var distance = (cell - from).LengthHorizontalSquared;
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = i;
			}
		}

		return best;
	}
}
