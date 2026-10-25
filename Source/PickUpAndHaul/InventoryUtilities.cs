using System.Linq;
using System.Runtime.CompilerServices;

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


		public bool TryAddItemToInventory(Thing thing, int pickupCount, out Thing splitThing, out bool canRetryPickup, bool tooHeavyMsg = true)
		{
			canRetryPickup = false;
			splitThing = null;
			if (thing == null
			    || thing.Destroyed
			    || !Settings.AllowCorpses && thing is Corpse
			    || thing is Pawn
			    || pawn.IsQuestLodger()
			    || pawn.inventory.innerContainer.Count >= pawn.inventory.innerContainer.maxStacks
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
				if (tooHeavyMsg)
				{
					Log.Message($"{pawn} can't put {thing} into inventory as it is too heavy");
				}

				return false;
			}

			var shouldMerge = comp.GetHashSet().Any(x => x.def == thing.def);
			splitThing = thing.SplitOff(countToPickUp);
			pawn.inventory.GetDirectlyHeldThings().TryAddOrTransfer(splitThing, shouldMerge);
			comp.RegisterHauledItem(splitThing);

			if (ModCompatibilityCheck.CombatExtendedIsActive)
			{
				CompatHelper.UpdateInventory(pawn);
			}

			Log.Message($"{pawn} stash {splitThing}:{splitThing.stackCount} into inventory");

			if (splitThing == thing || thing.stackCount == 0)
			{
				return true;
			}

			canRetryPickup = true;
			Log.Message($"{pawn} failed to stash {thing}:{thing.stackCount} into inventory");
			return false;
		}
	}
}
