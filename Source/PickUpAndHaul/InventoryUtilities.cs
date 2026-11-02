using System.Linq;
using System.Runtime.CompilerServices;
using Waylith.Injection;

namespace PickUpAndHaul;

public static class InventoryUtilities
{
	extension(Thing thing)
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool IsNotCorpseOrAllowed() => Settings.AllowCorpses || thing is not Corpse;
	}

	extension(Pawn pawn)
	{
		[InjectedField]
		public CompHauledToInventory HauledToInventoryCompField
		{
			get => null;
			set => throw new NotImplementedException();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CompHauledToInventory HauledToInventoryComp()
		{
			if (pawn == null)
			{
				return null;
			}

			pawn.HauledToInventoryCompField ??= pawn.TryGetComp<CompHauledToInventory>();
			return pawn.HauledToInventoryCompField;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool HasHauledThingInInventory(Thing thing)
		{
			return pawn.HauledToInventoryComp()?.Contains(thing) ?? false;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool TryAddItemToInventoryForStashing(Thing thing)
		{
			return pawn.TryAddItemToInventory(thing, thing.stackCount, out _);
		}

		public bool TryAddItemToInventory(Thing thing, int pickupCount, out int remainingToPickup, bool tooHeavyMsg = true)
		{
			remainingToPickup = 0;
			var inventory = pawn.inventory.innerContainer;
			if (thing == null
			    || thing.Destroyed
			    || thing.stackCount == 0
			    || thing is Pawn)
			{
				// skip with no message
				return false;
			}

			if (!Settings.AllowCorpses && thing is Corpse
			    || pawn.IsQuestLodger()
			    // tryAdd can fail but absorb stack count, so verify the entire stack can fit
			    || inventory.Count >= inventory.maxStacks
			   )
			{
				Log.Message($"{pawn} can't put {thing} into inventory");
				return false;
			}

			var comp = pawn.HauledToInventoryComp();
			if (comp == null)
			{
				Log.Message($"{pawn} does not have HauledToInventoryComp, skipping inventory handling");
				return false;
			}

			var countToPickUp = Mathf.Min(pickupCount, MassUtility.CountToPickUpUntilOverEncumbered(pawn, thing));
			if (ModCompatibilityCheck.CombatExtendedIsActive)
			{
				countToPickUp = CompatHelper.CanFitInInventory(pawn, thing);
			}
			// non-player pawns might have reduced the stack-count
			countToPickUp = Mathf.Min(countToPickUp, thing.stackCount);
			if (countToPickUp <= 0)
			{
				remainingToPickup = 0;
				if (tooHeavyMsg)
				{
					Log.Message($"{pawn} can't put {thing} into inventory as it is too heavy");
				}

				return false;
			}

			// always use splitOff as this avoids needing to touch TryTransferToContainer which behaves very differently to TryAdd in its failure modes
			var splitThing = thing.SplitOff(countToPickUp);
			Log.Message($"{pawn} checking to merge {splitThing} into existing stacks");
			// manually handle merging, as the ThingOwner API does not permit determining where the merged stacks went or if a new item is added
			// this ensures non-hauled stacks are left untouched
			for (var i = inventory.Count - 1; i >= 0 ; i--)
			{
				var inventoryThing = inventory[i];
				var count = splitThing.stackCount;
				if (inventoryThing.def != splitThing.def || !comp.Contains(inventoryThing) || !inventoryThing.TryAbsorbStack(splitThing, true))
				{
					continue;
				}
				Log.Message($"{pawn} inventoryThing:{inventoryThing} absorbed:{count - splitThing.stackCount}");
				if (splitThing.stackCount <= 0 || splitThing.Destroyed)
				{
					break;
				}
			}

			if (splitThing.stackCount > 0 && !splitThing.Destroyed)
			{
				var count = splitThing.stackCount;
				Log.Message($"{pawn} {splitThing}:{splitThing.stackCount} to add as a new item");
				if (inventory.TryAdd(splitThing, false))
				{
					comp.RegisterHauledItem(splitThing);
				}
				else
				{
					// this *really* shouldn't happen, but drop to the floor to stop the resource from disappearing
					if (splitThing.stackCount > 0)
					{
						Log.Message($"{pawn} failed to stash {splitThing}:{splitThing.stackCount} into inventory, dropping to ground");
						GenPlace.TryPlaceThing(splitThing, pawn.Position, pawn.Map, ThingPlaceMode.Near, out var placed);
					}
					else
					{
						// *really* shouldn't be a partial stack left!
						Log.Message($"{pawn} failed to stash {thing}:{count - splitThing.stackCount} into inventory");
					}

					// prevent retries
					remainingToPickup = 0;
					return false;
				}
			}

			remainingToPickup = thing.stackCount;
			CompatHelper.UpdateInventory(pawn);

			Log.Message($"{pawn} stash {thing}:{countToPickUp} into inventory");

			return true;
		}

		public void TryDropThing(Thing thing, int count, bool forbidden = false)
		{
			pawn.inventory.innerContainer.TryDrop(thing, ThingPlaceMode.Near, count, out thing);
			thing.SetForbidden(forbidden, false);
			CompatHelper.UpdateInventory(pawn);

			if (forbidden || !thing.Spawned || thing.def.alwaysHaulable || !thing.def.EverHaulable || thing.IsInAnyStorage())
			{
				return;
			}

			var designationManager = thing.Map.designationManager;
			if (designationManager.DesignationOn(thing, DesignationDefOf.Haul) == null)
			{
				designationManager.AddDesignation(new Designation(thing, DesignationDefOf.Haul));
			}
		}
	}
}
