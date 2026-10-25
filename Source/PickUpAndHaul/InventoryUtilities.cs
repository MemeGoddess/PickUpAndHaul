using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Mathematics;

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
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public CompHauledToInventory HauledToInventoryComp()
		{
			return pawn?.TryGetComp<CompHauledToInventory>();
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public bool HasHauledThingInInventory(Thing thing)
		{
			return pawn?.TryGetComp<CompHauledToInventory>()?.Contains(thing) ?? false;
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
			if (countToPickUp <= 0)
			{
				remainingToPickup = 0;
				if (tooHeavyMsg)
				{
					Log.Message($"{pawn} can't put {thing} into inventory as it is too heavy");
				}

				return false;
			}

			var shouldMerge = comp.GetHashSet().Any(x => x.def == thing.def);
			// always use splitOff as this avoids needing to touch TryTransferToContainer which behaves very differently to TryAdd in its failure modes
			var splitThing = thing.SplitOff(countToPickUp);
			remainingToPickup = thing.stackCount;
			if (!inventory.TryAdd(splitThing, shouldMerge))
			{
				Log.Message($"{pawn} failed to stash {thing}:{countToPickUp} into inventory");
				// prevent retries
				remainingToPickup = 0;
				return false;
			}

			// on merge; splitThing.stackCount can be between 0 and countToPickUp
			// the ThingOwner API does not permit determining where the merged stacks went
			comp.RegisterHauledItem(splitThing);

			if (ModCompatibilityCheck.CombatExtendedIsActive)
			{
				CompatHelper.UpdateInventory(pawn);
			}

			Log.Message($"{pawn} stash {thing}:{remainingToPickup} into inventory");

			return true;
		}
	}
}
