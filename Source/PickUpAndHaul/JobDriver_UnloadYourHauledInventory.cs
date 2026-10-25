using System.Linq;

namespace PickUpAndHaul;

public class JobDriver_UnloadYourHauledInventory : JobDriver
{
	private int _countToDrop = -1;
	private int _unloadDuration = 3;

	public override void ExposeData()
	{
		base.ExposeData();
		Scribe_Values.Look<int>(ref _countToDrop, "countToDrop", -1);
	}

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

		var HauledToInventoryComp = pawn.HauledToInventoryComp();
		yield return FindTargetOrDrop(HauledToInventoryComp);
		yield return PullItemFromInventory(HauledToInventoryComp, begin);

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
				hauledToInventoryComp.UnregisterHauledItem(thing);
				if (thing == null || !pawn.inventory.innerContainer.Contains(thing))
				{
					pawn.jobs.curDriver.JumpToToil(wait);
					return;
				}
				if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation) || !thing.def.EverStorable(false))
				{
					Log.Message($"{pawn} incapable of hauling, dropping {thing}");
					pawn.inventory.innerContainer.TryDrop(thing, ThingPlaceMode.Near, _countToDrop, out thing);
				}
				else
				{
					pawn.inventory.innerContainer.TryTransferToContainer(thing, pawn.carryTracker.innerContainer,
						_countToDrop, out thing);
					job.count = _countToDrop;
					job.SetTarget(TargetIndex.A, thing);
				}

				if (ModCompatibilityCheck.CombatExtendedIsActive)
				{
					CompatHelper.UpdateInventory(pawn);
				}

				thing.SetForbidden(false, false);
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
				var unloadableThing = FirstUnloadableThingWithDestination(pawn, hauledToInventoryComp, out var cell, out var destination);

				if (unloadableThing.count == 0)
				{
					if (hauledToInventoryComp.IsEmpty())
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

					Log.Message($"{pawn} found destination {job.targetB} for thing {unloadableThing.Thing}");
					if (!pawn.Map.reservationManager.Reserve(pawn, job, job.targetB, errorOnFailed: false))
					{
						Log.Message(
							$"{pawn} failed reserving destination {job.targetB}, dropping {unloadableThing.Thing}");
						pawn.inventory.innerContainer.TryDrop(unloadableThing.Thing, ThingPlaceMode.Near,
							unloadableThing.count, out _);
						return;
					}
					_countToDrop = unloadableThing.count;
					return;
				}

				Log.Message($"Pawn {pawn} unable to find hauling destination, dropping {unloadableThing.Thing}");
				pawn.inventory.innerContainer.TryDrop(unloadableThing.Thing, ThingPlaceMode.Near, unloadableThing.count, out _);
			}
		};
	}

	private static ThingCount FirstUnloadableThingWithDestination(Pawn pawn, CompHauledToInventory hauledToInventoryComp, out IntVec3 foundCell, out IHaulDestination foundDestination)
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
				//merged partially picked up stacks get a different thingID in inventory
				//we have no method of grabbing the newly generated thingID. This is the solution to that.
				hauledToInventoryComp.UnregisterHauledItem(thing);
				candidate = null;
				var stragglerDef = thing.def;
				foreach (var dirtyStraggler in innerPawnContainer)
				{
					if (dirtyStraggler.def == stragglerDef)
					{
						candidate = thing;
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
						Log.Message($"{pawn} on empty inventory, found valid zone for {candidate}");
						isInvalid = false;
						noStorageFree = false;
						cell = haulZone.cells[i];
						candidates.Add((candidate, cell, haulDestination));
					}
				}
				else if (haulDestination is Thing || cell != IntVec3.Invalid)
				{
					Log.Message($"{pawn} on empty inventory, found validate hauling destination for {candidate}");
					isInvalid = false;
					noStorageFree = false;
					candidates.Add((candidate, cell, haulDestination));
				}
			}

			if (isInvalid)
			{
				Log.Message($"{pawn} on empty inventory, could not find a destination for {candidate}");
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
			hauledToInventoryComp.Clear();
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
		var stackLimit = thingToPlace.def.stackLimit;
		var map = pawn.Map;
		var reservations = map.reservationManager;
		var from = pawn.Position;
		for (var i = 0; i < destinations.Count; i++)
		{
			var cell = destinations[i];
			var distance = (cell - from).LengthHorizontalSquared;
			if (distance < bestDistance && reservations.CanReserve(pawn, cell, 1, thingToPlace.stackCount))
			{
				var cellThings = cell.GetThingList(map);

				if (stackLimit > 1)
				{
					var canNotMerge = true;
					foreach (var cellThing in cellThings)
					{
						if (cellThing.CanStackWith(thingToPlace) && thingToPlace.stackCount + cellThing.stackCount < stackLimit)
						{
							canNotMerge = false;
							break;
						}
					}

					if (canNotMerge)
					{
						continue;
					}
				}
				else if (cellThings.Count > 0)
				{
					continue;
				}

				bestDistance = distance;
				best = i;
			}
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
